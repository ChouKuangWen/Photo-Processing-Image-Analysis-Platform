using PhotoPlatform.Application.Interfaces;
using PhotoPlatform.Application.Processing;
using PhotoPlatform.Domain.Entities;
using PhotoPlatform.Infrastructure.Processing;

namespace PhotoPlatform.UnitTests.Infrastructure;

public sealed class OriginalInputPreparationTests
{
    private static readonly string Hash = new('a', 64);
    private static readonly DateTimeOffset UploadTime = DateTimeOffset.Parse("2026-11-09T00:00:00Z");
    private static readonly ResolvedRuleManifest Manifest = new("test-only", Hash, "Full", "workflow-mvp-v1",
        [new("EXIF_PARSING", 1, "test-only-exif", []), new("SHA256_CALCULATION", 3, "test-only-sha", [])]);

    [Fact]
    public async Task Prepare_UsesCanonicalInputsAndTransfersSameContentHandleOwnership()
    {
        var reader = new Reader(Hash);
        await using (var prepared = await OriginalInputPreparation.PrepareAsync(1, 2, 3, UploadTime, Manifest, reader, null, null, default))
        {
            Assert.Same(reader.Handle, prepared.Content);
            Assert.Equal(Hash, prepared.Original.ContentVersion);
            Assert.Equal(UploadTime, prepared.Steps[0].UploadCreatedAt);
            Assert.Null(prepared.Steps[1].UploadCreatedAt);
            Assert.Equal(new ExifParsingInput(2, Hash, 3, "workflow-mvp-v1", "test-only-exif", UploadTime)
                .Identity.InputFingerprint, prepared.Steps[0].InputFingerprint);
            Assert.Equal(new Sha256CalculationInput(2, Hash, 3, "workflow-mvp-v1", "test-only-sha")
                .Identity.InputFingerprint, prepared.Steps[1].InputFingerprint);
            Assert.NotEqual(prepared.Steps[0].InputFingerprint, prepared.Steps[1].InputFingerprint);
        }
        Assert.Throws<ObjectDisposedException>(() => reader.Handle!.Stream);
    }

    [Theory]
    [InlineData("content")]
    [InlineData("length")]
    [InlineData("step")]
    [InlineData("missing-step")]
    public async Task Prepare_FrozenMismatchOrMissingBindingRejectsAndDisposesContent(string change)
    {
        var initialReader = new Reader(Hash);
        await using var original = await OriginalInputPreparation.PrepareAsync(1, 2, 3, UploadTime,
            Manifest, initialReader, null, null, default);
        var reader = new Reader(change == "content" ? new string('b', 64) : Hash);
        var error = await Assert.ThrowsAsync<FrozenInputUnavailableException>(() =>
            OriginalInputPreparation.PrepareAsync(1, 2, change == "length" ? 4 : 3,
                change == "step" ? UploadTime.AddSeconds(1) : UploadTime, Manifest, reader,
                original.Original, change == "missing-step" ? [] : original.Steps, default));
        Assert.Equal(change == "missing-step" ? FrozenInputFailureReason.Missing : FrozenInputFailureReason.Mismatch, error.Reason);
        Assert.Throws<ObjectDisposedException>(() => reader.Handle!.Stream);
    }

    [Fact]
    public async Task Prepare_CancellationDoesNotAcquireReaderContent()
    {
        var reader = new Reader(Hash);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => OriginalInputPreparation.PrepareAsync(1, 2, 3,
            UploadTime, Manifest, reader, null, null, cancellation.Token));
        Assert.Null(reader.Handle);
    }

    // The production reader's hashing is covered by SQL/Storage tests; this double supplies explicit test observations.
    private sealed class Reader(string version) : IImageContentReader
    {
        public ContentHandle? Handle { get; private set; }
        public Task<ContentHandle> OpenOriginalAsync(long imageId, CancellationToken cancellationToken)
        {
            Handle = new ContentHandle(new MemoryStream([1, 2, 3]), 3, version);
            return Task.FromResult(Handle);
        }
    }
}
