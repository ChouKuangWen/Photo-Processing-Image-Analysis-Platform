namespace PhotoPlatform.Application.DTOs;

public sealed record FileValidationResult(
    bool IsValid,
    string? ErrorCode,
    string? ErrorMessage);
