using Livekit.Server.Sdk.Dotnet;

var builder = WebApplication.CreateBuilder(args);
var MyAllowSpecificOrigins = "_myAllowSpecificOrigins";

IConfiguration config = new ConfigurationBuilder()
    .SetBasePath(Directory.GetCurrentDirectory())
    .AddJsonFile("appsettings.json")
    .AddEnvironmentVariables()
    .Build();

// Load env variables
var SERVER_PORT = config.GetValue<int>("SERVER_PORT");
var LIVEKIT_URL = config.GetValue<string>("LIVEKIT_URL");
var LIVEKIT_API_KEY = config.GetValue<string>("LIVEKIT_API_KEY");
var LIVEKIT_API_SECRET = config.GetValue<string>("LIVEKIT_API_SECRET");

// Enable CORS support
builder.Services.AddCors(options =>
{
    options.AddPolicy(
        name: MyAllowSpecificOrigins,
        builder =>
        {
            builder.WithOrigins("*").AllowAnyHeader();
        }
    );
});

builder.WebHost.UseKestrel(serverOptions =>
{
    serverOptions.ListenAnyIP(SERVER_PORT);
});

var app = builder.Build();
app.UseCors(MyAllowSpecificOrigins);

// Initialize WebhookReceiver
WebhookReceiver webhookReceiver = new WebhookReceiver(LIVEKIT_API_KEY, LIVEKIT_API_SECRET);

// Initializar RoomServiceClient
RoomServiceClient roomServiceClient = new RoomServiceClient(
    LIVEKIT_URL,
    LIVEKIT_API_KEY,
    LIVEKIT_API_SECRET
);

// Inititalize EgressServiceClient
EgressServiceClient egressServiceClient = new EgressServiceClient(
    LIVEKIT_URL,
    LIVEKIT_API_KEY,
    LIVEKIT_API_SECRET
);

// Initialize IngressServiceClient
IngressServiceClient ingressServiceClient = new IngressServiceClient(
    LIVEKIT_URL,
    LIVEKIT_API_KEY,
    LIVEKIT_API_SECRET
);

// Initialize SipServiceClient
SipServiceClient sipServiceClient = new SipServiceClient(
    LIVEKIT_URL,
    LIVEKIT_API_KEY,
    LIVEKIT_API_SECRET
);

// Access Token generator
app.MapGet(
    "/livekit/token",
    (HttpRequest request) =>
    {
        var user = request.Query["user"];
        var room = request.Query["room"];

        var token = new AccessToken(LIVEKIT_API_KEY, LIVEKIT_API_SECRET);
        token
            .WithGrants(
                new VideoGrants
                {
                    RoomJoin = true,
                    Room = room,
                    CanPublish = true,
                    CanSubscribe = true,
                    CanUpdateOwnMetadata = true,
                }
            )
            .WithTtl(TimeSpan.FromHours(1))
            .WithIdentity(user)
            .WithName(user)
            .WithAttributes(new Dictionary<string, string> { { "mykey", "myvalue" } });

        Console.WriteLine("Token generated for user " + user + " of room " + room);

        return Results.Ok(token.ToJwt());
    }
);

// LiveKit API usage example: returns the list of rooms, egresses and ingresses
app.MapGet(
    "/livekit/api",
    async (HttpRequest request) =>
    {
        var listRoomsResponse = await roomServiceClient.ListRooms(new ListRoomsRequest());
        Room[] rooms = listRoomsResponse.Rooms.ToArray();

        var listEgressResponse = await egressServiceClient.ListEgress(new ListEgressRequest());
        EgressInfo[] egresses = listEgressResponse.Items.ToArray();

        var listIngressResponse = await ingressServiceClient.ListIngress(new ListIngressRequest());
        IngressInfo[] ingresses = listIngressResponse.Items.ToArray();

        var listSipInboundResponse = await sipServiceClient.ListSIPInboundTrunk(
            new ListSIPInboundTrunkRequest()
        );
        SIPInboundTrunkInfo[] sipInboundTrunks = listSipInboundResponse.Items.ToArray();
        var listSipOutboundResponse = await sipServiceClient.ListSIPOutboundTrunk(
            new ListSIPOutboundTrunkRequest()
        );
        SIPOutboundTrunkInfo[] sipOutboundTrunks = listSipOutboundResponse.Items.ToArray();
        var listSipDispatchRuleResponse = await sipServiceClient.ListSIPDispatchRule(
            new ListSIPDispatchRuleRequest()
        );
        SIPDispatchRuleInfo[] sipDispatchRules = listSipDispatchRuleResponse.Items.ToArray();
        var sipInfo = new
        {
            sipInboundTrunks,
            sipOutboundTrunks,
            sipDispatchRules,
        };

        return Results.Ok(
            new
            {
                rooms,
                egresses,
                ingresses,
                sipInfo,
            }
        );
    }
);

// Webhook handler
app.MapPost(
    "/livekit/webhook",
    async (HttpRequest request) =>
    {
        var body = new StreamReader(request.Body);
        string postData = await body.ReadToEndAsync();

        var authHeader = request.Headers["Authorization"];
        if (authHeader.Count == 0)
        {
            return Results.BadRequest("Authorization header is required");
        }
        WebhookEvent webhookEvent = webhookReceiver.Receive(postData, authHeader.First());

        // Here you have available the WebhookEvent object
        Console.Out.WriteLine(webhookEvent);

        return Results.Ok();
    }
);

app.Run();
