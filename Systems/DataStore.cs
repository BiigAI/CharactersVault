using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using ServerCharacters.Models;

namespace ServerCharacters.Systems
{
    /// <summary>
    /// Low-level file I/O layer for all persistent ServerCharacters data.
    /// All paths are rooted under BepInEx/config/ServerCharacters/ on the server.
    /// </summary>
    public static class DataStore
    {
        private static string _rootDir = string.Empty;
        private static string _snapshotsDir = string.Empty;

        // File paths
        public static string BindingsFilePath => Path.Combine(_rootDir, "bindings.json");
        public static string OverridesFilePath => Path.Combine(_rootDir, "overrides.json");
        private static string SnapshotPath(string playerId) =>
            Path.Combine(_snapshotsDir, $"{playerId}.json");

        private static readonly JsonSerializerSettings JsonSettings = new JsonSerializerSettings
        {
            Formatting = Formatting.Indented,
            NullValueHandling = NullValueHandling.Ignore
        };

        /// <summary>
        /// Initialize directory structure. Called once at plugin startup.
        /// </summary>
        public static void Initialize()
        {
            // Root config dir is adjacent to BepInEx.dll
            string bepInExConfigDir = BepInEx.Paths.ConfigPath;
            _rootDir = Path.Combine(bepInExConfigDir, "ServerCharacters");
            _snapshotsDir = Path.Combine(_rootDir, "snapshots");

            Directory.CreateDirectory(_rootDir);
            Directory.CreateDirectory(_snapshotsDir);

            Plugin.Log.LogInfo($"[DataStore] Data directory: {_rootDir}");
        }

        // ── Bindings ─────────────────────────────────────────────────────────────

        public static Dictionary<string, CharacterRecord> LoadBindings()
        {
            try
            {
                if (!File.Exists(BindingsFilePath))
                    return new Dictionary<string, CharacterRecord>();

                string json = File.ReadAllText(BindingsFilePath);
                return JsonConvert.DeserializeObject<Dictionary<string, CharacterRecord>>(json, JsonSettings)
                    ?? new Dictionary<string, CharacterRecord>();
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"[DataStore] Failed to load bindings: {ex.Message}");
                return new Dictionary<string, CharacterRecord>();
            }
        }

        public static void SaveBindings(Dictionary<string, CharacterRecord> bindings)
        {
            try
            {
                string json = JsonConvert.SerializeObject(bindings, JsonSettings);
                File.WriteAllText(BindingsFilePath, json);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"[DataStore] Failed to save bindings: {ex.Message}");
            }
        }

        // ── Snapshots ─────────────────────────────────────────────────────────────

        public static PlayerSnapshot? LoadSnapshot(string playerId)
        {
            string path = SnapshotPath(playerId);
            try
            {
                if (!File.Exists(path)) return null;
                string json = File.ReadAllText(path);
                return JsonConvert.DeserializeObject<PlayerSnapshot>(json, JsonSettings);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"[DataStore] Failed to load snapshot for {playerId}: {ex.Message}");
                return null;
            }
        }

        public static void SaveSnapshot(PlayerSnapshot snapshot)
        {
            try
            {
                string json = JsonConvert.SerializeObject(snapshot, JsonSettings);
                File.WriteAllText(SnapshotPath(snapshot.PlayerId), json);

                if (ModConfig.VerboseLogging.Value)
                    Plugin.Log.LogInfo($"[DataStore] Saved snapshot for {snapshot.PlayerId} ('{snapshot.CharacterName}')");
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"[DataStore] Failed to save snapshot for {snapshot.PlayerId}: {ex.Message}");
            }
        }

        // ── Admin Overrides ───────────────────────────────────────────────────────

        /// <summary>
        /// Loads the set of Steam IDs that have an active one-time admin override.
        /// Admins edit overrides.json directly on the server to add entries.
        /// Format: { "76561198XXXXXXXXX": true, ... }
        /// </summary>
        public static HashSet<string> LoadOverrides()
        {
            try
            {
                if (!File.Exists(OverridesFilePath))
                    return new HashSet<string>();

                string json = File.ReadAllText(OverridesFilePath);
                var dict = JsonConvert.DeserializeObject<Dictionary<string, bool>>(json, JsonSettings)
                           ?? new Dictionary<string, bool>();

                var result = new HashSet<string>();
                foreach (var kvp in dict)
                {
                    if (kvp.Value) result.Add(kvp.Key);
                }
                return result;
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"[DataStore] Failed to load overrides: {ex.Message}");
                return new HashSet<string>();
            }
        }

        // ── Wipe ──────────────────────────────────────────────────────────────────

        /// <summary>
        /// Completely removes all server-side data for a player:
        ///   - Deletes their snapshot file
        ///   - Removes their character binding
        /// On their next join they will receive a blank character and re-register.
        /// </summary>
        public static bool WipePlayerData(string playerId)
        {
            bool wiped = false;

            // Delete snapshot file
            string snapshotPath = SnapshotPath(playerId);
            if (File.Exists(snapshotPath))
            {
                try
                {
                    File.Delete(snapshotPath);
                    Plugin.Log.LogInfo($"[DataStore] Deleted snapshot for {playerId}.");
                    wiped = true;
                }
                catch (Exception ex)
                {
                    Plugin.Log.LogError($"[DataStore] Failed to delete snapshot for {playerId}: {ex.Message}");
                }
            }

            // Remove binding
            var bindings = LoadBindings();
            if (bindings.ContainsKey(playerId))
            {
                bindings.Remove(playerId);
                SaveBindings(bindings);
                Plugin.Log.LogInfo($"[DataStore] Removed binding for {playerId}.");
                wiped = true;
            }

            return wiped;
        }

        // ── Admin Overrides (save) ────────────────────────────────────────────────

        /// <summary>
        /// Saves the current override set back to disk (used when consuming an override).
        /// </summary>
        public static void SaveOverrides(HashSet<string> overrides)
        {
            try
            {
                var dict = new Dictionary<string, bool>();
                foreach (string id in overrides)
                    dict[id] = true;

                string json = JsonConvert.SerializeObject(dict, JsonSettings);
                File.WriteAllText(OverridesFilePath, json);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"[DataStore] Failed to save overrides: {ex.Message}");
            }
        }
    }
}
