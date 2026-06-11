namespace Microsoft.Xna.Framework.Net.EOS
{
    /// <summary>
    /// Controls whether EOS integration fails fast or falls back to local providers.
    /// </summary>
    public enum EOSFallbackMode
    {
        PreferFallback,
        Strict
    }
}
