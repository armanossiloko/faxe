using Faxe.Flow;

namespace Faxe.Api;

/// <summary>Shuts down the shared Akka ActorSystem with the host.</summary>
public sealed class FlowRuntimeLifetime(FlowRuntime runtime) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StopAsync(CancellationToken cancellationToken) =>
        runtime.DisposeAsync().AsTask();
}
