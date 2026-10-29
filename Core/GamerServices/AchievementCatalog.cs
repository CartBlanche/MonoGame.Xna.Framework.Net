namespace Microsoft.Xna.Framework.GamerServices
{
    /// <summary>
    /// Global registration point for canonical achievement metadata.
    /// </summary>
    public static class AchievementCatalog
    {
        private static readonly object Gate = new();
        private static readonly Dictionary<string, AchievementDefinition> Definitions = new(StringComparer.Ordinal);

        /// <summary>
        /// Registers a definition, replacing any existing definition with the same key.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="definition"/> is null.</exception>
        public static void Register(AchievementDefinition definition)
        {
            ArgumentNullException.ThrowIfNull(definition);

            lock (Gate)
            {
                Definitions[definition.Key] = definition;
            }
        }

        /// <summary>
        /// Registers every definition in the sequence, replacing existing definitions with the same key. Null items are skipped.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="definitions"/> is null.</exception>
        public static void RegisterRange(IEnumerable<AchievementDefinition> definitions)
        {
            ArgumentNullException.ThrowIfNull(definitions);

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

        /// <summary>
        /// Gets the definition registered under <paramref name="key"/>, or null if there is none. Keys are compared ordinally.
        /// </summary>
        /// <exception cref="ArgumentException"><paramref name="key"/> is null, empty or whitespace.</exception>
        public static AchievementDefinition? Get(string key)
        {
            if (string.IsNullOrWhiteSpace(key))
                throw new ArgumentException("Achievement key cannot be empty.", nameof(key));

            lock (Gate)
            {
                Definitions.TryGetValue(key, out var definition);
                return definition;
            }
        }

        /// <summary>
        /// Gets a snapshot of all registered definitions, ordered by key.
        /// </summary>
        public static IReadOnlyList<AchievementDefinition> GetAll()
        {
            lock (Gate)
            {
                return Definitions.Values
                    .OrderBy(d => d.Key, StringComparer.Ordinal)
                    .ToList();
            }
        }

        /// <summary>
        /// Removes all registered definitions.
        /// </summary>
        public static void Clear()
        {
            lock (Gate)
            {
                Definitions.Clear();
            }
        }
    }
}
