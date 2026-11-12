using Newtonsoft.Json.Linq;

namespace Livekit.Server.Sdk.Dotnet.Test
{
    // The Phone Number service is only available in LiveKit Cloud, so these tests use a mocked
    // HttpClient instead of the integration test containers.
    public class PhoneNumberServiceClientTest
    {
        [Theory]
        [Trait("Category", "Unit")]
        [InlineData("SearchPhoneNumbers")]
        [InlineData("PurchasePhoneNumber")]
        [InlineData("ListPhoneNumbers")]
        [InlineData("GetPhoneNumber")]
        [InlineData("UpdatePhoneNumber")]
        [InlineData("ReleasePhoneNumbers")]
        public async Task Method_CallsPhoneNumberServiceRoute_WithSipAdminGrant(string method)
        {
            var handler = new TestHttpMessageHandler();
            var service = new PhoneNumberServiceClient(
                "http://localhost",
                "key",
                "secretsecretsecretsecretsecretsecret",
                new HttpClient(handler)
            );

            Task call = method switch
            {
                "SearchPhoneNumbers" => service.SearchPhoneNumbers(
                    new SearchPhoneNumbersRequest { CountryCode = "US" }
                ),
                "PurchasePhoneNumber" => service.PurchasePhoneNumber(
                    new PurchasePhoneNumberRequest { PhoneNumbers = { "+14155550100" } }
                ),
                "ListPhoneNumbers" => service.ListPhoneNumbers(new ListPhoneNumbersRequest()),
                "GetPhoneNumber" => service.GetPhoneNumber(
                    new GetPhoneNumberRequest { Id = "PN_test" }
                ),
                "UpdatePhoneNumber" => service.UpdatePhoneNumber(
                    new UpdatePhoneNumberRequest { Id = "PN_test", Name = "support" }
                ),
                "ReleasePhoneNumbers" => service.ReleasePhoneNumbers(
                    new ReleasePhoneNumbersRequest { Ids = { "PN_test" } }
                ),
                _ => throw new ArgumentException("Unknown method " + method),
            };
            await call;

            Assert.NotNull(handler.LastRequest?.RequestUri);
            Assert.Equal(
                "/twirp/livekit.PhoneNumberService/" + method,
                handler.LastRequest.RequestUri.AbsolutePath
            );
            Assert.True(handler.LastSipGrant().SelectToken("admin")?.Value<bool>());
        }

        [Fact]
        [Trait("Category", "Unit")]
        public async Task PurchasePhoneNumber_SendsTheRequest()
        {
            var handler = new TestHttpMessageHandler();
            var service = new PhoneNumberServiceClient(
                "http://localhost",
                "key",
                "secretsecretsecretsecretsecretsecret",
                new HttpClient(handler)
            );

            await service.PurchasePhoneNumber(
                new PurchasePhoneNumberRequest
                {
                    PhoneNumbers = { "+14155550100", "+14155550101" },
                    SipDispatchRuleId = "SDR_test",
                }
            );

            var sent = PurchasePhoneNumberRequest.Parser.ParseFrom(handler.LastRequestBody);
            Assert.Equal(new[] { "+14155550100", "+14155550101" }, sent.PhoneNumbers);
            Assert.Equal("SDR_test", sent.SipDispatchRuleId);
        }
    }
}
