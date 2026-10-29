using System;
using Microsoft.Xna.Framework;

namespace Microsoft.Xna.Framework.Net
{

    /// <summary>
    /// Types of network session join errors.
    /// </summary>
    public enum NetworkSessionJoinError
    {
        /// <summary>
        /// Unknown error.
        /// </summary>
        Unknown,

        /// <summary>
        /// Session is full.
        /// </summary>
        SessionFull,

        /// <summary>
        /// Session not found.
        /// </summary>
        SessionNotFound,

        /// <summary>
        /// Session not joinable.
        /// </summary>
        SessionNotJoinable,

        /// <summary>
        /// Protocol version mismatch between client and host.
        /// </summary>
        ProtocolVersionMismatch,

        /// <summary>
        /// Join request timed out (no response from host).
        /// </summary>
        Timeout,

        /// <summary>The session needs a password and none was given.</summary>
        PasswordRequired,

        /// <summary>The password given is not the session's.</summary>
        WrongPassword
    }
}
