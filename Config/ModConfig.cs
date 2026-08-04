using BepInEx.Configuration;

namespace ServerCharacters
{
    /// <summary>
    /// All configurable settings for the mod, exposed through BepInEx's .cfg system.
    /// Server admins can edit BepInEx/config/ServerCharacters.cfg on the server.
    /// Clients can also edit their local copy to adjust sync timeout behaviour.
    /// </summary>
    public static class ModConfig
    {
        // ── Enforcement ──────────────────────────────────────────────────────────
        public static ConfigEntry<bool> EnforceCharacterBinding { get; private set; } = null!;
        public static ConfigEntry<bool> EnforceInventorySnapshot { get; private set; } = null!;
        public static ConfigEntry<bool> EnforceSkillSnapshot { get; private set; } = null!;

        // ── Snapshot ─────────────────────────────────────────────────────────────
        /// <summary>How long (seconds) to wait after connect before reading the player's ZDO.
        /// Increase if players have large inventories and the ZDO sync takes longer.</summary>
        public static ConfigEntry<float> ZdoSyncWaitSeconds { get; private set; } = null!;

        /// <summary>Maximum time (seconds) to poll before giving up on finding the player ZDO.</summary>
        public static ConfigEntry<float> ZdoSyncMaxWaitSeconds { get; private set; } = null!;

        /// <summary>How often (minutes) to auto-save all connected players' snapshots as a safety net.</summary>
        public static ConfigEntry<float> AutoSaveIntervalMinutes { get; private set; } = null!;

        // ── Messages ─────────────────────────────────────────────────────────────
        public static ConfigEntry<string> KickMessageWrongCharacter { get; private set; } = null!;
        public static ConfigEntry<string> KickMessageMismatch { get; private set; } = null!;

        // ── Client Sync ───────────────────────────────────────────────────────────
        /// <summary>
        /// How long (seconds) the client will wait for the server to send profile data before
        /// giving up and disconnecting. Increase on high-latency connections.
        /// </summary>
        public static ConfigEntry<float> ProfileSyncTimeoutSeconds { get; private set; } = null!;

        // ── Logging ──────────────────────────────────────────────────────────────
        public static ConfigEntry<bool> VerboseLogging { get; private set; } = null!;

        public static void Initialize(ConfigFile cfg)
        {
            EnforceCharacterBinding = cfg.Bind(
                "Enforcement",
                "EnforceCharacterBinding",
                true,
                "If true, each Steam ID may only join with the character name it first registered with.");

            EnforceInventorySnapshot = cfg.Bind(
                "Enforcement",
                "EnforceInventorySnapshot",
                true,
                "If true, kick players whose inventory differs from the server's last known snapshot.");

            EnforceSkillSnapshot = cfg.Bind(
                "Enforcement",
                "EnforceSkillSnapshot",
                true,
                "If true, kick players whose skills differ from the server's last known snapshot.");

            ZdoSyncWaitSeconds = cfg.Bind(
                "Snapshot",
                "ZdoSyncWaitSeconds",
                1.0f,
                "Seconds to wait between ZDO polling attempts after a player connects. Default: 1.0");

            ZdoSyncMaxWaitSeconds = cfg.Bind(
                "Snapshot",
                "ZdoSyncMaxWaitSeconds",
                90.0f,
                "Maximum seconds to wait for the player's ZDO to populate before aborting the check. Default: 90.0");

            AutoSaveIntervalMinutes = cfg.Bind(
                "Snapshot",
                "AutoSaveIntervalMinutes",
                5.0f,
                "How often (in minutes) to auto-save snapshots for all connected players. Prevents data loss on unclean shutdowns. Default: 5.0");

            KickMessageWrongCharacter = cfg.Bind(
                "Messages",
                "KickMessageWrongCharacter",
                "ServerCharacters: You must use your registered character for this server. Contact an admin if this is an error.",
                "Message sent to players kicked for using the wrong character.");

            KickMessageMismatch = cfg.Bind(
                "Messages",
                "KickMessageMismatch",
                "ServerCharacters: Your character data does not match the server's records. Contact an admin if you believe this is a mistake.",
                "Message sent to players kicked for inventory/skill mismatch.");

            ProfileSyncTimeoutSeconds = cfg.Bind(
                "ClientSync",
                "ProfileSyncTimeoutSeconds",
                15.0f,
                "How long (seconds) the client waits for the server to send its profile data on join. " +
                "If the server does not respond in time, the client disconnects. Default: 15.0");

            VerboseLogging = cfg.Bind(
                "Debug",
                "VerboseLogging",
                false,
                "Enable extra debug logging to the BepInEx console/log file.");
        }
    }
}
