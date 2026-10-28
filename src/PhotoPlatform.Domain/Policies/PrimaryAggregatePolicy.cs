using PhotoPlatform.Domain.Enums;

namespace PhotoPlatform.Domain.Policies;

// 只計算主工作的貢獻，不寫資料庫；呼叫端須先核對完整 Batch，再於同一交易使用結果。
public static class PrimaryAggregatePolicy
{
    public static PrimaryContribution Contribution(ProcessingJobStatus status) => status switch
    {
        ProcessingJobStatus.Pending or ProcessingJobStatus.Processing => new(0, 0, 0),
        ProcessingJobStatus.Completed => new(1, 1, 0),
        ProcessingJobStatus.Failed => new(1, 0, 1),
        _ => throw new ArgumentOutOfRangeException(nameof(status))
    };

    // 舊狀態必須來自交易內讀到的資料；重複完成差額為零，次要工作完全不影響 Upload 計數。
    public static PrimaryContribution Delta(ProcessingJobStatus previous, ProcessingJobStatus next, bool isPrimary)
    {
        var before = Contribution(previous);
        var after = Contribution(next);
        return isPrimary ? new(after.Processed - before.Processed, after.Success - before.Success,
            after.Failed - before.Failed) : new(0, 0, 0);
    }

    public static PrimaryAggregate Calculate(int totalCount, IReadOnlyCollection<ProcessingJobStatus> primaryStatuses)
    {
        ArgumentNullException.ThrowIfNull(primaryStatuses);
        if (totalCount < 0 || primaryStatuses.Count != totalCount)
            throw new ArgumentException("A complete primary collection is required.", nameof(primaryStatuses));
        var contributions = primaryStatuses.Select(Contribution).ToArray();
        var success = contributions.Sum(value => value.Success);
        var failed = contributions.Sum(value => value.Failed);
        var processed = checked(success + failed);
        var status = totalCount == 0 || primaryStatuses.All(value => value == ProcessingJobStatus.Pending)
            ? "Pending" : processed < totalCount ? "Processing" : success > 0 ? "Completed" : "Failed";
        return new(processed, success, failed, status);
    }

    // 重複通知不能改寫結束時間；離開結束狀態要清空，下一次真正結束才使用新的時間。
    public static DateTimeOffset? CompletionTime(string previousStatus, DateTimeOffset? previousCompletedAt,
        PrimaryAggregate next, DateTimeOffset committedAt)
    {
        if (next.Status is not ("Completed" or "Failed"))
            return null;
        if (previousStatus is "Completed" or "Failed")
            return previousCompletedAt ?? throw new InvalidOperationException("Terminal batch completion time is missing.");
        return committedAt;
    }
}

public sealed record PrimaryContribution(int Processed, int Success, int Failed);
public sealed record PrimaryAggregate(int Processed, int Success, int Failed, string Status);
