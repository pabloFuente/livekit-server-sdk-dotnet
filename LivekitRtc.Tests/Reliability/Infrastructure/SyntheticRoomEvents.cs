// author: https://github.com/pabloFuente

using System.Reflection;
using Google.Protobuf;
using LiveKit.Proto;
using LiveKit.Rtc.Internal;

namespace LiveKit.Rtc.Tests.Reliability;

/// <summary>
/// Replays room event sequences into a connected <see cref="Room"/> through the entry point the
/// native library calls (<c>FfiClient.OnFfiCallback</c>), to check how the .NET layer applies an
/// order the FFI is known to produce. The participants and tracks of a replayed sequence are fake
/// (the server knows nothing about them), but their handles are real native handles that no wrapper
/// owns, so the room can wrap and release them like real ones, and a test can check they were
/// released: once released, the native library no longer knows the handle.
/// </summary>
internal sealed class SyntheticRoomEvents
{
    private readonly ulong _roomHandle;

    public SyntheticRoomEvents(Room room)
    {
        var field =
            typeof(Room).GetField("_roomHandle", BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new InvalidOperationException("Room._roomHandle was renamed");
        _roomHandle =
            ((FfiHandle?)field.GetValue(room))?.HandleId
            ?? throw new InvalidOperationException("The room is not connected");
    }

    /// <summary>
    /// A native handle no wrapper owns (the handle of an audio source: the room only wraps and
    /// releases the handles of the events it receives, whatever they designate).
    /// </summary>
    public static ulong NewNativeHandle() =>
        FfiClient
            .Instance.SendRequest(
                new FfiRequest
                {
                    NewAudioSource = new NewAudioSourceRequest
                    {
                        Type = AudioSourceType.AudioSourceNative,
                        SampleRate = 48000,
                        NumChannels = 1,
                        QueueSizeMs = 50,
                    },
                }
            )
            .NewAudioSource.Source.Handle.Id;

    /// <summary>
    /// Whether the native library still knows <paramref name="handleId"/>. Releases it if so,
    /// so call it once, for a handle the room was expected to release.
    /// </summary>
    public static bool WasReleased(ulong handleId) => !NativeMethods.DropHandle(handleId);

    public void Deliver(RoomEvent roomEvent)
    {
        roomEvent.RoomHandle = _roomHandle;
        DeliverFfiEvent(new FfiEvent { RoomEvent = roomEvent });
    }

    /// <summary>
    /// Delivers <paramref name="ffiEvent"/> on the calling thread, as the native library does.
    /// </summary>
    public static void DeliverFfiEvent(FfiEvent ffiEvent)
    {
        var data = ffiEvent.ToByteArray();
        var callback =
            typeof(FfiClient).GetMethod(
                "OnFfiCallback",
                BindingFlags.NonPublic | BindingFlags.Instance
            ) ?? throw new InvalidOperationException("FfiClient.OnFfiCallback was renamed");
        unsafe
        {
            fixed (byte* pointer = data)
                callback.Invoke(
                    FfiClient.Instance,
                    new object[] { (IntPtr)pointer, (UIntPtr)data.Length }
                );
        }
    }

    public void Deliver(params RoomEvent[] events)
    {
        foreach (var roomEvent in events)
            Deliver(roomEvent);
    }

    public static RoomEvent ParticipantConnected(string identity, string sid, ulong handle) =>
        new()
        {
            ParticipantConnected = new ParticipantConnected
            {
                Info = new OwnedParticipant
                {
                    Handle = new FfiOwnedHandle { Id = handle },
                    Info = new ParticipantInfo
                    {
                        Sid = sid,
                        Name = identity,
                        Identity = identity,
                        State = ParticipantState.Active,
                        Metadata = "",
                        Kind = ParticipantKind.Standard,
                        DisconnectReason = DisconnectReason.UnknownReason,
                        JoinedAt = 0,
                        ClientProtocol = 0,
                    },
                },
            },
        };

    public static RoomEvent ParticipantDisconnected(string identity) =>
        new()
        {
            ParticipantDisconnected = new ParticipantDisconnected
            {
                ParticipantIdentity = identity,
                DisconnectReason = DisconnectReason.ClientInitiated,
            },
        };

    public static RoomEvent TrackPublished(string identity, string trackSid, ulong handle) =>
        new()
        {
            TrackPublished = new TrackPublished
            {
                ParticipantIdentity = identity,
                Publication = new OwnedTrackPublication
                {
                    Handle = new FfiOwnedHandle { Id = handle },
                    Info = new TrackPublicationInfo
                    {
                        Sid = trackSid,
                        Name = trackSid,
                        Kind = TrackKind.KindAudio,
                        Source = TrackSource.SourceMicrophone,
                        Simulcasted = false,
                        Width = 0,
                        Height = 0,
                        MimeType = "audio/opus",
                        Muted = false,
                        Remote = true,
                        EncryptionType = EncryptionType.None,
                    },
                },
            },
        };

    public static RoomEvent TrackUnpublished(string identity, string trackSid) =>
        new()
        {
            TrackUnpublished = new TrackUnpublished
            {
                ParticipantIdentity = identity,
                PublicationSid = trackSid,
            },
        };

    public static RoomEvent TrackSubscribed(string identity, string trackSid, ulong handle) =>
        new()
        {
            TrackSubscribed = new TrackSubscribed
            {
                ParticipantIdentity = identity,
                Track = new OwnedTrack
                {
                    Handle = new FfiOwnedHandle { Id = handle },
                    Info = new TrackInfo
                    {
                        Sid = trackSid,
                        Name = trackSid,
                        Kind = TrackKind.KindAudio,
                        StreamState = StreamState.StateActive,
                        Muted = false,
                        Remote = true,
                    },
                },
            },
        };

    public static RoomEvent TrackUnsubscribed(string identity, string trackSid) =>
        new()
        {
            TrackUnsubscribed = new TrackUnsubscribed
            {
                ParticipantIdentity = identity,
                TrackSid = trackSid,
            },
        };

    /// <summary>
    /// A room metadata change: raised through the same FIFO chain as the other events, so once
    /// it was raised, every event delivered before it was processed.
    /// </summary>
    public static RoomEvent Sentinel(string marker) =>
        new() { RoomMetadataChanged = new RoomMetadataChanged { Metadata = marker } };
}
