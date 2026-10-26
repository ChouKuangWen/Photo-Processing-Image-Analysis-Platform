namespace PhotoPlatform.Domain.Enums;

public enum ProcessingCheckpointStatus
{
    Pending,
    Processing,
    RetryWaiting,
    Completed,
    Failed
}
