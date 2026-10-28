using PhotoPlatform.Domain.Enums;
using PhotoPlatform.Domain.Policies;

namespace PhotoPlatform.UnitTests.Processing;

public sealed class PrimaryBindingTests
{
    // 測試目的：確認每個狀態只貢獻規格允許的計數。
    // 測試情境：遍歷四個狀態及所有前後組合，另外計算次要工作。
    // 預期結果：重複完成不加計，失敗重試扣回失敗數，次要工作永遠零差額。
    [Fact]
    public void ContributionsAndDeltas_RespectPrimaryOwnership()
    {
        var expected = new Dictionary<ProcessingJobStatus, PrimaryContribution>
        {
            [ProcessingJobStatus.Pending] = new(0, 0, 0),
            [ProcessingJobStatus.Processing] = new(0, 0, 0),
            [ProcessingJobStatus.Completed] = new(1, 1, 0),
            [ProcessingJobStatus.Failed] = new(1, 0, 1)
        };
        foreach (var before in expected)
        foreach (var after in expected)
        {
            Assert.Equal(before.Value, PrimaryAggregatePolicy.Contribution(before.Key));
            Assert.Equal(new PrimaryContribution(after.Value.Processed - before.Value.Processed,
                after.Value.Success - before.Value.Success, after.Value.Failed - before.Value.Failed),
                PrimaryAggregatePolicy.Delta(before.Key, after.Key, true));
            Assert.Equal(new PrimaryContribution(0, 0, 0), PrimaryAggregatePolicy.Delta(before.Key, after.Key, false));
        }
        Assert.Throws<ArgumentOutOfRangeException>(() => PrimaryAggregatePolicy.Contribution((ProcessingJobStatus)99));
    }

    // 測試目的：確認空批次、全部等待及各種完成組合的 Batch 狀態。
    // 測試情境：包含失敗重試後仍有其他完成圖片的情況。
    // 預期結果：只有全部 Pending 才回 Pending，全失敗為 Failed，有成功的全完成批次為 Completed。
    [Theory]
    [InlineData("", "Pending", 0, 0)]
    [InlineData("Pending,Pending", "Pending", 0, 0)]
    [InlineData("Pending,Completed", "Processing", 1, 0)]
    [InlineData("Pending,Failed", "Processing", 0, 1)]
    [InlineData("Processing,Pending", "Processing", 0, 0)]
    [InlineData("Failed,Failed", "Failed", 0, 2)]
    [InlineData("Completed,Failed", "Completed", 1, 1)]
    public void Aggregate_UsesCompletePrimarySet(string values, string status, int success, int failed)
    {
        var states = values.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(Enum.Parse<ProcessingJobStatus>).ToArray();
        Assert.Equal(new PrimaryAggregate(success + failed, success, failed, status),
            PrimaryAggregatePolicy.Calculate(states.Length, states));
    }

    // 測試目的：確認不完整集合不能計算，結束時間不因重複通知改寫。
    // 測試情境：集合少一張，並模擬離開終態及再次完成。
    // 預期結果：拒絕不完整集合，重複完成保留時間，重試清空時間，再完成才設新時間。
    [Fact]
    public void CollectionAndCompletionTime_RejectMissingDataAndPreserveHistory()
    {
        Assert.Throws<ArgumentException>(() => PrimaryAggregatePolicy.Calculate(2, [ProcessingJobStatus.Pending]));
        var first = DateTimeOffset.UtcNow;
        var next = first.AddMinutes(1);
        var done = new PrimaryAggregate(1, 1, 0, "Completed");
        Assert.Equal(first, PrimaryAggregatePolicy.CompletionTime("Completed", first, done, next));
        Assert.Throws<InvalidOperationException>(() => PrimaryAggregatePolicy.CompletionTime("Completed", null, done, next));
        Assert.Null(PrimaryAggregatePolicy.CompletionTime("Failed", first, new(0, 0, 0, "Pending"), next));
        Assert.Equal(next, PrimaryAggregatePolicy.CompletionTime("Processing", null, done, next));
    }
}
