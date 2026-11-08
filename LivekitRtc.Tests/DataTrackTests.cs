// author: https://github.com/pabloFuente

using Xunit.Abstractions;

namespace LiveKit.Rtc.Tests;

/// <summary>
/// End-to-end tests for data tracks: publish, subscribe, push frames, unpublish.
/// </summary>
[Collection("RtcTests")]
public class DataTrackTests
{
    private readonly RtcTestFixture _fixture;
    private readonly ITestOutputHelper _output;

    public DataTrackTests(RtcTestFixture fixture, ITestOutputHelper output)
    {
        _fixture = fixture;
        _output = output;
    }

    private void Log(string message)
    {
        _output.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] {message}");
    }

    [Fact]
    public async Task PublishDataTrack_SubscriberReceivesFrames_StreamEndsOnUnpublish()
    {
        const string roomName = "test-data-track-room";
        const string publisherIdentity = "dt-publisher";
        const string subscriberIdentity = "dt-subscriber";

        using var publisherRoom = new Room();
        using var subscriberRoom = new Room();

        var publishedTcs = new TaskCompletionSource<RemoteDataTrack>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        var unpublishedTcs = new TaskCompletionSource<DataTrackUnpublishedEventArgs>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        subscriberRoom.DataTrackPublished += (_, track) =>
        {
            Log(
                $"DataTrackPublished: {track.Info.Name} ({track.Info.Sid}) by {track.PublisherIdentity}"
            );
            publishedTcs.TrySetResult(track);
        };
        subscriberRoom.DataTrackUnpublished += (_, args) =>
        {
            Log($"DataTrackUnpublished: {args.Sid}");
            unpublishedTcs.TrySetResult(args);
        };

        await _fixture.ConnectRoomsAndWaitForEvents(
            publisherRoom,
            subscriberRoom,
            _fixture.CreateToken(publisherIdentity, roomName),
            _fixture.CreateToken(subscriberIdentity, roomName),
            subscriberIdentity,
            Log
        );

        using var localTrack = await publisherRoom.LocalParticipant!.PublishDataTrackAsync(
            "telemetry"
        );
        Log($"Published data track {localTrack.Info.Sid}");
        Assert.Equal("telemetry", localTrack.Info.Name);
        Assert.NotEmpty(localTrack.Info.Sid);
        Assert.True(localTrack.IsPublished());

        var remoteTrack = await publishedTcs.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal("telemetry", remoteTrack.Info.Name);
        Assert.Equal(localTrack.Info.Sid, remoteTrack.Info.Sid);
        Assert.Equal(publisherIdentity, remoteTrack.PublisherIdentity);
        Assert.True(remoteTrack.IsPublished());
        Assert.Contains(remoteTrack.Info.Sid, subscriberRoom.RemoteDataTracks.Keys);

        using var stream = remoteTrack.Subscribe();

        // Frames pushed before the subscription is established are not buffered, so keep pushing
        // numbered frames until the reader has collected enough of them.
        const int wanted = 5;
        var received = new List<DataTrackFrame>();
        var readTask = Task.Run(async () =>
        {
            await foreach (var frame in stream)
            {
                received.Add(frame);
                if (received.Count >= wanted)
                    break;
            }
        });

        for (var i = 0; !readTask.IsCompleted && i < 200; i++)
        {
            localTrack.TryPush(new DataTrackFrame(BitConverter.GetBytes(i), (ulong)i));
            await Task.Delay(50);
        }
        await readTask.WaitAsync(TimeSpan.FromSeconds(10));
        Log($"Received {received.Count} frames");

        Assert.True(received.Count >= wanted);
        var previous = -1;
        foreach (var frame in received)
        {
            var sequence = BitConverter.ToInt32(frame.Payload, 0);
            Assert.True(
                sequence > previous,
                $"Frames must arrive in order ({sequence} after {previous})"
            );
            Assert.Equal((ulong)sequence, frame.UserTimestamp);
            previous = sequence;
        }

        // Unpublishing raises DataTrackUnpublished on the subscriber and ends its stream
        localTrack.Unpublish();
        var unpublished = await unpublishedTcs.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(localTrack.Info.Sid, unpublished.Sid);
        Assert.Same(remoteTrack, unpublished.Track);
        Assert.False(localTrack.IsPublished());
        Assert.DoesNotContain(remoteTrack.Info.Sid, subscriberRoom.RemoteDataTracks.Keys);

        using var eosCts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        DataTrackFrame? remaining;
        do
        {
            remaining = await stream.ReadAsync(eosCts.Token);
        } while (remaining != null);
        Assert.True(stream.IsEnded);
        Log("Stream ended after unpublish");

        var pushAfter = Assert.Throws<PushFrameException>(() =>
            localTrack.TryPush(new DataTrackFrame(new byte[] { 1 }))
        );
        Log($"Push after unpublish failed as expected: {pushAfter.Code}: {pushAfter.Message}");

        await publisherRoom.DisconnectAsync();
        await subscriberRoom.DisconnectAsync();
    }

    [Fact]
    public async Task RemoteDataTrack_PipelineOptions_KeepFramesFlowing_AndDisposeReleasesTheTrack()
    {
        const string roomName = "test-data-track-pipeline-room";
        const string subscriberIdentity = "dt-pipeline-subscriber";

        using var publisherRoom = new Room();
        using var subscriberRoom = new Room();
        var publishedTcs = new TaskCompletionSource<RemoteDataTrack>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        subscriberRoom.DataTrackPublished += (_, track) => publishedTcs.TrySetResult(track);

        await _fixture.ConnectRoomsAndWaitForEvents(
            publisherRoom,
            subscriberRoom,
            _fixture.CreateToken("dt-pipeline-publisher", roomName),
            _fixture.CreateToken(subscriberIdentity, roomName),
            subscriberIdentity,
            Log
        );

        using var localTrack = await publisherRoom.LocalParticipant!.PublishDataTrackAsync(
            "pipeline"
        );
        var remoteTrack = await publishedTcs.Task.WaitAsync(TimeSpan.FromSeconds(10));

        remoteTrack.SetPipelineOptions(maxPartialFrames: 4);
        var stream = remoteTrack.Subscribe();

        // Frames pushed before the subscription is established are dropped: push until one arrives
        var read = stream.ReadAsync();
        for (var i = 0; !read.IsCompleted && i < 200; i++)
        {
            localTrack.TryPush(new DataTrackFrame(new byte[] { 42 }));
            await Task.Delay(50);
        }
        var frame = await read.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.NotNull(frame);
        Assert.Equal(new byte[] { 42 }, frame.Payload);

        await stream.DisposeAsync();
        await stream.DisposeAsync();
        Assert.True(stream.IsEnded);
        await Assert.ThrowsAsync<ObjectDisposedException>(() => stream.ReadAsync());

        remoteTrack.Dispose();
        remoteTrack.Dispose();
        Assert.Throws<ObjectDisposedException>(() => remoteTrack.SetPipelineOptions());
        Assert.Throws<ObjectDisposedException>(() => remoteTrack.Subscribe());
        Assert.Throws<ObjectDisposedException>(() => remoteTrack.IsPublished());

        await publisherRoom.DisconnectAsync();
        await subscriberRoom.DisconnectAsync();
    }
}
