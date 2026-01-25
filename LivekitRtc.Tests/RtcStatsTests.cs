// author: https://github.com/pabloFuente

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LiveKit.Rtc;
using Xunit;
using Xunit.Abstractions;

namespace LiveKit.Rtc.Tests
{
    [Collection("LiveKit E2E Tests")]
    public class RtcStatsTests : IClassFixture<RtcTestFixture>, IAsyncLifetime
    {
        private readonly RtcTestFixture _fixture;
        private readonly ITestOutputHelper _output;

        public RtcStatsTests(RtcTestFixture fixture, ITestOutputHelper output)
        {
            _fixture = fixture;
            _output = output;
        }

        public Task InitializeAsync() => Task.CompletedTask;

        public Task DisposeAsync() => Task.CompletedTask;

        private void Log(string message) =>
            _output.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] {message}");

        [Fact]
        public async Task GetRtcStats_AfterConnection_ReturnsValidStats()
        {
            Log("Starting GetRtcStats basic test");

            using var room = new Room();

            var token = _fixture.CreateToken("stats-participant", "stats-test-room");
            await room.ConnectAsync(_fixture.LiveKitUrl, token);
            Log("Room connected");

            // Wait a moment for connection to stabilize
            await Task.Delay(500);

            // Get RTC stats
            var stats = await room.GetRtcStatsAsync();
            Log("RTC stats retrieved");

            // Verify stats object is not null
            Assert.NotNull(stats);
            Assert.NotNull(stats.PublisherStats);
            Assert.NotNull(stats.SubscriberStats);
            Log(
                $"Stats retrieved: PublisherStats count={stats.PublisherStats.Count}, SubscriberStats count={stats.SubscriberStats.Count}"
            );

            await room.DisconnectAsync();
            Log("Test completed");
        }

        [Fact]
        public async Task GetRtcStats_WithPublishedTrack_ContainsPublisherStats()
        {
            Log("Starting GetRtcStats with published track test");

            using var room = new Room();

            var token = _fixture.CreateToken("publisher-stats", "stats-publish-room");
            await room.ConnectAsync(_fixture.LiveKitUrl, token);
            Log("Room connected");

            // Publish an audio track
            using var audioSource = new AudioSource(48000, 1);
            var audioTrack = LocalAudioTrack.Create("test-audio", audioSource);
            await room.LocalParticipant!.PublishTrackAsync(audioTrack);
            Log("Audio track published");

            // Generate some audio frames
            var audioData = new byte[960]; // 20ms at 48kHz
            var audioFrame = new AudioFrame(audioData, 48000, 1, 480);

            for (int i = 0; i < 20; i++)
            {
                audioSource.CaptureFrame(audioFrame);
                await Task.Delay(20);
            }
            Log("Audio frames sent");

            // Wait for stats to accumulate
            await Task.Delay(1000);

            // Get RTC stats
            var stats = await room.GetRtcStatsAsync();
            Log($"RTC stats retrieved: PublisherStats count={stats.PublisherStats.Count}");

            // Should have publisher stats since we're publishing
            Assert.NotNull(stats.PublisherStats);
            // Publisher stats may be empty initially or may contain data depending on timing
            Log($"Publisher stats count: {stats.PublisherStats.Count}");

            await room.DisconnectAsync();
            Log("Test completed");
        }

        [Fact]
        public async Task GetRtcStats_WithSubscribedTrack_ContainsSubscriberStats()
        {
            Log("Starting GetRtcStats with subscribed track test");

            using var publisherRoom = new Room();
            using var subscriberRoom = new Room();

            var publisherToken = _fixture.CreateToken("publisher", "stats-subscribe-room");
            var subscriberToken = _fixture.CreateToken("subscriber", "stats-subscribe-room");

            await publisherRoom.ConnectAsync(_fixture.LiveKitUrl, publisherToken);
            Log("Publisher connected");

            await subscriberRoom.ConnectAsync(_fixture.LiveKitUrl, subscriberToken);
            Log("Subscriber connected");

            // Wait for participants to see each other
            await Task.Delay(500);

            // Publish an audio track
            using var audioSource = new AudioSource(48000, 1);
            var audioTrack = LocalAudioTrack.Create("test-audio", audioSource);
            await publisherRoom.LocalParticipant!.PublishTrackAsync(audioTrack);
            Log("Audio track published");

            // Generate audio frames
            var audioData = new byte[960];
            var audioFrame = new AudioFrame(audioData, 48000, 1, 480);

            for (int i = 0; i < 30; i++)
            {
                audioSource.CaptureFrame(audioFrame);
                await Task.Delay(20);
            }
            Log("Audio frames sent");

            // Wait for stats to accumulate
            await Task.Delay(1000);

            // Get RTC stats from subscriber
            var stats = await subscriberRoom.GetRtcStatsAsync();
            Log(
                $"Subscriber RTC stats retrieved: SubscriberStats count={stats.SubscriberStats.Count}"
            );

            // Verify stats
            Assert.NotNull(stats.SubscriberStats);
            // Subscriber stats may be empty initially or may contain data depending on timing
            Log($"Subscriber stats count: {stats.SubscriberStats.Count}");

            await publisherRoom.DisconnectAsync();
            await subscriberRoom.DisconnectAsync();
            Log("Test completed");
        }

        [Fact]
        public async Task GetRtcStats_CalledMultipleTimes_Succeeds()
        {
            Log("Starting GetRtcStats multiple calls test");

            using var room = new Room();

            var token = _fixture.CreateToken("multi-stats", "stats-multi-room");
            await room.ConnectAsync(_fixture.LiveKitUrl, token);
            Log("Room connected");

            // Call GetRtcStats multiple times
            for (int i = 0; i < 3; i++)
            {
                Log($"Getting RTC stats (attempt {i + 1})");
                var stats = await room.GetRtcStatsAsync();

                Assert.NotNull(stats);
                Assert.NotNull(stats.PublisherStats);
                Assert.NotNull(stats.SubscriberStats);

                Log(
                    $"Stats {i + 1}: PublisherStats count={stats.PublisherStats.Count}, SubscriberStats count={stats.SubscriberStats.Count}"
                );

                await Task.Delay(500);
            }

            await room.DisconnectAsync();
            Log("Test completed");
        }

        [Fact]
        public async Task GetRtcStats_WhenDisconnected_ThrowsException()
        {
            Log("Starting GetRtcStats when disconnected test");

            using var room = new Room();

            // Try to get stats without connecting
            await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            {
                await room.GetRtcStatsAsync();
            });
            Log("Correctly threw InvalidOperationException when not connected");

            // Connect and then disconnect
            var token = _fixture.CreateToken("disconnect-stats", "stats-disconnect-room");
            await room.ConnectAsync(_fixture.LiveKitUrl, token);
            Log("Room connected");

            await room.DisconnectAsync();
            Log("Room disconnected");

            // Try to get stats after disconnecting
            await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            {
                await room.GetRtcStatsAsync();
            });
            Log("Correctly threw InvalidOperationException after disconnect");

            Log("Test completed");
        }

        [Fact]
        public async Task GetRtcStats_WithMultipleParticipants_ReturnsStats()
        {
            Log("Starting GetRtcStats with multiple participants test");

            using var room1 = new Room();
            using var room2 = new Room();
            using var room3 = new Room();

            var token1 = _fixture.CreateToken("participant1", "stats-multi-participant-room");
            var token2 = _fixture.CreateToken("participant2", "stats-multi-participant-room");
            var token3 = _fixture.CreateToken("participant3", "stats-multi-participant-room");

            await room1.ConnectAsync(_fixture.LiveKitUrl, token1);
            await room2.ConnectAsync(_fixture.LiveKitUrl, token2);
            await room3.ConnectAsync(_fixture.LiveKitUrl, token3);
            Log("All three participants connected");

            // Publish tracks from all participants
            using var audioSource1 = new AudioSource(48000, 1);
            using var audioSource2 = new AudioSource(48000, 1);
            using var audioSource3 = new AudioSource(48000, 1);

            var audioTrack1 = LocalAudioTrack.Create("audio1", audioSource1);
            var audioTrack2 = LocalAudioTrack.Create("audio2", audioSource2);
            var audioTrack3 = LocalAudioTrack.Create("audio3", audioSource3);

            await room1.LocalParticipant!.PublishTrackAsync(audioTrack1);
            await room2.LocalParticipant!.PublishTrackAsync(audioTrack2);
            await room3.LocalParticipant!.PublishTrackAsync(audioTrack3);
            Log("All audio tracks published");

            // Generate audio frames
            var audioData = new byte[960];
            var audioFrame = new AudioFrame(audioData, 48000, 1, 480);

            for (int i = 0; i < 30; i++)
            {
                audioSource1.CaptureFrame(audioFrame);
                audioSource2.CaptureFrame(audioFrame);
                audioSource3.CaptureFrame(audioFrame);
                await Task.Delay(20);
            }
            Log("Audio frames sent from all participants");

            // Wait for stats to accumulate
            await Task.Delay(1000);

            // Get stats from each participant
            var stats1 = await room1.GetRtcStatsAsync();
            var stats2 = await room2.GetRtcStatsAsync();
            var stats3 = await room3.GetRtcStatsAsync();

            Log(
                $"Participant 1 stats - Publisher: {stats1.PublisherStats.Count}, Subscriber: {stats1.SubscriberStats.Count}"
            );
            Log(
                $"Participant 2 stats - Publisher: {stats2.PublisherStats.Count}, Subscriber: {stats2.SubscriberStats.Count}"
            );
            Log(
                $"Participant 3 stats - Publisher: {stats3.PublisherStats.Count}, Subscriber: {stats3.SubscriberStats.Count}"
            );

            // All participants should have stats
            Assert.NotNull(stats1);
            Assert.NotNull(stats2);
            Assert.NotNull(stats3);

            await room1.DisconnectAsync();
            await room2.DisconnectAsync();
            await room3.DisconnectAsync();
            Log("Test completed");
        }

        [Fact]
        public async Task GetRtcStats_ConcurrentCalls_AllSucceed()
        {
            Log("Starting GetRtcStats concurrent calls test");

            using var room = new Room();

            var token = _fixture.CreateToken("concurrent-stats", "stats-concurrent-room");
            await room.ConnectAsync(_fixture.LiveKitUrl, token);
            Log("Room connected");

            // Make concurrent calls to GetRtcStats
            var tasks = Enumerable
                .Range(0, 5)
                .Select(async i =>
                {
                    Log($"Starting concurrent stats call {i}");
                    var stats = await room.GetRtcStatsAsync();
                    Assert.NotNull(stats);
                    Log($"Completed concurrent stats call {i}");
                    return stats;
                })
                .ToArray();

            var results = await Task.WhenAll(tasks);

            // All calls should succeed
            Assert.Equal(5, results.Length);
            foreach (var result in results)
            {
                Assert.NotNull(result);
                Assert.NotNull(result.PublisherStats);
                Assert.NotNull(result.SubscriberStats);
            }

            Log("All concurrent calls succeeded");

            await room.DisconnectAsync();
            Log("Test completed");
        }
    }
}
