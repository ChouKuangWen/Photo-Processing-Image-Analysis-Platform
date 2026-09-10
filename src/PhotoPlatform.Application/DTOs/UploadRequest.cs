using PhotoPlatform.Application.Interfaces;
using PhotoPlatform.Domain.Enums;

namespace PhotoPlatform.Application.DTOs;

public sealed record UploadRequest(
    IReadOnlyList<IUploadFile> Files,
    WorkflowType Workflow);
