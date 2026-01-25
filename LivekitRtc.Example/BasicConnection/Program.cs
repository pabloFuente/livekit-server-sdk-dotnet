using System;
using System.Threading.Tasks;
using LiveKit.Rtc;

// Get connection parameters from arguments or environment variables
var url = args.Length > 0 ? args[0] : Environment.GetEnvironmentVariable("LIVEKIT_URL");
var token = args.Length > 1 ? args[1] : Environment.GetEnvironmentVariable("LIVEKIT_TOKEN");

if (string.IsNullOrEmpty(url) || string.IsNullOrEmpty(token))
{
    Console.WriteLine("Usage: dotnet run <LIVEKIT_URL> <ACCESS_TOKEN>");
    Console.WriteLine("Or set LIVEKIT_URL and LIVEKIT_TOKEN environment variables");
    return 1;
}

Console.WriteLine("=== LiveKit RTC - Basic Connection Example ===\n");

var room = new Room();

try
{
    // Connect to the room
    Console.WriteLine($"Connecting to {url}...");
    await room.ConnectAsync(url, token, new RoomOptions { AutoSubscribe = true });

    Console.WriteLine($"✓ Connected successfully!");
    Console.WriteLine($"  Room Name: {room.Name}");
    Console.WriteLine($"  Room SID: {room.Sid}");
    Console.WriteLine($"  Local Participant: {room.LocalParticipant?.Identity}");
    Console.WriteLine($"  Participants in room: {room.NumParticipants}");
    Console.WriteLine($"  Connection State: {room.ConnectionState}");

    // List remote participants
    if (room.RemoteParticipants.Count > 0)
    {
        Console.WriteLine("\nRemote participants:");
        foreach (var participant in room.RemoteParticipants.Values)
        {
            Console.WriteLine($"  - {participant.Identity} (SID: {participant.Sid})");
        }
    }
    else
    {
        Console.WriteLine("\nNo other participants in the room.");
    }

    // Stay connected for a few seconds
    Console.WriteLine("\nStaying connected for 5 seconds...");
    await Task.Delay(5000);

    // Disconnect
    Console.WriteLine("Disconnecting...");
    await room.DisconnectAsync();
    Console.WriteLine("✓ Disconnected successfully!");

    return 0;
}
catch (Exception ex)
{
    Console.WriteLine($"✗ Error: {ex.Message}");
    return 1;
}
finally
{
    room.Dispose();
}
