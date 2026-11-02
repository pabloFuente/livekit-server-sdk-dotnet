using System;
using System.Collections.Generic;

namespace Livekit.Server.Sdk.Dotnet
{
    /// <summary>
    /// Default values shared by the SDK.
    /// </summary>
    public static class Constants
    {
        /// <summary>
        /// Default lifetime of the tokens the SDK signs: 6 hours.
        /// </summary>
        public static readonly TimeSpan DefaultTtl = TimeSpan.FromHours(6);

        /// <summary>
        /// Default clock tolerance when verifying a token: 1 minute.
        /// </summary>
        public static readonly TimeSpan DefaultLeeway = TimeSpan.FromMinutes(1);
    }

    /// <summary>
    /// The <c>video</c> grant of an access token: what its holder may do in rooms.
    /// </summary>
    public class VideoGrants
    {
        /// <summary>
        /// Participant allowed to connect to LiveKit as Agent Framework worker.
        /// </summary>
        public bool Agent { get; set; } = false;

        /// <summary>
        /// Permission to publish tracks. Defaults to true.
        /// </summary>
        public bool CanPublish { get; set; } = true;

        /// <summary>
        /// Permission to publish data messages. Defaults to true.
        /// </summary>
        public bool CanPublishData { get; set; } = true;

        /// <summary>
        /// TrackSource types that the participant is allowed to publish. When set, it supersedes <c>CanPublish</c>. Only sources explicitly set here can be published.
        /// </summary>
        public List<string> CanPublishSources { get; set; } = new List<string>();

        /// <summary>
        /// Permission to subscribe to the tracks of other participants. Defaults to true.
        /// </summary>
        public bool CanSubscribe { get; set; } = true;

        /// <summary>
        /// Allow participant to subscribe to metrics.
        /// </summary>
        public bool CanSubscribeMetrics { get; set; } = false;

        /// <summary>
        /// Allow participant to manage an agent session via RemoteSession.
        /// </summary>
        public bool CanManageAgentSession { get; set; } = false;

        /// <summary>
        /// Permission to update its own name, metadata and attributes. Defaults to false.
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

    /// <summary>
    /// The <c>sip</c> grant of an access token: what its holder may do with the SIP service.
    /// </summary>
    public class SIPGrants
    {
        /// <summary>
        /// Permission to manage SIP trunks and dispatch rules.
        /// </summary>
        public bool Admin { get; set; } = false;

        /// <summary>
        /// Permission to make outbound calls.
        /// </summary>
        public bool Call { get; set; } = false;
    }

    /// <summary>
    /// The <c>inference</c> grant of an access token.
    /// </summary>
    public class InferenceGrants
    {
        /// <summary>
        /// Permission to perform inference.
        /// </summary>
        public bool Perform { get; set; } = false;
    }

    /// <summary>
    /// The <c>observability</c> grant of an access token.
    /// </summary>
    public class ObservabilityGrants
    {
        /// <summary>
        /// Permission to publish observability data.
        /// </summary>
        public bool Write { get; set; } = false;
    }

    /// <summary>
    /// All the claims of an access token, as built by <see cref="AccessToken"/> and read back by
    /// <see cref="TokenVerifier.Verify"/>.
    /// </summary>
    public class ClaimsModel
    {
        /// <summary>
        /// Participant identity (the <c>sub</c> claim).
        /// </summary>
        public string Identity { get; set; } = "";

        /// <summary>
        /// Display name of the participant.
        /// </summary>
        public string Name { get; set; } = "";

        /// <summary>
        /// Room grants (the <c>video</c> claim).
        /// </summary>
        public VideoGrants Video { get; set; } = new VideoGrants();

        /// <summary>
        /// SIP grants (the <c>sip</c> claim).
        /// </summary>
        public SIPGrants Sip { get; set; } = new SIPGrants();

        /// <summary>
        /// Inference grants, null when the token has none.
        /// </summary>
        public InferenceGrants Inference { get; set; }

        /// <summary>
        /// Observability grants, null when the token has none.
        /// </summary>
        public ObservabilityGrants Observability { get; set; }

        /// <summary>
        /// Participant metadata, visible to the other participants.
        /// </summary>
        public string Metadata { get; set; } = "";

        /// <summary>
        /// SHA-256 of the request body, which webhooks use to verify the body.
        /// </summary>
        public string Sha256 { get; set; } = "";

        /// <summary>
        /// Kind of participant: <c>standard</c>, <c>egress</c>, <c>ingress</c>, <c>agent</c> or <c>sip</c>.
        /// </summary>
        public string Kind { get; set; } = "";

        /// <summary>
        /// Participant attributes.
        /// </summary>
        public Dictionary<string, string> Attributes { get; set; } =
            new Dictionary<string, string>();

        /// <summary>
        /// Room configuration preset applied when the token creates the room.
        /// </summary>
        public string RoomPreset { get; set; } = "";

        /// <summary>
        /// Room configuration applied when the token creates the room; empty (never null) when not set.
        /// </summary>
        public RoomConfiguration RoomConfig { get; set; } = new RoomConfiguration();
    }
}
