namespace Livekit.Server.Sdk.Dotnet;

public static class Constants
{
    public static readonly TimeSpan DefaultTtl = TimeSpan.FromHours(6);
    public static readonly TimeSpan DefaultLeeway = TimeSpan.FromMinutes(1);
}

public class VideoGrants
{
    public bool RoomCreate { get; set; } = false;
    public bool RoomList { get; set; } = false;
    public bool RoomRecord { get; set; } = false;

    public bool RoomAdmin { get; set; } = false;
    public bool RoomJoin { get; set; } = false;
    public string Room { get; set; } = "";

    public bool CanPublish { get; set; } = true;
    public bool CanSubscribe { get; set; } = true;
    public bool CanPublishData { get; set; } = true;

    public List<string> CanPublishSources { get; set; } = new List<string>();

    public bool CanUpdateOwnMetadata { get; set; } = false;
    public bool IngressAdmin { get; set; } = false;
    public bool Hidden { get; set; } = false;
    public bool Recorder { get; set; } = false;
    public bool Agent { get; set; } = false;
}

public class SIPGrants
{
    public bool Admin { get; set; } = false;
    public bool Call { get; set; } = false;
}

public class ClaimsModel
{
    public string Identity { get; set; } = "";
    public string Name { get; set; } = "";
    public VideoGrants Video { get; set; } = new VideoGrants();
    public SIPGrants Sip { get; set; } = new SIPGrants();
    public Dictionary<string, string> Attributes { get; set; } = new Dictionary<string, string>();
    public string Metadata { get; set; } = "";
    public string Sha256 { get; set; } = "";
    public string Kind { get; set; } = "";
}
