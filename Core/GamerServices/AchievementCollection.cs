using System.Collections.ObjectModel;

namespace Microsoft.Xna.Framework.GamerServices
{
    /// <summary>
    /// Read-only achievement collection.
    /// </summary>
    public sealed class AchievementCollection : ReadOnlyCollection<Achievement>
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="AchievementCollection"/> class wrapping <paramref name="list"/>.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="list"/> is null.</exception>
        public AchievementCollection(IList<Achievement> list)
            : base(list ?? throw new ArgumentNullException(nameof(list)))
        {
        }

        /// <summary>
        /// Gets the achievement with the given key, or null if there is none. Keys are compared ordinally.
        /// </summary>
        /// <exception cref="ArgumentException"><paramref name="key"/> is null, empty or whitespace.</exception>
        public Achievement? this[string key]
        {
            get
            {
                if (string.IsNullOrWhiteSpace(key))
                    throw new ArgumentException("Achievement key cannot be empty.", nameof(key));

                return this.FirstOrDefault(a => string.Equals(a.Key, key, StringComparison.Ordinal));
            }
        }
    }
}
