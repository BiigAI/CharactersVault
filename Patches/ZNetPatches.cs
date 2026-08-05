using System;
using System.Collections;
using HarmonyLib;
using CharacterVault.Helpers;
using CharacterVault.Models;
using CharacterVault.Systems;
using UnityEngine;

namespace CharacterVault.Patches
{
    // ═══════════════════════════════════════════════════════════════════════════
    // PATCH 1: ZNet.RPC_PeerInfo — fires on server when a client sends peer data
    //
    // This is the earliest point we can read the player's character name and
    // Steam ID. We perform the binding check here, and if the player passes,
    // we schedule a coroutine to check their snapshot after the ZDO syncs.
    //
    // NOTE: RPC_PeerInfo is a private method. If you get a Harmony patch warning
    //       at startup, verify the exact method name in dnSpy for your game version.
    // ═══════════════════════════════════════════════════════════════════════════
    [HarmonyPatch(typeof(ZNet), "RPC_PeerInfo")]
    public static class ZNet_RPC_PeerInfo_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(ZNet __instance, ZRpc rpc)
        {
            try
            {
                if (!__instance.IsServer()) return;

                ZNetPeer? peer = ZNetHelper.GetPeerByRpc(rpc);
                if (peer == null) return;

                string playerId = ZNetHelper.GetPlayerId(peer);
                string characterName = peer.m_playerName;

                if (string.IsNullOrWhiteSpace(playerId) || string.IsNullOrWhiteSpace(characterName))
                {
                    if (ModConfig.VerboseLogging.Value)
                        Plugin.Log.LogInfo($"[ZNetPatch] Skipping peer with invalid playerId or empty name.");
                    return;
                }

                Plugin.Log.LogInfo($"[CharacterVault] Player joining: playerId={playerId}, Character='{characterName}'");

                // ── Step 1: Character binding check ──────────────────────────────
                if (ModConfig.EnforceCharacterBinding.Value)
                {
                    if (BindingManager.IsRegistered(playerId))
                    {
                        string? registeredName = BindingManager.GetRegisteredName(playerId);
                        if (!string.Equals(registeredName, characterName, StringComparison.OrdinalIgnoreCase))
                        {
                            Plugin.Log.LogWarning(
                                $"[CharacterVault] KICK {playerId}: tried '{characterName}', registered as '{registeredName}'");
                            KickPeer(peer, ModConfig.KickMessageWrongCharacter.Value);
                            return;
                        }
                    }
                    else
                    {
                        BindingManager.Register(playerId, characterName);
                    }
                }

                BindingManager.RecordJoin(playerId);

                // ── Step 2: Trigger Handshake ─────────────────────────────────────────
                NetworkManager.Instance.SendHandshakeRequest(peer);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"[ZNetPatch] Exception in RPC_PeerInfo patch: {ex}");
            }
        }

        private static void KickPeer(ZNetPeer peer, string reason)
        {
            try
            {
                Plugin.Log.LogWarning($"[CharacterVault] Kicking peer {peer.m_uid}: {reason}");
                NetworkManager.Instance.RejectPeer(peer, reason);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"[ZNetPatch] Error sending kick RPC: {ex.Message}");
            }
        }
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // PATCH 2: ZNet.Disconnect — fires on server when a peer disconnects
    //
    // We use this to clean up our handshake tracking set so we don't hold
    // stale entries for disconnected peers.
    // ═══════════════════════════════════════════════════════════════════════════
    [HarmonyPatch(typeof(ZNet), "Disconnect")]
    public static class ZNet_Disconnect_Patch
    {
        [HarmonyPrefix]
        public static void Prefix(ZNetPeer peer)
        {
            try
            {
                if (peer == null) return;
                NetworkManager.Instance?.OnPeerDisconnected(peer.m_uid);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"[ZNetPatch] Exception in Disconnect patch: {ex}");
            }
        }
    }

    [HarmonyPatch(typeof(FejdStartup), "ShowConnectError")]
    public static class FejdStartup_ShowConnectError_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(FejdStartup __instance)
        {
            string? reason = ConnectionRejectionManager.ConsumeReason();
            if (!string.IsNullOrWhiteSpace(reason))
                Traverse.Create(__instance).Field("m_connectionFailedError").Property("text").SetValue(reason);
        }
    }
}
