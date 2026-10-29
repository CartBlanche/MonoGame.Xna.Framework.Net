namespace Microsoft.Xna.Framework.GamerServices
{
	/// <summary>
	/// Exception thrown when network operations fail.
	/// </summary>
	public class NetworkException : Exception
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="NetworkException"/> class.
        /// </summary>
        public NetworkException() : base() { }
        /// <summary>
        /// Initializes a new instance of the <see cref="NetworkException"/> class with a message.
        /// </summary>
        public NetworkException(string message) : base(message) { }
        /// <summary>
        /// Initializes a new instance of the <see cref="NetworkException"/> class with a message and the exception that caused it.
        /// </summary>
        public NetworkException(string message, Exception innerException) : base(message, innerException) { }
    }
}
