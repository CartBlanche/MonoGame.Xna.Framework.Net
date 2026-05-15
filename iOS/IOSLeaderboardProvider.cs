using Microsoft.Xna.Framework.GamerServices;

namespace Microsoft.Xna.Framework.Net.iOS
{
    /// <summary>
    /// Leaderboard provider for iOS backend.
    /// Uses process-local shared state with optional Game Center submission/reads.
    /// </summary>
    public sealed class IOSLeaderboardProvider : ILeaderboardProvider
    {
        private sealed class Submission
        {
            public string GamerKey = string.Empty;
            public string Gamertag = string.Empty;
            public long Score;
            public DateTime UpdatedUtc;
        }

        private static readonly object Gate = new();
        private static readonly Dictionary<string, Dictionary<string, Submission>> Tables = new(StringComparer.Ordinal);

        public async Task SubmitAsync(LeaderboardWriter writer, CancellationToken cancellationToken = default)
        {
            if (writer == null)
                throw new ArgumentNullException(nameof(writer));

            cancellationToken.ThrowIfCancellationRequested();

            if (IOSRuntime.TryGetAppleGameCenterClient(out var client))
            {
                try
                {
                    await client.SubmitScoreAsync(writer.LeaderboardIdentity.Key, writer.Score, cancellationToken).ConfigureAwait(false);
                }
                catch
                {
                    // Fall back to local storage if live call fails.
                }
            }

            lock (Gate)
            {
                if (!Tables.TryGetValue(writer.LeaderboardIdentity.Key, out var table))
                {
                    table = new Dictionary<string, Submission>(StringComparer.Ordinal);
                    Tables[writer.LeaderboardIdentity.Key] = table;
                }

                var gamerKey = ResolveGamerKey(writer.Gamer);
                if (!table.TryGetValue(gamerKey, out var existing))
                {
                    table[gamerKey] = new Submission
                    {
                        GamerKey = gamerKey,
                        Gamertag = writer.Gamer.Gamertag,
                        Score = writer.Score,
                        UpdatedUtc = DateTime.UtcNow
                    };

                    return;
                }

                if (writer.Score > existing.Score)
                {
                    existing.Score = writer.Score;
                    existing.UpdatedUtc = DateTime.UtcNow;
                    existing.Gamertag = writer.Gamer.Gamertag;
                }
            }
        }

        public async Task<LeaderboardReader> ReadAsync(
            LeaderboardIdentity identity,
            int pageStart,
            int pageSize,
            SignedInGamer pivotGamer = null,
            CancellationToken cancellationToken = default)
        {
            if (identity == null)
                throw new ArgumentNullException(nameof(identity));
            if (pageStart < 0)
                throw new ArgumentOutOfRangeException(nameof(pageStart));
            if (pageSize <= 0)
                throw new ArgumentOutOfRangeException(nameof(pageSize));

            cancellationToken.ThrowIfCancellationRequested();

            if (IOSRuntime.TryGetAppleGameCenterClient(out var client))
            {
                try
                {
                    var remote = await client
                        .GetTopScoresAsync(identity.Key, pageStart + pageSize, cancellationToken)
                        .ConfigureAwait(false);

                    if (remote.Count > 0)
                    {
                        var rows = remote
                            .Skip(pageStart)
                            .Take(pageSize)
                            .Select(x => new LeaderboardEntry(
                                x.Rank,
                                string.IsNullOrWhiteSpace(x.PlayerDisplayName) ? "Player" : x.PlayerDisplayName,
                                x.Score,
                                x.IsCurrentPlayer))
                            .ToList();

                        return new LeaderboardReader(identity.Key, pageStart, remote.Count, rows);
                    }
                }
                catch
                {
                    // Fall back to local table if live call fails.
                }
            }

            lock (Gate)
            {
                if (!Tables.TryGetValue(identity.Key, out var table) || table.Count == 0)
                {
                    return new LeaderboardReader(identity.Key, pageStart, 0, new List<LeaderboardEntry>());
                }

                var ordered = table.Values
                    .OrderByDescending(s => s.Score)
                    .ThenBy(s => s.UpdatedUtc)
                    .ThenBy(s => s.GamerKey, StringComparer.Ordinal)
                    .ToList();

                if (pivotGamer != null)
                {
                    var pivotKey = ResolveGamerKey(pivotGamer);
                    var pivotIndex = ordered.FindIndex(s => s.GamerKey == pivotKey);
                    if (pivotIndex >= 0)
                    {
                        var half = pageSize / 2;
                        pageStart = Math.Max(0, pivotIndex - half);
                    }
                }

                var rows = ordered
                    .Select((s, i) => new { Submission = s, Rank = i + 1 })
                    .Skip(pageStart)
                    .Take(pageSize)
                    .Select(x => new LeaderboardEntry(
                        x.Rank,
                        x.Submission.Gamertag,
                        x.Submission.Score,
                        pivotGamer != null && x.Submission.GamerKey == ResolveGamerKey(pivotGamer)))
                    .ToList();

                return new LeaderboardReader(identity.Key, pageStart, ordered.Count, rows);
            }
        }

        private static string ResolveGamerKey(SignedInGamer gamer)
        {
            return string.IsNullOrWhiteSpace(gamer?.Gamertag) ? "Player" : gamer.Gamertag;
        }
    }
}