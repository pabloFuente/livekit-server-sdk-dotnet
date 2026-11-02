using Newtonsoft.Json.Linq;

namespace Livekit.Server.Sdk.Dotnet.Test
{
    /// <summary>
    /// Unit tests of the service clients that only need <see cref="TestHttpMessageHandler"/>. They
    /// deliberately live outside the "Integration tests" collection: xUnit builds
    /// <see cref="ServiceClientFixture"/> (and starts Docker) for every class in that collection,
    /// even when the filter selects only its unit tests.
    /// </summary>
    public class ServiceClientUnitTest
    {
        const string TEST_SECRET = "secretsecretsecretsecretsecretsecret";

        [Theory]
        [Trait("Category", "Unit")]
        [InlineData("Room")]
        [InlineData("AgentDispatch")]
        [InlineData("Egress")]
        [InlineData("Ingress")]
        [InlineData("Sip")]
        public async Task Constructor_UsesCustomHttpClient_HeaderIsSent(string service)
        {
            var handler = new TestHttpMessageHandler();
            var client = new HttpClient(handler);
            client.DefaultRequestHeaders.Add("X-Test-Header", "test-value");
            var randomHeaderValue = "random-value-" + Guid.NewGuid().ToString();
            client.DefaultRequestHeaders.Add("X-Test-Random-Out", randomHeaderValue);

            Task call = service switch
            {
                "Room" => new RoomServiceClient(
                    "http://localhost",
                    "key",
                    TEST_SECRET,
                    client
                ).ListRooms(new ListRoomsRequest()),
                "AgentDispatch" => new AgentDispatchServiceClient(
                    "http://localhost",
                    "key",
                    TEST_SECRET,
                    client
                ).ListDispatch(new ListAgentDispatchRequest()),
                "Egress" => new EgressServiceClient(
                    "http://localhost",
                    "key",
                    TEST_SECRET,
                    client
                ).ListEgress(new ListEgressRequest()),
                "Ingress" => new IngressServiceClient(
                    "http://localhost",
                    "key",
                    TEST_SECRET,
                    client
                ).ListIngress(new ListIngressRequest()),
                "Sip" => new SipServiceClient(
                    "http://localhost",
                    "key",
                    TEST_SECRET,
                    client
                ).ListSIPInboundTrunk(new ListSIPInboundTrunkRequest()),
                _ => throw new ArgumentException("Unknown service " + service),
            };
            await call;

            // The custom header is present in the outgoing request
            Assert.NotNull(handler.LastRequest);
            Assert.True(handler.LastRequest.Headers.Contains("X-Test-Header"));
            Assert.Equal(
                "test-value",
                handler.LastRequest.Headers.GetValues("X-Test-Header").First()
            );

            // The handler's response contains the marker header and echo header
            Assert.NotNull(handler.LastResponse);
            Assert.True(handler.LastResponse.Headers.Contains("X-Test-Handler"));
            Assert.Equal(
                "CustomHttpClientUsed",
                handler.LastResponse.Headers.GetValues("X-Test-Handler").First()
            );
            Assert.True(handler.LastResponse.Headers.Contains("X-Test-Random-In"));
            Assert.Equal(
                randomHeaderValue,
                handler.LastResponse.Headers.GetValues("X-Test-Random-In").First()
            );
        }

        [Theory]
        [Trait("Category", "Unit")]
        [InlineData(
            "http://localhost/my-subpath",
            "/my-subpath/twirp/livekit.RoomService/ListRooms"
        )]
        [InlineData(
            "http://localhost/my-subpath/",
            "/my-subpath/twirp/livekit.RoomService/ListRooms"
        )]
        [InlineData("http://localhost", "/twirp/livekit.RoomService/ListRooms")]
        public async Task Constructor_HostWithSubpath_KeepsSubpathInRoute(
            string host,
            string expectedPath
        )
        {
            var handler = new TestHttpMessageHandler();
            var service = new RoomServiceClient(host, "key", TEST_SECRET, new HttpClient(handler));

            await service.ListRooms(new ListRoomsRequest());

            Assert.NotNull(handler.LastRequest?.RequestUri);
            Assert.Equal(expectedPath, handler.LastRequest.RequestUri.AbsolutePath);
        }

        [Fact]
        [Trait("Category", "Unit")]
        public async Task GetDispatch_FiltersListDispatch_ReturnsNullWhenEmpty()
        {
            var handler = new TestHttpMessageHandler();
            var service = new AgentDispatchServiceClient(
                "http://localhost",
                "key",
                TEST_SECRET,
                new HttpClient(handler)
            );

            var dispatch = await service.GetDispatch("AD_test", TestConstants.ROOM_NAME);

            Assert.Null(dispatch);
            Assert.NotNull(handler.LastRequest?.RequestUri);
            Assert.Equal(
                "/twirp/livekit.AgentDispatchService/ListDispatch",
                handler.LastRequest.RequestUri.AbsolutePath
            );
            var sentRequest = ListAgentDispatchRequest.Parser.ParseFrom(handler.LastRequestBody);
            Assert.Equal("AD_test", sentRequest.DispatchId);
            Assert.Equal(TestConstants.ROOM_NAME, sentRequest.Room);
            var videoGrant = handler.LastVideoGrant();
            Assert.True(videoGrant.SelectToken("roomAdmin")?.Value<bool>());
            Assert.Equal(TestConstants.ROOM_NAME, videoGrant.SelectToken("room")?.Value<string>());
        }
    }
}
