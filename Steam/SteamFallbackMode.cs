namespace Microsoft.Xna.Framework.Net.Steam
{
    /// <summary>
    /// Controls how Steam back-end code behaves when Steam APIs are unavailable or fail.
    /// </summary>
    public enum SteamFallbackMode
    {
        /// <summary>
        /// Preserve existing behavior by allowing fallback to non-Steam vertical-slice paths.
        /// </summary>
        PreferFallback = 0,

        /// <summary>
        /// Enforce Steam-only behavior and surface failures immediately.
        /// </summary>
        Strict = 1
    }
}
