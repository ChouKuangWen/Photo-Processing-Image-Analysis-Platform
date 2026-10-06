using PhotoPlatform.Application.DTOs;
using PhotoPlatform.Application.Interfaces;
using PhotoPlatform.Application.Services;

namespace PhotoPlatform.UnitTests.Application;

public sealed class BatchStatusQueryTests
{
    [Theory]
    [InlineData(100, 80, 80)]
    [InlineData(8, 1, 12.5)]
    [InlineData(0, 5, 0)]
    [InlineData(10, -1, 0)]
    [InlineData(10, 11, 100)]
    [InlineData(10, 0, 0)]
    [InlineData(10, 10, 100)]
    // 驗證 persisted Batch 統計資料能正確轉成查詢結果。
    // Progress 使用 decimal 計算，TotalCount 為 0 時回傳 0，並限制在 0～100。
    public async Task ReadsPersistedAggregateAndUsesDecimalProgress(int total, int processed, decimal expected)
    {
        var id = Guid.NewGuid();
        var snapshot = new BatchStatusSnapshot(id, total, processed, 78, 2, "Processing");
        using var cancellation = new CancellationTokenSource();
        var persistence = new Stub((batchId, token) =>
        {
            Assert.Equal(id, batchId);
            Assert.Equal(cancellation.Token, token);
            return Task.FromResult<BatchStatusSnapshot?>(snapshot);
        });
        var result = await new BatchStatusQuery(persistence).GetBatchStatusAsync(id, cancellation.Token);
        Assert.Equal(new BatchStatusResult(id, total, processed, 78, 2, expected, "Processing"), result);
    }

    [Fact]
    // 驗證非整數進度保留 decimal 計算結果，不額外進行四捨五入。
    public async Task FractionalProgress_IsNotRoundedToANewPrecision()
    {
        var id = Guid.NewGuid();
        var persistence = new Stub((_, _) => Task.FromResult<BatchStatusSnapshot?>(new(id, 3, 1, 1, 0, "Processing")));
        var result = await new BatchStatusQuery(persistence).GetBatchStatusAsync(id, CancellationToken.None);
        Assert.Equal(1m / 3m * 100m, result!.ProgressPercentage);
    }

    [Fact]
    // Persistence 找不到 Batch 時，Query 應直接回傳 null，交由 API 層映射為 404。
    public async Task MissingBatch_ReturnsNull()
    {
        var persistence = new Stub((_, _) => Task.FromResult<BatchStatusSnapshot?>(null));
        Assert.Null(await new BatchStatusQuery(persistence).GetBatchStatusAsync(Guid.NewGuid(), CancellationToken.None));
    }

    [Fact]
    // Cancellation 必須繼續向上傳遞，不得被 Query 誤判為 Batch 不存在。
    public async Task PersistenceCancellation_IsNotConvertedToNotFound()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var persistence = new Stub((_, token) => Task.FromCanceled<BatchStatusSnapshot?>(token));
        var error = await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new BatchStatusQuery(persistence).GetBatchStatusAsync(Guid.NewGuid(), cancellation.Token));
        Assert.Equal(cancellation.Token, error.CancellationToken);
    }

    // Unit Test 專用的 Persistence 假實作，避免 BatchStatusQuery 測試依賴真實 SQL Server。
    private sealed class Stub(Func<Guid, CancellationToken, Task<BatchStatusSnapshot?>> execute) : IBatchStatusPersistence
    {
        public Task<BatchStatusSnapshot?> FindAsync(Guid id, CancellationToken token) => execute(id, token);
    }
}
