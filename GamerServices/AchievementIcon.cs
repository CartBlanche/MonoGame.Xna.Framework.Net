namespace Microsoft.Xna.Framework.GamerServices
{
    /// <summary>
    /// Binary achievement icon payload returned by media providers.
    /// </summary>
    public sealed class AchievementIcon
    {
        public AchievementIcon(byte[] data, string contentType, string cacheKey = null)
        {
            if (data == null)
                throw new ArgumentNullException(nameof(data));
            if (string.IsNullOrWhiteSpace(contentType))
                throw new ArgumentException("Content type cannot be empty.", nameof(contentType));

            Data = data;
            ContentType = contentType;
            CacheKey = cacheKey;
        }

        public byte[] Data { get; }
        public string ContentType { get; }
        public string CacheKey { get; }
    }
}
