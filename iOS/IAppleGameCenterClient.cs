namespace Microsoft.Xna.Framework.Net.iOS
{
    internal sealed class AppleGameCenterPlayer
    {
        public string Id { get; init; } = string.Empty;
        public string DisplayName { get; init; } = string.Empty;
    }

    internal sealed class AppleGameCenterScoreEntry
    {
        public int Rank { get; init; }
        public string PlayerDisplayName { get; init; } = string.Empty;
        public long Score { get; init; }
        public bool IsCurrentPlayer { get; init; }
    }

    internal sealed class AppleGameCenterAchievementProgress
    {
        public string Id { get; init; } = string.Empty;
        public bool IsUnlocked { get; init; }
        public float PercentComplete { get; init; }
        public DateTime? LastUpdatedUtc { get; init; }
        public bool IsRevealed { get; init; }
        public byte[] IconData { get; init; }
        public string IconContentType { get; init; } = "image/png";
    }

    internal interface IAppleGameCenterClient
    {
        Task<AppleGameCenterPlayer> AuthenticateAsync(CancellationToken cancellationToken = default);
        Task SubmitScoreAsync(string leaderboardId, long score, CancellationToken cancellationToken = default);
        Task<IReadOnlyList<AppleGameCenterScoreEntry>> GetTopScoresAsync(string leaderboardId, int maxResults, CancellationToken cancellationToken = default);
        Task<IReadOnlyDictionary<string, AppleGameCenterAchievementProgress>> GetAchievementProgressAsync(CancellationToken cancellationToken = default);
        Task UnlockAchievementAsync(string achievementId, CancellationToken cancellationToken = default);
        Task ReportProgressAsync(string achievementId, float percentComplete, CancellationToken cancellationToken = default);
    }
}