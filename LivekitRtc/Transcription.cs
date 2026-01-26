// author: https://github.com/pabloFuente

using System;
using System.Collections.Generic;

namespace LiveKit.Rtc
{
    /// <summary>
    /// Represents a transcription with multiple segments.
    /// </summary>
    public class Transcription
    {
        /// <summary>
        /// Gets the participant identity.
        /// </summary>
        public string ParticipantIdentity { get; }

        /// <summary>
        /// Gets the track SID.
        /// </summary>
        public string TrackSid { get; }

        /// <summary>
        /// Gets the transcription segments.
        /// </summary>
        public IReadOnlyList<Proto.TranscriptionSegment> Segments { get; }

        /// <summary>
        /// Initializes a new transcription.
        /// </summary>
        /// <param name="participantIdentity">The participant identity.</param>
        /// <param name="trackSid">The track SID.</param>
        /// <param name="segments">The transcription segments.</param>
        public Transcription(
            string participantIdentity,
            string trackSid,
            IReadOnlyList<Proto.TranscriptionSegment> segments
        )
        {
            ParticipantIdentity =
                participantIdentity ?? throw new ArgumentNullException(nameof(participantIdentity));
            TrackSid = trackSid ?? throw new ArgumentNullException(nameof(trackSid));
            Segments = segments ?? throw new ArgumentNullException(nameof(segments));
        }

        /// <summary>
        /// Creates a transcription from proto messages.
        /// </summary>
        internal static Transcription FromProto(
            string participantIdentity,
            string trackSid,
            Google.Protobuf.Collections.RepeatedField<Proto.TranscriptionSegment> segments
        )
        {
            return new Transcription(
                participantIdentity,
                trackSid,
                new List<Proto.TranscriptionSegment>(segments)
            );
        }
    }
}
