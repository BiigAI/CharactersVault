using System;
using System.Collections.Generic;
using CharacterVault.Models;

namespace CharacterVault.Systems
{
    /// <summary>
    /// Manages the permanent Steam ID → character name bindings.
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

        /// <summary>Returns true if this Steam ID has a registered binding.</summary>
        public static bool IsRegistered(string playerId) => _bindings.ContainsKey(playerId);

        /// <summary>Returns the registered character name for a Steam ID, or null if not registered.</summary>
        public static string? GetRegisteredName(string playerId) =>
            _bindings.TryGetValue(playerId, out CharacterRecord? record) ? record.CharacterName : null;

        /// <summary>
        /// Registers a new Steam ID → character name binding.
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

        /// <summary>
        /// Removes a binding entirely, allowing the player to re-register with a new character.
        /// Intended for admin use only (e.g. player wants to reset, or made a mistake at first join).
        /// </summary>
        public static bool RemoveBinding(string playerId)
        {
            if (!_bindings.Remove(playerId)) return false;
            Save();
            Plugin.Log.LogInfo($"[BindingManager] Removed binding for {playerId}");
            return true;
        }

        /// <summary>Returns a copy of all current bindings for display purposes.</summary>
        public static IReadOnlyDictionary<string, CharacterRecord> GetAll() => _bindings;
    }
}
