using Microsoft.EntityFrameworkCore;
using PhotoPlatform.Infrastructure.Persistence;
using PhotoPlatform.Infrastructure.Processing;

namespace PhotoPlatform.UnitTests.Infrastructure;

public sealed class ProcessingInvocationCoordinatorTests
{
    [Fact]
    public async Task MissingAdmissionTouchesNeitherDatabaseNorAdapter()
    {
        var factory = new RejectingFactory();
        var coordinator = new ProcessingInvocationCoordinator(new ProcessingExecutionStore(factory));
        var calls = 0;
        var result = await coordinator.ExecuteAsync(null, true, (_, _) => { calls++; return Task.CompletedTask; }, default);
        Assert.False(result);
        Assert.Equal(0, factory.Calls);
        Assert.Equal(0, calls);
    }

    [Fact]
    public async Task CancelledCoordinationPreservesTokenWithoutDatabaseOrAdapterCall()
    {
        var factory = new RejectingFactory();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var coordinator = new ProcessingInvocationCoordinator(new ProcessingExecutionStore(factory));
        var calls = 0;
        var error = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => coordinator.ExecuteAsync(null, true,
            (_, _) => { calls++; return Task.CompletedTask; }, cancellation.Token));
        Assert.Equal(cancellation.Token, error.CancellationToken);
        Assert.Equal(0, calls);
        Assert.Equal(0, factory.Calls);
    }

    private sealed class RejectingFactory : IDbContextFactory<PhotoPlatformDbContext>
    {
        internal int Calls { get; private set; }
        public PhotoPlatformDbContext CreateDbContext()
        {
            Calls++;
            throw new InvalidOperationException("Database must not be opened without admission.");
        }
    }
}
