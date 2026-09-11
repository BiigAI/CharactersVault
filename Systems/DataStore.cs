using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Threading;
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
        private static string SnapshotPath(string playerId)
        {
            if (!ZNetHelper.IsValidPlayerId(playerId))
            {
                Plugin.Log.LogWarning($"[CharacterVault :: DataStore] Invalid platform ID rejected: '{playerId}'");
                return string.Empty;
            }
            return Path.Combine(_snapshotsDir, $"{playerId}.json");
        }

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
            if (string.IsNullOrEmpty(path)) return null;

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
            if (snapshot == null || !ZNetHelper.IsValidPlayerId(snapshot.PlayerId))
            {
                Plugin.Log.LogWarning($"[CharacterVault :: DataStore] Cannot save snapshot with invalid or null platform ID.");
                return;
            }

            string path = SnapshotPath(snapshot.PlayerId);
            if (string.IsNullOrEmpty(path)) return;

            // Offload JSON serialization and disk write to background thread to avoid server main thread stutters
            ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    string json = SimpleJson.SerializeObject(snapshot, prettyPrint: false);
                    WriteAllTextAtomically(path, json);
                    Plugin.Log.LogInfo($"[CharacterVault :: DataStore] Saved snapshot for platform ID {snapshot.PlayerId} ('{snapshot.CharacterName}') -> '{path}'");
                }
                catch (Exception ex)
                {
                    Plugin.Log.LogError($"[CharacterVault :: DataStore] Failed to save snapshot for platform ID {snapshot.PlayerId}: {ex.Message}");
                }
            });
        }

        public static bool DeleteSnapshot(string playerId)
        {
            string path = SnapshotPath(playerId);
            if (string.IsNullOrEmpty(path)) return false;

            if (File.Exists(path))
            {
                try
                {
                    File.Delete(path);
                    Plugin.Log.LogInfo($"[CharacterVault :: DataStore] Deleted snapshot file for platform ID {playerId}.");
                    return true;
                }
                catch (Exception ex)
                {
                    Plugin.Log.LogError($"[CharacterVault :: DataStore] Failed to delete snapshot for {playerId}: {ex.Message}");
                    return false;
                }
            }

            return false;
        }

        // ── Wipe ──────────────────────────────────────────────────────────────────

        public static bool WipePlayerData(string playerId)
        {
            bool snapshotDeleted = DeleteSnapshot(playerId);

            var bindings = LoadBindings();
            bool bindingRemoved = false;
            if (bindings.ContainsKey(playerId))
            {
                bindings.Remove(playerId);
                SaveBindings(bindings);
                Plugin.Log.LogInfo($"[CharacterVault :: DataStore] Wipe: removed character binding for platform ID {playerId}.");
                bindingRemoved = true;
            }

            return snapshotDeleted || bindingRemoved;
        }

        /// <summary>
        /// Creates a timestamped local backup of the character's .fch file inside
        /// a 'CharactersVault_Backups' folder in the character directory before any reset.
        /// </summary>
        public static void BackupLocalProfile(PlayerProfile profile)
        {
            if (profile == null) return;
            try
            {
                if (ModConfig.AutoBackupBeforeReset != null && !ModConfig.AutoBackupBeforeReset.Value) return;

                string path = profile.GetPath();
                if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;

                string directory = Path.GetDirectoryName(path) ?? "";
                string backupDir = Path.Combine(directory, "CharactersVault_Backups");
                if (!Directory.Exists(backupDir))
                    Directory.CreateDirectory(backupDir);

                string fileName = Path.GetFileNameWithoutExtension(path);
                string extension = Path.GetExtension(path);
                string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                string backupPath = Path.Combine(backupDir, $"{fileName}_backup_{timestamp}{extension}");

                File.Copy(path, backupPath, overwrite: true);
                Plugin.Log.LogInfo($"[CharacterVault :: DataStore] Created local character backup: '{backupPath}'");
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"[CharacterVault :: DataStore] Could not create local character backup: {ex.Message}");
            }
        }

        private static void WriteAllTextAtomically(string path, string content)
        {
            string temporaryPath = $"{path}.{Guid.NewGuid():N}.tmp";
            try
            {
                File.WriteAllText(temporaryPath, content);
                File.Copy(temporaryPath, path, overwrite: true);
            }
            finally
            {
                if (File.Exists(temporaryPath))
                {
                    try { File.Delete(temporaryPath); } catch { }
                }
            }
        }
    }
}
