namespace PhotoPlatform.Application.DTOs;

// 只提供安全識別與原因；不帶 SQL、來源證據或內部路徑。Count 是完整原因數，不是顯示筆數。
public sealed record ProcessingPrerequisiteReason(string ReasonCode, long? ImageId = null, long? JobId = null);

public sealed record ProcessingPrerequisiteAssessment(Guid BatchId, bool Exists, DateTimeOffset CheckedAt,
    bool Ready, IReadOnlyList<ProcessingPrerequisiteReason> Reasons, int Count, bool Truncated, string? ResolutionHint);
