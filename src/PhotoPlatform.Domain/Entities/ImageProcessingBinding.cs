using PhotoPlatform.Domain.Enums;

namespace PhotoPlatform.Domain.Entities;

// 記住圖片主要交給哪個 Upload Job 處理，以及該主要工作的狀態；其他工作不能取代這個綁定。
// 工作、圖片和批次是否真的屬於同一組，交給後續交易核對；這個類別不自行猜測。

public sealed class ImageProcessingBinding
{
    // 先拒絕非正數編號和未定義的狀態，避免建立明顯錯誤的綁定；這裡不查詢資料庫。
    public ImageProcessingBinding(long imageId, Guid batchId, long primaryJobId,
        WorkflowType primaryWorkflow, ProcessingJobStatus primaryStatus, DateTimeOffset updatedAt)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(imageId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(primaryJobId);
        if (!Enum.IsDefined(primaryWorkflow))
            throw new ArgumentOutOfRangeException(nameof(primaryWorkflow));
        if (!Enum.IsDefined(primaryStatus))
            throw new ArgumentOutOfRangeException(nameof(primaryStatus));

        ImageId = imageId;
        BatchId = batchId;
        PrimaryJobId = primaryJobId;
        PrimaryWorkflow = primaryWorkflow;
        PrimaryStatus = primaryStatus;
        UpdatedAt = updatedAt;
    }

    // 外部只能讀取，不能直接改寫綁定；後續更新須走核准的處理流程。
    public long ImageId { get; private set; }
    public Guid BatchId { get; private set; }
    public long PrimaryJobId { get; private set; }
    public WorkflowType PrimaryWorkflow { get; private set; }
    public ProcessingJobStatus PrimaryStatus { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
}
