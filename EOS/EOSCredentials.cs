namespace Microsoft.Xna.Framework.Net.EOS
{
    /// <summary>
    /// EOS application credentials supplied at startup via <see cref="EOSPlatformBootstrap.Configure"/>.
    /// All five fields are required; <see cref="ProductName"/> and <see cref="ProductVersion"/> are optional.
    /// </summary>
    public sealed class EOSCredentials
    {
        /// <summary>The EOS product id from the Epic Developer Portal.</summary>
        public string ProductId { get; init; }
        /// <summary>The sandbox id from the Epic Developer Portal.</summary>
        public string SandboxId { get; init; }
        /// <summary>The deployment id from the Epic Developer Portal.</summary>
        public string DeploymentId { get; init; }
        /// <summary>The client id of the EOS client policy used to initialise the platform.</summary>
        public string ClientId { get; init; }
        /// <summary>The client secret that goes with <see cref="ClientId"/>. Keep it out of source control.</summary>
        public string ClientSecret { get; init; }
        /// <summary>
        /// Product name reported to EOS. Optional; falls back to the <c>MGNET_EOS_PRODUCT_NAME</c> environment variable, then to <c>MonoGame.Xna.Framework.Net</c>.
        /// </summary>
        public string ProductName { get; init; }
        /// <summary>
        /// Product version reported to EOS. Optional; falls back to the <c>MGNET_EOS_PRODUCT_VERSION</c> environment variable, then to <c>1.0.0</c>.
        /// </summary>
        public string ProductVersion { get; init; }
        /// <summary>
        /// Intended lobby bucket id. Currently not read by this library: <see cref="EOSBackend"/> uses a fixed bucket.
        /// </summary>
        public string BucketId { get; init; }
    }
}
