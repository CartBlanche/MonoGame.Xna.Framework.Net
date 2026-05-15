using Foundation;
using GameKit;
using UIKit;

namespace Microsoft.Xna.Framework.Net.iOS
{
    /// <summary>
    /// Concrete Game Center client for iOS devices.
    /// Uses native GameKit APIs for authentication, scores, and achievements.
    /// </summary>
    internal sealed class IOSGameCenterClient : IAppleGameCenterClient
    {
        public Task<AppleGameCenterPlayer> AuthenticateAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var tcs = new TaskCompletionSource<AppleGameCenterPlayer>(TaskCreationOptions.RunContinuationsAsynchronously);

            UIApplication.SharedApplication.BeginInvokeOnMainThread(() =>
            {
                try
                {
                    var localPlayer = GKLocalPlayer.Local;
                    localPlayer.AuthenticateHandler = (controller, error) =>
                    {
                        if (cancellationToken.IsCancellationRequested)
                        {
                            tcs.TrySetCanceled(cancellationToken);
                            return;
                        }

                        if (controller != null)
                        {
                            PresentAuthController(controller);
                            return;
                        }

                        if (error != null)
                        {
                            tcs.TrySetException(new InvalidOperationException($"Game Center auth failed: {error.LocalizedDescription}"));
                            return;
                        }

                        if (!localPlayer.Authenticated)
                        {
                            tcs.TrySetResult(new AppleGameCenterPlayer());
                            return;
                        }

                        tcs.TrySetResult(new AppleGameCenterPlayer
                        {
                            Id = ResolvePlayerId(localPlayer),
                            DisplayName = ResolveDisplayName(localPlayer)
                        });
                    };
                }
                catch (Exception ex)
                {
                    tcs.TrySetException(ex);
                }
            });

            return tcs.Task;
        }

        public Task SubmitScoreAsync(string leaderboardId, long score, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(leaderboardId))
                throw new ArgumentException("Leaderboard id cannot be empty.", nameof(leaderboardId));

            cancellationToken.ThrowIfCancellationRequested();

            var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            UIApplication.SharedApplication.BeginInvokeOnMainThread(() =>
            {
                try
                {
                    LoadLeaderboardAsync(leaderboardId, cancellationToken).ContinueWith(loadTask =>
                    {
                        if (loadTask.IsCanceled || cancellationToken.IsCancellationRequested)
                        {
                            tcs.TrySetCanceled(cancellationToken);
                            return;
                        }

                        if (loadTask.IsFaulted)
                        {
                            tcs.TrySetException(loadTask.Exception?.GetBaseException() ?? new InvalidOperationException("Game Center leaderboard load failed."));
                            return;
                        }

                        loadTask.Result.SubmitScore(new IntPtr(score), UIntPtr.Zero, GKLocalPlayer.Local, error =>
                        {
                            if (cancellationToken.IsCancellationRequested)
                            {
                                tcs.TrySetCanceled(cancellationToken);
                                return;
                            }

                            if (error != null)
                            {
                                tcs.TrySetException(new InvalidOperationException($"Game Center score submission failed: {error.LocalizedDescription}"));
                                return;
                            }

                            tcs.TrySetResult();
                        });
                    }, TaskScheduler.FromCurrentSynchronizationContext());
                }
                catch (Exception ex)
                {
                    tcs.TrySetException(ex);
                }
            });

            return tcs.Task;
        }

        public Task<IReadOnlyList<AppleGameCenterScoreEntry>> GetTopScoresAsync(string leaderboardId, int maxResults, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(leaderboardId))
                throw new ArgumentException("Leaderboard id cannot be empty.", nameof(leaderboardId));

            cancellationToken.ThrowIfCancellationRequested();
            maxResults = Math.Clamp(maxResults, 1, 100);

            var tcs = new TaskCompletionSource<IReadOnlyList<AppleGameCenterScoreEntry>>(TaskCreationOptions.RunContinuationsAsynchronously);

            UIApplication.SharedApplication.BeginInvokeOnMainThread(() =>
            {
                try
                {
                    var localPlayer = GKLocalPlayer.Local;
                    var localPlayerId = ResolvePlayerId(localPlayer);

                    LoadLeaderboardAsync(leaderboardId, cancellationToken).ContinueWith(loadTask =>
                    {
                        if (loadTask.IsCanceled || cancellationToken.IsCancellationRequested)
                        {
                            tcs.TrySetCanceled(cancellationToken);
                            return;
                        }

                        if (loadTask.IsFaulted)
                        {
                            tcs.TrySetException(loadTask.Exception?.GetBaseException() ?? new InvalidOperationException("Game Center leaderboard load failed."));
                            return;
                        }

                        loadTask.Result.LoadEntries(
                            GKLeaderboardPlayerScope.Global,
                            GKLeaderboardTimeScope.AllTime,
                            new NSRange(1, maxResults),
                            (localPlayerEntry, entries, totalPlayerCount, error) =>
                        {
                            if (cancellationToken.IsCancellationRequested)
                            {
                                tcs.TrySetCanceled(cancellationToken);
                                return;
                            }

                            if (error != null)
                            {
                                tcs.TrySetException(new InvalidOperationException($"Game Center score read failed: {error.LocalizedDescription}"));
                                return;
                            }

                            var list = new List<AppleGameCenterScoreEntry>();
                            if (entries != null)
                            {
                                foreach (var entry in entries)
                                {
                                    var scorePlayer = entry.Player;
                                    var scorePlayerId = ResolvePlayerId(scorePlayer);

                                    list.Add(new AppleGameCenterScoreEntry
                                    {
                                        Rank = checked((int)entry.Rank),
                                        PlayerDisplayName = ResolveDisplayName(scorePlayer),
                                        Score = (long)entry.Score,
                                        IsCurrentPlayer = !string.IsNullOrWhiteSpace(localPlayerId) && string.Equals(localPlayerId, scorePlayerId, StringComparison.Ordinal)
                                    });
                                }
                            }

                            tcs.TrySetResult(list);
                        });
                    }, TaskScheduler.FromCurrentSynchronizationContext());
                }
                catch (Exception ex)
                {
                    tcs.TrySetException(ex);
                }
            });

            return tcs.Task;
        }

        public async Task<IReadOnlyDictionary<string, AppleGameCenterAchievementProgress>> GetAchievementProgressAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var achievements = await LoadAchievementsAsync(cancellationToken).ConfigureAwait(false);
            var descriptions = await LoadAchievementDescriptionsAsync(cancellationToken).ConfigureAwait(false);
            var descriptionsById = descriptions.ToDictionary(d => d.Identifier, StringComparer.Ordinal);

            var output = new Dictionary<string, AppleGameCenterAchievementProgress>(StringComparer.Ordinal);

            foreach (var achievement in achievements)
            {
                cancellationToken.ThrowIfCancellationRequested();

                descriptionsById.TryGetValue(achievement.Identifier, out var description);

                byte[] iconBytes = null;
                if (description != null)
                {
                    iconBytes = await LoadAchievementIconAsync(description, cancellationToken).ConfigureAwait(false);
                }

                output[achievement.Identifier] = new AppleGameCenterAchievementProgress
                {
                    Id = achievement.Identifier,
                    IsUnlocked = achievement.Completed,
                    PercentComplete = (float)Math.Clamp(achievement.PercentComplete, 0.0, 100.0),
                    LastUpdatedUtc = ToUtcDateTime(achievement.LastReportedDate),
                    IsRevealed = description == null || !description.Hidden,
                    IconData = iconBytes,
                    IconContentType = "image/png"
                };
            }

            return output;
        }

        public Task UnlockAchievementAsync(string achievementId, CancellationToken cancellationToken = default)
        {
            return ReportProgressAsync(achievementId, 100f, cancellationToken);
        }

        public Task ReportProgressAsync(string achievementId, float percentComplete, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(achievementId))
                throw new ArgumentException("Achievement id cannot be empty.", nameof(achievementId));

            cancellationToken.ThrowIfCancellationRequested();
            percentComplete = Math.Clamp(percentComplete, 0f, 100f);

            var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

            UIApplication.SharedApplication.BeginInvokeOnMainThread(() =>
            {
                try
                {
                    var achievement = new GKAchievement(achievementId)
                    {
                        PercentComplete = percentComplete,
                        ShowsCompletionBanner = true
                    };

                    GKAchievement.ReportAchievements(new[] { achievement }, error =>
                    {
                        if (cancellationToken.IsCancellationRequested)
                        {
                            tcs.TrySetCanceled(cancellationToken);
                            return;
                        }

                        if (error != null)
                        {
                            tcs.TrySetException(new InvalidOperationException($"Game Center achievement report failed: {error.LocalizedDescription}"));
                            return;
                        }

                        tcs.TrySetResult();
                    });
                }
                catch (Exception ex)
                {
                    tcs.TrySetException(ex);
                }
            });

            return tcs.Task;
        }

        private static Task<IReadOnlyList<GKAchievement>> LoadAchievementsAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var tcs = new TaskCompletionSource<IReadOnlyList<GKAchievement>>(TaskCreationOptions.RunContinuationsAsynchronously);

            UIApplication.SharedApplication.BeginInvokeOnMainThread(() =>
            {
                try
                {
                    GKAchievement.LoadAchievements((achievements, error) =>
                    {
                        if (cancellationToken.IsCancellationRequested)
                        {
                            tcs.TrySetCanceled(cancellationToken);
                            return;
                        }

                        if (error != null)
                        {
                            tcs.TrySetException(new InvalidOperationException($"Game Center achievement load failed: {error.LocalizedDescription}"));
                            return;
                        }

                        tcs.TrySetResult(achievements ?? Array.Empty<GKAchievement>());
                    });
                }
                catch (Exception ex)
                {
                    tcs.TrySetException(ex);
                }
            });

            return tcs.Task;
        }

        private static Task<IReadOnlyList<GKAchievementDescription>> LoadAchievementDescriptionsAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var tcs = new TaskCompletionSource<IReadOnlyList<GKAchievementDescription>>(TaskCreationOptions.RunContinuationsAsynchronously);

            UIApplication.SharedApplication.BeginInvokeOnMainThread(() =>
            {
                try
                {
                    GKAchievementDescription.LoadAchievementDescriptions((descriptions, error) =>
                    {
                        if (cancellationToken.IsCancellationRequested)
                        {
                            tcs.TrySetCanceled(cancellationToken);
                            return;
                        }

                        if (error != null)
                        {
                            tcs.TrySetException(new InvalidOperationException($"Game Center achievement descriptions load failed: {error.LocalizedDescription}"));
                            return;
                        }

                        tcs.TrySetResult(descriptions ?? Array.Empty<GKAchievementDescription>());
                    });
                }
                catch (Exception ex)
                {
                    tcs.TrySetException(ex);
                }
            });

            return tcs.Task;
        }

        private static Task<byte[]> LoadAchievementIconAsync(GKAchievementDescription description, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var tcs = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);

            UIApplication.SharedApplication.BeginInvokeOnMainThread(() =>
            {
                try
                {
                    description.LoadImage((image, error) =>
                    {
                        if (cancellationToken.IsCancellationRequested)
                        {
                            tcs.TrySetCanceled(cancellationToken);
                            return;
                        }

                        if (error != null || image == null)
                        {
                            tcs.TrySetResult(null);
                            return;
                        }

                        using var data = image.AsPNG();
                        tcs.TrySetResult(data?.ToArray());
                    });
                }
                catch
                {
                    tcs.TrySetResult(null);
                }
            });

            return tcs.Task;
        }

        private static Task<GKLeaderboard> LoadLeaderboardAsync(string leaderboardId, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var tcs = new TaskCompletionSource<GKLeaderboard>(TaskCreationOptions.RunContinuationsAsynchronously);

            GKLeaderboard.LoadLeaderboards(new[] { leaderboardId }, (leaderboards, error) =>
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    tcs.TrySetCanceled(cancellationToken);
                    return;
                }

                if (error != null)
                {
                    tcs.TrySetException(new InvalidOperationException($"Game Center leaderboard load failed: {error.LocalizedDescription}"));
                    return;
                }

                var leaderboard = leaderboards?.FirstOrDefault(candidate =>
                    string.Equals(candidate.BaseLeaderboardId ?? candidate.GroupIdentifier, leaderboardId, StringComparison.Ordinal)
                    || string.Equals(candidate.Title, leaderboardId, StringComparison.Ordinal))
                    ?? leaderboards?.FirstOrDefault();

                if (leaderboard == null)
                {
                    tcs.TrySetException(new InvalidOperationException($"Game Center leaderboard '{leaderboardId}' is not configured."));
                    return;
                }

                tcs.TrySetResult(leaderboard);
            });

            return tcs.Task;
        }

        private static string ResolvePlayerId(GKPlayer player)
        {
            if (player == null)
            {
                return string.Empty;
            }

            if (!string.IsNullOrWhiteSpace(player.TeamPlayerId))
            {
                return player.TeamPlayerId;
            }

            if (!string.IsNullOrWhiteSpace(player.GamePlayerId))
            {
                return player.GamePlayerId;
            }

            return string.Empty;
        }

        private static DateTime? ToUtcDateTime(NSDate value)
        {
            if (value == null)
            {
                return null;
            }

            var seconds = value.SecondsSince1970;
            return DateTime.UnixEpoch.AddSeconds(seconds);
        }

        private static string ResolveDisplayName(GKPlayer player)
        {
            if (player == null)
            {
                return string.Empty;
            }

            if (!string.IsNullOrWhiteSpace(player.DisplayName))
            {
                return player.DisplayName;
            }

            if (!string.IsNullOrWhiteSpace(player.Alias))
            {
                return player.Alias;
            }

            return ResolvePlayerId(player);
        }

        private static void PresentAuthController(UIViewController controller)
        {
            UIWindow window = null;
            foreach (var scene in UIApplication.SharedApplication.ConnectedScenes)
            {
                if (scene is not UIWindowScene windowScene)
                {
                    continue;
                }

                foreach (var candidateWindow in windowScene.Windows)
                {
                    if (candidateWindow.IsKeyWindow)
                    {
                        window = candidateWindow;
                        break;
                    }

                    window ??= candidateWindow;
                }

                if (window != null)
                {
                    break;
                }
            }

            var root = window?.RootViewController;

            if (root == null)
            {
                return;
            }

            var top = root;
            while (top.PresentedViewController != null)
            {
                top = top.PresentedViewController;
            }

            top.PresentViewController(controller, true, null);
        }
    }
}