using System.Net;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Configurations;
using DotNet.Testcontainers.Containers;
using DotNet.Testcontainers.Networks;
using Livekit.Server.Sdk.Dotnet;

namespace LiveKit.Rtc.Tests;

/// <summary>
/// Test fixture that provides LiveKit server and Chrome containers for e2e tests.
/// </summary>
public class RtcTestFixture : IAsyncLifetime
{
    public const string API_KEY = "devkey";
    public const string API_SECRET = "secretsecretsecretsecretsecretsecretsecret";

    private const string LIVEKIT_SERVER_IMAGE = "livekit/livekit-server:latest";
    private const string REDIS_IMAGE = "redis:latest";
    private const string CHROME_IMAGE = "selenium/standalone-chrome:latest";
    private const string HTTP_SERVER_IMAGE = "nginx:alpine";

    private INetwork? _network;
    private IContainer? _redisContainer;
    private IContainer? _livekitServerContainer;
    private IContainer? _chromeContainer;
    private IContainer? _httpServerContainer;

    /// <summary>
    /// The LiveKit server URL for clients outside the container network.
    /// </summary>
    public string LiveKitUrl { get; private set; } = string.Empty;

    /// <summary>
    /// The LiveKit server URL for clients inside the container network.
    /// </summary>
    public string LiveKitInternalUrl { get; private set; } = string.Empty;

    /// <summary>
    /// The Chrome WebDriver URL.
    /// </summary>
    public string ChromeUrl { get; private set; } = string.Empty;

    /// <summary>
    /// The path to the web test files.
    /// </summary>
    public string WebFilesPath { get; private set; } = string.Empty;

    /// <summary>
    /// The internal URL for the web files (accessible from Chrome container).
    /// </summary>
    public string WebFilesInternalUrl { get; private set; } = string.Empty;

    /// <summary>
    /// Whether to use a local Chrome instance instead of a Docker container.
    /// Controlled by the USE_LOCAL_CHROME environment variable.
    /// </summary>
    public bool UseLocalChrome { get; private set; }

    public async Task InitializeAsync()
    {
        // Check if we should use local Chrome instead of Docker
        UseLocalChrome =
            Environment.GetEnvironmentVariable("USE_LOCAL_CHROME")?.ToLowerInvariant() == "true";

        // Set web files path first (needed for HTTP server mount)
        WebFilesPath = Path.Combine(AppContext.BaseDirectory, "web");

        // Create a shared network
        _network = new NetworkBuilder().WithName($"livekit-test-{Guid.NewGuid():N}").Build();
        await _network.CreateAsync();

        // Redis
        _redisContainer = new ContainerBuilder(REDIS_IMAGE)
            .WithName($"redis-{Guid.NewGuid():N}")
            .WithNetwork(_network)
            .WithNetworkAliases("redis")
            .WithPortBinding(6379, true)
            .WithWaitStrategy(
                Wait.ForUnixContainer().UntilMessageIsLogged("Ready to accept connections")
            )
            .Build();
        await _redisContainer.StartAsync();

        // LiveKit server
        _livekitServerContainer = new ContainerBuilder(LIVEKIT_SERVER_IMAGE)
            .WithName($"livekit-{Guid.NewGuid():N}")
            .WithNetwork(_network)
            .WithNetworkAliases("livekit")
            .WithEnvironment("LIVEKIT_KEYS", $"{API_KEY}: {API_SECRET}")
            .WithEnvironment("LIVEKIT_REDIS_ADDRESS", "redis:6379")
            .WithPortBinding(7880, true)
            .WithPortBinding(7881, true)
            .WithWaitStrategy(
                Wait.ForUnixContainer()
                    .UntilHttpRequestIsSucceeded(r =>
                        r.ForPort(7880).ForPath("/").ForStatusCode(HttpStatusCode.OK)
                    )
            )
            .Build();
        await _livekitServerContainer.StartAsync();

        var livekitHost = _livekitServerContainer.Hostname;
        var livekitPort = _livekitServerContainer.GetMappedPublicPort(7880);
        LiveKitUrl = $"ws://{livekitHost}:{livekitPort}";
        LiveKitInternalUrl = "ws://livekit:7880";

        // HTTP server for web files (nginx)
        _httpServerContainer = new ContainerBuilder(HTTP_SERVER_IMAGE)
            .WithName($"http-server-{Guid.NewGuid():N}")
            .WithNetwork(_network)
            .WithNetworkAliases("webserver")
            .WithPortBinding(80, true)
            .WithBindMount(WebFilesPath, "/usr/share/nginx/html", AccessMode.ReadOnly)
            .WithWaitStrategy(
                Wait.ForUnixContainer().UntilHttpRequestIsSucceeded(r => r.ForPort(80).ForPath("/"))
            )
            .Build();
        await _httpServerContainer.StartAsync();

        if (UseLocalChrome)
        {
            // When using local Chrome, we need to use the exposed host port for web files
            var httpHost = _httpServerContainer.Hostname;
            var httpPort = _httpServerContainer.GetMappedPublicPort(80);
            WebFilesInternalUrl = $"http://{httpHost}:{httpPort}";
        }
        else
        {
            // When using containerized Chrome, use internal network URL
            WebFilesInternalUrl = "http://webserver";
        }

        // Chrome with Selenium
        if (UseLocalChrome)
        {
            // Use local Chrome with ChromeDriver (assumes ChromeDriver is running on localhost:9515)
            // Or just use local Chrome directly - ChromeUrl will be null to indicate local driver
            ChromeUrl = string.Empty; // Empty string indicates use local ChromeDriver
        }
        else
        {
            // Use containerized Chrome
            _chromeContainer = new ContainerBuilder(CHROME_IMAGE)
                .WithName($"chrome-{Guid.NewGuid():N}")
                .WithNetwork(_network)
                .WithNetworkAliases("chrome")
                .WithPortBinding(4444, true)
                .WithPortBinding(7900, true) // noVNC for debugging
                .WithEnvironment("SE_NODE_MAX_SESSIONS", "4")
                .WithEnvironment("SE_VNC_NO_PASSWORD", "1")
                .WithWaitStrategy(
                    Wait.ForUnixContainer().UntilMessageIsLogged("Started Selenium Standalone")
                )
                .Build();
            await _chromeContainer.StartAsync();

            var chromeHost = _chromeContainer.Hostname;
            var chromePort = _chromeContainer.GetMappedPublicPort(4444);
            ChromeUrl = $"http://{chromeHost}:{chromePort}/wd/hub";
        }
    }

    public async Task DisposeAsync()
    {
        var disposeTasks = new List<Task>();

        if (_chromeContainer != null)
            disposeTasks.Add(_chromeContainer.DisposeAsync().AsTask());
        if (_httpServerContainer != null)
            disposeTasks.Add(_httpServerContainer.DisposeAsync().AsTask());
        if (_livekitServerContainer != null)
            disposeTasks.Add(_livekitServerContainer.DisposeAsync().AsTask());
        if (_redisContainer != null)
            disposeTasks.Add(_redisContainer.DisposeAsync().AsTask());

        await Task.WhenAll(disposeTasks);

        if (_network != null)
            await _network.DisposeAsync();
    }

    /// <summary>
    /// Creates an access token for a participant.
    /// </summary>
    public string CreateToken(string identity, string roomName)
    {
        return new AccessToken(API_KEY, API_SECRET)
            .WithIdentity(identity)
            .WithName(identity)
            .WithGrants(new VideoGrants { RoomJoin = true, Room = roomName })
            .ToJwt();
    }
}

/// <summary>
/// Collection definition for RTC tests.
/// </summary>
[CollectionDefinition("RtcTests")]
public class RtcTestCollection : ICollectionFixture<RtcTestFixture> { }
