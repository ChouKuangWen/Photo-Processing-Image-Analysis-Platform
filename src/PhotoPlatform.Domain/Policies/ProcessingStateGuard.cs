using PhotoPlatform.Domain.Exceptions;

namespace PhotoPlatform.Domain.Policies;

/// <summary>集中狀態前提與欄位邊界檢查，讓模型在變更狀態前拒絕無效輸入。</summary>
internal static class ProcessingStateGuard
{
    /// <summary>以一致的領域例外拒絕不允許的轉換；不在 Domain 決定 HTTP 回應。</summary>
    public static void Require(bool condition)
    {
        if (!condition)
            throw new InvalidProcessingStateException();
    }

    public static void TimeNotBefore(DateTimeOffset timestamp, DateTimeOffset earliest)
    {
        if (timestamp < earliest)
            throw new ArgumentOutOfRangeException(nameof(timestamp), "Timestamp precedes the lifecycle boundary.");
    }

    public static void Text(string value, int maximumLength, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        if (value.Length > maximumLength)
            throw new ArgumentOutOfRangeException(parameterName);
    }
}
