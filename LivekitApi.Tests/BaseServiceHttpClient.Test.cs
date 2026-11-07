using System.Collections.Concurrent;
using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Headers;
using Newtonsoft.Json.Linq;

namespace Livekit.Server.Sdk.Dotnet.Test
{
    /// <summary>
    /// Regression tests for how the service clients use a caller-supplied HttpClient: it must not
    /// be modified (it may be shared or already in use) and concurrent calls must each carry the
    /// token signed for them.
    /// </summary>
    public class BaseServiceHttpClientTest
    {
        const string TEST_KEY = "API87mWmmh7KM3V";
        const string TEST_SECRET = "helOnxxeT71NeOEBcYm3kW0s1pofQAbitubCw7AIsY0A";

        private sealed class RecordedRequest
        {
            public Uri Uri { get; set; } = null!;
            public string? BearerToken { get; set; }
            public string UserAgent { get; set; } = "";
        }

        /// <summary>
        /// Records every request and, when <see cref="HoldUntil"/> is set, keeps all of them in
        /// flight until that many have arrived, so concurrent calls really overlap.
        /// </summary>
        private sealed class RecordingHandler : HttpMessageHandler
        {
            public ConcurrentQueue<RecordedRequest> Requests { get; } = new();
            public int HoldUntil { get; set; }
            private int _arrived;
            private readonly TaskCompletionSource<bool> _gate = new(
                TaskCreationOptions.RunContinuationsAsynchronously
            );

            protected override async Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request,
                CancellationToken cancellationToken
            )
            {
                Requests.Enqueue(
                    new RecordedRequest
                    {
                        Uri = request.RequestUri!,
                        BearerToken = request.Headers.Authorization?.Parameter,
                        UserAgent = request.Headers.UserAgent.ToString(),
                    }
                );
                if (HoldUntil > 0)
                {
                    if (Interlocked.Increment(ref _arrived) >= HoldUntil)
                        _gate.TrySetResult(true);
                    await _gate.Task.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);
                }
                var content = new ByteArrayContent(Array.Empty<byte>());
                content.Headers.ContentType = new MediaTypeHeaderValue("application/protobuf");
                return new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = content };
            }
        }

        private static JObject VideoGrant(string? bearerToken)
        {
            var claim = new JwtSecurityTokenHandler()
                .ReadJwtToken(bearerToken)
                .Claims.First(c => c.Type == "video")
                .Value;
            return JObject.Parse(claim);
        }

        [Fact]
        [Trait("Category", "Unit")]
        public async Task SharedHttpClient_IsNotModified_AndEachClientUsesItsOwnHost()
        {
            var handler = new RecordingHandler();
            using var http = new HttpClient(handler);
            var rooms = new RoomServiceClient("http://host-a:7880", TEST_KEY, TEST_SECRET, http);
            var egress = new EgressServiceClient(
                "http://host-b:7880/livekit",
                TEST_KEY,
                TEST_SECRET,
                http
            );

            await rooms.ListRooms(new ListRoomsRequest());
            await egress.ListEgress(new ListEgressRequest());

            var requests = handler.Requests.ToArray();
            Assert.Equal(
                "http://host-a:7880/twirp/livekit.RoomService/ListRooms",
                requests[0].Uri.ToString()
            );
            Assert.Equal(
                "http://host-b:7880/livekit/twirp/livekit.Egress/ListEgress",
                requests[1].Uri.ToString()
            );
            Assert.True(VideoGrant(requests[0].BearerToken)["roomList"]!.Value<bool>());

            // The caller's client is left exactly as it was handed over
            Assert.Null(http.BaseAddress);
            Assert.Null(http.DefaultRequestHeaders.Authorization);
            Assert.Empty(http.DefaultRequestHeaders.UserAgent);
        }

        [Fact]
        [Trait("Category", "Unit")]
        public async Task HttpClient_ThatAlreadySentRequests_CanBeUsed()
        {
            var handler = new RecordingHandler();
            using var http = new HttpClient(handler);
            // Setting BaseAddress on a client that already sent a request throws; the SDK must not
            await http.GetAsync("http://elsewhere.invalid/health");

            var rooms = new RoomServiceClient("http://host-a:7880", TEST_KEY, TEST_SECRET, http);
            await rooms.ListRooms(new ListRoomsRequest());

            Assert.Equal(
                "http://host-a:7880/twirp/livekit.RoomService/ListRooms",
                handler.Requests.Last().Uri.ToString()
            );
        }

        [Fact]
        [Trait("Category", "Unit")]
        public async Task ConcurrentCalls_OnTheSameClient_SendTheirOwnToken()
        {
            var handler = new RecordingHandler { HoldUntil = 2 };
            using var http = new HttpClient(handler);
            var rooms = new RoomServiceClient("http://host-a:7880", TEST_KEY, TEST_SECRET, http);

            var list = Task.Run(() => rooms.ListRooms(new ListRoomsRequest()));
            var create = Task.Run(() => rooms.CreateRoom(new CreateRoomRequest { Name = "room" }));
            await Task.WhenAll(list, create);

            var byRoute = handler.Requests.ToDictionary(r => r.Uri.Segments.Last());
            Assert.True(VideoGrant(byRoute["ListRooms"].BearerToken)["roomList"]!.Value<bool>());
            Assert.False(VideoGrant(byRoute["ListRooms"].BearerToken)["roomCreate"]!.Value<bool>());
            Assert.True(VideoGrant(byRoute["CreateRoom"].BearerToken)["roomCreate"]!.Value<bool>());
            Assert.False(VideoGrant(byRoute["CreateRoom"].BearerToken)["roomList"]!.Value<bool>());
        }

        [Fact]
        [Trait("Category", "Unit")]
        public async Task ServiceClients_FallBackToEnvironmentVariables()
        {
            var previousKey = Environment.GetEnvironmentVariable("LIVEKIT_API_KEY");
            var previousSecret = Environment.GetEnvironmentVariable("LIVEKIT_API_SECRET");
            Environment.SetEnvironmentVariable("LIVEKIT_API_KEY", TEST_KEY);
            Environment.SetEnvironmentVariable("LIVEKIT_API_SECRET", TEST_SECRET);
            try
            {
                var handler = new RecordingHandler();
                using var http = new HttpClient(handler);
                var rooms = new RoomServiceClient("http://host-a:7880", client: http);
                await rooms.ListRooms(new ListRoomsRequest());

                var token = new JwtSecurityTokenHandler().ReadJwtToken(
                    handler.Requests.Single().BearerToken
                );
                Assert.Equal(TEST_KEY, token.Issuer);
            }
            finally
            {
                Environment.SetEnvironmentVariable("LIVEKIT_API_KEY", previousKey);
                Environment.SetEnvironmentVariable("LIVEKIT_API_SECRET", previousSecret);
            }
        }

        [Fact]
        [Trait("Category", "Unit")]
        public async Task EveryRequest_CarriesTheSdkUserAgent_AfterTheCallersOwn()
        {
            var handler = new RecordingHandler();
            using var http = new HttpClient(handler);
            http.DefaultRequestHeaders.UserAgent.ParseAdd("MyApp/1.0");
            var rooms = new RoomServiceClient("http://host-a:7880", TEST_KEY, TEST_SECRET, http);
            var ingress = new IngressServiceClient(
                "http://host-a:7880",
                TEST_KEY,
                TEST_SECRET,
                http
            );

            await rooms.ListRooms(new ListRoomsRequest());
            await ingress.ListIngress(new ListIngressRequest());

            var version = typeof(RoomServiceClient).Assembly.GetName().Version!.ToString(3);
            Assert.All(
                handler.Requests,
                r => Assert.Equal($"MyApp/1.0 livekit-server-sdk-dotnet/{version}", r.UserAgent)
            );
        }

        [Fact]
        [Trait("Category", "Unit")]
        public async Task WebSocketSchemes_AreConvertedToHttp()
        {
            var handler = new RecordingHandler();
            using var http = new HttpClient(handler);
            var secure = new RoomServiceClient("wss://host-a/sub", TEST_KEY, TEST_SECRET, http);
            var plain = new RoomServiceClient("ws://host-b:7880", TEST_KEY, TEST_SECRET, http);

            await secure.ListRooms(new ListRoomsRequest());
            await plain.ListRooms(new ListRoomsRequest());

            var requests = handler.Requests.ToArray();
            Assert.Equal(
                "https://host-a/sub/twirp/livekit.RoomService/ListRooms",
                requests[0].Uri.ToString()
            );
            Assert.Equal(
                "http://host-b:7880/twirp/livekit.RoomService/ListRooms",
                requests[1].Uri.ToString()
            );
        }

        private sealed class ErrorHandler : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request,
                CancellationToken cancellationToken
            )
            {
                var response = new HttpResponseMessage(System.Net.HttpStatusCode.NotFound)
                {
                    Content = new StringContent(
                        "{\"code\":\"not_found\",\"msg\":\"room \\\"abc\\\" not found\",\"meta\":{\"sip_status_code\":\"486\"}}",
                        System.Text.Encoding.UTF8,
                        "application/json"
                    ),
                };
                return Task.FromResult(response);
            }
        }

        [Fact]
        [Trait("Category", "Unit")]
        public async Task TwirpErrors_KeepCodeMessageMetaAndStatus()
        {
            using var http = new HttpClient(new ErrorHandler());
            var rooms = new RoomServiceClient("http://host-a:7880", TEST_KEY, TEST_SECRET, http);

            // Still a Twirp.Exception, so existing catch blocks keep working
            var error = await Assert.ThrowsAsync<TwirpException>(() =>
                rooms.ListRooms(new ListRoomsRequest())
            );
            Assert.Equal(Twirp.ErrorCode.Not_Found, error.Type);
            Assert.Equal("room \"abc\" not found", error.Message);
            Assert.Equal(404, error.Status);
            Assert.Equal("486", error.Meta["sip_status_code"]);
            Assert.IsAssignableFrom<Twirp.Exception>(error);
        }

        [Fact]
        [Trait("Category", "Unit")]
        public void ServiceClients_RejectMissingOrShortCredentials()
        {
            // Empty strings, not null, so that LIVEKIT_API_KEY/LIVEKIT_API_SECRET are not read
            Assert.Throws<ArgumentException>(() =>
                new RoomServiceClient("http://host-a:7880", "", TEST_SECRET)
            );
            Assert.Throws<ArgumentException>(() =>
                new RoomServiceClient("http://host-a:7880", TEST_KEY, "")
            );
            var ex = Assert.Throws<ArgumentException>(() =>
                new RoomServiceClient("http://host-a:7880", TEST_KEY, new string('s', 31))
            );
            Assert.Contains("at least 256 bits", ex.Message);
        }

        private sealed class FixedResponseHandler : HttpMessageHandler
        {
            private readonly int _status;
            private readonly string _body;

            public FixedResponseHandler(int status, string body)
            {
                _status = status;
                _body = body;
            }

            protected override Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request,
                CancellationToken cancellationToken
            ) =>
                Task.FromResult(
                    new HttpResponseMessage((System.Net.HttpStatusCode)_status)
                    {
                        Content = new StringContent(_body),
                    }
                );
        }

        [Theory]
        [Trait("Category", "Unit")]
        // The error page of a proxy in front of the server: the raw body is the message
        [InlineData(
            502,
            "<html><body>Bad Gateway</body></html>",
            "<html><body>Bad Gateway</body></html>"
        )]
        // An empty body
        [InlineData(503, "", "")]
        // A Twirp code this version of the SDK does not know
        [InlineData(500, "{\"code\":\"brand_new_code\",\"msg\":\"boom\"}", "boom")]
        public async Task ErrorsThatAreNotKnownTwirpErrors_KeepStatusAndMessage(
            int status,
            string body,
            string message
        )
        {
            using var http = new HttpClient(new FixedResponseHandler(status, body));
            var rooms = new RoomServiceClient("http://host-a:7880", TEST_KEY, TEST_SECRET, http);

            var error = await Assert.ThrowsAsync<TwirpException>(() =>
                rooms.ListRooms(new ListRoomsRequest())
            );
            Assert.Equal(Twirp.ErrorCode.Unknown, error.Type);
            Assert.Equal(message, error.Message);
            Assert.Equal(status, error.Status);
            Assert.Empty(error.Meta);
        }

        private sealed class NeverAnsweringHandler : HttpMessageHandler
        {
            protected override async Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request,
                CancellationToken cancellationToken
            )
            {
                await Task.Delay(Timeout.Infinite, cancellationToken);
                throw new InvalidOperationException("unreachable");
            }
        }

        [Fact]
        [Trait("Category", "Unit")]
        public async Task TimeoutOfTheCallersHttpClient_EndsTheCall()
        {
            // The client methods take no CancellationToken: the Timeout of a caller-supplied
            // HttpClient is what bounds a call, and it must surface as-is, not as a TwirpException
            using var http = new HttpClient(new NeverAnsweringHandler())
            {
                Timeout = TimeSpan.FromMilliseconds(200),
            };
            var rooms = new RoomServiceClient("http://host-a:7880", TEST_KEY, TEST_SECRET, http);

            await Assert.ThrowsAsync<TaskCanceledException>(() =>
                rooms.ListRooms(new ListRoomsRequest())
            );
        }
    }
}
