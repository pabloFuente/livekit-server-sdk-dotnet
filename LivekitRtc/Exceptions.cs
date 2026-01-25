// author: https://github.com/pabloFuente

using System;

namespace LiveKit.Rtc
{
    /// <summary>
    /// Exception thrown for room-related errors.
    /// </summary>
    public class RoomException : Exception
    {
        /// <summary>
        /// Creates a new RoomException with the specified message.
        /// </summary>
        /// <param name="message">The error message.</param>
        public RoomException(string message)
            : base(message) { }

        /// <summary>
        /// Creates a new RoomException with the specified message and inner exception.
        /// </summary>
        /// <param name="message">The error message.</param>
        /// <param name="innerException">The inner exception.</param>
        public RoomException(string message, Exception innerException)
            : base(message, innerException) { }
    }

    /// <summary>
    /// Exception thrown for track-related errors.
    /// </summary>
    public class TrackException : Exception
    {
        /// <summary>
        /// Creates a new TrackException with the specified message.
        /// </summary>
        /// <param name="message">The error message.</param>
        public TrackException(string message)
            : base(message) { }

        /// <summary>
        /// Creates a new TrackException with the specified message and inner exception.
        /// </summary>
        /// <param name="message">The error message.</param>
        /// <param name="innerException">The inner exception.</param>
        public TrackException(string message, Exception innerException)
            : base(message, innerException) { }
    }

    /// <summary>
    /// Exception thrown for FFI-related errors.
    /// </summary>
    public class FfiException : Exception
    {
        /// <summary>
        /// Creates a new FfiException with the specified message.
        /// </summary>
        /// <param name="message">The error message.</param>
        public FfiException(string message)
            : base(message) { }

        /// <summary>
        /// Creates a new FfiException with the specified message and inner exception.
        /// </summary>
        /// <param name="message">The error message.</param>
        /// <param name="innerException">The inner exception.</param>
        public FfiException(string message, Exception innerException)
            : base(message, innerException) { }
    }
}
