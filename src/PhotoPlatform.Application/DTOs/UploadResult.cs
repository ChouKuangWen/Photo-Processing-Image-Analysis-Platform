namespace PhotoPlatform.Application.DTOs;

public sealed record UploadResult(
    Guid BatchId,
    int TotalCount,
    string Status);
