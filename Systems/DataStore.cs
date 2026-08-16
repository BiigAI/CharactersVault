using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using CharacterVault.Helpers;
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
        private static string SnapshotPath(string playerId) =>
            Path.Combine(_snapshotsDir, $"{playerId}.json");

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
                var bindings = SimpleJson.DeserializeObject<Dictionary<string, CharacterRecord>>(json)
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
                string json = SimpleJson.SerializeObject(bindings, prettyPrint: true);
                WriteAllTextAtomically(BindingsFilePath, json);
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
                var snapshot = SimpleJson.DeserializeObject<PlayerSnapshot>(json);
                Plugin.Log.LogInfo($"[CharacterVault :: DataStore] Loaded snapshot file for platform ID {playerId} from '{path}'");
                return snapshot;
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"[CharacterVault :: DataStore] Failed to load snapshot for platform ID {playerId}: {ex.Message}");
                return null;
            }
        }

        public static void SaveSnapshot(PlayerSnapshot snapshot)
        {
            try
            {
                string path = SnapshotPath(snapshot.PlayerId);
                string json = SimpleJson.SerializeObject(snapshot, prettyPrint: true);
                WriteAllTextAtomically(path, json);
                Plugin.Log.LogInfo($"[CharacterVault :: DataStore] Saved snapshot for platform ID {snapshot.PlayerId} ('{snapshot.CharacterName}') -> '{path}'");
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"[CharacterVault :: DataStore] Failed to save snapshot for platform ID {snapshot.PlayerId}: {ex.Message}");
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
                    Plugin.Log.LogInfo($"[CharacterVault :: DataStore] Wipe: deleted snapshot file for platform ID {playerId}.");
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
                Plugin.Log.LogInfo($"[CharacterVault :: DataStore] Wipe: removed character binding for platform ID {playerId}.");
                wiped = true;
            }

            return wiped;
        }

        private static void WriteAllTextAtomically(string path, string content)
        {
            string temporaryPath = $"{path}.{Guid.NewGuid():N}.tmp";
            try
            {
                File.WriteAllText(temporaryPath, content);
                if (File.Exists(path))
                    File.Replace(temporaryPath, path, null);
                else
                    File.Move(temporaryPath, path);
            }
            finally
            {
                if (File.Exists(temporaryPath))
                    File.Delete(temporaryPath);
            }
        }
    }
}
