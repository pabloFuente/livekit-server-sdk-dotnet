using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Configurations;
using DotNet.Testcontainers.Containers;
using LiveKit.Proto;

namespace Livekit.Server.Sdk.Dotnet.Test;

public static class TestConstants
{
    public const string ROOM_NAME = "test-room";
    public const string ROOM_METADATA = "room-metadata";
    public const string PARTICIPANT_IDENTITY = "test-participant";
}

public class ServiceClientFixture : IDisposable
{
    public const string TEST_HTTP_URL = "http://localhost:7880";
    public const string TEST_API_KEY = "devkey";
    public const string TEST_API_SECRET = "secretsecretsecretsecretsecretsecretsecret";

    private const string LIVEKIT_SERVER_IMAGE = "livekit/livekit-server:latest";
    private const string LIVEKIT_EGRESS_IMAGE = "livekit/egress:latest";
    private const string LIVEKIT_INGRESS_IMAGE = "livekit/ingress:latest";
    private const string LIVEKIT_CLI_IMAGE = "livekit/livekit-cli:latest";
    private const string REDIS_IMAGE = "redis:latest";

    private string egressYaml =
        @"api_key: "
        + TEST_API_KEY
        + @"
api_secret: "
        + TEST_API_SECRET
        + @"
ws_url: {WS_URL}
insecure: true
redis:
    address: {REDIS_ADDRESS}";

    private string ingressYaml =
        @"api_key: "
        + TEST_API_KEY
        + @"
api_secret: "
        + TEST_API_SECRET
        + @"
ws_url: {WS_URL}
redis:
    address: {REDIS_ADDRESS}
rtmp_port: 1935
whip_port: 8085
http_relay_port: 9090
health_port: 9091";

    private IContainer redisContainer;
    private IContainer livekitServerContainer;
    private IContainer egressContainer;
    private IContainer ingressContainer;

    public ServiceClientFixture()
    {
        // Redis
        redisContainer = new ContainerBuilder()
            .WithImage(REDIS_IMAGE)
            .WithName("redis")
            .WithPortBinding(6379, 6379)
            .WithAutoRemove(true)
            .WithWaitStrategy(Wait.ForUnixContainer().UntilPortIsAvailable(6379))
            .Build();
        redisContainer.StartAsync().Wait();
        // Livekit server
        livekitServerContainer = new ContainerBuilder()
            .WithImage(LIVEKIT_SERVER_IMAGE)
            .WithName("livekit-server")
            .WithAutoRemove(true)
            .WithEnvironment("LIVEKIT_KEYS", TEST_API_KEY + ": " + TEST_API_SECRET)
            .WithEnvironment("LIVEKIT_REDIS_ADDRESS", redisContainer.IpAddress + ":6379")
            .WithPortBinding(7880, 7880)
            .DependsOn(redisContainer)
            .WithWaitStrategy(
                Wait.ForUnixContainer().UntilHttpRequestIsSucceeded(r => r.ForPort(7880))
            )
            .Build();
        livekitServerContainer.StartAsync().Wait();
        // Egress
        var egressConfigPath = GetTempFilePathWithExtension(".yaml");
        File.WriteAllText(
            egressConfigPath,
            egressYaml
                .Replace("{WS_URL}", "ws://" + livekitServerContainer.IpAddress + ":7880")
                .Replace("{REDIS_ADDRESS}", redisContainer.IpAddress + ":6379")
        );
        egressContainer = new ContainerBuilder()
            .WithImage(LIVEKIT_EGRESS_IMAGE)
            .WithName("egress")
            .WithAutoRemove(true)
            .WithBindMount(egressConfigPath, "/config.yaml")
            .WithEnvironment("EGRESS_CONFIG_FILE", "/config.yaml")
            .DependsOn(redisContainer)
            .DependsOn(livekitServerContainer)
            .Build();
        // Ingress
        var ingressConfigPath = GetTempFilePathWithExtension(".yaml");
        File.WriteAllText(
            ingressConfigPath,
            ingressYaml
                .Replace("{WS_URL}", "ws://" + livekitServerContainer.IpAddress + ":7880")
                .Replace("{REDIS_ADDRESS}", redisContainer.IpAddress + ":6379")
        );
        ingressContainer = new ContainerBuilder()
            .WithImage(LIVEKIT_INGRESS_IMAGE)
            .WithName("ingress")
            .WithAutoRemove(true)
            .WithBindMount(ingressConfigPath, "/config.yaml")
            .WithEnvironment("INGRESS_CONFIG_FILE", "/config.yaml")
            .DependsOn(redisContainer)
            .DependsOn(livekitServerContainer)
            .Build();
        Task.WaitAll(egressContainer.StartAsync(), ingressContainer.StartAsync());
    }

    public void Dispose()
    {
        var tasks = new List<Task>();
        if (redisContainer != null)
        {
            tasks.Add(redisContainer.DisposeAsync().AsTask());
        }
        if (livekitServerContainer != null)
        {
            tasks.Add(livekitServerContainer.DisposeAsync().AsTask());
        }
        if (egressContainer != null)
        {
            tasks.Add(egressContainer.DisposeAsync().AsTask());
        }
        if (ingressContainer != null)
        {
            tasks.Add(ingressContainer.DisposeAsync().AsTask());
        }
        Task.WhenAll(tasks).Wait();
    }

    public async Task JoinParticipant(string roomName, string participantIdentity)
    {
        await RunLivekitCliCommand(
            ["room", "join", "--identity", participantIdentity, roomName],
            Wait.ForUnixContainer().UntilMessageIsLogged(".*connected to room.*" + roomName + ".*")
        );
    }

    public async Task PublishVideoTrackInRoom(
        RoomServiceClient client,
        string roomName,
        string participantIdentity
    )
    {
        await RunLivekitCliCommand(
            ["room", "join", "--identity", participantIdentity, "--publish-demo", roomName],
            Wait.ForUnixContainer().UntilMessageIsLogged(".*published simulcast track.*")
        );
        // Wait for participant to have tracks up to 10 seconds
        ParticipantInfo participant = null;
        var timeout = DateTime.Now.AddSeconds(10);
        while ((participant == null || participant.Tracks.Count == 0) && DateTime.Now < timeout)
        {
            participant = await client.GetParticipant(
                new RoomParticipantIdentity { Room = roomName, Identity = participantIdentity }
            );
            if (participant.Tracks.Count == 0)
            {
                await Task.Delay(500);
            }
        }
        if (participant.Tracks.Count == 0)
        {
            Assert.Fail("Participant has no tracks");
        }
    }

    private async Task RunLivekitCliCommand(
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
            .WithImage(LIVEKIT_CLI_IMAGE)
            .WithAutoRemove(true)
            .DependsOn(livekitServerContainer)
            .WithCommand(commands);
        if (waitForContainerOS != null)
        {
            lkContainerBuilder = lkContainerBuilder.WithWaitStrategy(waitForContainerOS);
        }
        var livekitClientContainer = lkContainerBuilder.Build();
        await livekitClientContainer.StartAsync();
        return;
    }

    private string GetTempFilePathWithExtension(string extension)
    {
        var path = Path.GetTempPath();
        var fileName = Path.ChangeExtension(Guid.NewGuid().ToString(), extension);
        return Path.Combine(path, fileName);
    }
}

[CollectionDefinition("Integration tests")]
public class IntegrationTestsCollection : ICollectionFixture<ServiceClientFixture>
{
    // This class has no code, and is never created.
    // Its purpose is to be the place to apply [CollectionDefinition]
    // and all the ICollectionFixture<> interfaces.
}
