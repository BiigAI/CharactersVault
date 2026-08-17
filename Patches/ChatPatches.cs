using System;
using HarmonyLib;
using CharacterVault.Systems;

namespace CharacterVault.Patches
{
    // ═══════════════════════════════════════════════════════════════════════════
    // PATCH: Terminal.TryRunCommand — intercepts "/cv" / "cv" / "/vault" commands
    //
    // In Valheim, Chat inherits from Terminal. When a player types "/cv ...",
    // Valheim strips the leading slash and calls Terminal.TryRunCommand("cv ...").
    //
    // We intercept all "cv" and "vault" commands here and route them via our
    // dedicated AdminCommandHandler RPC to the server.
    // ═══════════════════════════════════════════════════════════════════════════
    [HarmonyPatch(typeof(Terminal), "TryRunCommand")]
    public static class Terminal_TryRunCommand_Patch
    {
        [HarmonyPrefix]
        public static bool Prefix(Terminal __instance, string text)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(text)) return true;

                string trimmed = text.Trim();
                if (trimmed.StartsWith("cv ", StringComparison.OrdinalIgnoreCase) ||
                    trimmed.Equals("cv", StringComparison.OrdinalIgnoreCase) ||
                    trimmed.StartsWith("/cv ", StringComparison.OrdinalIgnoreCase) ||
                    trimmed.Equals("/cv", StringComparison.OrdinalIgnoreCase) ||
                    trimmed.StartsWith("vault ", StringComparison.OrdinalIgnoreCase) ||
                    trimmed.Equals("vault", StringComparison.OrdinalIgnoreCase) ||
                    trimmed.StartsWith("/vault ", StringComparison.OrdinalIgnoreCase) ||
                    trimmed.Equals("/vault", StringComparison.OrdinalIgnoreCase))
                {
                    AdminCommandHandler.SendAdminCommand(trimmed);
                    return false; // Handled, suppress unknown command warning
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"[TerminalPatch] Error in TryRunCommand prefix: {ex}");
            }

            return true;
        }
    }
}

