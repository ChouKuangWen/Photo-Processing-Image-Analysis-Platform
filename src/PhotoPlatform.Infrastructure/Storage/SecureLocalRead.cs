using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace PhotoPlatform.Infrastructure.Storage;

// Windows local NTFS only. No path-based fallback is safe when the kernel cannot enforce no-reparse.
internal static class SecureLocalRead
{
    private const uint ReadAttributes = 0x80;
    private const uint Traverse = 0x20;
    private const uint Synchronize = 0x100000;
    private const uint GenericRead = 0x80000000;
    private const uint ShareRead = 1;
    private const uint OpenReparsePoint = 0x200000;
    private const uint SynchronousIoNonAlert = 0x20;
    private const uint DirectoryFile = 1;
    private const uint NonDirectoryFile = 0x40;
    private const uint DontReparse = 0x1000;
    private const string Failure = "Storage read could not be opened safely.";

    internal static Stream Open(string root, string reference, CancellationToken token, Action<string>? beforeComponent)
    {
        token.ThrowIfCancellationRequested();
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Secure local reads require Windows local NTFS.");
        var volume = Path.GetPathRoot(root);
        // Exclude UNC/device namespaces and drive-relative paths, whose resolution has other trust boundaries.
        if (volume is null || volume.Length != 3 || !char.IsAsciiLetter(volume[0]) ||
            volume[1] != ':' || volume[2] != '\\')
            throw new IOException(Failure);

        var directories = new List<SafeFileHandle>();
        SafeFileHandle? file = null;
        try
        {
            beforeComponent?.Invoke("before:anchor");
            token.ThrowIfCancellationRequested();
            var anchor = CreateFileW(volume, ReadAttributes | Traverse | Synchronize, ShareRead,
                IntPtr.Zero, 3, 0x02000000 | 0x00200000, IntPtr.Zero);
            directories.Add(anchor);
            if (anchor.IsInvalid) throw new IOException(Failure);
            Validate(anchor, directory: true);
            var filesystem = new StringBuilder(32);
            if (!GetVolumeInformationByHandleW(anchor, null, 0, out _, out _, out _, filesystem, filesystem.Capacity) ||
                !string.Equals(filesystem.ToString(), "NTFS", StringComparison.Ordinal) ||
                GetDriveTypeW(volume) != 3)
                throw new IOException(Failure);

            var components = root[volume.Length..].Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries)
                .Concat(reference.Split('/')).ToArray();
            for (var index = 0; index < components.Length; index++)
            {
                token.ThrowIfCancellationRequested();
                var name = components[index];
                if (name is "." or ".." || name.IndexOfAny(['\\', '/', ':', '\0']) >= 0)
                    throw new IOException(Failure);
                var isDirectory = index != components.Length - 1;
                beforeComponent?.Invoke(name);
                token.ThrowIfCancellationRequested();
                // Each name is resolved relative to the already-open parent object, never by reopening its path.
                // Ancestors remain open without delete sharing until the final file handle has been validated.
                var handle = OpenRelative(directories[^1], name, isDirectory);
                if (isDirectory)
                    directories.Add(handle);
                else
                    file = handle;
                Validate(handle, isDirectory);
            }
            beforeComponent?.Invoke("validated:file");
            token.ThrowIfCancellationRequested();
            var stream = new FileStream(file!, FileAccess.Read, 81920, isAsync: false);
            file = null; // FileStream now owns this exact validated handle.
            return stream;
        }
        catch (FileNotFoundException)
        {
            throw new FileNotFoundException("Storage content was not found.");
        }
        catch (IOException)
        {
            // Includes sharing failures during controlled replacement/opening. Never propagate native paths/details.
            throw new IOException(Failure);
        }
        catch (UnauthorizedAccessException)
        {
            throw new IOException(Failure);
        }
        catch (DllNotFoundException)
        {
            throw new PlatformNotSupportedException("Secure local read APIs are unavailable.");
        }
        catch (EntryPointNotFoundException)
        {
            throw new PlatformNotSupportedException("Secure local read APIs are unavailable.");
        }
        finally
        {
            file?.Dispose();
            for (var index = directories.Count - 1; index >= 0; index--)
                directories[index].Dispose();
        }
    }

    private static SafeFileHandle OpenRelative(SafeFileHandle parent, string name, bool directory)
    {
        var text = Marshal.StringToHGlobalUni(name);
        var unicodePointer = IntPtr.Zero;
        try
        {
            unicodePointer = Marshal.AllocHGlobal(Marshal.SizeOf<UnicodeString>());
            var length = checked((ushort)(name.Length * 2));
            Marshal.StructureToPtr(new UnicodeString { Length = length, MaximumLength = length, Buffer = text }, unicodePointer, false);
            var attributes = new ObjectAttributes
            {
                Length = Marshal.SizeOf<ObjectAttributes>(),
                RootDirectory = parent.DangerousGetHandle(),
                ObjectName = unicodePointer,
                Attributes = DontReparse | 0x40 // OBJ_CASE_INSENSITIVE
            };
            var status = NtCreateFile(out var rawHandle,
                directory ? ReadAttributes | Traverse | Synchronize : GenericRead | Synchronize,
                ref attributes, out _, IntPtr.Zero, 0, ShareRead, 1, // FILE_OPEN, never create/truncate
                OpenReparsePoint | SynchronousIoNonAlert | (directory ? DirectoryFile : NonDirectoryFile),
                IntPtr.Zero, 0);
            GC.KeepAlive(parent);
            var handle = new SafeFileHandle(rawHandle, ownsHandle: true);
            if (status != 0)
            {
                handle.Dispose();
                if (status is unchecked((int)0xc0000034) or unchecked((int)0xc000003a))
                    throw new FileNotFoundException("Storage content was not found.");
                throw new IOException(Failure);
            }
            return handle;
        }
        finally
        {
            if (unicodePointer != IntPtr.Zero) Marshal.FreeHGlobal(unicodePointer);
            Marshal.FreeHGlobal(text);
        }
    }

    private static void Validate(SafeFileHandle handle, bool directory)
    {
        if (handle.IsInvalid || GetFileType(handle) != 1 ||
            !GetFileInformationByHandle(handle, out var info) ||
            (info.Attributes & 0x400) != 0 || // FILE_ATTRIBUTE_REPARSE_POINT
            ((info.Attributes & 0x10) != 0) != directory ||
            (!directory && info.NumberOfLinks != 1))
            throw new IOException(Failure);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct UnicodeString { public ushort Length, MaximumLength; public IntPtr Buffer; }
    [StructLayout(LayoutKind.Sequential)]
    private struct ObjectAttributes
    {
        public int Length;
        public IntPtr RootDirectory, ObjectName;
        public uint Attributes;
        public IntPtr SecurityDescriptor, SecurityQualityOfService;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct IoStatusBlock { public IntPtr Status; public UIntPtr Information; }
    [StructLayout(LayoutKind.Sequential)]
    private struct FileInformation
    {
        public uint Attributes;
        public FILETIME CreationTime, LastAccessTime, LastWriteTime;
        public uint VolumeSerialNumber, FileSizeHigh, FileSizeLow, NumberOfLinks, FileIndexHigh, FileIndexLow;
    }

    [DllImport("ntdll.dll", ExactSpelling = true)]
    private static extern int NtCreateFile(out IntPtr handle, uint access, ref ObjectAttributes attributes,
        out IoStatusBlock status, IntPtr allocationSize, uint fileAttributes, uint share, uint disposition,
        uint options, IntPtr eaBuffer, uint eaLength);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern SafeFileHandle CreateFileW(string path, uint access, uint share, IntPtr security,
        uint disposition, uint flags, IntPtr template);
    [DllImport("kernel32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandle(SafeFileHandle handle, out FileInformation info);
    [DllImport("kernel32.dll", ExactSpelling = true)]
    private static extern uint GetFileType(SafeFileHandle handle);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern uint GetDriveTypeW(string root);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetVolumeInformationByHandleW(SafeFileHandle handle, StringBuilder? name, int nameSize,
        out uint serial, out uint maxComponent, out uint flags, StringBuilder filesystem, int filesystemSize);
}
