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
            Twirp.Exception ex = await Assert.ThrowsAsync<Twirp.Exception>(async () =>
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
            Twirp.Exception ex = await Assert.ThrowsAsync<Twirp.Exception>(async () =>
                await sipClient.CreateSIPOutboundTrunk(
                    new CreateSIPOutboundTrunkRequest { Trunk = new SIPOutboundTrunkInfo { } }
                )
            );
            Assert.EndsWith("no trunk numbers specified", ex.Message);
            ex = await Assert.ThrowsAsync<Twirp.Exception>(async () =>
                await sipClient.CreateSIPOutboundTrunk(
                    new CreateSIPOutboundTrunkRequest
                    {
                        Trunk = new SIPOutboundTrunkInfo { Address = "my-test-trunk.com" },
                    }
                )
            );
            Assert.EndsWith("no trunk numbers specified", ex.Message);
            ex = await Assert.ThrowsAsync<Twirp.Exception>(async () =>
                await sipClient.CreateSIPOutboundTrunk(
                    new CreateSIPOutboundTrunkRequest
                    {
                        Trunk = new SIPOutboundTrunkInfo { Numbers = { "+111", "+222" } },
                    }
                )
            );
            Assert.EndsWith("no outbound address specified", ex.Message);
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
            var dispatchRuleInfo = new SIPDispatchRuleInfo();
            dispatchRuleInfo.Name = "Demo dispatch rule";
            dispatchRuleInfo.Metadata = "Demo dispatch rule metadata";
            dispatchRuleInfo.Rule = new SIPDispatchRule
            {
                DispatchRuleDirect = new SIPDispatchRuleDirect
                {
                    RoomName = TestConstants.ROOM_NAME,
                    Pin = "1234",
                },
            };
            var request = new CreateSIPDispatchRuleRequest { DispatchRule = dispatchRuleInfo };
            // Create dispatch rule
            var dispatchRule = await sipClient.CreateSIPDispatchRule(request);
            Assert.NotNull(dispatchRule);
            Assert.Equal(TestConstants.ROOM_NAME, dispatchRule.Rule.DispatchRuleDirect.RoomName);
            Assert.Equal("1234", dispatchRule.Rule.DispatchRuleDirect.Pin);
            Assert.Equal("Demo dispatch rule", dispatchRule.Name);
            Assert.Equal("Demo dispatch rule metadata", dispatchRule.Metadata);
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
            CreateSIPParticipantRequest request = new CreateSIPParticipantRequest { };

            Twirp.Exception ex = await Assert.ThrowsAsync<Twirp.Exception>(async () =>
                await sipClient.CreateSIPParticipant(request)
            );
            Assert.EndsWith("missing sip trunk id", ex.Message);

            request.SipTrunkId = "non-existing-trunk";

            ex = await Assert.ThrowsAsync<Twirp.Exception>(async () =>
                await sipClient.CreateSIPParticipant(request)
            );
            Assert.Equal("missing sip callee number", ex.Message);

            request.SipCallTo = "+3333";

            ex = await Assert.ThrowsAsync<Twirp.Exception>(async () =>
                await sipClient.CreateSIPParticipant(request)
            );
            Assert.EndsWith("missing room name", ex.Message);

            request.RoomName = TestConstants.ROOM_NAME;

            ex = await Assert.ThrowsAsync<Twirp.Exception>(async () =>
                await sipClient.CreateSIPParticipant(request)
            );
            Assert.EndsWith("requested sip trunk does not exist", ex.Message);

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

            request.SipTrunkId = trunk.SipTrunkId;

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

            await sipClient.CreateSIPParticipant(request);

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

            Twirp.Exception ex = await Assert.ThrowsAsync<Twirp.Exception>(async () =>
                await sipClient.TransferSIPParticipant(transferRequest)
            );
            Assert.EndsWith("Missing room name", ex.Message);

            transferRequest.RoomName = TestConstants.ROOM_NAME;

            ex = await Assert.ThrowsAsync<Twirp.Exception>(async () =>
                await sipClient.TransferSIPParticipant(transferRequest)
            );
            Assert.EndsWith("Missing participant identity", ex.Message);

            transferRequest.ParticipantIdentity = TestConstants.PARTICIPANT_IDENTITY;
            transferRequest.TransferTo = "+14155550100";
            transferRequest.PlayDialtone = false;

            ex = await Assert.ThrowsAsync<Twirp.Exception>(async () =>
                await sipClient.TransferSIPParticipant(transferRequest)
            );
            Assert.EndsWith("can't transfer non established call", ex.Message);

            ex = await Assert.ThrowsAsync<Twirp.Exception>(async () =>
                await sipClient.TransferSIPParticipant(transferRequest)
            );
            Assert.EndsWith("participant does not exist", ex.Message);
        }

        [Fact]
        [Trait("Category", "Integration")]
        [Trait("Category", "SipService")]
        public async Task Update_Sip_Inbound_Trunk()
        {
            // Create inbound trunk
            var createRequest = new CreateSIPInboundTrunkRequest
            {
                Trunk = new SIPInboundTrunkInfo
                {
                    Name = "Inbound trunk to update",
                    Numbers = { "+11112222" },
                    AllowedAddresses = { "2.2.2.0/24" },
                },
            };
            var trunk = await sipClient.CreateSIPInboundTrunk(createRequest);

            // Update trunk name, metadata and numbers
            var newName = "Updated inbound trunk";
            var newMetadata = "Updated metadata";
            var newNumbers = new ListUpdate { Set = { "+33334444" } };

            var updateRequest = new UpdateSIPInboundTrunkRequest
            {
                SipTrunkId = trunk.SipTrunkId,
                Update = new SIPInboundTrunkUpdate
                {
                    Name = newName,
                    Metadata = newMetadata,
                    Numbers = newNumbers,
                },
            };

            var updatedTrunk = await sipClient.UpdateSIPInboundTrunk(updateRequest);
            Assert.NotNull(updatedTrunk);
            Assert.Equal(newName, updatedTrunk.Name);
            Assert.Equal(newMetadata, updatedTrunk.Metadata);
            Assert.Contains("+33334444", updatedTrunk.Numbers);
        }

        [Fact]
        [Trait("Category", "Integration")]
        [Trait("Category", "SipService")]
        public async Task Update_Sip_Outbound_Trunk()
        {
            // Create outbound trunk
            var createRequest = new CreateSIPOutboundTrunkRequest
            {
                Trunk = new SIPOutboundTrunkInfo
                {
                    Name = "Outbound trunk to update",
                    Address = "sip.example.com",
                    Numbers = { "+55556666" },
                    MediaEncryption = SIPMediaEncryption.SipMediaEncryptDisable,
                },
            };
            var trunk = await sipClient.CreateSIPOutboundTrunk(createRequest);

            // Update trunk name and address
            var newName = "Updated outbound trunk";
            var newAddress = "sip.updated.com";
            var updateRequest = new UpdateSIPOutboundTrunkRequest
            {
                SipTrunkId = trunk.SipTrunkId,
                Update = new SIPOutboundTrunkUpdate
                {
                    Name = newName,
                    Address = newAddress,
                    MediaEncryption = SIPMediaEncryption.SipMediaEncryptRequire,
                },
            };
            var updatedTrunk = await sipClient.UpdateSIPOutboundTrunk(updateRequest);
            Assert.NotNull(updatedTrunk);
            Assert.Equal(newName, updatedTrunk.Name);
            Assert.Equal(newAddress, updatedTrunk.Address);
            Assert.Equal(SIPMediaEncryption.SipMediaEncryptRequire, updatedTrunk.MediaEncryption);
            Assert.Contains("+55556666", updatedTrunk.Numbers);
        }

        [Fact]
        [Trait("Category", "Integration")]
        [Trait("Category", "SipService")]
        public async Task Update_Sip_Dispatch_Rule()
        {
            // Create dispatch rule
            var createRequest = new CreateSIPDispatchRuleRequest
            {
                DispatchRule = new SIPDispatchRuleInfo
                {
                    Name = "Dispatch rule to update",
                    Rule = new SIPDispatchRule
                    {
                        DispatchRuleDirect = new SIPDispatchRuleDirect
                        {
                            RoomName = TestConstants.ROOM_NAME,
                            Pin = "1234",
                        },
                    },
                },
            };
            var rule = await sipClient.CreateSIPDispatchRule(createRequest);

            // Update rule name and pin
            var newName = "Updated dispatch rule";
            var newPin = "5678";
            var updateRequest = new UpdateSIPDispatchRuleRequest
            {
                SipDispatchRuleId = rule.SipDispatchRuleId,
                Update = new SIPDispatchRuleUpdate
                {
                    Name = newName,
                    Rule = new SIPDispatchRule
                    {
                        DispatchRuleDirect = new SIPDispatchRuleDirect
                        {
                            RoomName = TestConstants.ROOM_NAME,
                            Pin = newPin,
                        },
                    },
                },
            };
            var updatedRule = await sipClient.UpdateSIPDispatchRule(updateRequest);
            Assert.NotNull(updatedRule);
            Assert.Equal(newName, updatedRule.Name);
            Assert.Equal(newPin, updatedRule.Rule.DispatchRuleDirect.Pin);
        }
    }
}
