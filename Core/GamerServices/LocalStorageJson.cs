using System.Text.Json.Serialization;

namespace Microsoft.Xna.Framework.GamerServices
{
    // Generated serialisation for the local achievement and leaderboard files: reflection-based System.Text.Json
    // is not safe once an app is trimmed (iOS), and the trimmer warns about it.
    [JsonSerializable(typeof(PersistentLocalAchievementProvider.StorageModel), TypeInfoPropertyName = "AchievementStorage")]
    [JsonSerializable(typeof(PersistentLocalLeaderboardProvider.StorageModel), TypeInfoPropertyName = "LeaderboardStorage")]
    internal sealed partial class LocalStorageJson : JsonSerializerContext
    {
    }
}
