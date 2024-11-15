// <auto-generated>
//     Generated by protoc-gen-twirpcs.  DO NOT EDIT!
// </auto-generated>
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using Google.Protobuf; // For ".ToByteArray()"

public class Twirp {
  public static readonly MediaTypeWithQualityHeaderValue CONTENT_TYPE_PROTOBUF = new MediaTypeWithQualityHeaderValue("application/protobuf");

  public enum ErrorCode {
    NoError, Canceled, Unknown, Invalid_Argument, Malformed, Deadline_Exceeded, Not_Found, Bad_Route, Already_Exists, Permission_Denied,
    Unauthenticated, Resource_Exhausted, Failed_Precondition, Aborted, Out_Of_Range, Unimplemented, Internal, Unavailable, Data_Loss
  };

  public class Exception : System.Exception {
    public readonly ErrorCode Type;

    public Exception(ErrorCode type, string message) : base(message) { Type = type; }
  }

  private static Twirp.Exception createException(string jsonData) {
    var codeStr = parseJSONString(jsonData, "code");
    if (codeStr == null) {
      return new Twirp.Exception(ErrorCode.Unknown, jsonData);
    }

    ErrorCode errorCode = ErrorCode.Unknown;
    System.Enum.TryParse<ErrorCode>(codeStr, true, out errorCode);
    var msg = parseJSONString(jsonData, "msg");
    if (msg == null) {
      msg = jsonData;
    }
    return new Twirp.Exception(errorCode, msg);
  }

  private static string parseJSONString(string jsonData, string key) {
    var keyIndex = jsonData.IndexOf(key + "\":\"");
    if (keyIndex == -1) { return null; }
    keyIndex += key.Length + 3;
    var dataEnd = jsonData.IndexOf("\"", keyIndex);
    if (dataEnd == -1) { return null; }
    return jsonData.Substring(keyIndex, dataEnd - keyIndex);
  }

  private delegate Resp doParsing<Resp>(byte[] data) where Resp : IMessage;
  private static async Task<Resp> DoRequest<Req, Resp>(HttpClient client, string address, Req req, doParsing<Resp> parserFunc) where Req : IMessage where Resp : IMessage {
    using (var content = new ByteArrayContent(req.ToByteArray())) {
      content.Headers.ContentType = CONTENT_TYPE_PROTOBUF;
      using (HttpResponseMessage response = await client.PostAsync(address, content)) {
        var byteArr = await response.Content.ReadAsByteArrayAsync();
        if (!response.IsSuccessStatusCode) {
          string errorJSON = System.Text.Encoding.UTF8.GetString(byteArr, 0, byteArr.Length);
          throw createException(errorJSON);
        }
        return parserFunc(byteArr);
      }
    }
  }

  // start recording or streaming a room, participant, or tracks
  public static async Task<global::LiveKit.Proto.EgressInfo> StartRoomCompositeEgress(HttpClient client, global::LiveKit.Proto.RoomCompositeEgressRequest req) {
    return await DoRequest<global::LiveKit.Proto.RoomCompositeEgressRequest, global::LiveKit.Proto.EgressInfo>(client, "/twirp/livekit.Egress/StartRoomCompositeEgress", req, global::LiveKit.Proto.EgressInfo.Parser.ParseFrom);
  }

  public static async Task<global::LiveKit.Proto.EgressInfo> StartWebEgress(HttpClient client, global::LiveKit.Proto.WebEgressRequest req) {
    return await DoRequest<global::LiveKit.Proto.WebEgressRequest, global::LiveKit.Proto.EgressInfo>(client, "/twirp/livekit.Egress/StartWebEgress", req, global::LiveKit.Proto.EgressInfo.Parser.ParseFrom);
  }

  public static async Task<global::LiveKit.Proto.EgressInfo> StartParticipantEgress(HttpClient client, global::LiveKit.Proto.ParticipantEgressRequest req) {
    return await DoRequest<global::LiveKit.Proto.ParticipantEgressRequest, global::LiveKit.Proto.EgressInfo>(client, "/twirp/livekit.Egress/StartParticipantEgress", req, global::LiveKit.Proto.EgressInfo.Parser.ParseFrom);
  }

  public static async Task<global::LiveKit.Proto.EgressInfo> StartTrackCompositeEgress(HttpClient client, global::LiveKit.Proto.TrackCompositeEgressRequest req) {
    return await DoRequest<global::LiveKit.Proto.TrackCompositeEgressRequest, global::LiveKit.Proto.EgressInfo>(client, "/twirp/livekit.Egress/StartTrackCompositeEgress", req, global::LiveKit.Proto.EgressInfo.Parser.ParseFrom);
  }

  public static async Task<global::LiveKit.Proto.EgressInfo> StartTrackEgress(HttpClient client, global::LiveKit.Proto.TrackEgressRequest req) {
    return await DoRequest<global::LiveKit.Proto.TrackEgressRequest, global::LiveKit.Proto.EgressInfo>(client, "/twirp/livekit.Egress/StartTrackEgress", req, global::LiveKit.Proto.EgressInfo.Parser.ParseFrom);
  }

  // update web composite layout
  public static async Task<global::LiveKit.Proto.EgressInfo> UpdateLayout(HttpClient client, global::LiveKit.Proto.UpdateLayoutRequest req) {
    return await DoRequest<global::LiveKit.Proto.UpdateLayoutRequest, global::LiveKit.Proto.EgressInfo>(client, "/twirp/livekit.Egress/UpdateLayout", req, global::LiveKit.Proto.EgressInfo.Parser.ParseFrom);
  }

  // add or remove stream endpoints
  public static async Task<global::LiveKit.Proto.EgressInfo> UpdateStream(HttpClient client, global::LiveKit.Proto.UpdateStreamRequest req) {
    return await DoRequest<global::LiveKit.Proto.UpdateStreamRequest, global::LiveKit.Proto.EgressInfo>(client, "/twirp/livekit.Egress/UpdateStream", req, global::LiveKit.Proto.EgressInfo.Parser.ParseFrom);
  }

  // list available egress
  public static async Task<global::LiveKit.Proto.ListEgressResponse> ListEgress(HttpClient client, global::LiveKit.Proto.ListEgressRequest req) {
    return await DoRequest<global::LiveKit.Proto.ListEgressRequest, global::LiveKit.Proto.ListEgressResponse>(client, "/twirp/livekit.Egress/ListEgress", req, global::LiveKit.Proto.ListEgressResponse.Parser.ParseFrom);
  }

  // stop a recording or stream
  public static async Task<global::LiveKit.Proto.EgressInfo> StopEgress(HttpClient client, global::LiveKit.Proto.StopEgressRequest req) {
    return await DoRequest<global::LiveKit.Proto.StopEgressRequest, global::LiveKit.Proto.EgressInfo>(client, "/twirp/livekit.Egress/StopEgress", req, global::LiveKit.Proto.EgressInfo.Parser.ParseFrom);
  }

  public static async Task<global::LiveKit.Proto.AgentDispatch> CreateDispatch(HttpClient client, global::LiveKit.Proto.CreateAgentDispatchRequest req) {
    return await DoRequest<global::LiveKit.Proto.CreateAgentDispatchRequest, global::LiveKit.Proto.AgentDispatch>(client, "/twirp/livekit.AgentDispatchService/CreateDispatch", req, global::LiveKit.Proto.AgentDispatch.Parser.ParseFrom);
  }

  public static async Task<global::LiveKit.Proto.AgentDispatch> DeleteDispatch(HttpClient client, global::LiveKit.Proto.DeleteAgentDispatchRequest req) {
    return await DoRequest<global::LiveKit.Proto.DeleteAgentDispatchRequest, global::LiveKit.Proto.AgentDispatch>(client, "/twirp/livekit.AgentDispatchService/DeleteDispatch", req, global::LiveKit.Proto.AgentDispatch.Parser.ParseFrom);
  }

  public static async Task<global::LiveKit.Proto.ListAgentDispatchResponse> ListDispatch(HttpClient client, global::LiveKit.Proto.ListAgentDispatchRequest req) {
    return await DoRequest<global::LiveKit.Proto.ListAgentDispatchRequest, global::LiveKit.Proto.ListAgentDispatchResponse>(client, "/twirp/livekit.AgentDispatchService/ListDispatch", req, global::LiveKit.Proto.ListAgentDispatchResponse.Parser.ParseFrom);
  }

  // Creates a room with settings. Requires `roomCreate` permission.
  // This method is optional; rooms are automatically created when clients connect to them for the first time.
  public static async Task<global::LiveKit.Proto.Room> CreateRoom(HttpClient client, global::LiveKit.Proto.CreateRoomRequest req) {
    return await DoRequest<global::LiveKit.Proto.CreateRoomRequest, global::LiveKit.Proto.Room>(client, "/twirp/livekit.RoomService/CreateRoom", req, global::LiveKit.Proto.Room.Parser.ParseFrom);
  }

  // List rooms that are active on the server. Requires `roomList` permission.
  public static async Task<global::LiveKit.Proto.ListRoomsResponse> ListRooms(HttpClient client, global::LiveKit.Proto.ListRoomsRequest req) {
    return await DoRequest<global::LiveKit.Proto.ListRoomsRequest, global::LiveKit.Proto.ListRoomsResponse>(client, "/twirp/livekit.RoomService/ListRooms", req, global::LiveKit.Proto.ListRoomsResponse.Parser.ParseFrom);
  }

  // Deletes an existing room by name or id. Requires `roomCreate` permission.
  // DeleteRoom will disconnect all participants that are currently in the room.
  public static async Task<global::LiveKit.Proto.DeleteRoomResponse> DeleteRoom(HttpClient client, global::LiveKit.Proto.DeleteRoomRequest req) {
    return await DoRequest<global::LiveKit.Proto.DeleteRoomRequest, global::LiveKit.Proto.DeleteRoomResponse>(client, "/twirp/livekit.RoomService/DeleteRoom", req, global::LiveKit.Proto.DeleteRoomResponse.Parser.ParseFrom);
  }

  // Lists participants in a room, Requires `roomAdmin`
  public static async Task<global::LiveKit.Proto.ListParticipantsResponse> ListParticipants(HttpClient client, global::LiveKit.Proto.ListParticipantsRequest req) {
    return await DoRequest<global::LiveKit.Proto.ListParticipantsRequest, global::LiveKit.Proto.ListParticipantsResponse>(client, "/twirp/livekit.RoomService/ListParticipants", req, global::LiveKit.Proto.ListParticipantsResponse.Parser.ParseFrom);
  }

  // Get information on a specific participant, Requires `roomAdmin`
  public static async Task<global::LiveKit.Proto.ParticipantInfo> GetParticipant(HttpClient client, global::LiveKit.Proto.RoomParticipantIdentity req) {
    return await DoRequest<global::LiveKit.Proto.RoomParticipantIdentity, global::LiveKit.Proto.ParticipantInfo>(client, "/twirp/livekit.RoomService/GetParticipant", req, global::LiveKit.Proto.ParticipantInfo.Parser.ParseFrom);
  }

  // Removes a participant from room. Requires `roomAdmin`
  public static async Task<global::LiveKit.Proto.RemoveParticipantResponse> RemoveParticipant(HttpClient client, global::LiveKit.Proto.RoomParticipantIdentity req) {
    return await DoRequest<global::LiveKit.Proto.RoomParticipantIdentity, global::LiveKit.Proto.RemoveParticipantResponse>(client, "/twirp/livekit.RoomService/RemoveParticipant", req, global::LiveKit.Proto.RemoveParticipantResponse.Parser.ParseFrom);
  }

  // Mute/unmute a participant's track, Requires `roomAdmin`
  public static async Task<global::LiveKit.Proto.MuteRoomTrackResponse> MutePublishedTrack(HttpClient client, global::LiveKit.Proto.MuteRoomTrackRequest req) {
    return await DoRequest<global::LiveKit.Proto.MuteRoomTrackRequest, global::LiveKit.Proto.MuteRoomTrackResponse>(client, "/twirp/livekit.RoomService/MutePublishedTrack", req, global::LiveKit.Proto.MuteRoomTrackResponse.Parser.ParseFrom);
  }

  // Update participant metadata, will cause updates to be broadcasted to everyone in the room. Requires `roomAdmin`
  public static async Task<global::LiveKit.Proto.ParticipantInfo> UpdateParticipant(HttpClient client, global::LiveKit.Proto.UpdateParticipantRequest req) {
    return await DoRequest<global::LiveKit.Proto.UpdateParticipantRequest, global::LiveKit.Proto.ParticipantInfo>(client, "/twirp/livekit.RoomService/UpdateParticipant", req, global::LiveKit.Proto.ParticipantInfo.Parser.ParseFrom);
  }

  // Subscribes or unsubscribe a participant from tracks. Requires `roomAdmin`
  public static async Task<global::LiveKit.Proto.UpdateSubscriptionsResponse> UpdateSubscriptions(HttpClient client, global::LiveKit.Proto.UpdateSubscriptionsRequest req) {
    return await DoRequest<global::LiveKit.Proto.UpdateSubscriptionsRequest, global::LiveKit.Proto.UpdateSubscriptionsResponse>(client, "/twirp/livekit.RoomService/UpdateSubscriptions", req, global::LiveKit.Proto.UpdateSubscriptionsResponse.Parser.ParseFrom);
  }

  // Send data over data channel to participants in a room, Requires `roomAdmin`
  public static async Task<global::LiveKit.Proto.SendDataResponse> SendData(HttpClient client, global::LiveKit.Proto.SendDataRequest req) {
    return await DoRequest<global::LiveKit.Proto.SendDataRequest, global::LiveKit.Proto.SendDataResponse>(client, "/twirp/livekit.RoomService/SendData", req, global::LiveKit.Proto.SendDataResponse.Parser.ParseFrom);
  }

  // Update room metadata, will cause updates to be broadcasted to everyone in the room, Requires `roomAdmin`
  public static async Task<global::LiveKit.Proto.Room> UpdateRoomMetadata(HttpClient client, global::LiveKit.Proto.UpdateRoomMetadataRequest req) {
    return await DoRequest<global::LiveKit.Proto.UpdateRoomMetadataRequest, global::LiveKit.Proto.Room>(client, "/twirp/livekit.RoomService/UpdateRoomMetadata", req, global::LiveKit.Proto.Room.Parser.ParseFrom);
  }

  // Create a new Ingress
  public static async Task<global::LiveKit.Proto.IngressInfo> CreateIngress(HttpClient client, global::LiveKit.Proto.CreateIngressRequest req) {
    return await DoRequest<global::LiveKit.Proto.CreateIngressRequest, global::LiveKit.Proto.IngressInfo>(client, "/twirp/livekit.Ingress/CreateIngress", req, global::LiveKit.Proto.IngressInfo.Parser.ParseFrom);
  }

  // Update an existing Ingress. Ingress can only be updated when it's in ENDPOINT_WAITING state.
  public static async Task<global::LiveKit.Proto.IngressInfo> UpdateIngress(HttpClient client, global::LiveKit.Proto.UpdateIngressRequest req) {
    return await DoRequest<global::LiveKit.Proto.UpdateIngressRequest, global::LiveKit.Proto.IngressInfo>(client, "/twirp/livekit.Ingress/UpdateIngress", req, global::LiveKit.Proto.IngressInfo.Parser.ParseFrom);
  }

  public static async Task<global::LiveKit.Proto.ListIngressResponse> ListIngress(HttpClient client, global::LiveKit.Proto.ListIngressRequest req) {
    return await DoRequest<global::LiveKit.Proto.ListIngressRequest, global::LiveKit.Proto.ListIngressResponse>(client, "/twirp/livekit.Ingress/ListIngress", req, global::LiveKit.Proto.ListIngressResponse.Parser.ParseFrom);
  }

  public static async Task<global::LiveKit.Proto.IngressInfo> DeleteIngress(HttpClient client, global::LiveKit.Proto.DeleteIngressRequest req) {
    return await DoRequest<global::LiveKit.Proto.DeleteIngressRequest, global::LiveKit.Proto.IngressInfo>(client, "/twirp/livekit.Ingress/DeleteIngress", req, global::LiveKit.Proto.IngressInfo.Parser.ParseFrom);
  }

  // rpc CreateSIPTrunk(CreateSIPTrunkRequest) returns (SIPTrunkInfo) { option deprecated = true; }; DELETED
  public static async Task<global::LiveKit.Proto.ListSIPTrunkResponse> ListSIPTrunk(HttpClient client, global::LiveKit.Proto.ListSIPTrunkRequest req) {
    return await DoRequest<global::LiveKit.Proto.ListSIPTrunkRequest, global::LiveKit.Proto.ListSIPTrunkResponse>(client, "/twirp/livekit.SIP/ListSIPTrunk", req, global::LiveKit.Proto.ListSIPTrunkResponse.Parser.ParseFrom);
  }

  public static async Task<global::LiveKit.Proto.SIPInboundTrunkInfo> CreateSIPInboundTrunk(HttpClient client, global::LiveKit.Proto.CreateSIPInboundTrunkRequest req) {
    return await DoRequest<global::LiveKit.Proto.CreateSIPInboundTrunkRequest, global::LiveKit.Proto.SIPInboundTrunkInfo>(client, "/twirp/livekit.SIP/CreateSIPInboundTrunk", req, global::LiveKit.Proto.SIPInboundTrunkInfo.Parser.ParseFrom);
  }

  public static async Task<global::LiveKit.Proto.SIPOutboundTrunkInfo> CreateSIPOutboundTrunk(HttpClient client, global::LiveKit.Proto.CreateSIPOutboundTrunkRequest req) {
    return await DoRequest<global::LiveKit.Proto.CreateSIPOutboundTrunkRequest, global::LiveKit.Proto.SIPOutboundTrunkInfo>(client, "/twirp/livekit.SIP/CreateSIPOutboundTrunk", req, global::LiveKit.Proto.SIPOutboundTrunkInfo.Parser.ParseFrom);
  }

  public static async Task<global::LiveKit.Proto.GetSIPInboundTrunkResponse> GetSIPInboundTrunk(HttpClient client, global::LiveKit.Proto.GetSIPInboundTrunkRequest req) {
    return await DoRequest<global::LiveKit.Proto.GetSIPInboundTrunkRequest, global::LiveKit.Proto.GetSIPInboundTrunkResponse>(client, "/twirp/livekit.SIP/GetSIPInboundTrunk", req, global::LiveKit.Proto.GetSIPInboundTrunkResponse.Parser.ParseFrom);
  }

  public static async Task<global::LiveKit.Proto.GetSIPOutboundTrunkResponse> GetSIPOutboundTrunk(HttpClient client, global::LiveKit.Proto.GetSIPOutboundTrunkRequest req) {
    return await DoRequest<global::LiveKit.Proto.GetSIPOutboundTrunkRequest, global::LiveKit.Proto.GetSIPOutboundTrunkResponse>(client, "/twirp/livekit.SIP/GetSIPOutboundTrunk", req, global::LiveKit.Proto.GetSIPOutboundTrunkResponse.Parser.ParseFrom);
  }

  public static async Task<global::LiveKit.Proto.ListSIPInboundTrunkResponse> ListSIPInboundTrunk(HttpClient client, global::LiveKit.Proto.ListSIPInboundTrunkRequest req) {
    return await DoRequest<global::LiveKit.Proto.ListSIPInboundTrunkRequest, global::LiveKit.Proto.ListSIPInboundTrunkResponse>(client, "/twirp/livekit.SIP/ListSIPInboundTrunk", req, global::LiveKit.Proto.ListSIPInboundTrunkResponse.Parser.ParseFrom);
  }

  public static async Task<global::LiveKit.Proto.ListSIPOutboundTrunkResponse> ListSIPOutboundTrunk(HttpClient client, global::LiveKit.Proto.ListSIPOutboundTrunkRequest req) {
    return await DoRequest<global::LiveKit.Proto.ListSIPOutboundTrunkRequest, global::LiveKit.Proto.ListSIPOutboundTrunkResponse>(client, "/twirp/livekit.SIP/ListSIPOutboundTrunk", req, global::LiveKit.Proto.ListSIPOutboundTrunkResponse.Parser.ParseFrom);
  }

  public static async Task<global::LiveKit.Proto.SIPTrunkInfo> DeleteSIPTrunk(HttpClient client, global::LiveKit.Proto.DeleteSIPTrunkRequest req) {
    return await DoRequest<global::LiveKit.Proto.DeleteSIPTrunkRequest, global::LiveKit.Proto.SIPTrunkInfo>(client, "/twirp/livekit.SIP/DeleteSIPTrunk", req, global::LiveKit.Proto.SIPTrunkInfo.Parser.ParseFrom);
  }

  public static async Task<global::LiveKit.Proto.SIPDispatchRuleInfo> CreateSIPDispatchRule(HttpClient client, global::LiveKit.Proto.CreateSIPDispatchRuleRequest req) {
    return await DoRequest<global::LiveKit.Proto.CreateSIPDispatchRuleRequest, global::LiveKit.Proto.SIPDispatchRuleInfo>(client, "/twirp/livekit.SIP/CreateSIPDispatchRule", req, global::LiveKit.Proto.SIPDispatchRuleInfo.Parser.ParseFrom);
  }

  public static async Task<global::LiveKit.Proto.ListSIPDispatchRuleResponse> ListSIPDispatchRule(HttpClient client, global::LiveKit.Proto.ListSIPDispatchRuleRequest req) {
    return await DoRequest<global::LiveKit.Proto.ListSIPDispatchRuleRequest, global::LiveKit.Proto.ListSIPDispatchRuleResponse>(client, "/twirp/livekit.SIP/ListSIPDispatchRule", req, global::LiveKit.Proto.ListSIPDispatchRuleResponse.Parser.ParseFrom);
  }

  public static async Task<global::LiveKit.Proto.SIPDispatchRuleInfo> DeleteSIPDispatchRule(HttpClient client, global::LiveKit.Proto.DeleteSIPDispatchRuleRequest req) {
    return await DoRequest<global::LiveKit.Proto.DeleteSIPDispatchRuleRequest, global::LiveKit.Proto.SIPDispatchRuleInfo>(client, "/twirp/livekit.SIP/DeleteSIPDispatchRule", req, global::LiveKit.Proto.SIPDispatchRuleInfo.Parser.ParseFrom);
  }

  public static async Task<global::LiveKit.Proto.SIPParticipantInfo> CreateSIPParticipant(HttpClient client, global::LiveKit.Proto.CreateSIPParticipantRequest req) {
    return await DoRequest<global::LiveKit.Proto.CreateSIPParticipantRequest, global::LiveKit.Proto.SIPParticipantInfo>(client, "/twirp/livekit.SIP/CreateSIPParticipant", req, global::LiveKit.Proto.SIPParticipantInfo.Parser.ParseFrom);
  }

  public static async Task<Google.Protobuf.WellKnownTypes.Empty> TransferSIPParticipant(HttpClient client, global::LiveKit.Proto.TransferSIPParticipantRequest req) {
    return await DoRequest<global::LiveKit.Proto.TransferSIPParticipantRequest, Google.Protobuf.WellKnownTypes.Empty>(client, "/twirp/livekit.SIP/TransferSIPParticipant", req, Google.Protobuf.WellKnownTypes.Empty.Parser.ParseFrom);
  }
}