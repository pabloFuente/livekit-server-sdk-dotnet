// author: https://github.com/pabloFuente

using System;
using System.Collections.Generic;

namespace LiveKit.Rtc
{
    /// <summary>
    /// Represents a transcription segment.
    /// </summary>
    public class TranscriptionSegment
    {
        /// <summary>
        /// Gets the segment ID.
        /// </summary>
        public string Id { get; }

        /// <summary>
        /// Gets the transcribed text.
        /// </summary>
        public string Text { get; }

        /// <summary>
        /// Gets the start time in milliseconds.
        /// </summary>
        public long StartTime { get; }

        /// <summary>
        /// Gets the end time in milliseconds.
        /// </summary>
        public long EndTime { get; }

        /// <summary>
        /// Gets the language code.
        /// </summary>
        public string Language { get; }

        /// <summary>
        /// Gets whether this segment is final.
        /// </summary>
        public bool IsFinal { get; }

        /// <summary>
        /// Initializes a new transcription segment.
        /// </summary>
        /// <param name="id">The segment ID.</param>
        /// <param name="text">The transcribed text.</param>
        /// <param name="startTime">The start time in milliseconds.</param>
        /// <param name="endTime">The end time in milliseconds.</param>
        /// <param name="language">The language code.</param>
        /// <param name="isFinal">Whether this segment is final.</param>
        public TranscriptionSegment(
            string id,
            string text,
            long startTime,
            long endTime,
            string language,
            bool isFinal
        )
        {
            Id = id ?? throw new ArgumentNullException(nameof(id));
            Text = text ?? string.Empty;
            StartTime = startTime;
            EndTime = endTime;
            Language = language ?? string.Empty;
            IsFinal = isFinal;
        }

        /// <summary>
        /// Creates a segment from a proto message.
        /// </summary>
        internal static TranscriptionSegment FromProto(LiveKit.Proto.TranscriptionSegment proto)
        {
            return new TranscriptionSegment(
                proto.Id,
                proto.Text,
                (long)proto.StartTime,
                (long)proto.EndTime,
                proto.Language,
                proto.Final
            );
        }
    }

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
        public IReadOnlyList<TranscriptionSegment> Segments { get; }

        /// <summary>
        /// Initializes a new transcription.
        /// </summary>
        /// <param name="participantIdentity">The participant identity.</param>
        /// <param name="trackSid">The track SID.</param>
        /// <param name="segments">The transcription segments.</param>
        public Transcription(
            string participantIdentity,
            string trackSid,
            IReadOnlyList<TranscriptionSegment> segments
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
            Google.Protobuf.Collections.RepeatedField<LiveKit.Proto.TranscriptionSegment> segments
        )
        {
            var list = new List<TranscriptionSegment>(segments.Count);
            foreach (var segment in segments)
            {
                list.Add(TranscriptionSegment.FromProto(segment));
            }
            return new Transcription(participantIdentity, trackSid, list);
        }
    }
}
