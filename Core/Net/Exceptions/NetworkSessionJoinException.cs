namespace Microsoft.Xna.Framework.Net
{
	/// <summary>
	/// Exception thrown when network session join operations fail.
	/// </summary>
	public class NetworkSessionJoinException : Exception
    {
        /// <summary>
        /// Gets the join error type.
        /// </summary>
        public NetworkSessionJoinError JoinError { get; }

        /// <summary>Initializes a new instance of <see cref="NetworkSessionJoinException"/> with <see cref="JoinError"/> set to <see cref="NetworkSessionJoinError.Unknown"/>.</summary>
        public NetworkSessionJoinException() : base() 
        {
            JoinError = NetworkSessionJoinError.Unknown;
        }

        /// <summary>Initializes a new instance of <see cref="NetworkSessionJoinException"/> with a message and <see cref="JoinError"/> set to <see cref="NetworkSessionJoinError.Unknown"/>.</summary>
        public NetworkSessionJoinException(string message) : base(message) 
        {
            JoinError = NetworkSessionJoinError.Unknown;
        }

        /// <summary>Initializes a new instance of <see cref="NetworkSessionJoinException"/> with a message and the exception that caused it; <see cref="JoinError"/> is <see cref="NetworkSessionJoinError.Unknown"/>.</summary>
        public NetworkSessionJoinException(string message, Exception innerException) : base(message, innerException) 
        {
            JoinError = NetworkSessionJoinError.Unknown;
        }

        /// <summary>Initializes a new instance of <see cref="NetworkSessionJoinException"/> for the given reason, with the default message.</summary>
        public NetworkSessionJoinException(NetworkSessionJoinError joinError) : base() 
        {
            JoinError = joinError;
        }

        /// <summary>Initializes a new instance of <see cref="NetworkSessionJoinException"/> with a message and the reason the join failed.</summary>
        public NetworkSessionJoinException(string message, NetworkSessionJoinError joinError) : base(message) 
        {
            JoinError = joinError;
        }
    }
}
