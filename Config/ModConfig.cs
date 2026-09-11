using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Configuration;

namespace CharacterVault
{
    /// <summary>
    /// All configurable settings for the mod, exposed through BepInEx's .cfg system.
    /// Server admins can edit BepInEx/config/CharacterVault.cfg on the server.
    /// Clients can also edit their local copy to adjust sync timeout behaviour.
    /// </summary>
    public static class ModConfig
    {
        // ── Enforcement ──────────────────────────────────────────────────────────
        public static ConfigEntry<bool> EnforceCharacterBinding { get; private set; } = null!;
        public static ConfigEntry<bool> AllowCharacterImportOnFirstJoin { get; private set; } = null!;

        // ── Client Sync ───────────────────────────────────────────────────────────
        /// <summary>How often the client performs a full profile sync as a safety net.</summary>
        public static ConfigEntry<float> AutoSaveIntervalMinutes { get; private set; } = null!;

        /// <summary>Safeguard to block joining with an existing character that has progression to protect local saves.</summary>
        public static ConfigEntry<bool> ProtectExistingCharacters { get; private set; } = null!;

        /// <summary>Automatically create a timestamped backup of the local .fch file before any reset.</summary>
        public static ConfigEntry<bool> AutoBackupBeforeReset { get; private set; } = null!;

        // ── Messages ─────────────────────────────────────────────────────────────
        public static ConfigEntry<string> KickMessageWrongCharacter { get; private set; } = null!;

        /// <summary>
        /// How long (seconds) the client will wait for the server to send profile data before
        /// giving up and disconnecting. Increase on high-latency connections.
        /// </summary>
        public static ConfigEntry<float> ProfileSyncTimeoutSeconds { get; private set; } = null!;

        // ── UI ───────────────────────────────────────────────────────────────────
        /// <summary>Show an informational banner on the character select screen.</summary>
        public static ConfigEntry<bool> ShowCharacterSelectWarning { get; private set; } = null!;

        // ── Logging ──────────────────────────────────────────────────────────────
        public static ConfigEntry<bool> VerboseLogging { get; private set; } = null!;

        public static void Initialize(ConfigFile cfg)
        {
            EnforceCharacterBinding = cfg.Bind(
                "Enforcement",
                "EnforceCharacterBinding",
                true,
                "If true, each platform ID may only join with the character name it first registered with.");

            AllowCharacterImportOnFirstJoin = cfg.Bind(
                "Enforcement",
                "AllowCharacterImportOnFirstJoin",
                false,
                "If true, players joining without a server snapshot can import their existing character gear and skills instead of starting fresh.");

            AutoSaveIntervalMinutes = cfg.Bind(
                "ClientSync",
                "AutoSaveIntervalMinutes",
                5.0f,
                "How often (in minutes) the client performs a full profile sync. Default: 5.0");

            ProtectExistingCharacters = cfg.Bind(
                "ClientSync",
                "ProtectExistingCharacters",
                true,
                "If true, safely disconnects players who join with an existing character with progression to protect their local save from accidental wipes.");

            AutoBackupBeforeReset = cfg.Bind(
                "ClientSync",
                "AutoBackupBeforeReset",
                true,
                "If true, automatically creates a timestamped local backup of the character save file in CharactersVault_Backups before any reset.");

            KickMessageWrongCharacter = cfg.Bind(
                "Messages",
                "KickMessageWrongCharacter",
                "Wrong Character",
                "Message sent to players kicked for using the wrong character.");

            ProfileSyncTimeoutSeconds = cfg.Bind(
                "ClientSync",
                "ProfileSyncTimeoutSeconds",
                15.0f,
                "How long (seconds) the client waits for the server to send its profile data on join. " +
                "If the server does not respond in time, the client disconnects. Default: 15.0");

            ShowCharacterSelectWarning = cfg.Bind(
                "UI",
                "ShowCharacterSelectWarning",
                true,
                "Show an informational banner on the character selection screen reminding players about server-side character rules.");

            VerboseLogging = cfg.Bind(
                "Debug",
                "VerboseLogging",
                false,
                "Enable extra debug logging to the BepInEx console/log file.");

            // Programmatically clean up any stale / orphaned entries (e.g. decommissioned WebPortal settings)
            ClearOrphanedEntries(cfg);
        }

        /// <summary>
        /// Programmatically removes any stale / orphaned configuration options (such as old WebPortal
        /// or decommissioned settings) from the config file and rewrites it cleanly.
        /// </summary>
        private static void ClearOrphanedEntries(ConfigFile cfg)
        {
            try
            {
                PropertyInfo? prop = typeof(ConfigFile).GetProperty(
                    "OrphanedEntries",
                    BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance);

                Dictionary<ConfigDefinition, string>? orphaned = null;

                if (prop != null)
                {
                    orphaned = prop.GetValue(cfg) as Dictionary<ConfigDefinition, string>;
                }

                if (orphaned == null)
                {
                    FieldInfo? field = typeof(ConfigFile).GetField(
                        "<OrphanedEntries>k__BackingField",
                        BindingFlags.NonPublic | BindingFlags.Instance)
                        ?? typeof(ConfigFile).GetField("_orphanedEntries", BindingFlags.NonPublic | BindingFlags.Instance);

                    orphaned = field?.GetValue(cfg) as Dictionary<ConfigDefinition, string>;
                }

                if (orphaned != null && orphaned.Count > 0)
                {
                    Plugin.Log.LogInfo($"[{Plugin.ModName}] Removing {orphaned.Count} stale/orphaned config entry/entries from {cfg.ConfigFilePath}...");
                    orphaned.Clear();
                    cfg.Save();
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"[{Plugin.ModName}] Could not clean orphaned config entries: {ex.Message}");
            }
        }
    }
}
