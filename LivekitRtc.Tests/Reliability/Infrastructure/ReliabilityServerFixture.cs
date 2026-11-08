// author: https://github.com/pabloFuente

using System.Net;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Livekit.Server.Sdk.Dotnet;

namespace LiveKit.Rtc.Tests.Reliability;

/// <summary>
/// A LiveKit server of its own for the reliability tests: one container, single-node (no Redis),
/// no browser. It starts in a couple of seconds, and the outage tests can pause it without
/// affecting the e2e tests that share <see cref="RtcTestFixture"/>. The server's participant list
/// is how the tests check, from outside the SDK, that a room the SDK gave up on really left.
/// </summary>
public sealed class ReliabilityServerFixture : IAsyncLifetime
{
    private const string Image = "livekit/livekit-server:latest";

    private IContainer? _server;

    public string Url { get; private set; } = string.Empty;

    public RoomServiceClient RoomService { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        _server = new ContainerBuilder(Image)
            .WithName($"livekit-reliability-{Guid.NewGuid():N}")
            .WithEnvironment(
                "LIVEKIT_KEYS",
                $"{RtcTestFixture.API_KEY}: {RtcTestFixture.API_SECRET}"
            )
            .WithPortBinding(7880, true)
            .WithPortBinding(7881, true)
            .WithWaitStrategy(
                Wait.ForUnixContainer()
                    .UntilHttpRequestIsSucceeded(r =>
                        r.ForPort(7880).ForPath("/").ForStatusCode(HttpStatusCode.OK)
                    )
            )
            .Build();
        await _server.StartAsync();

        Url = $"ws://{_server.Hostname}:{_server.GetMappedPublicPort(7880)}";
        RoomService = new RoomServiceClient(
            Url.Replace("ws://", "http://"),
            RtcTestFixture.API_KEY,
            RtcTestFixture.API_SECRET
        );
    }

    public async Task DisposeAsync()
    {
        if (_server != null)
            await _server.DisposeAsync();
    }

    /// <summary>
    /// A room name no other test uses.
    /// </summary>
    public static string NewRoomName(string prefix) =>
        $"{(prefix.Length > 24 ? prefix[..24] : prefix)}-{Guid.NewGuid().ToString("N")[..12]}";

    public string Token(string identity, string room, bool canUpdateOwnMetadata = false) =>
        new AccessToken(RtcTestFixture.API_KEY, RtcTestFixture.API_SECRET)
            .WithIdentity(identity)
            .WithName(identity)
            .WithGrants(
                new VideoGrants
                {
                    RoomJoin = true,
                    Room = room,
                    CanUpdateOwnMetadata = canUpdateOwnMetadata,
                }
            )
            .ToJwt();

    /// <summary>
    /// Connects a new <see cref="Room"/> as <paramref name="identity"/>.
    /// </summary>
    public async Task<Room> ConnectAsync(
        string identity,
        string roomName,
        RoomOptions? options = null
    )
    {
        var room = new Room();
        try
        {
            await room.ConnectAsync(Url, Token(identity, roomName), options);
            return room;
        }
        catch
        {
            room.Dispose();
            throw;
        }
    }

    /// <summary>
    /// The identities the server lists in <paramref name="roomName"/> (none once it is gone).
    /// </summary>
    public async Task<string[]> IdentitiesAsync(string roomName)
    {
        try
        {
            var listed = await RoomService.ListParticipants(
                new ListParticipantsRequest { Room = roomName }
            );
            return listed.Participants.Select(p => p.Identity).OrderBy(i => i).ToArray();
        }
        catch (TwirpException e) when (e.Type == Twirp.ErrorCode.Not_Found)
        {
            return Array.Empty<string>();
        }
    }

    /// <summary>
    /// The SIDs of the tracks the server lists for <paramref name="identity"/>.
    /// </summary>
    public async Task<string[]> TrackSidsAsync(string roomName, string identity)
    {
        try
        {
            var listed = await RoomService.ListParticipants(
                new ListParticipantsRequest { Room = roomName }
            );
            return listed
                .Participants.Where(p => p.Identity == identity)
                .SelectMany(p => p.Tracks)
                .Select(t => t.Sid)
                .OrderBy(s => s)
                .ToArray();
        }
        catch (TwirpException e) when (e.Type == Twirp.ErrorCode.Not_Found)
        {
            return Array.Empty<string>();
        }
    }

    /// <summary>
    /// How long the server may take to drop a participant that left. A clean disconnection is seen
    /// at once, but a leave request that does not get through (a room closed during a negotiation
    /// or a reconnection) is only noticed at the server's own timeout, which passes 30 s on a loaded
    /// CI runner. A room the SDK never closed stays listed however long this is.
    /// </summary>
    public static readonly TimeSpan DepartureTimeout = TimeSpan.FromSeconds(60);

    /// <summary>
    /// Waits until the server lists nobody in <paramref name="roomName"/>, within
    /// <see cref="DepartureTimeout"/>.
    /// </summary>
    public Task WaitUntilEmptyAsync(string roomName) =>
        Eventually.TrueAsync(
            async () => (await IdentitiesAsync(roomName)).Length == 0,
            DepartureTimeout,
            $"the server lists nobody in {roomName}"
        );

    /// <summary>
    /// Freezes the server process (<c>docker pause</c>): connections stay open but nothing is
    /// answered, like a server that hangs or a network that silently drops packets.
    /// </summary>
    public Task PauseAsync() => _server!.PauseAsync();

    public Task UnpauseAsync() => _server!.UnpauseAsync();
}
