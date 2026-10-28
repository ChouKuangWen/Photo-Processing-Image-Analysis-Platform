using PhotoPlatform.Application.DTOs;

namespace PhotoPlatform.Application.Interfaces;

// 只核對來源綁定與 Batch 計數；結果不是跨交易的執行許可，也不代表 Workflow 輸入已備妥。
public interface IProcessingPrerequisiteService
{
    Task<ProcessingPrerequisiteAssessment> AssessBatchAsync(Guid batchId, CancellationToken cancellationToken);
}
