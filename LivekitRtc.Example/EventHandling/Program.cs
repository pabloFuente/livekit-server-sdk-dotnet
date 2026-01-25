using System;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using LiveKit.Rtc;

var url = args.Length > 0 ? args[0] : Environment.GetEnvironmentVariable("LIVEKIT_URL");
var token = args.Length > 1 ? args[1] : Environment.GetEnvironmentVariable("LIVEKIT_TOKEN");

if (string.IsNullOrEmpty(url) || string.IsNullOrEmpty(token))
{
    Console.WriteLine("Usage: dotnet run <LIVEKIT_URL> <ACCESS_TOKEN>");
    return 1;
}

Console.WriteLine("=== LiveKit RTC - Event Handling Example ===\n");
Console.WriteLine("Demonstrating all major room events. Press Ctrl+C to exit.\n");

var room = new Room();
var cts = new CancellationTokenSource();

Console.CancelKeyPress += (sender, e) =>
{
    e.Cancel = true;
    cts.Cancel();
};

// Register all event handlers
room.Connected += (sender, e) =>
{
    Console.WriteLine($"[Connected] Connected to room");
};

room.Disconnected += (sender, reason) =>
{
    Console.WriteLine($"[Disconnected] Reason: {reason}");
};

room.Reconnecting += (sender, e) =>
{
    Console.WriteLine($"[Reconnecting] Attempting to reconnect...");
};

room.Reconnected += (sender, e) =>
{
    Console.WriteLine($"[Reconnected] Successfully reconnected");
};

room.ParticipantConnected += (sender, participant) =>
{
    Console.WriteLine(
        $"[ParticipantConnected] {participant.Identity} joined (SID: {participant.Sid})"
    );
};

room.ParticipantDisconnected += (sender, participant) =>
{
    Console.WriteLine($"[ParticipantDisconnected] {participant.Identity} left");
};

room.LocalTrackPublished += (sender, e) =>
{
    Console.WriteLine(
        $"[LocalTrackPublished] {e.Publication.Track?.Kind} track published (SID: {e.Publication.Sid})"
    );
};

room.LocalTrackUnpublished += (sender, e) =>
{
    Console.WriteLine($"[LocalTrackUnpublished] Track unpublished (SID: {e.Publication.Sid})");
};

room.TrackPublished += (sender, e) =>
{
    Console.WriteLine(
        $"[TrackPublished] {e.Participant.Identity} published {e.Publication.Kind} track"
    );
};

room.TrackUnpublished += (sender, e) =>
{
    Console.WriteLine(
        $"[TrackUnpublished] {e.Participant.Identity} unpublished {e.Publication.Kind} track"
    );
};

room.TrackSubscribed += (sender, e) =>
{
    Console.WriteLine(
        $"[TrackSubscribed] Subscribed to {e.Track.Kind} from {e.Participant.Identity}"
    );
};

room.TrackUnsubscribed += (sender, e) =>
{
    Console.WriteLine(
        $"[TrackUnsubscribed] Unsubscribed from {e.Track.Kind} from {e.Participant.Identity}"
    );
};

room.TrackMuted += (sender, e) =>
{
    Console.WriteLine($"[TrackMuted] {e.Participant.Identity}'s {e.Publication.Kind} track muted");
};

room.TrackUnmuted += (sender, e) =>
{
    Console.WriteLine(
        $"[TrackUnmuted] {e.Participant.Identity}'s {e.Publication.Kind} track unmuted"
    );
};

room.ActiveSpeakersChanged += (sender, e) =>
{
    var speakers = string.Join(", ", e.Speakers.Select(p => p.Identity));
    Console.WriteLine($"[ActiveSpeakersChanged] Active speakers: {speakers}");
};

room.ConnectionQualityChanged += (sender, e) =>
{
    Console.WriteLine($"[ConnectionQualityChanged] {e.Participant.Identity}: {e.Quality}");
};

room.DataReceived += (sender, e) =>
{
    var message = Encoding.UTF8.GetString(e.Data);
    var from = e.Participant?.Identity ?? "server";
    Console.WriteLine($"[DataReceived] From {from}: {message}");
};

room.ConnectionStateChanged += (sender, state) =>
{
    Console.WriteLine($"[ConnectionStateChanged] State: {state}");
};

room.RoomMetadataChanged += (sender, metadata) =>
{
    Console.WriteLine($"[RoomMetadataChanged] New metadata: {metadata}");
};

room.ParticipantMetadataChanged += (sender, participant) =>
{
    Console.WriteLine(
        $"[ParticipantMetadataChanged] {participant.Identity}: {participant.Metadata}"
    );
};

room.ParticipantNameChanged += (sender, participant) =>
{
    Console.WriteLine(
        $"[ParticipantNameChanged] {participant.Identity} changed name to: {participant.Name}"
    );
};

try
{
    // Connect to room
    await room.ConnectAsync(url, token, new RoomOptions { AutoSubscribe = true });
    Console.WriteLine($"\n✓ Connected to room: {room.Name}");
    Console.WriteLine($"  Local participant: {room.LocalParticipant?.Identity}\n");

    // Keep running until cancelled
    Console.WriteLine("Monitoring events... (Press Ctrl+C to exit)\n");
    await Task.Delay(Timeout.Infinite, cts.Token);
}
catch (OperationCanceledException)
{
    Console.WriteLine("\nShutting down...");
}
catch (Exception ex)
{
    Console.WriteLine($"\n✗ Error: {ex.Message}");
    return 1;
}
finally
{
    await room.DisconnectAsync();
    room.Dispose();
    Console.WriteLine("✓ Disconnected");
}

return 0;
