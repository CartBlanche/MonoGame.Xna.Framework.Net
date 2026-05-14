namespace Microsoft.Xna.Framework.Net.Android
{
    internal sealed class GooglePlayGamesPlayer
    {
        public string Id { get; init; } = string.Empty;
        public string DisplayName { get; init; } = string.Empty;
    }

    internal sealed class GooglePlayGamesScoreEntry
    {
        public int Rank { get; init; }
        public string PlayerDisplayName { get; init; } = string.Empty;
        public long Score { get; init; }
        public bool IsCurrentPlayer { get; init; }
    }

    internal sealed class GooglePlayGamesAchievementProgress
    {
        public string Id { get; init; } = string.Empty;
        public bool IsUnlocked { get; init; }
        public float PercentComplete { get; init; }
        public DateTime? LastUpdatedUtc { get; init; }
        public bool IsRevealed { get; init; }
        public string IconUrl { get; init; } = string.Empty;
    }

    internal interface IGooglePlayGamesClient
    {
        Task<GooglePlayGamesPlayer> GetCurrentPlayerAsync(CancellationToken cancellationToken = default);
        Task SubmitScoreAsync(string leaderboardId, long score, CancellationToken cancellationToken = default);
        Task<IReadOnlyList<GooglePlayGamesScoreEntry>> GetTopScoresAsync(string leaderboardId, int maxResults, CancellationToken cancellationToken = default);
        Task<IReadOnlyDictionary<string, GooglePlayGamesAchievementProgress>> GetAchievementProgressAsync(CancellationToken cancellationToken = default);
        Task UnlockAchievementAsync(string achievementId, CancellationToken cancellationToken = default);
        Task IncrementAchievementAsync(string achievementId, int steps, CancellationToken cancellationToken = default);
    }
}
