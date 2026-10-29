namespace Microsoft.Xna.Framework.Net.EOS
{
    /// <summary>
    /// Controls whether EOS integration fails fast or falls back to local providers.
    /// </summary>
    public enum EOSFallbackMode
    {
        /// <summary>Use the LAN or local providers when EOS is not ready or a live call fails.</summary>
        PreferFallback,
        /// <summary>Throw instead of falling back when EOS is not ready.</summary>
        Strict
    }
}
