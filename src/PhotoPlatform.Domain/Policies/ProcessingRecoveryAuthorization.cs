namespace PhotoPlatform.Domain.Policies;

/// <summary>
/// 表達呼叫端已確認舊程序停止且可安全恢復的領域前提，不是身分授權或 SQL 執行權憑證。
/// 本型別不偵測程序存活，也不自行驗證持久化結果或外部副作用。
/// </summary>
public sealed class ProcessingRecoveryAuthorization
{
    private ProcessingRecoveryAuthorization()
    {
    }

    /// <summary>兩項外部確認皆成立才建立恢復資格；證據核對由呼叫端負責。</summary>
    public static ProcessingRecoveryAuthorization Confirm(
        bool previousProcessStopped,
        bool safeToResume)
    {
        ProcessingStateGuard.Require(previousProcessStopped && safeToResume);
        return new ProcessingRecoveryAuthorization();
    }
}
