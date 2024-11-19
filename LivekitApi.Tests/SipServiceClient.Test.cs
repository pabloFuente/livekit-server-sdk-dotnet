using Google.Protobuf;
using LiveKit.Proto;

namespace Livekit.Server.Sdk.Dotnet.Test
{
    [Collection("Integration tests")]
    public class SipServiceClientTest
    {
        private ServiceClientFixture fixture;

        public SipServiceClientTest(ServiceClientFixture fixture)
        {
            this.fixture = fixture;
        }

        private SipServiceClient sipClient = new SipServiceClient(
            ServiceClientFixture.TEST_HTTP_URL,
            ServiceClientFixture.TEST_API_KEY,
            ServiceClientFixture.TEST_API_SECRET
        );

        [Fact]
        [Trait("Category", "Integration")]
        [Trait("Category", "SipService")]
        public async void List_Sip_Inbound_Trunks()
        {
            var response = await sipClient.ListSIPInboundTrunk(new ListSIPInboundTrunkRequest());
            Assert.NotNull(response.Items);
        }

        [Fact]
        [Trait("Category", "Integration")]
        [Trait("Category", "SipService")]
        public async void List_Sip_Outbound_Trunks()
        {
            var response = await sipClient.ListSIPOutboundTrunk(new ListSIPOutboundTrunkRequest());
            Assert.NotNull(response.Items);
        }

        [Fact]
        [Trait("Category", "Integration")]
        [Trait("Category", "SipService")]
        public async void List_Sip_Dispatch_Rules()
        {
            var response = await sipClient.ListSIPDispatchRule(new ListSIPDispatchRuleRequest());
            Assert.NotNull(response.Items);
        }

        [Fact]
        [Trait("Category", "Integration")]
        [Trait("Category", "SipService")]
        public async void Create_Sip_Inbound_Trunk()
        {
            var request = new CreateSIPInboundTrunkRequest
            {
                Trunk = new SIPInboundTrunkInfo
                {
                    Name = "Demo inbound trunk",
                    Numbers = { "+1234567890" },
                    AllowedNumbers = { "+11111111", "+22222222" },
                    AllowedAddresses = { "1.1.1.0/24" },
                },
            };
            var response = await sipClient.CreateSIPInboundTrunk(request);
            Assert.NotNull(response);
            Assert.Equal(request.Trunk.Name, response.Name);
            Assert.Equal(request.Trunk.Numbers, response.Numbers);
            Assert.Equal(request.Trunk.AllowedNumbers, response.AllowedNumbers);
        }

        [Fact]
        [Trait("Category", "Integration")]
        [Trait("Category", "SipService")]
        public async void Create_Sip_Outbound_Trunk()
        {
            var request = new CreateSIPOutboundTrunkRequest
            {
                Trunk = new SIPOutboundTrunkInfo
                {
                    Name = "Demo outbound trunk",
                    Address = "my-test-trunk.com",
                    Numbers = { "+1234567890" },
                    AuthUsername = "username",
                    AuthPassword = "password",
                },
            };
            var response = await sipClient.CreateSIPOutboundTrunk(request);
            Assert.NotNull(response);
            Assert.Equal(request.Trunk.Name, response.Name);
            Assert.Equal(request.Trunk.Address, response.Address);
            Assert.Equal(request.Trunk.Numbers, response.Numbers);
            Assert.Equal(request.Trunk.AuthUsername, response.AuthUsername);
            Assert.Equal(request.Trunk.AuthPassword, response.AuthPassword);
        }

        [Fact]
        [Trait("Category", "Integration")]
        [Trait("Category", "SipService")]
        public async void Get_Sip_Inbound_Trunk()
        {
            var inboundTrunk = await sipClient.CreateSIPInboundTrunk(
                new CreateSIPInboundTrunkRequest { Trunk = new SIPInboundTrunkInfo { } }
            );
            var getRequest = new GetSIPInboundTrunkRequest { SipTrunkId = inboundTrunk.SipTrunkId };
            var response = await sipClient.GetSIPInboundTrunk(getRequest);
            Assert.NotNull(response);
            Assert.Equal(inboundTrunk.SipTrunkId, response.Trunk.SipTrunkId);
        }

        [Fact]
        [Trait("Category", "Integration")]
        [Trait("Category", "SipService")]
        public async void Get_Sip_Outound_Trunk()
        {
            var outboundTrunk = await sipClient.CreateSIPOutboundTrunk(
                new CreateSIPOutboundTrunkRequest { Trunk = new SIPOutboundTrunkInfo { } }
            );
            var getRequest = new GetSIPOutboundTrunkRequest
            {
                SipTrunkId = outboundTrunk.SipTrunkId,
            };
            var response = await sipClient.GetSIPOutboundTrunk(getRequest);
            Assert.NotNull(response);
            Assert.Equal(outboundTrunk.SipTrunkId, response.Trunk.SipTrunkId);
        }

        [Fact]
        [Trait("Category", "Integration")]
        [Trait("Category", "SipService")]
        public async void Delete_Sip_Trunk()
        {
            var trunk = await sipClient.CreateSIPInboundTrunk(
                new CreateSIPInboundTrunkRequest { Trunk = new SIPInboundTrunkInfo { } }
            );
            var allTrunks = await sipClient.ListSIPInboundTrunk(new ListSIPInboundTrunkRequest { });
            Assert.Contains(allTrunks.Items, t => t.SipTrunkId == trunk.SipTrunkId);
            var deleteInboundTrunk = new DeleteSIPTrunkRequest { SipTrunkId = trunk.SipTrunkId };
            await sipClient.DeleteSIPTrunk(deleteInboundTrunk);
            allTrunks = await sipClient.ListSIPInboundTrunk(new ListSIPInboundTrunkRequest { });
            Assert.DoesNotContain(allTrunks.Items, t => t.SipTrunkId == trunk.SipTrunkId);
        }

        [Fact]
        [Trait("Category", "Integration")]
        [Trait("Category", "SipService")]
        public async void Dispatch_Rule()
        {
            var request = new CreateSIPDispatchRuleRequest
            {
                Name = "Demo dispatch rule",
                Metadata = "Demo dispatch rule metadata",
                Rule = new SIPDispatchRule
                {
                    DispatchRuleDirect = new SIPDispatchRuleDirect
                    {
                        RoomName = TestConstants.ROOM_NAME,
                        Pin = "1234",
                    },
                },
            };
            // Create dispatch rule
            var dispatchRule = await sipClient.CreateSIPDispatchRule(request);
            Assert.NotNull(dispatchRule);
            Assert.Equal(TestConstants.ROOM_NAME, request.Rule.DispatchRuleDirect.RoomName);
            Assert.Equal("1234", request.Rule.DispatchRuleDirect.Pin);
            // List dispatch rules
            var allDispatchRules = await sipClient.ListSIPDispatchRule(
                new ListSIPDispatchRuleRequest { }
            );
            Assert.NotNull(allDispatchRules);
            Assert.Contains(
                allDispatchRules.Items,
                r => r.SipDispatchRuleId == dispatchRule.SipDispatchRuleId
            );
            // Delete dispatch rule
            var deleteRequest = new DeleteSIPDispatchRuleRequest
            {
                SipDispatchRuleId = dispatchRule.SipDispatchRuleId,
            };
            await sipClient.DeleteSIPDispatchRule(deleteRequest);
            allDispatchRules = await sipClient.ListSIPDispatchRule(
                new ListSIPDispatchRuleRequest { }
            );
            Assert.DoesNotContain(
                allDispatchRules.Items,
                r => r.SipDispatchRuleId == dispatchRule.SipDispatchRuleId
            );
        }
    }
}
