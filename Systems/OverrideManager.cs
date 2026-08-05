using System.Collections.Generic;

namespace CharacterVault.Systems
{
    /// <summary>
    /// Manages in-memory one-time admin overrides that allow a player to bypass the snapshot
    /// mismatch check on their next join. Overrides are NOT persisted across server restarts
    /// (intentional — admin must actively re-grant after a restart).
    ///
    /// Persistent overrides are loaded at the moment a player joins from the overrides.json file
    /// so that admins can add them while the server is running without a restart.
    ///
    /// Override lifecycle:
    ///   1. Admin adds Steam ID to overrides.json
    ///   2. Player connects → ConsumeOverride() returns true → override is removed from file
    ///   3. Player is allowed in with current data saved as the new baseline snapshot
    /// </summary>
    public static class OverrideManager
    {
        // In-memory cache. Populated from disk each time a player joins (intentionally fresh read).
        private static readonly HashSet<string> _memoryOverrides = new HashSet<string>();

        /// <summary>
        /// Check if a Steam ID has an active override.
        /// Always re-reads from disk so that admins can add overrides while the server is running.
        /// </summary>
        public static bool HasOverride(string playerId)
        {
            // Always refresh from disk first
            var diskOverrides = DataStore.LoadOverrides();
            return _memoryOverrides.Contains(playerId) || diskOverrides.Contains(playerId);
        }

        /// <summary>
        /// Check and atomically consume an override (if present).
        /// Returns true if the override existed (and has now been removed), false otherwise.
        /// </summary>
        public static bool ConsumeOverride(string playerId)
        {
            // Check disk first (admin may have added it while server was running)
            var diskOverrides = DataStore.LoadOverrides();
            bool hadDiskOverride = diskOverrides.Remove(playerId);
            bool hadMemoryOverride = _memoryOverrides.Remove(playerId);

            if (hadDiskOverride)
            {
                // Persist the removal
                DataStore.SaveOverrides(diskOverrides);
            }

            if (hadDiskOverride || hadMemoryOverride)
            {
                Plugin.Log.LogWarning($"[OverrideManager] Consumed override for {playerId}. Player allowed through.");
                return true;
            }

            return false;
        }

        /// <summary>
        /// Grant an in-memory override for a Steam ID. Used by admin chat commands.
        /// For persistence across server restarts, the admin should also edit overrides.json.
        /// </summary>
        public static void GrantMemoryOverride(string playerId)
        {
            _memoryOverrides.Add(playerId);
            Plugin.Log.LogWarning($"[OverrideManager] Granted in-memory override for {playerId}.");
        }

        /// <summary>
        /// Revoke an in-memory override without consuming it.
        /// </summary>
        public static bool RevokeOverride(string playerId) => _memoryOverrides.Remove(playerId);

        /// <summary>Returns all Steam IDs currently with in-memory overrides.</summary>
        public static IReadOnlyCollection<string> GetMemoryOverrides() => _memoryOverrides;
    }
}
