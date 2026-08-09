using System;
using System.Linq;
using CharacterVault.Models;

namespace CharacterVault.Systems
{
    /// <summary>
    /// Captures and compares player state snapshots using raw profile byte data.
    /// </summary>
    public static class SnapshotManager
    {
        public static PlayerSnapshot CreateSnapshot(string playerId, string characterName, byte[] profileBytes, bool isPlayerData)
        {
            Plugin.Log.LogInfo($"[CharacterVault :: Snapshot] Creating snapshot for platform ID {playerId} ('{characterName}'), raw bytes length: {profileBytes?.Length ?? 0}");
            return new PlayerSnapshot
            {
                PlayerId = playerId,
                CharacterName = characterName,
                SnapshotTime = DateTime.UtcNow,
                IsPlayerData = isPlayerData,
                ProfileDataBase64 = profileBytes != null && profileBytes.Length > 0
                    ? Convert.ToBase64String(profileBytes)
                    : string.Empty
            };
        }

        public static PlayerSnapshot? GetSnapshot(string playerId)
        {
            var snapshot = DataStore.LoadSnapshot(playerId);
            if (snapshot != null && snapshot.HasData)
            {
                Plugin.Log.LogInfo($"[CharacterVault :: Snapshot] Found existing snapshot for platform ID {playerId} ('{snapshot.CharacterName}'), created: {snapshot.SnapshotTime}");
            }
            else
            {
                Plugin.Log.LogInfo($"[CharacterVault :: Snapshot] No existing snapshot found for platform ID {playerId}.");
            }
            return snapshot;
        }

        public static void SaveSnapshot(PlayerSnapshot snapshot)
        {
            Plugin.Log.LogInfo($"[CharacterVault :: Snapshot] Saving snapshot for platform ID {snapshot.PlayerId} ('{snapshot.CharacterName}')...");
            DataStore.SaveSnapshot(snapshot);
        }
    }
}
