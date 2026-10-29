using System;
using System.Diagnostics;

namespace Microsoft.Xna.Framework.Net
{
    /// <summary>
    /// Where the framework writes what it is doing: a session that has no logger of its own, the LAN
    /// discovery, and every back-end. A game points <see cref="Logger"/> at its own log once, at start-up.
    /// </summary>
    public static class NetworkLogging
    {
        private static volatile INetworkLogger logger = new DebugNetworkLogger();

        /// <summary>
        /// The framework's logger. The default writes to the debugger's output, which a release build discards;
        /// assign <see cref="NullNetworkLogger"/> to silence it or any <see cref="INetworkLogger"/> to keep it.
        /// </summary>
        public static INetworkLogger Logger
        {
            get => logger;
            set => logger = value ?? new NullNetworkLogger();
        }

        /// <summary>
        /// A logger that hands every call to whatever <see cref="Logger"/> is at that moment, so a session
        /// created before the game chose a logger still uses it.
        /// </summary>
        internal static INetworkLogger Current { get; } = new ForwardingLogger();

        private sealed class ForwardingLogger : INetworkLogger
        {
            public void LogInfo(string message) => logger.LogInfo(message);
            public void LogWarning(string message) => logger.LogWarning(message);
            public void LogError(string message, Exception? ex = null) => logger.LogError(message, ex);
        }
    }

    /// <summary>Writes to the debugger's output (<see cref="Debug"/>), so nothing is written in a release build.</summary>
    public class DebugNetworkLogger : INetworkLogger
    {
        /// <inheritdoc/>
        public void LogInfo(string message) => Debug.WriteLine($"[NET] {message}");

        /// <inheritdoc/>
        public void LogWarning(string message) => Debug.WriteLine($"[NET-WARN] {message}");

        /// <inheritdoc/>
        public void LogError(string message, Exception? ex = null)
        {
            Debug.WriteLine($"[NET-ERROR] {message}");
            if (ex != null)
                Debug.WriteLine($"  {ex}");
        }
    }
}
