namespace Microsoft.Xna.Framework.GamerServices
{
    /// <summary>
    /// Global registration point for canonical achievement metadata.
    /// </summary>
    public static class AchievementCatalog
    {
        private static readonly object Gate = new();
        private static readonly Dictionary<string, AchievementDefinition> Definitions = new(StringComparer.Ordinal);

        public static void Register(AchievementDefinition definition)
        {
            if (definition == null)
                throw new ArgumentNullException(nameof(definition));

            lock (Gate)
            {
                Definitions[definition.Key] = definition;
            }
        }

        public static void RegisterRange(IEnumerable<AchievementDefinition> definitions)
        {
            if (definitions == null)
                throw new ArgumentNullException(nameof(definitions));

            lock (Gate)
            {
                foreach (var definition in definitions)
                {
                    if (definition == null)
                        continue;

                    Definitions[definition.Key] = definition;
                }
            }
        }

        public static AchievementDefinition Get(string key)
        {
            if (string.IsNullOrWhiteSpace(key))
                throw new ArgumentException("Achievement key cannot be empty.", nameof(key));

            lock (Gate)
            {
                Definitions.TryGetValue(key, out var definition);
                return definition;
            }
        }

        public static IReadOnlyList<AchievementDefinition> GetAll()
        {
            lock (Gate)
            {
                return Definitions.Values
                    .OrderBy(d => d.Key, StringComparer.Ordinal)
                    .ToList();
            }
        }

        public static void Clear()
        {
            lock (Gate)
            {
                Definitions.Clear();
            }
        }
    }
}
