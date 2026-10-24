using PhotoPlatform.Domain.Entities;

namespace PhotoPlatform.Application.Interfaces;

public interface IProcessingQueue
{
    Task EnqueueAsync(
        ProcessingJob job,
        CancellationToken cancellationToken);

    ValueTask<ProcessingJob> DequeueAsync(
        CancellationToken cancellationToken);
}
