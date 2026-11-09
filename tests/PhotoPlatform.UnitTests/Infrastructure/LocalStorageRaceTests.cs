using System.Diagnostics;
using PhotoPlatform.Infrastructure.Storage;

namespace PhotoPlatform.UnitTests.Infrastructure;

public sealed class LocalStorageRaceTests : IDisposable
{
    private const string Id = "550e8400e29b41d4a716446655440000";
    private const string Reference = "original/" + Id;
    private readonly string root = Path.Combine(Path.GetTempPath(), "PhotoPlatform-security-" + Guid.NewGuid().ToString("N"));
    private readonly List<(string Path, bool Directory)> links = [];
    private string Storage => Path.Combine(root, "storage");
    private string Originals => Path.Combine(Storage, "original");
    private string Original => Path.Combine(Originals, Id);
    private string Outside => Path.Combine(root, "outside");
    private string ExternalFile => Path.Combine(Outside, Id);

    public LocalStorageRaceTests()
    {
        Directory.CreateDirectory(Originals);
        Directory.CreateDirectory(Outside);
        File.WriteAllBytes(Original, [1]);
        File.WriteAllBytes(ExternalFile, [9]);
    }

    private LocalFileStorageService Service(Action<string>? hook = null, string? storage = null) =>
        new(storage ?? Storage, () => Id, hook);

    [WindowsFact]
    public async Task Read_OpensValidatedObjectAndReleasesFileOnDispose()
    {
        var stream = await Service().OpenReadAsync(Reference, default);
        Assert.Equal(1, stream.ReadByte());
        Assert.ThrowsAny<IOException>(() => File.Move(Original, Original + "-moved"));
        await stream.DisposeAsync();
        File.Move(Original, Original + "-moved");
    }

    [WindowsFact]
    public async Task Read_RejectsOutsideAbsoluteReferenceWithoutLeakingPath()
    {
        var exception = await Assert.ThrowsAnyAsync<ArgumentException>(() =>
            Service().OpenReadAsync(ExternalFile, default));
        Assert.DoesNotContain(root, exception.Message);
        Assert.Null(exception.InnerException);
        Assert.Equal(new byte[] { 9 }, File.ReadAllBytes(ExternalFile));
    }

    [WindowsFact]
    public async Task Read_RejectsExistingParentJunctionWithoutFollowingOutside()
    {
        Directory.Move(Originals, Path.Combine(Storage, "saved"));
        Junction(Originals, Outside);
        await RejectedAsync(Service());
    }

    [WindowsFact]
    public async Task Read_RejectsJunctionAboveConfiguredRoot()
    {
        var alias = Path.Combine(root, "alias");
        Junction(alias, Storage);
        await RejectedAsync(Service(storage: alias));
    }

    [WindowsFact]
    public async Task Read_RejectsParentReplacedWithJunctionAtControlledOpenBoundary()
    {
        var synchronized = false;
        var service = Service(component =>
        {
            if (component != "before:anchor") return;
            synchronized = true;
            Directory.Move(Originals, Path.Combine(Storage, "saved"));
            Junction(Originals, Outside);
        });
        await RejectedAsync(service);
        Assert.True(synchronized);
        // A failed open must have released every ancestor handle.
        var moved = Storage + "-moved";
        Directory.Move(Storage, moved);
        Directory.Move(moved, Storage);
    }

    [WindowsFact]
    public async Task Read_PinnedAncestorsCannotBeRenamedBeforeFinalOpen()
    {
        var synchronized = false;
        var service = Service(component =>
        {
            if (component != Id) return;
            synchronized = true;
            Assert.ThrowsAny<IOException>(() => Directory.Move(Originals, Originals + "-moved"));
            Assert.ThrowsAny<IOException>(() => Directory.Move(Storage, Storage + "-moved"));
        });
        await using var stream = await service.OpenReadAsync(Reference, default);
        Assert.True(synchronized);
        Assert.Equal(1, stream.ReadByte());
    }

    [WindowsFact]
    public async Task Read_ValidatedFileCannotBeReplacedBeforeHandoff()
    {
        var synchronized = false;
        var service = Service(component =>
        {
            if (component != "validated:file") return;
            synchronized = true;
            Assert.ThrowsAny<IOException>(() => File.Move(Original, Original + "-moved"));
        });
        await using var stream = await service.OpenReadAsync(Reference, default);
        Assert.True(synchronized);
        Assert.Equal(1, stream.ReadByte());
    }

    [WindowsFact]
    public async Task Read_CancellationAfterFileValidationReleasesFileAndAllAncestors()
    {
        using var cancellation = new CancellationTokenSource();
        var service = Service(component =>
        {
            if (component == "validated:file") cancellation.Cancel();
        });
        var exception = await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            service.OpenReadAsync(Reference, cancellation.Token));
        Assert.Equal(cancellation.Token, exception.CancellationToken);
        using (var file = new FileStream(Original, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            Assert.Equal(1, file.ReadByte());
        Directory.Move(Storage, Storage + "-moved");
        Directory.Move(Storage + "-moved", Storage);
    }

    [WindowsLinkFact]
    public async Task Read_RejectsFileSymbolicLink()
    {
        File.Delete(Original);
        File.CreateSymbolicLink(Original, ExternalFile);
        links.Add((Original, false));
        await RejectedAsync(Service());
    }

    [WindowsFact]
    public async Task Read_SharingViolationDuringTargetReplacementIsSanitizedAndReleasesHandles()
    {
        IOException? nativeFailure = null;
        var service = Service(component =>
        {
            if (component != Id) return;
            try
            {
                File.Move(Original, Original + "-saved");
            }
            catch (IOException exception)
            {
                nativeFailure = exception;
                throw;
            }
            throw new InvalidOperationException("Expected pinned parent sharing protection.");
        });
        await RejectedAsync(service);
        Assert.NotNull(nativeFailure);
        Assert.Equal(32, nativeFailure.HResult & 0xffff); // ERROR_SHARING_VIOLATION
        using (var file = new FileStream(Original, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            Assert.Equal(1, file.ReadByte());
        Directory.Move(Storage, Storage + "-moved");
        Directory.Move(Storage + "-moved", Storage);
    }

    [WindowsFact]
    public async Task Read_UnexpectedProgrammingFailureIsNotTranslatedIntoStorageFailure()
    {
        var failure = new InvalidOperationException("Controlled programming failure.");
        var service = Service(component =>
        {
            if (component == Id) throw failure;
        });
        Assert.Same(failure, await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.OpenReadAsync(Reference, default)));
        Directory.Move(Storage, Storage + "-moved");
        Directory.Move(Storage + "-moved", Storage);
    }

    [WindowsLinkFact]
    public async Task Read_RejectsDirectorySymbolicLink()
    {
        Directory.Move(Originals, Path.Combine(Storage, "saved"));
        Directory.CreateSymbolicLink(Originals, Outside);
        links.Add((Originals, true));
        await RejectedAsync(Service());
    }

    [WindowsLinkFact]
    public async Task Read_RejectsTargetReplacedWithSymbolicLinkAtControlledOpenBoundary()
    {
        var synchronized = false;
        var service = Service(component =>
        {
            if (component != Id) return;
            synchronized = true;
            File.Move(Original, Original + "-saved");
            File.CreateSymbolicLink(Original, ExternalFile);
            links.Add((Original, false));
        });
        await RejectedAsync(service);
        Assert.True(synchronized);
    }

    [NonWindowsFact]
    public async Task Read_UnsupportedPlatformFailsClosed()
    {
        var exception = await Assert.ThrowsAsync<PlatformNotSupportedException>(() =>
            Service().OpenReadAsync(Reference, default));
        Assert.DoesNotContain(root, exception.Message);
    }

    private async Task RejectedAsync(LocalFileStorageService service)
    {
        var exception = await Assert.ThrowsAsync<IOException>(() => service.OpenReadAsync(Reference, default));
        Assert.Equal("Storage read could not be opened safely.", exception.Message);
        Assert.Null(exception.InnerException);
        Assert.Equal(new byte[] { 9 }, File.ReadAllBytes(ExternalFile));
    }

    private void Junction(string path, string target)
    {
        // Only generated test paths are passed to the Windows built-in; no client strings or shell deletion.
        Assert.DoesNotContain('"', path);
        Assert.DoesNotContain('"', target);
        var start = new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "cmd.exe"))
        {
            Arguments = $"/d /c mklink /J \"{path}\" \"{target}\"",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        using var process = Process.Start(start)!;
        process.StandardOutput.ReadToEnd();
        process.StandardError.ReadToEnd();
        process.WaitForExit();
        Assert.Equal(0, process.ExitCode);
        links.Add((path, true));
    }

    public void Dispose()
    {
        // Unlink only links created by this test before recursively removing its isolated directory.
        foreach (var link in links)
        {
            if (link.Directory) Directory.Delete(link.Path);
            else File.Delete(link.Path);
        }
        var resolved = Path.GetFullPath(root);
        if (!resolved.StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase) ||
            !Path.GetFileName(resolved).StartsWith("PhotoPlatform-security-", StringComparison.Ordinal))
            throw new InvalidOperationException("Unexpected security test cleanup target.");
        Directory.Delete(resolved, true);
    }

    public class WindowsFactAttribute : FactAttribute
    {
        public WindowsFactAttribute()
        {
            if (!OperatingSystem.IsWindows()) Skip = "Windows NTFS security test; not supported on this platform.";
        }
    }
    public sealed class NonWindowsFactAttribute : FactAttribute
    {
        public NonWindowsFactAttribute()
        {
            if (OperatingSystem.IsWindows()) Skip = "Non-Windows fail-closed branch is not executable on Windows.";
        }
    }
    public sealed class WindowsLinkFactAttribute : WindowsFactAttribute
    {
        public WindowsLinkFactAttribute()
        {
            if (Skip is not null) return;
            var probe = Path.Combine(Path.GetTempPath(), "PhotoPlatform-link-probe-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(probe);
            try
            {
                File.WriteAllText(Path.Combine(probe, "target"), "");
                File.CreateSymbolicLink(Path.Combine(probe, "link"), Path.Combine(probe, "target"));
            }
            catch (UnauthorizedAccessException) { Skip = "Windows symbolic-link privilege/developer mode is unavailable."; }
            catch (IOException exception) when ((exception.HResult & 0xffff) == 1314)
            {
                Skip = "Windows symbolic-link privilege/developer mode is unavailable (1314).";
            }
            finally
            {
                File.Delete(Path.Combine(probe, "link"));
                File.Delete(Path.Combine(probe, "target"));
                Directory.Delete(probe);
            }
        }
    }
}
