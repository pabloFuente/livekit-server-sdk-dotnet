using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;

namespace Livekit.Server.Sdk.Dotnet.Test;

public class ServiceClientFixture : IAsyncLifetime
{
    public const string TEST_API_KEY = "devkey";
    public const string TEST_API_SECRET = "secretsecretsecretsecretsecretsecretsecret";

    private readonly IContainer container = new ContainerBuilder()
        .WithImage("livekit/livekit-server:latest")
        .WithName("livekit")
        .WithAutoRemove(true)
        .WithEnvironment("LIVEKIT_KEYS", TEST_API_KEY + ": " + TEST_API_SECRET)
        .WithPortBinding(7880, 7880)
        .WithWaitStrategy(Wait.ForUnixContainer().UntilHttpRequestIsSucceeded(r => r.ForPort(7880)))
        .Build();

    public string ContainerId => $"{container.Id}";

    public Task InitializeAsync() => container.StartAsync();

    public Task DisposeAsync() => container.DisposeAsync().AsTask();
}
