using System;
using HarmonyLib;
using ServerCharacters.Systems;
using UnityEngine;

namespace ServerCharacters.Patches
{
    // ═══════════════════════════════════════════════════════════════════════════
    // PATCH: Chat.RPC_ChatMessage — fires on server when any player sends a chat message
    //
    // We intercept messages starting with "/sc" here. If the sender is a server admin,
    // the message is routed to AdminCommandHandler and suppressed from normal chat.
    //
    // Valheim 0.221+ changed the signature: instead of a raw ZPackage, the method now
    // receives parsed UserInfo and string text directly.
    // ═══════════════════════════════════════════════════════════════════════════
    [HarmonyPatch(typeof(Chat), "RPC_ChatMessage")]
    public static class Chat_RPC_ChatMessage_Patch
    {
        [HarmonyPrefix]
        public static bool Prefix(long sender, Vector3 position, int type, UserInfo userInfo, string text)
        {
            try
            {
                // Only process on server
                if (ZNet.instance == null || !ZNet.instance.IsServer()) return true;

                // Only handle "/sc" commands
                if (!AdminCommandHandler.IsCommand(text)) return true;

                // Route to admin handler — suppress the chat message if it's a recognized command
                bool handled = AdminCommandHandler.Handle(sender, text);
                return !handled; // Return false (skip original) if we handled it
            }
            catch (Exception ex)
            {
                if (ModConfig.VerboseLogging.Value)
                    Plugin.Log.LogWarning($"[ChatPatch] Exception in chat prefix: {ex.Message}");
                return true;
            }
        }
    }
}
