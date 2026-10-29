using PhotoPlatform.Application.Processing;
using PhotoPlatform.Domain.Entities;

namespace PhotoPlatform.Application.Interfaces;

// 沿用正式規格的介面；本階段沒有執行實作，後續仍須先經 SQL Claim 才能呼叫步驟。
public interface IWorkflowOrchestrator
{
    Task<WorkflowDefinition> ResolveWorkflowAsync(string workflow, CancellationToken cancellationToken);
    Task ExecuteAsync(ProcessingJob job, CancellationToken cancellationToken);
}
