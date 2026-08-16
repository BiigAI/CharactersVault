using System;
using System.Text;
using CharacterVault.Helpers;

namespace CharacterVault.Systems
{
    /// <summary>
    /// Handles in-game admin commands (/sc) sent from the client to the dedicated server.
    ///
    /// Available commands:
    ///   /sc list              — List all registered character bindings
    ///   /sc status [playerId]  — Show binding and snapshot info for a player
    ///   /sc remove [playerId]  — Remove a character binding (allows re-register)
    ///   /sc wipe [playerId]    — Fully delete all server data for a player
    ///   /sc help              — Show command overview
    /// </summary>
    public static class AdminCommandHandler
    {
        public const string RpcAdminCommand = "CharacterVault_AdminCmd";
        public const string RpcAdminResponse = "CharacterVault_AdminResp";

        public static void RegisterRPCs()
        {
            ZRoutedRpc.instance.Register<string>(RpcAdminCommand, RPC_AdminCommand);
            ZRoutedRpc.instance.Register<string>(RpcAdminResponse, RPC_AdminResponse);
        }

        /// <summary>
        /// Called on client when the user enters an /sc command in chat or console.
        /// </summary>
        public static void SendAdminCommand(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return;

            if (!text.StartsWith("/", StringComparison.OrdinalIgnoreCase))
                text = "/" + text;

            if (ZNet.instance == null)
            {
                DisplayResponse("<color=yellow>[CharactersVault]</color> Not connected to a server.");
                return;
            }

            if (ZNet.instance.IsServer())
            {
                // Running on local server / host — execute directly
                string response = ExecuteCommand(text);
                DisplayResponse(response);
                return;
            }

            // Client sending command to dedicated server
            Plugin.Log.LogInfo($"[AdminCmd] Sending admin command to server: {text}");
            ZRoutedRpc.instance.InvokeRoutedRPC(0L, RpcAdminCommand, text);
        }

        /// <summary>
        /// Server-side RPC handler for incoming admin commands from clients.
        /// </summary>
        private static void RPC_AdminCommand(long sender, string text)
        {
            if (ZNet.instance == null || !ZNet.instance.IsServer()) return;

            ZNetPeer? peer = ZNet.instance.GetPeer(sender);
            if (peer == null) return;

            string playerId = ZNetHelper.GetPlayerId(peer);

            if (!ZNetHelper.IsAdmin(peer))
            {
                Plugin.Log.LogWarning($"[AdminCmd] Non-admin {playerId} (host: {peer.m_socket?.GetHostName()}) tried to run: {text}");
                ZRoutedRpc.instance.InvokeRoutedRPC(sender, RpcAdminResponse, "<color=red>[CharactersVault] Access denied: You are not listed in the server's adminlist.txt.</color>");
                return;
            }

            Plugin.Log.LogInfo($"[AdminCmd] Executing '{text}' for admin {playerId}");
            string response = ExecuteCommand(text);
            ZRoutedRpc.instance.InvokeRoutedRPC(sender, RpcAdminResponse, response);
        }

        /// <summary>
        /// Client-side RPC handler for receiving admin command responses from the server.
        /// </summary>
        private static void RPC_AdminResponse(long sender, string response)
        {
            DisplayResponse(response);
        }

        private static void DisplayResponse(string response)
        {
            Plugin.Log.LogInfo($"[AdminCmd] Response:\n{response}");
            if (Chat.instance != null)
            {
                Chat.instance.AddString(response);
            }
        }

        /// <summary>
        /// Parse and execute the command.
        /// </summary>
        private static string ExecuteCommand(string text)
        {
            string body = text;
            if (body.StartsWith("/sc", StringComparison.OrdinalIgnoreCase))
                body = body.Substring(3).Trim();
            else if (body.StartsWith("sc", StringComparison.OrdinalIgnoreCase))
                body = body.Substring(2).Trim();

            string[] tokens = body.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length == 0)
            {
                return PrintHelp();
            }

            string command = tokens[0].ToLowerInvariant();
            switch (command)
            {
                case "remove":
                    return CmdRemove(tokens);

                case "wipe":
                    return CmdWipe(tokens);

                case "list":
                    return CmdList();

                case "status":
                    return CmdStatus(tokens);

                case "help":
                    return PrintHelp();

                default:
                    return $"<color=yellow>[CharactersVault]</color> Unknown command '{command}'. Type <color=#33FF33>/sc help</color> for a list.";
            }
        }

        private static string CmdRemove(string[] tokens)
        {
            if (tokens.Length < 2)
                return "<color=yellow>[CharactersVault]</color> Missing player ID. Example: <color=#33FF33>/sc remove Steam_76561198XXXXXXXXX</color>";

            string targetId = tokens[1];
            bool removed = BindingManager.RemoveBinding(targetId);
            return removed
                ? $"<color=green>[CharactersVault]</color> Removed binding for {targetId}. They may re-register with a new character."
                : $"<color=yellow>[CharactersVault]</color> No binding found for {targetId}.";
        }

        private static string CmdWipe(string[] tokens)
        {
            if (tokens.Length < 2)
                return "<color=yellow>[CharactersVault]</color> Missing player ID. Example: <color=#33FF33>/sc wipe Steam_76561198XXXXXXXXX</color>";

            string targetId = tokens[1];
            bool wiped = DataStore.WipePlayerData(targetId);
            BindingManager.Load();

            return wiped
                ? $"<color=green>[CharactersVault]</color> Wiped all server data for {targetId}. On next join they will receive a blank character."
                : $"<color=yellow>[CharactersVault]</color> No data found for {targetId} — nothing to wipe.";
        }

        private static string CmdList()
        {
            var bindings = BindingManager.GetAll();
            if (bindings.Count == 0)
                return "<color=yellow>[CharactersVault]</color> No character bindings registered.";

            var sb = new StringBuilder($"<color=cyan>[CharactersVault]</color> Character Bindings ({bindings.Count}):\n");
            foreach (var kvp in bindings)
            {
                sb.AppendLine($"  <color=#FFCC00>{kvp.Key}</color> → '{kvp.Value.CharacterName}' (since {kvp.Value.RegisteredAt:yyyy-MM-dd})");
            }
            return sb.ToString().TrimEnd();
        }

        private static string CmdStatus(string[] tokens)
        {
            if (tokens.Length < 2)
                return "<color=yellow>[CharactersVault]</color> Missing player ID. Example: <color=#33FF33>/sc status Steam_76561198XXXXXXXXX</color>";

            string targetId = tokens[1];
            var binding = BindingManager.GetRegisteredName(targetId);
            var snapshot = DataStore.LoadSnapshot(targetId);

            var sb = new StringBuilder($"<color=cyan>[CharactersVault]</color> Status for {targetId}:\n");
            sb.AppendLine($"  Binding:   {(binding != null ? $"'{binding}'" : "Not registered")}");
            sb.Append($"  Snapshot:  {(snapshot != null ? $"Taken {snapshot.SnapshotTime:yyyy-MM-dd HH:mm} UTC" : "None")}");
            return sb.ToString();
        }

        private static string PrintHelp() =>
            "<color=cyan>[CharactersVault]</color> Admin Commands:\n" +
            "  <color=#33FF33>/sc list</color> — Show all bindings\n" +
            "  <color=#33FF33>/sc status [playerId]</color> — Show binding + snapshot info\n" +
            "  <color=#33FF33>/sc remove [playerId]</color> — Remove character binding (allows re-register)\n" +
            "  <color=#33FF33>/sc wipe [playerId]</color> — Delete ALL server data for player (blank slate next join)\n" +
            "  <color=#33FF33>/sc help</color> — This message";
    }
}
