using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using BepInEx;
using HarmonyLib;
using CharacterVault.Models;
using CharacterVault.Systems;
using UnityEngine;

namespace CharacterVault.Patches
{
    // ═══════════════════════════════════════════════════════════════════════════════
    // CLIENT PROFILE SYNCHRONISATION
    //
    // Flow:
    //   1. Client joins server -> Game_Start_Patch sets _waitingForProfile = true.
    //   2. Any local auto-save / SavePlayerToDisk is SUPPRESSED while _waitingForProfile == true,
    //      preventing the client from uploading single-player items to the server.
    //   3. Server sends authoritative .fch bytes over NetworkManager RPC.
    //   4. Client receives bytes -> ReceiveServerProfile(byte[] fullData):
    //      - Writes server bytes to local .fch disk file
    //      - Calls profile.LoadPlayerFromDisk() to reload PlayerProfile in memory
    //      - Calls profile.LoadPlayerData(Player.m_localPlayer) to update live in-world player
    //      - Sets _waitingForProfile = false
    //   5. Now player's offline items are stripped, and subsequent saves upload valid server data.
    // ═══════════════════════════════════════════════════════════════════════════════

    public static class ClientProfilePatches
    {
        private static byte[]? _serverProfileData = null;
        private static bool _waitingForProfile = false;
        internal static bool FirstJoinInitializationPending = false;
        internal static bool IsFirstJoinInitializationActive = false;

        // ── Public API (called by NetworkManager) ─────────────────────────────────

        /// <summary>
        /// Called by NetworkManager when the server's full profile bytes arrive.
        /// Overwrites local disk save, reloads PlayerProfile memory, and updates live player.
        /// </summary>
        public static void ReceiveServerProfile(byte[] profileData, bool isPlayerData, bool isFirstJoin)
        {
            Plugin.Log.LogInfo($"[ClientProfilePatches] Received {profileData.Length} bytes from server.");

            if (isFirstJoin)
            {
                FirstJoinInitializationPending = true;
                _waitingForProfile = false;
                Plugin.Log.LogInfo("[ClientProfilePatches] First join confirmed. Preserving appearance and clearing gameplay state after local profile load.");
                return;
            }

            if (profileData == null || profileData.Length == 0)
            {
                Plugin.Log.LogError("[ClientProfilePatches] Server returned an empty profile. Keeping local profile blocked.");
                return;
            }

            _serverProfileData = profileData;

            if (Game.instance != null && Game.instance.GetPlayerProfile() != null)
            {
                var profile = Game.instance.GetPlayerProfile();
                string characterName = profile.GetName();

                bool written = isPlayerData
                    ? ApplyServerPlayerData(profile, profileData)
                    : WriteServerDataToDisk(profile, profileData);

                if (written)
                {
                    // A full .fch must be reloaded; live player data can be applied directly.
                    if (!isPlayerData)
                    {
                        try
                        {
                            Traverse.Create(profile).Method("LoadPlayerFromDisk").GetValue();
                            profile.SetName(characterName);
                            Plugin.Log.LogInfo("[ClientProfilePatches] Reloaded PlayerProfile memory from server data.");
                        }
                        catch (Exception ex)
                        {
                            Plugin.Log.LogError($"[ClientProfilePatches] Failed to reload PlayerProfile from disk: {ex.Message}");
                        }
                    }

                    // 3. Release the load gate before applying the authoritative profile to the player.
                    _waitingForProfile = false;

                    // If local player has already spawned in-world, update live player state.
                    if (Player.m_localPlayer != null)
                    {
                        try
                        {
                            Traverse.Create(profile).Method("LoadPlayerData", Player.m_localPlayer).GetValue();
                            Plugin.Log.LogInfo("[ClientProfilePatches] Re-applied server profile data to live Player instance!");
                        }
                        catch (Exception ex)
                        {
                            Plugin.Log.LogError($"[ClientProfilePatches] Failed to apply server profile to live Player: {ex.Message}");
                        }
                    }
                }
            }

            _waitingForProfile = false;
        }

        /// <summary>
        /// Called at the start of a server join flow to prime the wait state.
        /// </summary>
        public static void ExpectServerProfile()
        {
            _serverProfileData = null;
            _waitingForProfile = true;
            Plugin.Log.LogInfo("[ClientProfilePatches] Waiting for server profile data...");
        }

        public static bool IsWaitingForProfile() => _waitingForProfile;
        public static bool IsInitializingFirstJoin() => IsFirstJoinInitializationActive;
        public static byte[]? GetServerProfile() => _serverProfileData;

        public static void Reset()
        {
            _serverProfileData = null;
            _waitingForProfile = false;
            FirstJoinInitializationPending = false;
            IsFirstJoinInitializationActive = false;
        }

        private static bool WriteServerDataToDisk(PlayerProfile profile, byte[] data)
        {
            if (data == null || data.Length == 0)
            {
                Plugin.Log.LogWarning("[WriteServerDataToDisk] Server sent empty profile bytes — skipping disk overwrite.");
                return false;
            }

            try
            {
                string path = profile.GetPath();
                FileHelpers.FileSource source = GetFileSource(profile);
                var writer = new FileWriter(path, FileHelpers.FileHelperType.Binary, source);
                writer.m_binary.Write(data);
                writer.Finish();

                if (writer.Status != FileWriter.WriterStatus.CloseSucceeded)
                    throw new IOException($"Valheim failed to write the profile ({writer.Status}).");

                Plugin.Log.LogInfo($"[WriteServerDataToDisk] Wrote {data.Length} server bytes to '{path}'.");
                return true;
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"[WriteServerDataToDisk] Failed to write server profile to disk: {ex}");
                return false;
            }
        }

        public static byte[] ReadProfileBytes(PlayerProfile profile)
        {
            string path = profile.GetPath();
            FileHelpers.FileSource source = GetFileSource(profile);
            var reader = new FileReader(path, source, FileHelpers.FileHelperType.Binary);
            try
            {
                int remainingBytes = (int)(reader.m_binary.BaseStream.Length - reader.m_binary.BaseStream.Position);
                return reader.m_binary.ReadBytes(remainingBytes);
            }
            finally
            {
                reader.Dispose();
            }
        }

        public static byte[] CaptureLivePlayerData()
        {
            if (Game.instance == null || Player.m_localPlayer == null)
                return Array.Empty<byte>();

            var profile = Game.instance.GetPlayerProfile();
            if (profile == null) return Array.Empty<byte>();

            profile.SavePlayerData(Player.m_localPlayer);
            return (byte[])Traverse.Create(profile).Field("m_playerData").GetValue();
        }

        private static bool ApplyServerPlayerData(PlayerProfile profile, byte[] data)
        {
            try
            {
                Traverse.Create(profile).Field("m_playerData").SetValue(data);
                Plugin.Log.LogInfo($"[ClientProfilePatches] Applied {data.Length} bytes of authoritative live player data.");
                return true;
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"[ClientProfilePatches] Failed to apply live player data: {ex}");
                return false;
            }
        }

        private static FileHelpers.FileSource GetFileSource(PlayerProfile profile)
        {
            return (FileHelpers.FileSource)Traverse.Create(profile).Field("m_fileSource").GetValue();
        }

        public static Type? GetUtilsType()
        {
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    var type = asm.GetType("Utils");
                    if (type != null) return type;
                }
                catch { }
            }
            return null;
        }
    }

    // ── Patch 1: Game.Start — set up the wait state early ─────────────────────

    [HarmonyPatch(typeof(Game), "Start")]
    public static class Game_Start_Patch
    {
        [HarmonyPrefix]
        public static void Prefix(Game __instance)
        {
            if (ZNet.instance != null && !ZNet.instance.IsServer())
            {
                // Connecting to a dedicated server as a client
                ClientProfilePatches.ExpectServerProfile();

                // Register RPCs now that ZRoutedRpc is ready
                NetworkManager.Instance.RegisterRPCs();
            }
            else if (ZNet.instance != null && ZNet.instance.IsServer())
            {
                // Server side RPC registration
                NetworkManager.Instance.RegisterRPCs();
            }
        }
    }

    // ── Patch 2: PlayerProfile.SavePlayerToDisk — upload to server on save ───────
    //
    // CRITICAL SECURITY FIX: Suppress profile uploads while _waitingForProfile is true!
    // This prevents the client from uploading single-player items to the server on spawn.

    [HarmonyPatch(typeof(PlayerProfile), "SavePlayerToDisk")]
    public static class PlayerProfile_SavePlayerToDisk_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(PlayerProfile __instance)
        {
            if (ZNet.instance == null || ZNet.instance.IsServer())
                return;

            if (ClientProfilePatches.IsWaitingForProfile())
            {
                Plugin.Log.LogWarning("[ClientProfilePatches] Suppressed profile upload to server (still waiting for authoritative server profile).");
                return;
            }

            Plugin.Log.LogInfo("[ClientProfilePatches] Player saved — uploading profile to server.");

            try
            {
                byte[] profileBytes = ClientProfilePatches.ReadProfileBytes(__instance);
                NetworkManager.Instance.SendProfileDataToServer(profileBytes);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"[ClientProfilePatches] Failed to upload saved profile to server: {ex}");
            }
        }
    }

    // Do not let Valheim populate the joining player from its local .fch until the
    // server snapshot has replaced that file. ReceiveServerProfile applies the same
    // method after it releases the gate.
    [HarmonyPatch(typeof(PlayerProfile), "LoadPlayerData")]
    public static class PlayerProfile_LoadPlayerData_Patch
    {
        [HarmonyPrefix]
        public static bool Prefix()
        {
            if (ZNet.instance != null && !ZNet.instance.IsServer() && ClientProfilePatches.IsWaitingForProfile())
            {
                Plugin.Log.LogInfo("[ClientProfilePatches] Blocked local profile data while waiting for authoritative server profile.");
                return false;
            }

            return true;
        }

        [HarmonyPostfix]
        public static void Postfix(PlayerProfile __instance, Player player)
        {
            if (!ClientProfilePatches.FirstJoinInitializationPending) return;

            ClientProfilePatches.FirstJoinInitializationPending = false;
            ClientProfilePatches.IsFirstJoinInitializationActive = true;
            try
            {
                player.UnequipAllItems();
                player.GetInventory().RemoveAll();
                player.GiveDefaultItems();
                player.SetGuardianPower(string.Empty);
                Traverse.Create(player).Field("m_skills").GetValue<Skills>().Clear();
                player.m_customData.Clear();
                ClearPlayerCollection(player, "m_foods");
                ClearPlayerCollection(player, "m_knownRecipes");
                ClearPlayerCollection(player, "m_knownStations");
                ClearPlayerCollection(player, "m_knownMaterial");
                ClearPlayerCollection(player, "m_shownTutorials");
                ClearPlayerCollection(player, "m_uniques");
                ClearPlayerCollection(player, "m_trophies");
                ClearPlayerCollection(player, "m_knownBiome");
                ClearPlayerCollection(player, "m_knownTexts");

                __instance.SavePlayerData(player);
                byte[] playerData = (byte[])Traverse.Create(__instance).Field("m_playerData").GetValue();
                NetworkManager.Instance.SendProfileDataToServer(playerData, isPlayerData: true);
                Plugin.Log.LogInfo("[ClientProfilePatches] Created initial clean player snapshot while preserving local appearance.");
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"[ClientProfilePatches] Failed to initialize first-join player state: {ex}");
            }
            finally
            {
                ClientProfilePatches.IsFirstJoinInitializationActive = false;
            }
        }

        private static void ClearPlayerCollection(Player player, string fieldName)
        {
            object collection = Traverse.Create(player).Field(fieldName).GetValue();
            collection.GetType().GetMethod("Clear").Invoke(collection, null);
        }
    }

    // The periodic checkpoint can be several minutes old when a player leaves.
    // Save before the client tears down its server connection so the save postfix
    // uploads the final in-server character state.
    [HarmonyPatch(typeof(ZNet), "Disconnect")]
    public static class ZNet_Disconnect_SaveProfile_Patch
    {
        [HarmonyPrefix]
        public static void Prefix()
        {
            if (ZNet.instance == null || ZNet.instance.IsServer() || ClientProfilePatches.IsWaitingForProfile())
                return;

            if (Game.instance == null || Game.instance.GetPlayerProfile() == null)
                return;

            try
            {
                Plugin.Log.LogInfo("[ClientProfilePatches] Saving profile before server disconnect.");
                Game.instance.SavePlayerProfile(true);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"[ClientProfilePatches] Failed to save profile before disconnect: {ex}");
            }
        }
    }

    [HarmonyPatch]
    public static class Inventory_Change_Checkpoint_Patch
    {
        public static IEnumerable<MethodBase> TargetMethods()
        {
            foreach (MethodInfo method in AccessTools.GetDeclaredMethods(typeof(Inventory)))
            {
                if (method.Name == "Changed")
                    yield return method;
            }
        }

        [HarmonyPostfix]
        public static void Postfix(Inventory __instance)
        {
            if (!LocalPlayerState.IsInventory(__instance)) return;
            ClientSyncManager.Instance.QueueSnapshotUpdate("inventory change");
        }
    }

    [HarmonyPatch]
    public static class Skills_Change_Checkpoint_Patch
    {
        public static IEnumerable<MethodBase> TargetMethods()
        {
            foreach (MethodInfo method in AccessTools.GetDeclaredMethods(typeof(Skills)))
            {
                if (method.Name == "LowerAllSkills" || method.Name == "OnDeath")
                    yield return method;
            }
        }

        [HarmonyPostfix]
        public static void Postfix(Skills __instance)
        {
            if (!LocalPlayerState.IsSkills(__instance)) return;
            ClientSyncManager.Instance.QueueSnapshotUpdate("skill change");
        }
    }

    [HarmonyPatch(typeof(Player), "OnSkillLevelup")]
    public static class Player_SkillLevelup_Checkpoint_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(Player __instance)
        {
            if (__instance != Player.m_localPlayer) return;
            ClientSyncManager.Instance.QueueSnapshotUpdate("skill level up");
        }
    }

    [HarmonyPatch]
    public static class Player_GuardianPower_Checkpoint_Patch
    {
        public static IEnumerable<MethodBase> TargetMethods()
        {
            foreach (MethodInfo method in AccessTools.GetDeclaredMethods(typeof(Player)))
            {
                if (method.Name == "SetGuardianPower" || method.Name == "SetForsakenPower")
                    yield return method;
            }
        }

        [HarmonyPrefix]
        public static void Prefix(Player __instance, out string __state)
        {
            __state = __instance.GetGuardianPowerName();
        }

        [HarmonyPostfix]
        public static void Postfix(Player __instance, string __state)
        {
            if (__instance != Player.m_localPlayer || __state == __instance.GetGuardianPowerName()) return;
            ClientSyncManager.Instance.QueueSnapshotUpdate("forsaken power change");
        }
    }

    internal static class LocalPlayerState
    {
        public static bool IsInventory(Inventory inventory)
        {
            if (Player.m_localPlayer == null) return false;
            object? localInventory = Traverse.Create(Player.m_localPlayer).Field("m_inventory").GetValue();
            return ReferenceEquals(localInventory, inventory);
        }

        public static bool IsSkills(Skills skills)
        {
            if (Player.m_localPlayer == null) return false;
            object? localSkills = Traverse.Create(Player.m_localPlayer).Field("m_skills").GetValue();
            return ReferenceEquals(localSkills, skills);
        }
    }
}
