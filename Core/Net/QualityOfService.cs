namespace Microsoft.Xna.Framework.Net
{
	/// <summary>
	/// Quality of service information for a network session.
	/// </summary>
	public class QualityOfService
    {
        /// <summary>
        /// Gets the average round trip time in milliseconds.
        /// </summary>
        public TimeSpan AverageRoundtripTime { get; internal set; } = TimeSpan.FromMilliseconds(50);

        /// <summary>
        /// Gets the minimum round trip time in milliseconds.
        /// </summary>
        public TimeSpan MinimumRoundtripTime { get; internal set; } = TimeSpan.FromMilliseconds(20);

        /// <summary>
        /// Gets the maximum round trip time. (Not in XNA.)
        /// </summary>
        public TimeSpan MaximumRoundtripTime { get; internal set; } = TimeSpan.FromMilliseconds(100);

        /// <summary>
        /// Gets an estimate of the upstream bandwidth, from this machine to the session host, in bytes per second.
        /// </summary>
        public int BytesPerSecondUpstream { get; internal set; } = 0;

        /// <summary>
        /// Gets an estimate of the downstream bandwidth, from the session host to this machine, in bytes per second.
        /// </summary>
        public int BytesPerSecondDownstream { get; internal set; } = 0;

        /// <summary>
        /// Gets whether the connection is available.
        /// </summary>
        public bool IsAvailable { get; internal set; } = true;
    }
}
