// author: https://github.com/pabloFuente

using System.Threading;
using System.Threading.Tasks;
using LiveKit.Proto;
using LiveKit.Rtc.Internal;

namespace LiveKit.Rtc
{
    /// <summary>
    /// Base class for track publications.
    /// </summary>
    public abstract class TrackPublication
    {
        internal FfiHandle Handle { get; }
        internal TrackPublicationInfo Info { get; private set; }

        /// <summary>
        /// Track publication SID.
        /// </summary>
        public string Sid => Info.Sid;

        /// <summary>
        /// Track name.
        /// </summary>
        public string Name => Info.Name;

        /// <summary>
        /// Track kind.
        /// </summary>
        public Proto.TrackKind Kind => Info.Kind;

        /// <summary>
        /// Track source.
        /// </summary>
        public Proto.TrackSource Source => Info.Source;

        /// <summary>
        /// Whether the track is simulcasted.
        /// </summary>
        public bool Simulcasted => Info.Simulcasted;

        /// <summary>
        /// Video width (if applicable).
        /// </summary>
        public uint Width => Info.Width;

        /// <summary>
        /// Video height (if applicable).
        /// </summary>
        public uint Height => Info.Height;

        /// <summary>
        /// MIME type.
        /// </summary>
        public string MimeType => Info.MimeType;

        /// <summary>
        /// Whether the track is muted.
        /// </summary>
        public bool IsMuted => Info.Muted;

        /// <summary>
        /// Encryption type for this track.
        /// </summary>
        public Proto.EncryptionType EncryptionType => Info.EncryptionType;

        /// <summary>
        /// The associated track (may be null for remote publications until subscribed).
        /// </summary>
        public Track? Track { get; protected set; }

        internal TrackPublication(FfiHandle handle, TrackPublicationInfo info)
        {
            Handle = handle;
            Info = info;
        }

        internal void UpdateInfo(TrackPublicationInfo info)
        {
            Info = info;
        }
    }

    /// <summary>
    /// Local track publication.
    /// </summary>
    public class LocalTrackPublication : TrackPublication
    {
        private readonly TaskCompletionSource<bool> _firstSubscription =
            new TaskCompletionSource<bool>();

        /// <summary>
        /// The local participant that owns this publication.
        /// </summary>
        public LocalParticipant Participant { get; }

        /// <summary>
        /// The local track.
        /// </summary>
        public new LocalTrack? Track
        {
            get => base.Track as LocalTrack;
            internal set => base.Track = value;
        }

        internal LocalTrackPublication(
            FfiHandle handle,
            TrackPublicationInfo info,
            LocalParticipant participant
        )
            : base(handle, info)
        {
            Participant = participant;
        }

        /// <summary>
        /// Waits until at least one remote participant has subscribed to this track.
        /// This is useful when you want to ensure someone is receiving your published track
        /// before proceeding (e.g., before starting to send audio/video frames).
        /// </summary>
        /// <param name="cancellationToken">Cancellation token to abort the wait.</param>
        /// <returns>A task that completes when the first subscription occurs.</returns>
        public async Task WaitForSubscriptionAsync(CancellationToken cancellationToken = default)
        {
            if (cancellationToken == default)
            {
                await _firstSubscription.Task;
            }
            else
            {
                var tcs = new TaskCompletionSource<bool>();
                using (cancellationToken.Register(() => tcs.TrySetCanceled()))
                {
                    var completedTask = await Task.WhenAny(_firstSubscription.Task, tcs.Task);
                    await completedTask; // Propagate cancellation if needed
                }
            }
        }

        /// <summary>
        /// Internal method to signal that the first subscription has occurred.
        /// </summary>
        internal void ResolveFirstSubscription()
        {
            _firstSubscription.TrySetResult(true);
        }
    }

    /// <summary>
    /// Remote track publication.
    /// </summary>
    public class RemoteTrackPublication : TrackPublication
    {
        /// <summary>
        /// The remote participant that owns this publication.
        /// </summary>
        public RemoteParticipant Participant { get; }

        /// <summary>
        /// The remote track (null until subscribed).
        /// </summary>
        public new RemoteTrack? Track
        {
            get => base.Track as RemoteTrack;
            internal set => base.Track = value;
        }

        /// <summary>
        /// Whether this publication is subscribed.
        /// </summary>
        public bool IsSubscribed => Track != null;

        internal RemoteTrackPublication(
            FfiHandle handle,
            TrackPublicationInfo info,
            RemoteParticipant participant
        )
            : base(handle, info)
        {
            Participant = participant;
        }

        /// <summary>
        /// Sets whether the track should be subscribed.
        /// </summary>
        /// <param name="subscribed">True to subscribe, false to unsubscribe.</param>
        public void SetSubscribed(bool subscribed)
        {
            var request = new FfiRequest
            {
                SetSubscribed = new SetSubscribedRequest
                {
                    Subscribe = subscribed,
                    PublicationHandle = Handle.HandleId,
                },
            };

            FfiClient.Instance.SendRequest(request);
        }
    }
}
