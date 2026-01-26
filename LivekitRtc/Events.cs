// author: https://github.com/pabloFuente

using System;
using System.Collections.Generic;

namespace LiveKit.Rtc
{
    /// <summary>
    /// Event arguments for local track published events.
    /// </summary>
    public class LocalTrackPublishedEventArgs : EventArgs
    {
        /// <summary>
        /// The publication that was published/unpublished.
        /// </summary>
        public LocalTrackPublication Publication { get; }

        /// <summary>
        /// The local participant.
        /// </summary>
        public LocalParticipant Participant { get; }

        internal LocalTrackPublishedEventArgs(
            LocalTrackPublication publication,
            LocalParticipant participant
        )
        {
            Publication = publication;
            Participant = participant;
        }
    }

    /// <summary>
    /// Event arguments for remote track published events.
    /// </summary>
    public class TrackPublishedEventArgs : EventArgs
    {
        /// <summary>
        /// The publication that was published/unpublished.
        /// </summary>
        public RemoteTrackPublication Publication { get; }

        /// <summary>
        /// The remote participant.
        /// </summary>
        public RemoteParticipant Participant { get; }

        internal TrackPublishedEventArgs(
            RemoteTrackPublication publication,
            RemoteParticipant participant
        )
        {
            Publication = publication;
            Participant = participant;
        }
    }

    /// <summary>
    /// Event arguments for track subscribed events.
    /// </summary>
    public class TrackSubscribedEventArgs : EventArgs
    {
        /// <summary>
        /// The subscribed track.
        /// </summary>
        public RemoteTrack Track { get; }

        /// <summary>
        /// The track publication.
        /// </summary>
        public RemoteTrackPublication Publication { get; }

        /// <summary>
        /// The remote participant.
        /// </summary>
        public RemoteParticipant Participant { get; }

        internal TrackSubscribedEventArgs(
            RemoteTrack track,
            RemoteTrackPublication publication,
            RemoteParticipant participant
        )
        {
            Track = track;
            Publication = publication;
            Participant = participant;
        }
    }

    /// <summary>
    /// Event arguments for track muted events.
    /// </summary>
    public class TrackMutedEventArgs : EventArgs
    {
        /// <summary>
        /// The track publication.
        /// </summary>
        public TrackPublication Publication { get; }

        /// <summary>
        /// The participant.
        /// </summary>
        public Participant Participant { get; }

        internal TrackMutedEventArgs(TrackPublication publication, Participant participant)
        {
            Publication = publication;
            Participant = participant;
        }
    }

    /// <summary>
    /// Event arguments for active speakers changed events.
    /// </summary>
    public class ActiveSpeakersChangedEventArgs : EventArgs
    {
        /// <summary>
        /// The list of active speakers.
        /// </summary>
        public IReadOnlyList<Participant> Speakers { get; }

        internal ActiveSpeakersChangedEventArgs(IReadOnlyList<Participant> speakers)
        {
            Speakers = speakers;
        }
    }

    /// <summary>
    /// Event arguments for connection quality changed events.
    /// </summary>
    public class ConnectionQualityChangedEventArgs : EventArgs
    {
        /// <summary>
        /// The new connection quality.
        /// </summary>
        public Proto.ConnectionQuality Quality { get; }

        /// <summary>
        /// The participant whose connection quality changed.
        /// </summary>
        public Participant Participant { get; }

        internal ConnectionQualityChangedEventArgs(
            Proto.ConnectionQuality quality,
            Participant participant
        )
        {
            Quality = quality;
            Participant = participant;
        }
    }

    /// <summary>
    /// Event arguments for data received events.
    /// </summary>
    public class DataReceivedEventArgs : EventArgs
    {
        /// <summary>
        /// The received data.
        /// </summary>
        public byte[] Data { get; }

        /// <summary>
        /// The participant who sent the data (null if from server).
        /// </summary>
        public Participant? Participant { get; }

        /// <summary>
        /// The data packet kind.
        /// </summary>
        public Proto.DataPacketKind Kind { get; }

        /// <summary>
        /// The topic of the data message.
        /// </summary>
        public string? Topic { get; }

        internal DataReceivedEventArgs(
            byte[] data,
            Participant? participant,
            Proto.DataPacketKind kind,
            string? topic
        )
        {
            Data = data;
            Participant = participant;
            Kind = kind;
            Topic = topic;
        }
    }

    /// <summary>
    /// Event arguments for local track subscribed events.
    /// </summary>
    public class LocalTrackSubscribedEventArgs : EventArgs
    {
        /// <summary>
        /// The local track publication that was subscribed.
        /// </summary>
        public LocalTrackPublication Publication { get; }

        internal LocalTrackSubscribedEventArgs(LocalTrackPublication publication)
        {
            Publication = publication;
        }
    }

    /// <summary>
    /// Event arguments for track subscription failed events.
    /// </summary>
    public class TrackSubscriptionFailedEventArgs : EventArgs
    {
        /// <summary>
        /// The SID of the track that failed to subscribe.
        /// </summary>
        public string TrackSid { get; }

        /// <summary>
        /// The participant whose track failed to subscribe.
        /// </summary>
        public RemoteParticipant Participant { get; }

        /// <summary>
        /// The error message describing why the subscription failed.
        /// </summary>
        public string Error { get; }

        internal TrackSubscriptionFailedEventArgs(
            string trackSid,
            RemoteParticipant participant,
            string error
        )
        {
            TrackSid = trackSid;
            Participant = participant;
            Error = error;
        }
    }

    /// <summary>
    /// Event arguments for participant attributes changed events.
    /// </summary>
    public class ParticipantAttributesChangedEventArgs : EventArgs
    {
        /// <summary>
        /// The participant whose attributes changed.
        /// </summary>
        public Participant Participant { get; }

        /// <summary>
        /// All current attributes of the participant.
        /// </summary>
        public IReadOnlyDictionary<string, string> Attributes { get; }

        /// <summary>
        /// The attributes that changed (subset of all attributes).
        /// </summary>
        public IReadOnlyDictionary<string, string> ChangedAttributes { get; }

        internal ParticipantAttributesChangedEventArgs(
            Participant participant,
            IReadOnlyDictionary<string, string> attributes,
            IReadOnlyDictionary<string, string> changedAttributes
        )
        {
            Participant = participant;
            Attributes = attributes;
            ChangedAttributes = changedAttributes;
        }
    }

    /// <summary>
    /// Event arguments for participant encryption status changed events.
    /// </summary>
    public class ParticipantEncryptionStatusChangedEventArgs : EventArgs
    {
        /// <summary>
        /// The participant whose encryption status changed.
        /// </summary>
        public Participant Participant { get; }

        /// <summary>
        /// Whether the participant is encrypted.
        /// </summary>
        public bool IsEncrypted { get; }

        internal ParticipantEncryptionStatusChangedEventArgs(
            Participant participant,
            bool isEncrypted
        )
        {
            Participant = participant;
            IsEncrypted = isEncrypted;
        }
    }

    /// <summary>
    /// Event arguments for chat message received events.
    /// </summary>
    public class ChatMessageReceivedEventArgs : EventArgs
    {
        /// <summary>
        /// The chat message.
        /// </summary>
        public Proto.ChatMessage Message { get; }

        /// <summary>
        /// The participant who sent the message (null if from server).
        /// </summary>
        public Participant? Participant { get; }

        internal ChatMessageReceivedEventArgs(Proto.ChatMessage message, Participant? participant)
        {
            Message = message;
            Participant = participant;
        }
    }

    /// <summary>
    /// Event arguments for SIP DTMF received events.
    /// </summary>
    public class SipDtmfReceivedEventArgs : EventArgs
    {
        /// <summary>
        /// The DTMF code.
        /// </summary>
        public uint Code { get; }

        /// <summary>
        /// The DTMF digit (if applicable).
        /// </summary>
        public string? Digit { get; }

        /// <summary>
        /// The participant who sent the DTMF.
        /// </summary>
        public RemoteParticipant Participant { get; }

        internal SipDtmfReceivedEventArgs(uint code, string? digit, RemoteParticipant participant)
        {
            Code = code;
            Digit = digit;
            Participant = participant;
        }
    }

    /// <summary>
    /// Event arguments for E2EE state changed events.
    /// </summary>
    public class E2EEStateChangedEventArgs : EventArgs
    {
        /// <summary>
        /// The participant whose encryption state changed.
        /// </summary>
        public Participant Participant { get; }

        /// <summary>
        /// The new encryption state.
        /// </summary>
        public Proto.EncryptionState State { get; }

        internal E2EEStateChangedEventArgs(Participant participant, Proto.EncryptionState state)
        {
            Participant = participant;
            State = state;
        }
    }

    /// <summary>
    /// Event arguments for transcription received events.
    /// </summary>
    public class TranscriptionReceivedEventArgs : EventArgs
    {
        /// <summary>
        /// The participant whose transcription was received (null if not applicable).
        /// </summary>
        public Participant? Participant { get; }

        /// <summary>
        /// The track SID associated with the transcription (null if not applicable).
        /// </summary>
        public string? TrackSid { get; }

        /// <summary>
        /// The transcription segments.
        /// </summary>
        public IReadOnlyList<Proto.TranscriptionSegment> Segments { get; }

        internal TranscriptionReceivedEventArgs(
            Participant? participant,
            string? trackSid,
            IReadOnlyList<Proto.TranscriptionSegment> segments
        )
        {
            Participant = participant;
            TrackSid = trackSid;
            Segments = segments;
        }
    }
}
