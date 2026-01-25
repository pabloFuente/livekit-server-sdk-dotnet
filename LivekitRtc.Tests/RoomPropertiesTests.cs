// author: https://github.com/pabloFuente

using System;
using System.Threading.Tasks;
using LiveKit.Rtc;
using Xunit;
using Xunit.Abstractions;

namespace LiveKit.Rtc.Tests
{
    [Collection("LiveKit E2E Tests")]
    public class RoomPropertiesTests : IClassFixture<RtcTestFixture>, IAsyncLifetime
    {
        private readonly RtcTestFixture _fixture;
        private readonly ITestOutputHelper _output;

        public RoomPropertiesTests(RtcTestFixture fixture, ITestOutputHelper output)
        {
            _fixture = fixture;
            _output = output;
        }

        public Task InitializeAsync() => Task.CompletedTask;

        public Task DisposeAsync() => Task.CompletedTask;

        private void Log(string message) =>
            _output.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] {message}");

        [Fact]
        public async Task RoomProperties_AfterConnection_ArePopulated()
        {
            Log("Starting Room properties test");

            using var room = new Room();

            var token = _fixture.CreateToken("properties-test", "properties-room");
            await room.ConnectAsync(_fixture.LiveKitUrl, token);
            Log("Room connected");

            // Wait a moment for room info to be populated
            await Task.Delay(500);

            // Check new properties are accessible
            Log($"Room SID: {room.Sid}");
            Log($"Room Name: {room.Name}");
            Log($"Creation Time: {room.CreationTime}");
            Log($"Departure Timeout: {room.DepartureTimeout}");
            Log($"Empty Timeout: {room.EmptyTimeout}");
            Log($"E2EE Manager: {(room.E2EEManager != null ? "Present" : "Null")}");

            // Basic assertions
            Assert.NotNull(room.Sid);
            Assert.NotNull(room.Name);

            // CreationTime should be a valid date (not DateTime.MinValue in normal cases)
            // Note: In some test environments, this might not be set
            Log($"Creation time is valid: {room.CreationTime > DateTime.MinValue}");

            // Timeouts should be non-negative
            Assert.True(room.DepartureTimeout >= 0);
            Assert.True(room.EmptyTimeout >= 0);

            // E2EEManager should be null since we didn't enable E2EE
            Assert.Null(room.E2EEManager);

            await room.DisconnectAsync();
            Log("Test completed");
        }

        [Fact]
        public async Task RoomProperties_WithE2EE_E2EEManagerPopulated()
        {
            Log("Starting Room E2EE properties test");

            using var room = new Room();

            var token = _fixture.CreateToken("e2ee-test", "e2ee-room");
            var options = new RoomOptions
            {
                E2EE = new E2EEOptions { KeyProviderOptions = new KeyProviderOptions() },
            };
            await room.ConnectAsync(_fixture.LiveKitUrl, token, options);
            Log("Room connected with E2EE options");

            // Wait a moment for room info to be populated
            await Task.Delay(500);

            // E2EEManager should be present when E2EE options are provided
            Assert.NotNull(room.E2EEManager);
            Log($"E2EE Manager successfully initialized: {room.E2EEManager != null}");

            await room.DisconnectAsync();
            Log("Test completed");
        }
    }
}
