// author: https://github.com/pabloFuente

using System;
using LiveKit.Proto;
using LiveKit.Rtc.Internal;

namespace LiveKit.Rtc
{
    /// <summary>
    /// Base class for all tracks.
    /// </summary>
    public abstract class Track : IDisposable
    {
        internal FfiHandle Handle { get; }

        /// <summary>
        /// Track session ID.
        /// </summary>
        public string Sid { get; protected set; } = string.Empty;

        /// <summary>
        /// Track name.
        /// </summary>
        public string Name { get; protected set; } = string.Empty;

        /// <summary>
        /// Track kind.
        /// </summary>
        public Proto.TrackKind Kind { get; protected set; }

        /// <summary>
        /// Track source.
        /// </summary>
        public Proto.TrackSource Source { get; protected set; }

        /// <summary>
        /// Whether the track is muted.
        /// </summary>
        public bool IsMuted { get; protected set; }

        private bool _disposed;

        internal Track(FfiHandle handle)
        {
            Handle = handle;
        }

        /// <summary>
        /// Disposes the track and releases resources.
        /// </summary>
        public virtual void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
            Handle.Dispose();
        }
    }

    /// <summary>
    /// Base class for local tracks.
    /// </summary>
    public abstract class LocalTrack : Track
    {
        internal LocalTrack(FfiHandle handle)
            : base(handle) { }

        /// <summary>
        /// Mutes the track.
        /// </summary>
        public virtual void Mute()
        {
            var request = new FfiRequest
            {
                LocalTrackMute = new LocalTrackMuteRequest
                {
                    TrackHandle = Handle.HandleId,
                    Mute = true,
                },
            };

            var response = FfiClient.Instance.SendRequest(request);

            if (response.LocalTrackMute != null)
            {
                IsMuted = response.LocalTrackMute.Muted;
            }
        }

        /// <summary>
        /// Unmutes the track.
        /// </summary>
        public virtual void Unmute()
        {
            var request = new FfiRequest
            {
                LocalTrackMute = new LocalTrackMuteRequest
                {
                    TrackHandle = Handle.HandleId,
                    Mute = false,
                },
            };

            var response = FfiClient.Instance.SendRequest(request);

            if (response.LocalTrackMute != null)
            {
                IsMuted = response.LocalTrackMute.Muted;
            }
        }
    }

    /// <summary>
    /// Base class for remote tracks.
    /// </summary>
    public abstract class RemoteTrack : Track
    {
        internal RemoteTrack(FfiHandle handle)
            : base(handle) { }
    }

    /// <summary>
    /// Local audio track.
    /// </summary>
    public class LocalAudioTrack : LocalTrack
    {
        internal LocalAudioTrack(FfiHandle handle)
            : base(handle)
        {
            Kind = Proto.TrackKind.KindAudio;
        }

        internal LocalAudioTrack(FfiHandle handle, OwnedTrack trackInfo)
            : base(handle)
        {
            Kind = Proto.TrackKind.KindAudio;
            if (trackInfo?.Info != null)
            {
                Sid = trackInfo.Info.Sid ?? string.Empty;
                Name = trackInfo.Info.Name ?? string.Empty;
            }
        }

        /// <summary>
        /// Creates a new local audio track from an audio source.
        /// </summary>
        /// <param name="name">The track name.</param>
        /// <param name="source">The audio source to use.</param>
        /// <returns>A new local audio track.</returns>
        public static LocalAudioTrack Create(string name, AudioSource source)
        {
            var request = new FfiRequest
            {
                CreateAudioTrack = new CreateAudioTrackRequest
                {
                    Name = name,
                    SourceHandle = source.Handle.HandleId,
                },
            };

            var response = FfiClient.Instance.SendRequest(request);
            var trackInfo = response.CreateAudioTrack.Track;

            var handle = FfiHandle.FromId(trackInfo.Handle.Id);
            return new LocalAudioTrack(handle, trackInfo);
        }
    }

    /// <summary>
    /// Local video track.
    /// </summary>
    public class LocalVideoTrack : LocalTrack
    {
        internal LocalVideoTrack(FfiHandle handle)
            : base(handle)
        {
            Kind = Proto.TrackKind.KindVideo;
        }

        internal LocalVideoTrack(FfiHandle handle, OwnedTrack trackInfo)
            : base(handle)
        {
            Kind = Proto.TrackKind.KindVideo;
            if (trackInfo?.Info != null)
            {
                Sid = trackInfo.Info.Sid ?? string.Empty;
                Name = trackInfo.Info.Name ?? string.Empty;
            }
        }

        /// <summary>
        /// Creates a new local video track from a video source.
        /// </summary>
        /// <param name="name">The track name.</param>
        /// <param name="source">The video source to use.</param>
        /// <returns>A new local video track.</returns>
        public static LocalVideoTrack Create(string name, VideoSource source)
        {
            var request = new FfiRequest
            {
                CreateVideoTrack = new CreateVideoTrackRequest
                {
                    Name = name,
                    SourceHandle = source.Handle.HandleId,
                },
            };

            var response = FfiClient.Instance.SendRequest(request);
            var trackInfo = response.CreateVideoTrack.Track;

            var handle = FfiHandle.FromId(trackInfo.Handle.Id);
            return new LocalVideoTrack(handle, trackInfo);
        }
    }

    /// <summary>
    /// Remote audio track.
    /// </summary>
    public class RemoteAudioTrack : RemoteTrack
    {
        internal RemoteAudioTrack(FfiHandle handle)
            : base(handle)
        {
            Kind = Proto.TrackKind.KindAudio;
        }
    }

    /// <summary>
    /// Remote video track.
    /// </summary>
    public class RemoteVideoTrack : RemoteTrack
    {
        internal RemoteVideoTrack(FfiHandle handle)
            : base(handle)
        {
            Kind = Proto.TrackKind.KindVideo;
        }
    }
}
