---
uid: LiveKit.Rtc
summary: *content
---
Real-time participant SDK built on the LiveKit Rust SDK. Start with [Room](xref:LiveKit.Rtc.Room): [ConnectAsync](xref:LiveKit.Rtc.Room.ConnectAsync*) joins a room with an access token, [LocalParticipant](xref:LiveKit.Rtc.LocalParticipant) publishes tracks, data and RPC calls, and the events of `Room` report every change.

- Audio: [AudioSource](xref:LiveKit.Rtc.AudioSource) with [LocalAudioTrack](xref:LiveKit.Rtc.LocalAudioTrack) to publish, [AudioStream](xref:LiveKit.Rtc.AudioStream) to receive, [AudioResampler](xref:LiveKit.Rtc.AudioResampler) and [AudioMixer](xref:LiveKit.Rtc.AudioMixer) to process.
- Video: [VideoSource](xref:LiveKit.Rtc.VideoSource) or [EncodedVideoSource](xref:LiveKit.Rtc.EncodedVideoSource) with [LocalVideoTrack](xref:LiveKit.Rtc.LocalVideoTrack) to publish, [VideoStream](xref:LiveKit.Rtc.VideoStream) to receive, [VideoFrame](xref:LiveKit.Rtc.VideoFrame) to convert.
- Data: data messages, text and byte streams, and data tracks ([LocalDataTrack](xref:LiveKit.Rtc.LocalDataTrack), [RemoteDataTrack](xref:LiveKit.Rtc.RemoteDataTrack)).
- RPC: [PerformRpcAsync](xref:LiveKit.Rtc.LocalParticipant.PerformRpcAsync*) and [RegisterRpcMethod](xref:LiveKit.Rtc.LocalParticipant.RegisterRpcMethod*).
- End-to-end encryption: [RoomOptions.E2EE](xref:LiveKit.Rtc.RoomOptions.E2EE) and [E2EEManager](xref:LiveKit.Rtc.E2EEManager).
