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
}
