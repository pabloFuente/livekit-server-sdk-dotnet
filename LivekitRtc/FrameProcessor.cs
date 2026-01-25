// author: https://github.com/pabloFuente

using System;

namespace LiveKit.Rtc
{
    /// <summary>
    /// Information about the stream being processed.
    /// </summary>
    public class FrameProcessorStreamInfo
    {
        /// <summary>
        /// Gets or sets the room name.
        /// </summary>
        public string RoomName { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the participant identity.
        /// </summary>
        public string ParticipantIdentity { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the publication SID.
        /// </summary>
        public string PublicationSid { get; set; } = string.Empty;
    }

    /// <summary>
    /// Credentials for the stream being processed.
    /// </summary>
    public class FrameProcessorCredentials
    {
        /// <summary>
        /// Gets or sets the access token.
        /// </summary>
        public string Token { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the LiveKit server URL.
        /// </summary>
        public string Url { get; set; } = string.Empty;
    }

    /// <summary>
    /// Abstract base class for processing audio or video frames.
    /// </summary>
    /// <typeparam name="TFrame">The type of frame to process (AudioFrame or VideoFrame).</typeparam>
    public abstract class FrameProcessor<TFrame>
        where TFrame : class
    {
        /// <summary>
        /// Gets whether the frame processor is enabled.
        /// </summary>
        public abstract bool IsEnabled { get; set; }

        /// <summary>
        /// Called when stream information is updated.
        /// </summary>
        /// <param name="info">The updated stream information.</param>
        public virtual void OnStreamInfoUpdated(FrameProcessorStreamInfo info)
        {
            // Default implementation does nothing
        }

        /// <summary>
        /// Called when credentials are updated.
        /// </summary>
        /// <param name="credentials">The updated credentials.</param>
        public virtual void OnCredentialsUpdated(FrameProcessorCredentials credentials)
        {
            // Default implementation does nothing
        }

        /// <summary>
        /// Process a frame and return the processed frame.
        /// </summary>
        /// <param name="frame">The frame to process.</param>
        /// <returns>The processed frame.</returns>
        public abstract TFrame Process(TFrame frame);

        /// <summary>
        /// Close and clean up the frame processor.
        /// </summary>
        public abstract void Close();
    }
}
