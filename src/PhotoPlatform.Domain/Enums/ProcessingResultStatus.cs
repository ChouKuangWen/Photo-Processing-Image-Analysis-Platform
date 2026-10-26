namespace PhotoPlatform.Domain.Enums;

/// <summary>步驟完成後的結果分類，與個別 invocation 的 Log 終態不同；警告降級不抹除失敗歷史。</summary>
public enum ProcessingResultStatus
{
    Success,
    Warning,
    Skipped
}
