namespace Microsoft.Xna.Framework.Net.iOS
{
    /// <summary>
    /// Controls whether iOS integration fails fast or falls back to local providers.
    /// </summary>
    public enum IOSFallbackMode
    {
        /// <summary>Use the LAN or local providers when the iOS runtime is not initialized or a live call fails.</summary>
        PreferFallback,
        /// <summary>Throw instead of falling back when the iOS runtime is not initialized.</summary>
        Strict
    }
}