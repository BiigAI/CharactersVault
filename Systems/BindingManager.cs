using System;
using System.Collections.Generic;
using CharacterVault.Models;

namespace CharacterVault.Systems
{
    /// <summary>
    /// Manages the permanent platform ID → character name bindings.
    /// Once a player registers (first successful join), they may only join with that character name.
    /// </summary>
    public static class BindingManager
    {
        private static Dictionary<string, CharacterRecord> _bindings = new Dictionary<string, CharacterRecord>();

        /// <summary>Load bindings from disk. Call once at startup.</summary>
        public static void Load()
        {
            _bindings = DataStore.LoadBindings();
            Plugin.Log.LogInfo($"[BindingManager] Loaded {_bindings.Count} character binding(s).");
        }

        /// <summary>Persist current bindings to disk.</summary>
        private static void Save() => DataStore.SaveBindings(_bindings);

        /// <summary>Returns true if this platform ID has a registered binding.</summary>
        public static bool IsRegistered(string playerId) => _bindings.ContainsKey(playerId);

        /// <summary>Returns the registered character name for a platform ID, or null if not registered.</summary>
        public static string? GetRegisteredName(string playerId) =>
            _bindings.TryGetValue(playerId, out CharacterRecord? record) ? record.CharacterName : null;

        /// <summary>
        /// Registers a new platform ID → character name binding.
        /// Only call if <see cref="IsRegistered"/> returns false.
        /// </summary>
        public static void Register(string playerId, string characterName)
        {
            _bindings[playerId] = new CharacterRecord
            {
                PlayerId = playerId,
                CharacterName = characterName,
                RegisteredAt = DateTime.UtcNow,
                LastSeenAt = DateTime.UtcNow
            };
            Save();
            Plugin.Log.LogInfo($"[BindingManager] Registered {playerId} → '{characterName}'");
        }

        /// <summary>
        /// Updates the LastSeenAt timestamp for an existing binding.
        /// Call on successful join.
        /// </summary>
        public static void RecordJoin(string playerId)
        {
            if (_bindings.TryGetValue(playerId, out CharacterRecord? record))
            {
                record.LastSeenAt = DateTime.UtcNow;
                Save();
            }
        }

        private static readonly HashSet<string> _importAllowedPlayers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Grants a one-time import authorization for a player on their next join.</summary>
        public static void AllowImport(string playerId)
        {
            if (!string.IsNullOrEmpty(playerId))
            {
                _importAllowedPlayers.Add(playerId);
                Plugin.Log.LogInfo($"[BindingManager] Granted one-time character import pass to {playerId}");
            }
        }

        /// <summary>Checks and consumes a one-time import authorization for a player.</summary>
        public static bool ConsumeImportAllowed(string playerId)
        {
            return !string.IsNullOrEmpty(playerId) && _importAllowedPlayers.Remove(playerId);
        }

        /// <summary>Checks if a player has an active one-time import authorization.</summary>
        public static bool IsImportAllowed(string playerId)
        {
            return !string.IsNullOrEmpty(playerId) && _importAllowedPlayers.Contains(playerId);
        }

        /// <summary>
        /// Removes a character binding from bindings.json, but retains their server snapshot.
        /// This allows the player to resume their character or re-bind cleanly.
        /// </summary>
        public static bool UnbindPlayer(string playerId)
        {
            if (!_bindings.Remove(playerId)) return false;
            Save();
            Plugin.Log.LogInfo($"[BindingManager] Unbound player {playerId} (retained server snapshot).");
            return true;
        }

        /// <summary>
        /// Removes a binding entirely and clears stored snapshot data, allowing the player to re-register with a fresh character.
        /// Intended for admin use only (e.g. player wants to reset, or made a mistake at first join).
        /// </summary>
        public static bool RemoveBinding(string playerId)
        {
            DataStore.DeleteSnapshot(playerId);
            if (!_bindings.Remove(playerId)) return false;
            Save();
            Plugin.Log.LogInfo($"[BindingManager] Removed binding and snapshot for {playerId}");
            return true;
        }

        /// <summary>
        /// Removes a binding and deletes the player's snapshot from disk, keeping memory and disk fully synchronized.
        /// </summary>
        public static bool WipePlayer(string playerId)
        {
            bool snapshotDeleted = DataStore.DeleteSnapshot(playerId);
            bool bindingRemoved = _bindings.Remove(playerId);
            if (bindingRemoved)
            {
                Save();
            }
            return bindingRemoved || snapshotDeleted;
        }

        /// <summary>Returns a copy of all current bindings for display purposes.</summary>
        public static IReadOnlyDictionary<string, CharacterRecord> GetAll() =>
            new Dictionary<string, CharacterRecord>(_bindings);
    }
}
