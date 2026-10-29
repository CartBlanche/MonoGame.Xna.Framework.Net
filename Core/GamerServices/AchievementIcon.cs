namespace Microsoft.Xna.Framework.GamerServices
{
    /// <summary>
    /// Binary achievement icon payload returned by media providers.
    /// </summary>
    public sealed class AchievementIcon
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="AchievementIcon"/> class.
        /// </summary>
        /// <param name="data">Encoded image bytes.</param>
        /// <param name="contentType">Media type of <paramref name="data"/>, for example <c>image/png</c>.</param>
        /// <param name="cacheKey">Optional key callers can use to cache the decoded icon.</param>
        /// <param name="width">Width in pixels, or 0 if unknown.</param>
        /// <param name="height">Height in pixels, or 0 if unknown.</param>
        /// <exception cref="ArgumentNullException"><paramref name="data"/> is null.</exception>
        /// <exception cref="ArgumentException"><paramref name="contentType"/> is null, empty or whitespace.</exception>
        public AchievementIcon(
            byte[] data,
            string contentType,
            string? cacheKey = null,
            int width = 0,
            int height = 0)
        {
            ArgumentNullException.ThrowIfNull(data);
            if (string.IsNullOrWhiteSpace(contentType))
                throw new ArgumentException("Content type cannot be empty.", nameof(contentType));

            Data = data;
            ContentType = contentType;
            CacheKey = cacheKey;
            Width = width;
            Height = height;
        }

        /// <summary>
        /// Gets the encoded image bytes.
        /// </summary>
        public byte[] Data { get; }
        /// <summary>
        /// Gets the media type of <see cref="Data"/>.
        /// </summary>
        public string ContentType { get; }
        /// <summary>
        /// Gets the optional cache key, or null.
        /// </summary>
        public string? CacheKey { get; }
        /// <summary>
        /// Gets the width in pixels, or 0 if unknown.
        /// </summary>
        public int Width { get; }
        /// <summary>
        /// Gets the height in pixels, or 0 if unknown.
        /// </summary>
        public int Height { get; }
    }
}
