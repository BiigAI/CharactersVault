using System;
using System.Linq;
using ServerCharacters.Models;

namespace ServerCharacters.Systems
{
    /// <summary>
    /// Captures and compares player state snapshots using raw ZDO byte data.
    ///
    /// Why raw bytes?
    /// Valheim's inventory and skill ZPackage formats can change between game updates.
    /// Storing and comparing raw bytes is version-agnostic: any change to the serialized
    /// data will be detected, regardless of the specific fields added or removed.
    ///
    /// The trade-off is that false positives from internal Valheim format changes are possible
    /// after a game update — the admin override system exists to handle those cases.
    /// </summary>
    public static class SnapshotManager
    {
        /// <summary>
        /// Create a snapshot containing the player's profile byte array.
        /// </summary>
        public static PlayerSnapshot CreateSnapshot(string playerId, string characterName, byte[] profileBytes)
        {
            return new PlayerSnapshot
            {
                PlayerId = playerId,
                CharacterName = characterName,
                SnapshotTime = DateTime.UtcNow,
                ProfileDataBase64 = profileBytes != null && profileBytes.Length > 0
                    ? Convert.ToBase64String(profileBytes)
                    : string.Empty
            };
        }

        /// <summary>
        /// Load the stored snapshot for a given Steam ID. Returns null if none exists.
        /// </summary>
        public static PlayerSnapshot? GetSnapshot(string playerId) =>
            DataStore.LoadSnapshot(playerId);

        /// <summary>
        /// Persist a snapshot to disk.
        /// </summary>
        public static void SaveSnapshot(PlayerSnapshot snapshot) =>
            DataStore.SaveSnapshot(snapshot);
    }
}
