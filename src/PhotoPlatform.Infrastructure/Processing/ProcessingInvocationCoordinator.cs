using PhotoPlatform.Infrastructure.Persistence;

namespace PhotoPlatform.Infrastructure.Processing;

// An internal handoff only. Admission/snapshot ownership stays with the caller; T3 is a later task.
internal sealed class ProcessingInvocationCoordinator(ProcessingExecutionStore store)
{
    internal async Task<bool> ExecuteAsync(ExecutionAdmission? admission, bool claim,
        Func<StartedProcessingInvocation, CancellationToken, Task> invokeAdapter, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(invokeAdapter);
        token.ThrowIfCancellationRequested();
        if (admission is null) return false;
        var invocation = claim ? await store.ClaimAsync(admission, token) : await store.StartNextAsync(admission, token);
        if (invocation is null) return false;
        token.ThrowIfCancellationRequested();
        // The writer's transaction and DbContext have been disposed before this point.
        await invokeAdapter(invocation, token);
        return true;
    }
}
