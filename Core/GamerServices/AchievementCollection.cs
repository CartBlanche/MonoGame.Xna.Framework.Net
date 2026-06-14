using System.Collections.ObjectModel;

namespace Microsoft.Xna.Framework.GamerServices
{
    /// <summary>
    /// Read-only achievement collection.
    /// </summary>
    public sealed class AchievementCollection : ReadOnlyCollection<Achievement>
    {
        public AchievementCollection(IList<Achievement> list)
            : base(list ?? throw new ArgumentNullException(nameof(list)))
        {
        }

        public Achievement this[string key]
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
