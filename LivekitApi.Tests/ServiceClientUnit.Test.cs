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

        // Forwarding and moving are LiveKit Cloud-only, so their integration tests are skipped:
        // this is what checks them
        [Theory]
        [Trait("Category", "Unit")]
        [InlineData("ForwardParticipant")]
        [InlineData("MoveParticipant")]
        public async Task ForwardAndMoveParticipant_SendDestinationRoomGrant(string method)
        {
            var handler = new TestHttpMessageHandler();
            var service = new RoomServiceClient(
                "http://localhost",
                "key",
                TEST_SECRET,
                new HttpClient(handler)
            );

            Task call = method switch
            {
                "ForwardParticipant" => service.ForwardParticipant(
                    new ForwardParticipantRequest
                    {
                        Room = "source-room",
                        Identity = "participant",
                        DestinationRoom = "destination-room",
                    }
                ),
                "MoveParticipant" => service.MoveParticipant(
                    new MoveParticipantRequest
                    {
                        Room = "source-room",
                        Identity = "participant",
                        DestinationRoom = "destination-room",
                    }
                ),
                _ => throw new ArgumentException("Unknown method " + method),
            };
            await call;

            Assert.NotNull(handler.LastRequest?.RequestUri);
            Assert.Equal(
                "/twirp/livekit.RoomService/" + method,
                handler.LastRequest.RequestUri.AbsolutePath
            );
            var videoGrant = handler.LastVideoGrant();
            Assert.True(videoGrant.SelectToken("roomAdmin")?.Value<bool>());
            Assert.Equal("source-room", videoGrant.SelectToken("room")?.Value<string>());
            Assert.Equal(
                "destination-room",
                videoGrant.SelectToken("destinationRoom")?.Value<string>()
            );
        }

        [Fact]
        [Trait("Category", "Unit")]
        public async Task DeleteDispatch_SendsRoomAdminGrantOfTheRoom()
        {
            var handler = new TestHttpMessageHandler();
            var service = new AgentDispatchServiceClient(
                "http://localhost",
                "key",
                TEST_SECRET,
                new HttpClient(handler)
            );

            await service.DeleteDispatch(
                new DeleteAgentDispatchRequest
                {
                    DispatchId = "AD_test",
                    Room = TestConstants.ROOM_NAME,
                }
            );

            Assert.NotNull(handler.LastRequest?.RequestUri);
            Assert.Equal(
                "/twirp/livekit.AgentDispatchService/DeleteDispatch",
                handler.LastRequest.RequestUri.AbsolutePath
            );
            var sentRequest = DeleteAgentDispatchRequest.Parser.ParseFrom(handler.LastRequestBody);
            Assert.Equal("AD_test", sentRequest.DispatchId);
            var videoGrant = handler.LastVideoGrant();
            Assert.True(videoGrant.SelectToken("roomAdmin")?.Value<bool>());
            Assert.Equal(TestConstants.ROOM_NAME, videoGrant.SelectToken("room")?.Value<string>());
        }

        // Needs a running stream egress, which the integration stack does not have
        [Fact]
        [Trait("Category", "Unit")]
        public async Task UpdateStream_SendsRoomRecordGrant()
        {
            var handler = new TestHttpMessageHandler();
            var service = new EgressServiceClient(
                "http://localhost",
                "key",
                TEST_SECRET,
                new HttpClient(handler)
            );

            await service.UpdateStream(
                new UpdateStreamRequest
                {
                    EgressId = "EG_test",
                    AddOutputUrls = { "rtmp://localhost/live/stream" },
                }
            );

            Assert.NotNull(handler.LastRequest?.RequestUri);
            Assert.Equal(
                "/twirp/livekit.Egress/UpdateStream",
                handler.LastRequest.RequestUri.AbsolutePath
            );
            var sentRequest = UpdateStreamRequest.Parser.ParseFrom(handler.LastRequestBody);
            Assert.Equal("EG_test", sentRequest.EgressId);
            Assert.True(handler.LastVideoGrant().SelectToken("roomRecord")?.Value<bool>());
        }

        [Fact]
        [Trait("Category", "Unit")]
        public async Task ListSIPTrunk_SendsSipAdminGrant()
        {
            var handler = new TestHttpMessageHandler();
            var service = new SipServiceClient(
                "http://localhost",
                "key",
                TEST_SECRET,
                new HttpClient(handler)
            );

#pragma warning disable CS0618 // Deprecated upstream, still part of the public API
            await service.ListSIPTrunk(new ListSIPTrunkRequest());
#pragma warning restore CS0618

            Assert.NotNull(handler.LastRequest?.RequestUri);
            Assert.Equal(
                "/twirp/livekit.SIP/ListSIPTrunk",
                handler.LastRequest.RequestUri.AbsolutePath
            );
            Assert.True(handler.LastSipGrant().SelectToken("admin")?.Value<bool>());
        }
    }
}
