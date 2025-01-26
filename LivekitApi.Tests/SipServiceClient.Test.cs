using Google.Protobuf;
using Google.Protobuf.Collections;
using Xunit;
using Xunit.Abstractions;

namespace Livekit.Server.Sdk.Dotnet.Test
{
    [Collection("Integration tests")]
    public class SipServiceClientTest
    {
        private ServiceClientFixture fixture;
        private readonly ITestOutputHelper output;

        public SipServiceClientTest(ServiceClientFixture fixture, ITestOutputHelper output)
        {
            this.fixture = fixture;
            this.output = output;
        }

        private SipServiceClient sipClient = new SipServiceClient(
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
        [Trait("Category", "Integration")]
        [Trait("Category", "SipService")]
        public async Task List_Sip_Inbound_Trunks()
        {
            var response = await sipClient.ListSIPInboundTrunk(new ListSIPInboundTrunkRequest());
            Assert.NotNull(response.Items);
        }

        [Fact]
        [Trait("Category", "Integration")]
        [Trait("Category", "SipService")]
        public async Task List_Sip_Outbound_Trunks()
        {
            var response = await sipClient.ListSIPOutboundTrunk(new ListSIPOutboundTrunkRequest());
            Assert.NotNull(response.Items);
        }

        [Fact]
        [Trait("Category", "Integration")]
        [Trait("Category", "SipService")]
        public async Task List_Sip_Dispatch_Rules()
        {
            var response = await sipClient.ListSIPDispatchRule(new ListSIPDispatchRuleRequest());
            Assert.NotNull(response.Items);
        }

        [Fact]
        [Trait("Category", "Integration")]
        [Trait("Category", "SipService")]
        public async Task Create_Sip_Inbound_Trunk()
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
        public async Task Create_Sip_Inbound_Trunk_Exceptions()
        {
            Twirp.Exception ex = await Assert.ThrowsAsync<Twirp.Exception>(
                async () =>
                    await sipClient.CreateSIPInboundTrunk(
                        new CreateSIPInboundTrunkRequest { Trunk = new SIPInboundTrunkInfo { } }
                    )
            );
            Assert.Equal(
                "for security, one of the fields must be set: AuthUsername+AuthPassword, AllowedAddresses or Numbers",
                ex.Message
            );
        }

        [Fact]
        [Trait("Category", "Integration")]
        [Trait("Category", "SipService")]
        public async Task Create_Sip_Outbound_Trunk()
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
        public async Task Create_Sip_Outbound_Trunk_Exceptions()
        {
            Twirp.Exception ex = await Assert.ThrowsAsync<Twirp.Exception>(
                async () =>
                    await sipClient.CreateSIPOutboundTrunk(
                        new CreateSIPOutboundTrunkRequest { Trunk = new SIPOutboundTrunkInfo { } }
                    )
            );
            Assert.Equal("no trunk numbers specified", ex.Message);
            ex = await Assert.ThrowsAsync<Twirp.Exception>(
                async () =>
                    await sipClient.CreateSIPOutboundTrunk(
                        new CreateSIPOutboundTrunkRequest
                        {
                            Trunk = new SIPOutboundTrunkInfo { Address = "my-test-trunk.com" },
                        }
                    )
            );
            Assert.Equal("no trunk numbers specified", ex.Message);
            ex = await Assert.ThrowsAsync<Twirp.Exception>(
                async () =>
                    await sipClient.CreateSIPOutboundTrunk(
                        new CreateSIPOutboundTrunkRequest
                        {
                            Trunk = new SIPOutboundTrunkInfo { Numbers = { "+111", "+222" } },
                        }
                    )
            );
            Assert.Equal("no outbound address specified", ex.Message);
        }

        [Fact]
        [Trait("Category", "Integration")]
        [Trait("Category", "SipService")]
        public async Task Get_Sip_Inbound_Trunk()
        {
            var inboundTrunk = await sipClient.CreateSIPInboundTrunk(
                new CreateSIPInboundTrunkRequest
                {
                    Trunk = new SIPInboundTrunkInfo { Numbers = { "+111", "+222" } },
                }
            );
            var getRequest = new GetSIPInboundTrunkRequest { SipTrunkId = inboundTrunk.SipTrunkId };
            var response = await sipClient.GetSIPInboundTrunk(getRequest);
            Assert.NotNull(response);
            Assert.Equal(inboundTrunk.SipTrunkId, response.Trunk.SipTrunkId);
        }

        [Fact]
        [Trait("Category", "Integration")]
        [Trait("Category", "SipService")]
        public async Task Get_Sip_Outound_Trunk()
        {
            var outboundTrunk = await sipClient.CreateSIPOutboundTrunk(
                new CreateSIPOutboundTrunkRequest
                {
                    Trunk = new SIPOutboundTrunkInfo
                    {
                        Numbers = { "+111", "+222" },
                        Address = "my-test-trunk.com",
                    },
                }
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
        public async Task Delete_Sip_Trunk()
        {
            var trunk = await sipClient.CreateSIPInboundTrunk(
                new CreateSIPInboundTrunkRequest
                {
                    Trunk = new SIPInboundTrunkInfo { Numbers = { "+111", "+222" } },
                }
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
        public async Task Dispatch_Rule()
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

        [Fact]
        [Trait("Category", "Integration")]
        [Trait("Category", "SipService")]
        public async Task Create_Sip_Participant()
        {
            Twirp.Exception ex = await Assert.ThrowsAsync<Twirp.Exception>(
                async () =>
                    await sipClient.CreateSIPParticipant(
                        new CreateSIPParticipantRequest { SipTrunkId = "non-existing-trunk" }
                    )
            );
            Assert.Equal("requested sip trunk does not exist", ex.Message);

            SIPOutboundTrunkInfo trunk = await sipClient.CreateSIPOutboundTrunk(
                new CreateSIPOutboundTrunkRequest
                {
                    Trunk = new SIPOutboundTrunkInfo
                    {
                        Name = "Demo outbound trunk",
                        Address = "my-test-trunk.com",
                        Numbers = { "+1234567890" },
                        AuthUsername = "username",
                        AuthPassword = "password",
                    },
                }
            );

            CreateSIPParticipantRequest request = new CreateSIPParticipantRequest
            {
                SipTrunkId = "trunk",
            };

            ex = await Assert.ThrowsAsync<Twirp.Exception>(
                async () =>
                    await sipClient.CreateSIPParticipant(
                        new CreateSIPParticipantRequest { SipTrunkId = "non-existing-trunk" }
                    )
            );
            Assert.Equal("requested sip trunk does not exist", ex.Message);

            request.SipTrunkId = trunk.SipTrunkId;

            ex = await Assert.ThrowsAsync<Twirp.Exception>(
                async () => await sipClient.CreateSIPParticipant(request)
            );
            Assert.Equal("call-to number must be set", ex.Message);

            request.SipCallTo = "+3333";

            ex = await Assert.ThrowsAsync<Twirp.Exception>(
                async () => await sipClient.CreateSIPParticipant(request)
            );
            Assert.Equal("room name must be set", ex.Message);

            request.RoomName = TestConstants.ROOM_NAME;

            ex = await Assert.ThrowsAsync<Twirp.Exception>(
                async () => await sipClient.CreateSIPParticipant(request)
            );
            Assert.Equal("update room failed: identity cannot be empty", ex.Message);

            request.ParticipantIdentity = TestConstants.PARTICIPANT_IDENTITY;

            request.ParticipantName = "Test Caller";
            request.SipNumber = "+1111";
            request.RingingTimeout = new Google.Protobuf.WellKnownTypes.Duration { Seconds = 10 };
            request.PlayDialtone = true;
            request.ParticipantMetadata = "meta";
            request.ParticipantAttributes.Add("extra", "1");
            request.MediaEncryption = SIPMediaEncryption.SipMediaEncryptRequire;
            request.MaxCallDuration = new Google.Protobuf.WellKnownTypes.Duration { Seconds = 99 };
            request.KrispEnabled = true;
            request.IncludeHeaders = SIPHeaderOptions.SipAllHeaders;
            request.Dtmf = "1234#";
            request.HidePhoneNumber = true;
            request.Headers.Add("X-A", "A");

            SIPParticipantInfo sipParticipantInfo = await sipClient.CreateSIPParticipant(request);

            Assert.NotNull(sipParticipantInfo);
            Assert.Equal(TestConstants.PARTICIPANT_IDENTITY, request.ParticipantIdentity);
            Assert.Equal(TestConstants.ROOM_NAME, request.RoomName);

            var sipParticipant = await roomClient.GetParticipant(
                new RoomParticipantIdentity
                {
                    Room = TestConstants.ROOM_NAME,
                    Identity = TestConstants.PARTICIPANT_IDENTITY,
                }
            );
            Assert.NotNull(sipParticipant);
            Assert.Equal(TestConstants.PARTICIPANT_IDENTITY, sipParticipant.Identity);
            Assert.Equal("Test Caller", sipParticipant.Name);
            Assert.Equal("meta", sipParticipant.Metadata);
            Assert.Equal("1", sipParticipant.Attributes["extra"]);
        }

        [Fact]
        [Trait("Category", "Integration")]
        [Trait("Category", "SipService")]
        public async Task Transfer_Sip_Participant()
        {
            var trunk = await sipClient.CreateSIPOutboundTrunk(
                new CreateSIPOutboundTrunkRequest
                {
                    Trunk = new SIPOutboundTrunkInfo
                    {
                        Name = "Demo outbound trunk",
                        Address = "my-test-trunk.com",
                        Numbers = { "+1234567890" },
                        AuthUsername = "username",
                        AuthPassword = "password",
                    },
                }
            );

            var request = new CreateSIPParticipantRequest
            {
                SipTrunkId = trunk.SipTrunkId,
                SipCallTo = "+3333",
                RoomName = TestConstants.ROOM_NAME,
                ParticipantIdentity = TestConstants.PARTICIPANT_IDENTITY,
                ParticipantName = "Test Caller",
                SipNumber = "+1111",
            };

            SIPParticipantInfo sipParticipantInfo = await sipClient.CreateSIPParticipant(request);

            var transferRequest = new TransferSIPParticipantRequest { };

            Twirp.Exception ex = await Assert.ThrowsAsync<Twirp.Exception>(
                async () => await sipClient.TransferSIPParticipant(transferRequest)
            );
            Assert.Equal("Missing room name", ex.Message);

            transferRequest.RoomName = TestConstants.ROOM_NAME;

            ex = await Assert.ThrowsAsync<Twirp.Exception>(
                async () => await sipClient.TransferSIPParticipant(transferRequest)
            );
            Assert.Equal("Missing participant identity", ex.Message);

            transferRequest.ParticipantIdentity = TestConstants.PARTICIPANT_IDENTITY;
            transferRequest.TransferTo = "+14155550100";
            transferRequest.PlayDialtone = false;

            ex = await Assert.ThrowsAsync<Twirp.Exception>(
                async () => await sipClient.TransferSIPParticipant(transferRequest)
            );
            Assert.Equal("can't transfer non established call", ex.Message);

            ex = await Assert.ThrowsAsync<Twirp.Exception>(
              async () => await sipClient.TransferSIPParticipant(transferRequest)
            );
            Assert.Equal("participant does not exist", ex.Message);
        }
    }
}
