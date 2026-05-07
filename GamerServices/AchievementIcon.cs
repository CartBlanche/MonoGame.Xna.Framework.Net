namespace Microsoft.Xna.Framework.GamerServices
{
    /// <summary>
    /// Binary achievement icon payload returned by media providers.
    /// </summary>
    public sealed class AchievementIcon
    {
        public AchievementIcon(
            byte[] data,
            string contentType,
            string cacheKey = null,
            int width = 0,
            int height = 0)
        {
            if (data == null)
                throw new ArgumentNullException(nameof(data));
            if (string.IsNullOrWhiteSpace(contentType))
                throw new ArgumentException("Content type cannot be empty.", nameof(contentType));

            Data = data;
            ContentType = contentType;
            CacheKey = cacheKey;
            Width = width;
            Height = height;
        }

        public byte[] Data { get; }
        public string ContentType { get; }
        public string CacheKey { get; }
        public int Width { get; }
        public int Height { get; }
    }
}
