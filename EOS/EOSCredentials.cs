namespace Microsoft.Xna.Framework.Net.EOS
{
    /// <summary>
    /// EOS application credentials supplied at startup via <see cref="EOSPlatformBootstrap.Configure"/>.
    /// All five fields are required; <see cref="ProductName"/> and <see cref="ProductVersion"/> are optional.
    /// </summary>
    public sealed class EOSCredentials
    {
        public string ProductId { get; init; }
        public string SandboxId { get; init; }
        public string DeploymentId { get; init; }
        public string ClientId { get; init; }
        public string ClientSecret { get; init; }
        public string ProductName { get; init; }
        public string ProductVersion { get; init; }
        public string BucketId { get; init; }
    }
}
