using Newtonsoft.Json.Linq;

namespace Livekit.Server.Sdk.Dotnet.Test
{
    [Collection("Integration tests")]
    public class AgentDispatchServiceClientTest
    {
        private readonly AgentDispatchServiceClient agentDispatchClient =
            new AgentDispatchServiceClient(
                ServiceClientFixture.TEST_HTTP_URL,
                ServiceClientFixture.TEST_API_KEY,
                ServiceClientFixture.TEST_API_SECRET
            );
        private readonly RoomServiceClient roomClient = new RoomServiceClient(
            ServiceClientFixture.TEST_HTTP_URL,
            ServiceClientFixture.TEST_API_KEY,
            ServiceClientFixture.TEST_API_SECRET
        );

        [Fact]
        async Task Constructor_UsesCustomHttpClient_HeaderIsSent()
        {
            var handler = new TestHttpMessageHandler();
            var client = new HttpClient(handler);
            client.DefaultRequestHeaders.Add("X-Test-Header", "test-value");
            var randomHeaderValue = "random-value-" + Guid.NewGuid().ToString();
            client.DefaultRequestHeaders.Add("X-Test-Random-Out", randomHeaderValue);

            var service = new AgentDispatchServiceClient(
                "http://localhost",
                "key",
                "secretsecretsecretsecretsecretsecret",
                client
            );

            await service.ListDispatch(new ListAgentDispatchRequest());

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

        [Fact]
        [Trait("Category", "Unit")]
        public async Task Get_Dispatch_FiltersListDispatch_ReturnsNullWhenEmpty()
        {
            var handler = new TestHttpMessageHandler();
            var service = new AgentDispatchServiceClient(
                "http://localhost",
                "key",
                "secretsecretsecretsecretsecretsecret",
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

        [Fact]
        [Trait("Category", "Integration")]
        [Trait("Category", "AgentDispatchService")]
        public async Task Get_Dispatch()
        {
            await roomClient.CreateRoom(new CreateRoomRequest { Name = TestConstants.ROOM_NAME });
            try
            {
                var created = await agentDispatchClient.CreateDispatch(
                    new CreateAgentDispatchRequest
                    {
                        AgentName = "test-agent",
                        Room = TestConstants.ROOM_NAME,
                        Metadata = "dispatch-metadata",
                    }
                );

                var dispatch = await agentDispatchClient.GetDispatch(
                    created.Id,
                    TestConstants.ROOM_NAME
                );
                Assert.NotNull(dispatch);
                Assert.Equal(created.Id, dispatch.Id);
                Assert.Equal("test-agent", dispatch.AgentName);
                Assert.Equal("dispatch-metadata", dispatch.Metadata);

                Assert.Null(
                    await agentDispatchClient.GetDispatch("AD_unknown", TestConstants.ROOM_NAME)
                );
            }
            finally
            {
                await roomClient.DeleteRoom(
                    new DeleteRoomRequest { Room = TestConstants.ROOM_NAME }
                );
            }
        }
    }
}
