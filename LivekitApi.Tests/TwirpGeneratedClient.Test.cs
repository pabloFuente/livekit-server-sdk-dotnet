namespace Livekit.Server.Sdk.Dotnet.Test
{
    /// <summary>
    /// The public methods of the generated <c>Twirp</c> class, which callers can use instead of the
    /// service clients, must behave like the service clients when a call fails or is blocked on.
    /// </summary>
    public class TwirpGeneratedClientTest
    {
        private sealed class ErrorHandler : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request,
                CancellationToken cancellationToken
            ) =>
                Task.FromResult(
                    new HttpResponseMessage(System.Net.HttpStatusCode.InternalServerError)
                    {
                        Content = new StringContent(
                            "{\"code\":\"dataloss\",\"msg\":\"lost \\\"abc\\\"\",\"meta\":{\"k\":\"v\"}}"
                        ),
                    }
                );
        }

        [Fact]
        [Trait("Category", "Unit")]
        public async Task GeneratedMethods_RaiseTwirpException()
        {
            using var http = new HttpClient(new ErrorHandler())
            {
                BaseAddress = new Uri("http://host-a:7880/"),
            };

            var error = await Assert.ThrowsAsync<TwirpException>(() =>
                Twirp.ListRooms(http, new ListRoomsRequest())
            );
            // The wire code is "dataloss", without the underscore of the enum member
            Assert.Equal(Twirp.ErrorCode.Data_Loss, error.Type);
            Assert.Equal("lost \"abc\"", error.Message);
            Assert.Equal(500, error.Status);
            Assert.Equal("v", error.Meta["k"]);
        }

        /// <summary>
        /// Answers after a delay, so the caller's continuation is really scheduled. HttpClient runs
        /// the handler on the caller's thread, so it must not capture the context either.
        /// </summary>
        private sealed class DelayedHandler : HttpMessageHandler
        {
            protected override async Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request,
                CancellationToken cancellationToken
            )
            {
                await Task.Delay(50, cancellationToken).ConfigureAwait(false);
                return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(new byte[0]),
                };
            }
        }

        /// <summary>
        /// A UI-like context whose only thread is blocked by the caller: whatever is posted to it
        /// never runs.
        /// </summary>
        private sealed class BlockedContext : SynchronizationContext
        {
            public override void Post(SendOrPostCallback d, object? state) { }
        }

        [Fact]
        [Trait("Category", "Unit")]
        public void GeneratedMethods_DoNotDeadlockWhenBlockedOnFromASynchronizationContext()
        {
            using var http = new HttpClient(new DelayedHandler())
            {
                BaseAddress = new Uri("http://host-a:7880/"),
            };
            var previous = SynchronizationContext.Current;
            SynchronizationContext.SetSynchronizationContext(new BlockedContext());
            try
            {
                var call = Twirp.ListRooms(http, new ListRoomsRequest());
                // Blocking on the call is what this test is about
#pragma warning disable xUnit1031
                Assert.True(
                    call.Wait(TimeSpan.FromSeconds(10)),
                    "the call waits for a continuation posted to the blocked context"
                );
#pragma warning restore xUnit1031
            }
            finally
            {
                SynchronizationContext.SetSynchronizationContext(previous);
            }
        }
    }
}
