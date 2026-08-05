using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using CharacterVault.Models;

namespace CharacterVault.Systems
{
    /// <summary>
    /// Low-level file I/O layer for all persistent CharacterVault data.
    /// All paths are rooted under BepInEx/config/CharacterVault/ on the server.
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
            string bepInExConfigDir = BepInEx.Paths.ConfigPath;
            _rootDir = Path.Combine(bepInExConfigDir, "CharacterVault");
            _snapshotsDir = Path.Combine(_rootDir, "snapshots");

            Directory.CreateDirectory(_rootDir);
            Directory.CreateDirectory(_snapshotsDir);

            Plugin.Log.LogInfo($"[CharacterVault :: DataStore] Root Data Directory: {_rootDir}");
            Plugin.Log.LogInfo($"[CharacterVault :: DataStore] Snapshots Directory: {_snapshotsDir}");
        }

        // ── Bindings ─────────────────────────────────────────────────────────────

        public static Dictionary<string, CharacterRecord> LoadBindings()
        {
            try
            {
                if (!File.Exists(BindingsFilePath))
                    return new Dictionary<string, CharacterRecord>();

                string json = File.ReadAllText(BindingsFilePath);
                var bindings = JsonConvert.DeserializeObject<Dictionary<string, CharacterRecord>>(json, JsonSettings)
                    ?? new Dictionary<string, CharacterRecord>();
                
                return bindings;
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"[CharacterVault :: DataStore] Failed to load bindings from '{BindingsFilePath}': {ex.Message}");
                return new Dictionary<string, CharacterRecord>();
            }
        }

        public static void SaveBindings(Dictionary<string, CharacterRecord> bindings)
        {
            try
            {
                string json = JsonConvert.SerializeObject(bindings, JsonSettings);
                File.WriteAllText(BindingsFilePath, json);
                Plugin.Log.LogInfo($"[CharacterVault :: DataStore] Saved {bindings.Count} binding(s) to '{BindingsFilePath}'");
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"[CharacterVault :: DataStore] Failed to save bindings: {ex.Message}");
            }
        }

        // ── Snapshots ─────────────────────────────────────────────────────────────

        public static PlayerSnapshot? LoadSnapshot(string playerId)
        {
            string path = SnapshotPath(playerId);
            try
            {
                if (!File.Exists(path))
                {
                    Plugin.Log.LogInfo($"[CharacterVault :: DataStore] No snapshot file found at '{path}'");
                    return null;
                }
                string json = File.ReadAllText(path);
                var snapshot = JsonConvert.DeserializeObject<PlayerSnapshot>(json, JsonSettings);
                Plugin.Log.LogInfo($"[CharacterVault :: DataStore] Loaded snapshot file for SteamID {playerId} from '{path}'");
                return snapshot;
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"[CharacterVault :: DataStore] Failed to load snapshot for SteamID {playerId}: {ex.Message}");
                return null;
            }
        }

        public static void SaveSnapshot(PlayerSnapshot snapshot)
        {
            try
            {
                string path = SnapshotPath(snapshot.PlayerId);
                string json = JsonConvert.SerializeObject(snapshot, JsonSettings);
                File.WriteAllText(path, json);
                Plugin.Log.LogInfo($"[CharacterVault :: DataStore] SUCCESSFULLY SAVED SNAPSHOT for SteamID {snapshot.PlayerId} ('{snapshot.CharacterName}') -> '{path}'");
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"[CharacterVault :: DataStore] Failed to save snapshot for SteamID {snapshot.PlayerId}: {ex.Message}");
            }
        }

        // ── Admin Overrides ───────────────────────────────────────────────────────

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
                Plugin.Log.LogError($"[CharacterVault :: DataStore] Failed to load overrides: {ex.Message}");
                return new HashSet<string>();
            }
        }

        // ── Wipe ──────────────────────────────────────────────────────────────────

        public static bool WipePlayerData(string playerId)
        {
            bool wiped = false;

            string snapshotPath = SnapshotPath(playerId);
            if (File.Exists(snapshotPath))
            {
                try
                {
                    File.Delete(snapshotPath);
                    Plugin.Log.LogInfo($"[CharacterVault :: DataStore] WIPE: Deleted snapshot file for SteamID {playerId}.");
                    wiped = true;
                }
                catch (Exception ex)
                {
                    Plugin.Log.LogError($"[CharacterVault :: DataStore] WIPE FAILED: Could not delete snapshot for {playerId}: {ex.Message}");
                }
            }

            var bindings = LoadBindings();
            if (bindings.ContainsKey(playerId))
            {
                bindings.Remove(playerId);
                SaveBindings(bindings);
                Plugin.Log.LogInfo($"[CharacterVault :: DataStore] WIPE: Removed character binding for SteamID {playerId}.");
                wiped = true;
            }

            return wiped;
        }

        public static void SaveOverrides(HashSet<string> overrides)
        {
            try
            {
                var dict = new Dictionary<string, bool>();
                foreach (string id in overrides)
                    dict[id] = true;

                string json = JsonConvert.SerializeObject(dict, JsonSettings);
                File.WriteAllText(OverridesFilePath, json);
                Plugin.Log.LogInfo($"[CharacterVault :: DataStore] Saved {overrides.Count} override(s) to '{OverridesFilePath}'");
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"[CharacterVault :: DataStore] Failed to save overrides: {ex.Message}");
            }
        }
    }
}
