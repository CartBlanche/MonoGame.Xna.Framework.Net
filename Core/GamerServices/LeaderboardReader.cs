using System.Collections;

namespace Microsoft.Xna.Framework.GamerServices
{
    /// <summary>
    /// Reads leaderboard entries for a range.
    /// </summary>
    public sealed class LeaderboardReader : IDisposable, IEnumerable<LeaderboardEntry>
    {
        private readonly List<LeaderboardEntry> entries;

        internal LeaderboardReader(string leaderboardKey, int pageStart, int totalRowCount, List<LeaderboardEntry> entries)
        {
            LeaderboardKey = leaderboardKey;
            PageStart = pageStart;
            TotalRowCount = totalRowCount;
            this.entries = entries;
        }

        /// <summary>
        /// Gets the key of the leaderboard that was read.
        /// </summary>
        public string LeaderboardKey { get; }
        /// <summary>
        /// Gets the zero-based row index of the first entry in this page.
        /// </summary>
        public int PageStart { get; }
        /// <summary>
        /// Gets the total number of rows on the leaderboard, not just in this page.
        /// </summary>
        public int TotalRowCount { get; }
        /// <summary>
        /// Gets the number of entries in this page.
        /// </summary>
        public int Count => entries.Count;

        /// <summary>
        /// Gets the entry at the given index within this page.
        /// </summary>
        public LeaderboardEntry this[int index] => entries[index];

        /// <summary>
        /// Returns an enumerator over the entries in this page.
        /// </summary>
        public IEnumerator<LeaderboardEntry> GetEnumerator() => entries.GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        /// <summary>
        /// Does nothing; present for XNA API compatibility.
        /// </summary>
        public void Dispose()
        {
            // Included for API compatibility.
        }
    }
}
