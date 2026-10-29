namespace Microsoft.Xna.Framework.GamerServices
{
	/// <summary>
	/// Exception thrown when gamer privilege operations fail.
	/// </summary>
	public class GamerPrivilegeException : Exception
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="GamerPrivilegeException"/> class.
        /// </summary>
        public GamerPrivilegeException() : base() { }
        /// <summary>
        /// Initializes a new instance of the <see cref="GamerPrivilegeException"/> class with a message.
        /// </summary>
        public GamerPrivilegeException(string message) : base(message) { }
        /// <summary>
        /// Initializes a new instance of the <see cref="GamerPrivilegeException"/> class with a message and the exception that caused it.
        /// </summary>
        public GamerPrivilegeException(string message, Exception innerException) : base(message, innerException) { }
    }
}
