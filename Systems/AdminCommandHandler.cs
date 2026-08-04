using System;
using System.Text;
using ServerCharacters.Helpers;

namespace ServerCharacters.Systems
{
    /// <summary>
    /// Processes admin commands received via server-side chat interception.
    ///
    /// Commands are prefixed with "/sc" and only execute if the sender is in the server's admin list.
    ///
    /// File-based administration (editing overrides.json / bindings.json directly) is always
    /// available as a fallback and is the primary admin method for headless servers.
    ///
    /// Available commands:
    ///   /sc allow [playerId]   — Grant one-time override for mismatch (in-memory + disk)
    ///   /sc deny [playerId]    — Revoke a pending in-memory override
    ///   /sc remove [playerId]  — Remove a character binding (player can re-register)
    ///   /sc wipe [playerId]    — Fully delete all server data for a player (blank slate on next join)
    ///   /sc list              — List all registered bindings
    ///   /sc status [playerId]  — Show binding and last snapshot info for a player
    ///   /sc help              — List commands
    /// </summary>
    public static class AdminCommandHandler
    {
        private const string Prefix = "/sc";

        /// <summary>
        /// Returns true if the text starts with the command prefix.
        /// </summary>
        public static bool IsCommand(string text) =>
            text.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// Parse and execute an admin command. Logs the result.
        /// </summary>
        /// <param name="senderSteamId">Steam ID of the player who sent the message.</param>
        /// <param name="text">Full chat message text (including the /sc prefix).</param>
        /// <returns>True if the command was recognized and handled, false otherwise.</returns>
        public static bool Handle(long senderUid, string text)
        {
            ZNetPeer? peer = ZNet.instance?.GetPeer(senderUid);
            if (peer == null) return false;
            
            string senderPlayerId = ZNetHelper.GetPlayerId(peer);

            // Verify admin privileges
            if (!IsAdmin(senderPlayerId))
            {
                Plugin.Log.LogWarning($"[AdminCmd] Non-admin {senderPlayerId} tried to run: {text}");
                return false;
            }

            // Strip prefix and split into tokens
            string body = text.Substring(Prefix.Length).Trim();
            string[] tokens = body.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);

            if (tokens.Length == 0)
            {
                LogToAdmin(peer, PrintHelp());
                return true;
            }

            string command = tokens[0].ToLowerInvariant();

            switch (command)
            {
                case "allow":
                    return CmdAllow(peer, tokens);

                case "deny":
                    return CmdDeny(peer, tokens);

                case "remove":
                    return CmdRemove(peer, tokens);

                case "wipe":
                    return CmdWipe(peer, tokens);

                case "list":
                    return CmdList(peer);

                case "status":
                    return CmdStatus(peer, tokens);

                case "help":
                    LogToAdmin(peer, PrintHelp());
                    return true;

                default:
                    LogToAdmin(peer, $"Unknown command '{command}'. Type /sc help for a list.");
                    return true;
            }
        }

        // ── Commands ──────────────────────────────────────────────────────────────

        private static bool CmdAllow(ZNetPeer adminPeer, string[] tokens)
        {
            if (!TryGetPlayerId(tokens, 1, adminPeer, out string targetId)) return true;

            // Grant both in-memory and on-disk override
            OverrideManager.GrantMemoryOverride(targetId);
            var diskOverrides = DataStore.LoadOverrides();
            diskOverrides.Add(targetId);
            DataStore.SaveOverrides(diskOverrides);

            LogToAdmin(adminPeer, $"Granted one-time override for {targetId}. They may join once with mismatched data.");
            return true;
        }

        private static bool CmdDeny(ZNetPeer adminPeer, string[] tokens)
        {
            if (!TryGetPlayerId(tokens, 1, adminPeer, out string targetId)) return true;

            bool removed = OverrideManager.RevokeOverride(targetId);
            var diskOverrides = DataStore.LoadOverrides();
            bool removedDisk = diskOverrides.Remove(targetId);
            if (removedDisk) DataStore.SaveOverrides(diskOverrides);

            LogToAdmin(adminPeer, removed || removedDisk
                ? $"Revoked override for {targetId}."
                : $"No active override found for {targetId}.");
            return true;
        }

        private static bool CmdRemove(ZNetPeer adminPeer, string[] tokens)
        {
            if (!TryGetPlayerId(tokens, 1, adminPeer, out string targetId)) return true;

            bool removed = BindingManager.RemoveBinding(targetId);
            LogToAdmin(adminPeer, removed
                ? $"Removed binding for {targetId}. They may re-register with a new character."
                : $"No binding found for {targetId}.");
            return true;
        }

        private static bool CmdWipe(ZNetPeer adminPeer, string[] tokens)
        {
            if (!TryGetPlayerId(tokens, 1, adminPeer, out string targetId)) return true;

            bool wiped = DataStore.WipePlayerData(targetId);

            if (wiped)
            {
                LogToAdmin(adminPeer,
                    $"Wiped all server data for {targetId}. " +
                    $"On their next join they will receive a blank character (no items, no skills).");
            }
            else
            {
                LogToAdmin(adminPeer, $"No data found for {targetId} — nothing to wipe.");
            }
            return true;
        }

        private static bool CmdList(ZNetPeer adminPeer)
        {
            var bindings = BindingManager.GetAll();
            if (bindings.Count == 0)
            {
                LogToAdmin(adminPeer, "No character bindings registered.");
                return true;
            }

            var sb = new StringBuilder($"Character Bindings ({bindings.Count}):\n");
            foreach (var kvp in bindings)
                sb.AppendLine($"  {kvp.Key} → '{kvp.Value.CharacterName}' (since {kvp.Value.RegisteredAt:yyyy-MM-dd})");

            LogToAdmin(adminPeer, sb.ToString().TrimEnd());
            return true;
        }

        private static bool CmdStatus(ZNetPeer adminPeer, string[] tokens)
        {
            if (!TryGetPlayerId(tokens, 1, adminPeer, out string targetId)) return true;

            var binding = BindingManager.GetRegisteredName(targetId);
            var snapshot = DataStore.LoadSnapshot(targetId);
            bool hasOverride = OverrideManager.HasOverride(targetId);

            var sb = new StringBuilder($"Status for {targetId}:\n");
            sb.AppendLine($"  Binding:   {(binding != null ? $"'{binding}'" : "Not registered")}");
            sb.AppendLine($"  Snapshot:  {(snapshot != null ? $"Taken {snapshot.SnapshotTime:yyyy-MM-dd HH:mm} UTC" : "None")}");
            sb.Append($"  Override:  {(hasOverride ? "ACTIVE (will consume on next join)" : "None")}");

            LogToAdmin(adminPeer, sb.ToString());
            return true;
        }

        // ── Helpers ───────────────────────────────────────────────────────────────

        private static bool TryGetPlayerId(string[] tokens, int index, ZNetPeer adminPeer, out string targetId)
        {
            targetId = "";
            if (tokens.Length <= index)
            {
                LogToAdmin(adminPeer, $"Invalid or missing player ID. Example: /sc {tokens[0]} Steam_76561198XXXXXXXXX");
                return false;
            }
            targetId = tokens[index];
            return true;
        }

        private static bool IsAdmin(string playerId)
        {
            return ZNet.instance != null && ZNetHelper.IsAdmin(playerId);
        }

        private static void LogToAdmin(ZNetPeer adminPeer, string message)
        {
            // Log to BepInEx console (visible in server terminal)
            Plugin.Log.LogInfo($"[AdminCmd] → {message}");

            // Send as server message to the admin player
            try
            {
                if (adminPeer != null)
                {
                    // Send via ZRoutedRpc as a "chat" message from server
                    ZRoutedRpc.instance.InvokeRoutedRPC(
                        adminPeer.m_uid,
                        "ChatMessage",
                        new object[]
                        {
                            adminPeer.m_refPos,          // position
                            (int)Talker.Type.Normal, // chat type
                            "ServerCharacters",       // sender name
                            message                   // message text
                        }
                    );
                }
            }
            catch (Exception ex)
            {
                if (ModConfig.VerboseLogging.Value)
                    Plugin.Log.LogWarning($"[AdminCmd] Could not send in-game response: {ex.Message}");
            }
        }

        private static string PrintHelp() =>
            "ServerCharacters Admin Commands:\n" +
            "  /sc allow [playerId]  — One-time override for mismatched player\n" +
            "  /sc deny [playerId]   — Revoke pending override\n" +
            "  /sc remove [playerId] — Remove character binding (allows re-register)\n" +
            "  /sc wipe [playerId]   — Delete ALL server data for player (blank slate next join)\n" +
            "  /sc list             — Show all bindings\n" +
            "  /sc status [playerId] — Show binding + snapshot info\n" +
            "  /sc help             — This message\n" +
            $"  Override file: {DataStore.OverridesFilePath}";
    }
}
