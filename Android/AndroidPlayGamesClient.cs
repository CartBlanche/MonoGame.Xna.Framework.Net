using Android.App;
using Android.Gms.Extensions;
using Android.Gms.Games;
using Android.Gms.Games.Achievement;
using Android.Gms.Games.Leaderboard;

using Microsoft.Xna.Framework.GamerServices;

namespace Microsoft.Xna.Framework.Net.Android
{
    /// <summary>
    /// <see cref="IGooglePlayGamesClient"/> backed by Xamarin.GooglePlayServices.Games.V2 bindings.
    /// </summary>
    internal sealed class AndroidPlayGamesClient : IGooglePlayGamesClient
    {
        private readonly Activity _activity;

        internal AndroidPlayGamesClient(Activity activity)
        {
            _activity = activity ?? throw new ArgumentNullException(nameof(activity));
        }

        public async Task<GooglePlayGamesPlayer> GetCurrentPlayerAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var signInClient = PlayGames.GetGamesSignInClient(_activity);
            var authResult = await signInClient.IsAuthenticated()
                .AsAsync<AuthenticationResult>()
                .ConfigureAwait(false);

            if (authResult == null || !authResult.IsAuthenticated)
                return new GooglePlayGamesPlayer { Id = string.Empty, DisplayName = string.Empty };

            var gamer = SignedInGamer.Current;
            return new GooglePlayGamesPlayer
            {
                Id = gamer.Gamertag,
                DisplayName = gamer.Gamertag
            };
        }

        public Task SubmitScoreAsync(string leaderboardId, long score, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(leaderboardId))
                throw new ArgumentException("Leaderboard id cannot be empty.", nameof(leaderboardId));
            cancellationToken.ThrowIfCancellationRequested();

            var client = PlayGames.GetLeaderboardsClient(_activity);
            client.SubmitScore(leaderboardId, score);
            return Task.CompletedTask;
        }

        public async Task<IReadOnlyList<GooglePlayGamesScoreEntry>> GetTopScoresAsync(
            string leaderboardId, int maxResults, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(leaderboardId))
                throw new ArgumentException("Leaderboard id cannot be empty.", nameof(leaderboardId));
            cancellationToken.ThrowIfCancellationRequested();
            maxResults = Math.Clamp(maxResults, 1, 25);

            var client = PlayGames.GetLeaderboardsClient(_activity);
            var result = await client.LoadTopScores(
                    leaderboardId,
                    LeaderboardVariant.TimeSpanAllTime,
                    LeaderboardVariant.CollectionPublic,
                    maxResults)
                .AsAsync<LeaderboardsClientLeaderboardScores>()
                .ConfigureAwait(false);

            var output = new List<GooglePlayGamesScoreEntry>();
            var buffer = result?.Scores;
            if (buffer == null)
                return output;

            for (int i = 0; i < buffer.Count; i++)
            {
                var entry = (LeaderboardScoreEntity)buffer.Get(i);
                output.Add(new GooglePlayGamesScoreEntry
                {
                    Rank = i + 1,
                    PlayerDisplayName = entry?.ScoreHolderDisplayName ?? string.Empty,
                    Score = entry?.RawScore ?? 0,
                    IsCurrentPlayer = false
                });
            }

            buffer.Release();
            return output;
        }

        public async Task<IReadOnlyDictionary<string, GooglePlayGamesAchievementProgress>> GetAchievementProgressAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var client = PlayGames.GetAchievementsClient(_activity);
            var buffer = await client.Load(false)
                .AsAsync<AchievementBuffer>()
                .ConfigureAwait(false);

            var output = new Dictionary<string, GooglePlayGamesAchievementProgress>(StringComparer.Ordinal);
            if (buffer == null)
                return output;

            for (int i = 0; i < buffer.Count; i++)
            {
                var ach = (AchievementRef)buffer.Get(i);
                if (ach == null || string.IsNullOrWhiteSpace(ach.AchievementId))
                    continue;

                bool isUnlocked = ach.State == 2; // STATE_UNLOCKED
                bool isRevealed = isUnlocked || ach.State == 1; // STATE_REVEALED

                float percent;
                if (isUnlocked)
                    percent = 100f;
                else if (ach.TotalSteps > 0)
                    percent = Math.Clamp(100f * ach.CurrentSteps / ach.TotalSteps, 0f, 99f);
                else
                    percent = 0f;

                DateTime? lastUpdated = ach.LastUpdatedTimestamp > 0
                    ? DateTimeOffset.FromUnixTimeMilliseconds(ach.LastUpdatedTimestamp).UtcDateTime
                    : null;

                string iconUrl = isUnlocked
                    ? ach.UnlockedImageUrl ?? string.Empty
                    : ach.RevealedImageUrl ?? string.Empty;

                output[ach.AchievementId] = new GooglePlayGamesAchievementProgress
                {
                    Id = ach.AchievementId,
                    IsUnlocked = isUnlocked,
                    PercentComplete = percent,
                    LastUpdatedUtc = lastUpdated,
                    IsRevealed = isRevealed,
                    IconUrl = iconUrl
                };
            }

            buffer.Release();
            return output;
        }

        public Task UnlockAchievementAsync(string achievementId, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(achievementId))
                throw new ArgumentException("Achievement id cannot be empty.", nameof(achievementId));
            cancellationToken.ThrowIfCancellationRequested();

            var client = PlayGames.GetAchievementsClient(_activity);
            client.Unlock(achievementId);
            return Task.CompletedTask;
        }

        public Task IncrementAchievementAsync(string achievementId, int steps, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(achievementId))
                throw new ArgumentException("Achievement id cannot be empty.", nameof(achievementId));
            cancellationToken.ThrowIfCancellationRequested();
            steps = Math.Max(1, steps);

            var client = PlayGames.GetAchievementsClient(_activity);
            client.Increment(achievementId, steps);
            return Task.CompletedTask;
        }
    }
}
