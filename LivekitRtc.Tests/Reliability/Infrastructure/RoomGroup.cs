// author: https://github.com/pabloFuente

using System.Text;

namespace LiveKit.Rtc.Tests.Reliability;

/// <summary>
/// Several participants connected to one LiveKit room, each already listing all the others, so
/// data sent right away reaches everyone. Disposing the group disposes every room.
/// </summary>
internal sealed class RoomGroup : IAsyncDisposable
{
    private readonly Dictionary<string, Room> _rooms;

    private RoomGroup(string name, Dictionary<string, Room> rooms)
    {
        Name = name;
        _rooms = rooms;
    }

    public string Name { get; }

    public Room this[string identity] => _rooms[identity];

    /// <summary>
    /// Connects one room per identity. <paramref name="configure"/> runs on each room before it
    /// connects, to subscribe to events that the connection already raises.
    /// </summary>
    public static async Task<RoomGroup> ConnectAsync(
        ReliabilityServerFixture server,
        string roomName,
        string[] identities,
        Action<string, Room>? configure = null
    )
    {
        var rooms = new Dictionary<string, Room>();
        try
        {
            foreach (var identity in identities)
            {
                var room = new Room();
                rooms[identity] = room;
                configure?.Invoke(identity, room);
                await room.ConnectAsync(server.Url, server.Token(identity, roomName));
            }
            await Eventually.TrueAsync(
                () =>
                    rooms.All(r =>
                        identities
                            .Where(i => i != r.Key)
                            .All(i => r.Value.RemoteParticipants.ContainsKey(i))
                    ),
                TimeSpan.FromSeconds(15),
                $"every participant of {roomName} sees the others"
            );
            return new RoomGroup(roomName, rooms);
        }
        catch
        {
            foreach (var room in rooms.Values)
                room.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Sends reliable data packets "0", "1", ... to <paramref name="to"/>, one after the other.
    /// </summary>
    public async Task SendSequenceAsync(
        string from,
        string to,
        int count,
        string topic = "seq",
        CancellationToken stop = default
    )
    {
        var options = new DataPublishOptions
        {
            Reliable = true,
            Topic = topic,
            DestinationIdentities = new[] { to },
        };
        for (var i = 0; i < count && !stop.IsCancellationRequested; i++)
            await _rooms[from]
                .LocalParticipant!.PublishDataAsync(Encoding.UTF8.GetBytes($"{i}"), options);
    }

    public static int SequenceNumber(DataReceivedEventArgs e) =>
        int.Parse(Encoding.UTF8.GetString(e.Data));

    public async ValueTask DisposeAsync()
    {
        await Task.WhenAll(
            _rooms.Values.Select(room =>
                Task.Run(async () =>
                {
                    await room.DisconnectAsync();
                    room.Dispose();
                })
            )
        );
    }
}
