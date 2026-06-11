namespace Microsoft.Xna.Framework.Net.EOS
{
    internal sealed class EpicAccountPlayer
    {
        public string Id { get; init; } = string.Empty;
        public string DisplayName { get; init; } = string.Empty;
    }

    internal sealed class EpicLeaderboardEntry
    {
        public int Rank { get; init; }
        public string PlayerDisplayName { get; init; } = string.Empty;
        public long Score { get; init; }
        public bool IsCurrentPlayer { get; init; }
    }

    internal sealed class EpicAchievementProgress
    {
        public string Id { get; init; } = string.Empty;
        public bool IsUnlocked { get; init; }
        public float PercentComplete { get; init; }
        public DateTime? LastUpdatedUtc { get; init; }
        public bool IsRevealed { get; init; }
        public byte[] IconData { get; init; }
        public string IconContentType { get; init; } = "image/png";
    }

    internal interface IEpicOnlineServicesClient
    {
        Task<EpicAccountPlayer> AuthenticateAsync(CancellationToken cancellationToken = default);
        Task SubmitScoreAsync(string leaderboardId, long score, CancellationToken cancellationToken = default);
        Task<IReadOnlyList<EpicLeaderboardEntry>> GetTopScoresAsync(string leaderboardId, int maxResults, CancellationToken cancellationToken = default);
        Task<IReadOnlyDictionary<string, EpicAchievementProgress>> GetAchievementProgressAsync(CancellationToken cancellationToken = default);
        Task UnlockAchievementAsync(string achievementId, CancellationToken cancellationToken = default);
        Task ReportProgressAsync(string achievementId, float percentComplete, CancellationToken cancellationToken = default);
    }
}
