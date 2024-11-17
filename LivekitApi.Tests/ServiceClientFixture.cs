using Docker.DotNet;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Configurations;
using DotNet.Testcontainers.Containers;
using DotNet.Testcontainers.Networks;
using LiveKit.Proto;

namespace Livekit.Server.Sdk.Dotnet.Test;

public class ServiceClientFixture : IAsyncLifetime
{
    public const string TEST_API_KEY = "devkey";
    public const string TEST_API_SECRET = "secretsecretsecretsecretsecretsecretsecret";

    private static IContainer livekitServerContainer;

    public Task InitializeAsync()
    {
        livekitServerContainer = new ContainerBuilder()
            .WithImage("livekit/livekit-server:latest")
            .WithAutoRemove(true)
            .WithEnvironment("LIVEKIT_KEYS", TEST_API_KEY + ": " + TEST_API_SECRET)
            .WithPortBinding(7880, 7880)
            .WithWaitStrategy(
                Wait.ForUnixContainer().UntilHttpRequestIsSucceeded(r => r.ForPort(7880))
            )
            .Build();
        return livekitServerContainer.StartAsync();
    }

    public Task DisposeAsync()
    {
        return livekitServerContainer.DisposeAsync().AsTask();
    }

    private static async Task runLivekitCliCommand(
        string[] args,
        IWaitForContainerOS? waitForContainerOS = null
    )
    {
        var uri = new UriBuilder(
            "http",
            livekitServerContainer.IpAddress,
            livekitServerContainer.GetMappedPublicPort(7880)
        ).Uri;
        var url = uri.ToString().TrimEnd('/');
        string[] commands =
        {
            "--url",
            url,
            "--api-key",
            TEST_API_KEY,
            "--api-secret",
            TEST_API_SECRET,
        };
        commands = commands.Concat(args).ToArray();
        var lkContainerBuilder = new ContainerBuilder()
            .WithImage("livekit/livekit-cli:latest")
            .WithAutoRemove(true)
            .WithCommand(commands);
        if (waitForContainerOS != null)
        {
            lkContainerBuilder = lkContainerBuilder.WithWaitStrategy(waitForContainerOS);
        }
        var livekitClientContainer = lkContainerBuilder.Build();
        await livekitClientContainer.StartAsync();
        return;
    }

    public static async Task JoinParticipant(string roomName, string participantIdentity)
    {
        await runLivekitCliCommand(
            ["room", "join", "--identity", participantIdentity, roomName],
            Wait.ForUnixContainer().UntilMessageIsLogged("connected to room")
        );
    }

    public static async Task PublishVideoTrackInRoom(string roomName, string participantIdentity)
    {
        await runLivekitCliCommand(
            ["room", "join", "--identity", participantIdentity, "--publish-demo", roomName],
            Wait.ForUnixContainer().UntilMessageIsLogged("published simulcast track")
        );
    }
}
