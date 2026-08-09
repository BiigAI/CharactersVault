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

        // ── Client Sync ───────────────────────────────────────────────────────────
        /// <summary>How often the client performs a full profile sync as a safety net.</summary>
        public static ConfigEntry<float> AutoSaveIntervalMinutes { get; private set; } = null!;

        // ── Messages ─────────────────────────────────────────────────────────────
        public static ConfigEntry<string> KickMessageWrongCharacter { get; private set; } = null!;

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
                "If true, each platform ID may only join with the character name it first registered with.");

            AutoSaveIntervalMinutes = cfg.Bind(
                "ClientSync",
                "AutoSaveIntervalMinutes",
                5.0f,
                "How often (in minutes) the client performs a full profile sync. Default: 5.0");

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

            VerboseLogging = cfg.Bind(
                "Debug",
                "VerboseLogging",
                false,
                "Enable extra debug logging to the BepInEx console/log file.");
        }
    }
}
