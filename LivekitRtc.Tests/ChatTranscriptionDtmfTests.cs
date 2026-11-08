using System.Threading.Channels;
using Livekit.Server.Sdk.Dotnet;
using Xunit.Abstractions;

namespace LiveKit.Rtc.Tests;

/// <summary>
/// End-to-end tests of the messages a local participant sends besides data packets and streams:
/// chat messages, transcriptions and SIP DTMF tones, each received by another participant as its
/// room event.
/// </summary>
[Collection("RtcTests")]
public class ChatTranscriptionDtmfTests : IAsyncLifetime
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    private readonly RtcTestFixture _fixture;
    private readonly ITestOutputHelper _output;
    private readonly Room _receiver = new();
    private readonly Room _sender = new();

    public ChatTranscriptionDtmfTests(RtcTestFixture fixture, ITestOutputHelper output)
    {
        _fixture = fixture;
        _output = output;
    }

    private void Log(string message) =>
        _output.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] {message}");

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        await _sender.DisconnectAsync();
        _sender.Dispose();
        await _receiver.DisconnectAsync();
        _receiver.Dispose();
    }

    private LocalParticipant Sender => _sender.LocalParticipant!;

    // The receiver connects first and waits until it sees the sender
    private Task ConnectAsync(string roomName, string? senderToken = null) =>
        _fixture.ConnectRoomsAndWaitForEvents(
            _receiver,
            _sender,
            _fixture.CreateToken("receiver", roomName),
            senderToken ?? _fixture.CreateToken("sender", roomName),
            "sender",
            Log,
            timeoutSeconds: 10
        );

    [Fact]
    public async Task ChatMessage_SentThenEdited_ReachesTheOtherParticipant()
    {
        var received = Channel.CreateUnbounded<ChatMessageReceivedEventArgs>();
        _receiver.ChatMessageReceived += (_, e) => received.Writer.TryWrite(e);
        await ConnectAsync("chat-message-room");

        var sent = await Sender.SendChatMessageAsync("hello");
        Assert.Equal("hello", sent.Message);
        Assert.NotEmpty(sent.Id);

        var first = await received.Reader.ReadAsync().AsTask().WaitAsync(Timeout);
        Assert.Equal(sent.Id, first.Message.Id);
        Assert.Equal("hello", first.Message.Message);
        Assert.Equal("sender", first.Participant?.Identity);

        var edited = await Sender.EditChatMessageAsync("hello, edited", sent);
        Assert.Equal(sent.Id, edited.Id);
        Assert.Equal("hello, edited", edited.Message);

        var second = await received.Reader.ReadAsync().AsTask().WaitAsync(Timeout);
        Assert.Equal(sent.Id, second.Message.Id);
        Assert.Equal("hello, edited", second.Message.Message);

        await Assert.ThrowsAsync<ArgumentException>(() => Sender.SendChatMessageAsync(""));
        await Assert.ThrowsAsync<ArgumentException>(() => Sender.EditChatMessageAsync("", sent));
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            Sender.EditChatMessageAsync("text", null!)
        );
    }

    [Fact]
    public async Task Transcription_OfAPublishedTrack_ReachesTheOtherParticipant()
    {
        var received = new TaskCompletionSource<TranscriptionReceivedEventArgs>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        var trackPublished = new TaskCompletionSource<string>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        _receiver.TranscriptionReceived += (_, e) => received.TrySetResult(e);
        _receiver.TrackPublished += (_, e) => trackPublished.TrySetResult(e.Publication.Sid);
        // LiveKit only forwards the transcriptions of agents
        const string roomName = "transcription-room";
        var agentToken = new AccessToken(RtcTestFixture.API_KEY, RtcTestFixture.API_SECRET)
            .WithIdentity("sender")
            .WithKind(ParticipantInfo.Types.Kind.Agent)
            .WithGrants(new VideoGrants { RoomJoin = true, Room = roomName })
            .ToJwt();
        await ConnectAsync(roomName, agentToken);

        // A transcription names the track it transcribes, which the receiver must know
        using var audioSource = new AudioSource(48000, 1);
        using var audioTrack = LocalAudioTrack.Create("transcribed-audio", audioSource);
        var publication = await Sender.PublishTrackAsync(audioTrack);
        Assert.Equal(publication.Sid, await trackPublished.Task.WaitAsync(Timeout));

        var segment = new Proto.TranscriptionSegment
        {
            Id = "segment-1",
            Text = "hello world",
            StartTime = 0,
            EndTime = 1000,
            Final = true,
            Language = "en",
        };
        await Sender.PublishTranscriptionAsync(
            new Transcription("sender", publication.Sid, new[] { segment })
        );

        var transcription = await received.Task.WaitAsync(Timeout);
        Assert.Equal("sender", transcription.Participant?.Identity);
        Assert.Equal(publication.Sid, transcription.TrackSid);
        var receivedSegment = Assert.Single(transcription.Segments);
        Assert.Equal("segment-1", receivedSegment.Id);
        Assert.Equal("hello world", receivedSegment.Text);
        Assert.Equal(1000ul, receivedSegment.EndTime);
        Assert.True(receivedSegment.Final);
        Assert.Equal("en", receivedSegment.Language);

        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            Sender.PublishTranscriptionAsync(null!)
        );
    }

    [Fact]
    public async Task SipDtmf_ReachesTheOtherParticipant()
    {
        var received = new TaskCompletionSource<SipDtmfReceivedEventArgs>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        _receiver.SipDtmfReceived += (_, e) => received.TrySetResult(e);
        await ConnectAsync("sip-dtmf-room");

        await Sender.PublishSipDtmfAsync(5, "5");

        var dtmf = await received.Task.WaitAsync(Timeout);
        Assert.Equal(5u, dtmf.Code);
        Assert.Equal("5", dtmf.Digit);
        Assert.Equal("sender", dtmf.Participant.Identity);
    }
}
