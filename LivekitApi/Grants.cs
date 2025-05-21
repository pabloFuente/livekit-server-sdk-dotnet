using System;
using System.Collections.Generic;

namespace Livekit.Server.Sdk.Dotnet
{
    public static class Constants
    {
        public static readonly TimeSpan DefaultTtl = TimeSpan.FromHours(6);
        public static readonly TimeSpan DefaultLeeway = TimeSpan.FromMinutes(1);
    }

    public class VideoGrants
    {
        /// <summary>
        /// Participant allowed to connect to LiveKit as Agent Framework worker.
        /// </summary>
        public bool Agent { get; set; } = false;

        /// <summary>
        /// Allow participant to publish. If neither <c>CanPublish</c> or <c>CanSubscribe</c> is set, both publish and subscribe are enabled.
        /// </summary>
        public bool CanPublish { get; set; } = true;

        /// <summary>
        /// Allow participants to publish data, defaults to true if not set.
        /// </summary>
        public bool CanPublishData { get; set; } = true;

        /// <summary>
        /// TrackSource types that the participant is allowed to publish. When set, it supersedes <c>CanPublish</c>. Only sources explicitly set here can be published.
        /// </summary>
        public List<string> CanPublishSources { get; set; } = new List<string>();

        /// <summary>
        /// Allow participant to subscribe to other tracks.
        /// </summary>
        public bool CanSubscribe { get; set; } = true;

        /// <summary>
        /// Allow participant to subscribe to metrics.
        /// </summary>
        public bool CanSubscribeMetrics { get; set; } = false;

        /// <summary>
        /// By default, a participant is not allowed to update its own metadata.
        /// </summary>
        public bool CanUpdateOwnMetadata { get; set; } = false;

        /// <summary>
        /// Destination room which this participant can forward to.
        /// </summary>
        public string DestinationRoom { get; set; } = "";

        /// <summary>
        /// Participant isn't visible to others.
        /// </summary>
        public bool Hidden { get; set; } = false;

        /// <summary>
        /// Permissions to control ingress, not specific to any room or ingress.
        /// </summary>
        public bool IngressAdmin { get; set; } = false;

        /// <summary>
        /// Participant is recording the room, when set, allows room to indicate it's being recorded.
        /// </summary>
        public bool Recorder { get; set; } = false;

        /// <summary>
        /// Name of the room, must be set for <c>RoomAdmin</c> or <c>RoomJoin</c> permissions.
        /// </summary>
        public string Room { get; set; } = "";

        /// <summary>
        /// Permission to control a specific room, <c>Room</c> must be set.
        /// </summary>
        public bool RoomAdmin { get; set; } = false;

        /// <summary>
        /// Permission to create a room.
        /// </summary>
        public bool RoomCreate { get; set; } = false;

        /// <summary>
        /// Permission to join a room as a participant, <c>Room</c> must be set.
        /// </summary>
        public bool RoomJoin { get; set; } = false;

        /// <summary>
        /// Permission to list rooms.
        /// </summary>
        public bool RoomList { get; set; } = false;

        /// <summary>
        /// Permission to start a recording.
        /// </summary>
        public bool RoomRecord { get; set; } = false;
    }

    public class SIPGrants
    {
        /// <summary>
        /// Permission to manage sip resources.
        /// </summary>
        public bool Admin { get; set; } = false;

        /// <summary>
        /// Permission to make outbound calls.
        /// </summary>
        public bool Call { get; set; } = false;
    }

    public class ClaimsModel
    {
        public string Identity { get; set; } = "";
        public string Name { get; set; } = "";
        public VideoGrants Video { get; set; } = new VideoGrants();
        public SIPGrants Sip { get; set; } = new SIPGrants();
        public string Metadata { get; set; } = "";
        public string Sha256 { get; set; } = "";
        public string Kind { get; set; } = "";
        public Dictionary<string, string> Attributes { get; set; } =
            new Dictionary<string, string>();
        public string RoomPreset { get; set; } = "";
        public RoomConfiguration RoomConfig { get; set; } = new RoomConfiguration();
    }
}
