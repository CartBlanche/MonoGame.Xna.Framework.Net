namespace Microsoft.Xna.Framework.Net.EOS
{
    /// <summary>
    /// Minimal default EOS client implementation.
    ///
    /// This vertical slice keeps EOS calls behind an injectable seam so host-runnable
    /// tests and fallback behavior remain deterministic when SDK credentials/runtime are unavailable.
    /// </summary>
    internal sealed class EOSClient : IEpicOnlineServicesClient
    {
        public Task<EpicAccountPlayer> AuthenticateAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(new EpicAccountPlayer());
        }

        public Task SubmitScoreAsync(string leaderboardId, long score, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(leaderboardId))
                throw new ArgumentException("Leaderboard id cannot be empty.", nameof(leaderboardId));

            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<EpicLeaderboardEntry>> GetTopScoresAsync(string leaderboardId, int maxResults, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(leaderboardId))
                throw new ArgumentException("Leaderboard id cannot be empty.", nameof(leaderboardId));

            maxResults = Math.Clamp(maxResults, 1, 100);
            return Task.FromResult<IReadOnlyList<EpicLeaderboardEntry>>(Array.Empty<EpicLeaderboardEntry>());
        }

        public Task<IReadOnlyDictionary<string, EpicAchievementProgress>> GetAchievementProgressAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult<IReadOnlyDictionary<string, EpicAchievementProgress>>(new Dictionary<string, EpicAchievementProgress>());
        }

        public Task UnlockAchievementAsync(string achievementId, CancellationToken cancellationToken = default)
        {
            return ReportProgressAsync(achievementId, 100f, cancellationToken);
        }

        public Task ReportProgressAsync(string achievementId, float percentComplete, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(achievementId))
                throw new ArgumentException("Achievement id cannot be empty.", nameof(achievementId));

            return Task.CompletedTask;
        }
    }
}
