using PhotoPlatform.Domain.Enums;
using PhotoPlatform.Domain.Policies;

namespace PhotoPlatform.Domain.Entities;

/// <summary>
/// 表示一次初始處理或人工重試的執行週期；恢復沿用同一 Run，終態不重新開啟。
/// </summary>
public sealed class ProcessingRun
{
    public ProcessingRun(long jobId, int runNo, ProcessingRunReason reason,
        string workflowVersion, DateTimeOffset createdAt, string? traceId = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(jobId);
        ArgumentOutOfRangeException.ThrowIfLessThan(runNo, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(runNo, 4);
        if (!Enum.IsDefined(reason))
            throw new ArgumentOutOfRangeException(nameof(reason));
        ProcessingStateGuard.Require((runNo == 1) == (reason == ProcessingRunReason.Initial));
        ProcessingStateGuard.Text(workflowVersion, 50, nameof(workflowVersion));
        if (traceId is not null)
            ProcessingStateGuard.Text(traceId, 100, nameof(traceId));

        JobId = jobId;
        RunNo = runNo;
        Reason = reason;
        WorkflowVersion = workflowVersion;
        CreatedAt = createdAt;
        TraceId = traceId;
        // 初始 Run 隨首次 Claim 建立；人工重試先等待 Claim，不預先宣告已開始執行。
        Status = reason == ProcessingRunReason.Initial ? ProcessingRunStatus.Active : ProcessingRunStatus.Pending;
        StartedAt = reason == ProcessingRunReason.Initial ? createdAt : null;
    }

    public long Id { get; private set; }
    public long JobId { get; private set; }
    public int RunNo { get; private set; }
    public ProcessingRunReason Reason { get; private set; }
    public ProcessingRunStatus Status { get; private set; }
    public string WorkflowVersion { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? StartedAt { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }
    public string? TraceId { get; private set; }

    /// <summary>啟動 Pending Run；首次設定 StartedAt，恢復時不覆寫原始開始時間。</summary>
    public void Activate(DateTimeOffset startedAt)
    {
        ProcessingStateGuard.Require(Status == ProcessingRunStatus.Pending);
        ProcessingStateGuard.TimeNotBefore(startedAt, StartedAt ?? CreatedAt);
        StartedAt ??= startedAt;
        Status = ProcessingRunStatus.Active;
    }

    public void Complete(DateTimeOffset completedAt) => Finish(ProcessingRunStatus.Completed, completedAt);

    public void Fail(DateTimeOffset completedAt) => Finish(ProcessingRunStatus.Failed, completedAt);

    /// <summary>只允許已確認安全的 Active Run 回到 Pending，不建立新的 Run 或重開終態。</summary>
    public void Recover(ProcessingRecoveryAuthorization authorization)
    {
        ArgumentNullException.ThrowIfNull(authorization);
        ProcessingStateGuard.Require(Status == ProcessingRunStatus.Active);
        Status = ProcessingRunStatus.Pending;
    }

    private void Finish(ProcessingRunStatus status, DateTimeOffset completedAt)
    {
        // 終態保留歷史結果；重複完成或失敗不得改寫原本的結束時間。
        ProcessingStateGuard.Require(Status == ProcessingRunStatus.Active);
        ProcessingStateGuard.TimeNotBefore(completedAt, StartedAt!.Value);
        Status = status;
        CompletedAt = completedAt;
    }
}
