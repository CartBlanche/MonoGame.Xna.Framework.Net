namespace Microsoft.Xna.Framework.Net
{
    /// <summary>
    /// Chooses the thread a <see cref="NetworkSession"/> raises its events and delivers game data on.
    /// </summary>
    public enum EventDispatchMode
    {
        /// <summary>
        /// Events are queued as they happen and raised from <see cref="NetworkSession.Update"/>, on the
        /// thread that calls it, as XNA did. Received data becomes available at the same point.
        /// </summary>
        OnUpdate,

        /// <summary>
        /// Events are raised straight away, on whichever thread produced them. For servers and tools that
        /// never call <see cref="NetworkSession.Update"/>; handlers must be thread-safe.
        /// </summary>
        Immediate
    }
}
