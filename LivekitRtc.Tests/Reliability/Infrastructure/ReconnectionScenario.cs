// author: https://github.com/pabloFuente

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;

namespace LiveKit.Rtc.Tests.Reliability;

/// <summary>
/// A publisher with a live audio and video track, watched by an observer, for the reconnection
/// tests. Records the publisher's reconnection events in order, and checks that the SDK's view,
/// the server's and the observer's agree.
/// </summary>
internal sealed class ReconnectionScenario : IAsyncDisposable
{
    private static readonly TimeSpan Settle = TimeSpan.FromSeconds(30);

    public RoomGroup Group { get; private init; } = null!;
    public Room Publisher => Group["publisher"];
    public Room Observer => Group["observer"];
    public LocalTrackPublication[] Publications { get; private set; } = null!;
    public ConcurrentQueue<string> Events { get; } = new();
    public ConcurrentQueue<string> ReceivedData { get; } = new();

    /// <summary>
    /// Both rooms' connection events and the data sent and received, with their time, for the
    /// failure message of a wait that times out: what happened after the wait started included.
    /// </summary>
    public ConcurrentQueue<string> Timeline { get; } = new();

    private readonly Stopwatch _clock = Stopwatch.StartNew();

    private readonly List<IDisposable> _media = new();
    private readonly List<MediaPump> _pumps = new();

    public static async Task<ReconnectionScenario> StartAsync(
        ReliabilityServerFixture server,
        string name
    )
    {
        var group = await RoomGroup.ConnectAsync(
            server,
            ReliabilityServerFixture.NewRoomName(name),
            new[] { "publisher", "observer" }
        );
        var scenario = new ReconnectionScenario { Group = group };
        var publisher = group["publisher"];
        publisher.Reconnecting += (_, _) => scenario.Events.Enqueue("Reconnecting");
        publisher.Reconnected += (_, _) => scenario.Events.Enqueue("Reconnected");
        publisher.Disconnected += (_, r) => scenario.Events.Enqueue($"Disconnected {r}");
        publisher.LocalTrackRepublished += (_, e) =>
            scenario.Events.Enqueue($"LocalTrackRepublished {e.PreviousSid}");
        publisher.DataReceived += (_, e) =>
        {
            var text = Encoding.UTF8.GetString(e.Data);
            scenario.ReceivedData.Enqueue(text);
            scenario.Mark($"publisher received '{text}'");
        };
        foreach (var identity in new[] { "publisher", "observer" })
        {
            var room = group[identity];
            room.Reconnecting += (_, _) => scenario.Mark($"{identity} Reconnecting");
            room.Reconnected += (_, _) => scenario.Mark($"{identity} Reconnected");
            room.Disconnected += (_, r) => scenario.Mark($"{identity} Disconnected {r}");
            room.LocalTrackRepublished += (_, e) =>
                scenario.Mark($"{identity} LocalTrackRepublished {e.PreviousSid}");
        }

        var audioSource = new AudioSource(48000, 1);
        var videoSource = new VideoSource(320, 240);
        var audioTrack = LocalAudioTrack.Create("reconnect-audio", audioSource);
        var videoTrack = LocalVideoTrack.Create("reconnect-video", videoSource);
        scenario._media.AddRange(
            new IDisposable[] { audioTrack, videoTrack, audioSource, videoSource }
        );
        scenario._pumps.Add(MediaPump.Audio(audioSource));
        scenario._pumps.Add(MediaPump.Video(videoSource));
        var local = publisher.LocalParticipant!;
        scenario.Publications = new[]
        {
            await local.PublishTrackAsync(audioTrack),
            await local.PublishTrackAsync(
                videoTrack,
                new TrackPublishOptions { Simulcast = false }
            ),
        };
        await scenario.WaitUntilConsistentAsync(server);
        return scenario;
    }

    /// <summary>
    /// The publisher lists its two publications under their current SIDs, each track carries
    /// its publication's SID, the server lists exactly those, and the observer is subscribed to
    /// both.
    /// </summary>
    public async Task WaitUntilConsistentAsync(ReliabilityServerFixture server)
    {
        var local = Publisher.LocalParticipant!;
        Assert.Equal(2, local.TrackPublications.Count);
        foreach (var publication in Publications)
        {
            Assert.Same(publication, local.GetTrackPublication(publication.Sid));
            Assert.Equal(publication.Sid, publication.Track!.Sid);
        }
        var expected = Publications.Select(p => p.Sid).OrderBy(s => s).ToArray();
        await Eventually.TrueAsync(
            async () =>
                (await server.TrackSidsAsync(Group.Name, "publisher")).SequenceEqual(expected),
            Settle,
            $"the server lists [{string.Join(", ", expected)}]"
        );
        await Eventually.TrueAsync(
            () =>
                Observer.RemoteParticipants.TryGetValue("publisher", out var remote)
                && expected.All(sid =>
                    remote.GetTrackPublication(sid) is RemoteTrackPublication p && p.Track != null
                ),
            Settle,
            "the observer is subscribed to the current tracks"
        );
    }

    /// <summary>Adds an entry to <see cref="Timeline"/>.</summary>
    public void Mark(string what) =>
        Timeline.Enqueue($"{_clock.ElapsedMilliseconds, 7} ms  {what}");

    private string DescribeTimeline() =>
        $"Timeline:{Environment.NewLine}{string.Join(Environment.NewLine, Timeline)}";

    /// <summary>
    /// Waits until both rooms are connected. An outage of the server makes the observer reconnect
    /// too, and the observer's state is only consistent once its own reconnection has finished.
    /// </summary>
    public Task WaitUntilBothConnectedAsync(TimeSpan timeout) =>
        Eventually.TrueAsync(
            () =>
                Publisher.ConnectionState == Proto.ConnectionState.ConnConnected
                && Observer.ConnectionState == Proto.ConnectionState.ConnConnected,
            timeout,
            () =>
                $"both rooms are connected (publisher {Publisher.ConnectionState}, observer "
                + $"{Observer.ConnectionState}). {DescribeTimeline()}"
        );

    /// <summary>
    /// Data sent by the observer arrives once: a handler duplicated by a reconnection would
    /// raise it twice.
    /// </summary>
    public async Task AssertDataArrivesOnceAsync(string marker)
    {
        Mark($"observer sends '{marker}'");
        await Observer.LocalParticipant!.PublishDataAsync(
            Encoding.UTF8.GetBytes(marker),
            new DataPublishOptions { DestinationIdentities = new[] { "publisher" } }
        );
        await Eventually.TrueAsync(
            () => ReceivedData.Contains(marker),
            Settle,
            () => $"data '{marker}' arrives. {DescribeTimeline()}"
        );
        await Task.Delay(200);
        Assert.Single(ReceivedData, marker);
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var pump in _pumps)
            await pump.DisposeAsync();
        await Group.DisposeAsync();
        foreach (var media in _media)
            media.Dispose();
    }
}
