using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
        private static bool _serverImportAllowed = false;
        private static Coroutine? _profileTimeoutCoroutine = null;
        internal static bool FirstJoinInitializationPending = false;
        internal static bool IsFirstJoinInitializationActive = false;

        public static void SetServerImportAllowed(bool allowed) => _serverImportAllowed = allowed;
        public static bool IsServerImportAllowed => _serverImportAllowed;

        // ── Public API (called by NetworkManager) ─────────────────────────────────

        /// <summary>
        /// Called by NetworkManager when the server's full profile bytes arrive.
        /// Overwrites local disk save, reloads PlayerProfile memory, and updates live player.
        /// </summary>
        public static void ReceiveServerProfile(byte[] profileData, bool isPlayerData, bool isFirstJoin)
        {
            Plugin.Log.LogInfo($"[ClientProfilePatches] Received {profileData?.Length ?? 0} bytes from server.");

            if (_profileTimeoutCoroutine != null && NetworkManager.Instance != null)
            {
                NetworkManager.Instance.StopCoroutine(_profileTimeoutCoroutine);
                _profileTimeoutCoroutine = null;
            }

            if (isFirstJoin)
            {
                _waitingForProfile = false;
                if (Player.m_localPlayer != null && Game.instance?.GetPlayerProfile() != null)
                {
                    HandleFirstJoin(Game.instance.GetPlayerProfile(), Player.m_localPlayer);
                }
                else
                {
                    FirstJoinInitializationPending = true;
                    Plugin.Log.LogInfo("[ClientProfilePatches] First join confirmed. Waiting for player load to initialize or validate state.");
                }
                return;
            }

            if (profileData == null || profileData.Length == 0)
            {
                Plugin.Log.LogError("[ClientProfilePatches] Server returned an empty profile. Releasing load gate and disconnecting.");
                _waitingForProfile = false;
                ConnectionRejectionManager.SetReason("Server returned empty character profile data.");
                Game.instance?.Disconnect();
                return;
            }

            _serverProfileData = profileData;

            var profile = Game.instance?.GetPlayerProfile();
            if (profile != null)
            {
                bool diverged = HasDataDiverged(profile, profileData, isPlayerData);
                if (diverged && ModConfig.PromptOnOfflineProgress != null && ModConfig.PromptOnOfflineProgress.Value)
                {
                    Plugin.Log.LogWarning($"[ClientProfilePatches] Character '{profile.GetName()}' has offline progress. Prompting user before overwrite.");

                    ShowYesNoPrompt(
                        "Offline Progress Detected",
                        "You've progressed offline on this character. These advancements will be removed upon joining the server.\n\nContinue?",
                        onYes: () =>
                        {
                            if (ModConfig.AutoBackupBeforeReset != null && ModConfig.AutoBackupBeforeReset.Value)
                            {
                                DataStore.BackupLocalProfile(profile);
                                Plugin.Log.LogInfo("[ClientProfilePatches] Created offline progress backup before server overwrite.");
                            }
                            ApplyServerProfile(profile, profileData, isPlayerData);
                        },
                        onNo: () =>
                        {
                            Plugin.Log.LogWarning("[ClientProfilePatches] Player cancelled joining server to protect offline progress. Disconnecting cleanly.");
                            _waitingForProfile = false;
                            ConnectionRejectionManager.SetReason(
                                "<color=#33CCFF>CharactersVault</color>\n\n" +
                                "Disconnected to protect your offline character progression.\n\n" +
                                "To join this server without overwriting your singleplayer progress, please use a dedicated character for this server."
                            );
                            Game.instance?.Disconnect();
                        }
                    );
                    return;
                }

                ApplyServerProfile(profile, profileData, isPlayerData);
            }
            else
            {
                _waitingForProfile = false;
            }
        }

        private static void ApplyServerProfile(PlayerProfile profile, byte[] profileData, bool isPlayerData)
        {
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

                // Release the load gate before applying the authoritative profile to the player.
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

            _waitingForProfile = false;
        }

        public static bool HasDataDiverged(PlayerProfile profile, byte[] serverData, bool isPlayerData)
        {
            if (profile == null || serverData == null || serverData.Length == 0)
                return false;

            try
            {
                byte[]? localData = null;
                if (isPlayerData)
                {
                    localData = (byte[]?)Traverse.Create(profile).Field("m_playerData").GetValue();
                    if (localData == null || localData.Length == 0)
                    {
                        localData = CaptureLivePlayerData();
                    }
                }
                else
                {
                    localData = ReadProfileBytes(profile);
                }

                if (localData == null || localData.Length == 0)
                    return false;

                if (localData.Length != serverData.Length)
                    return true;

                for (int i = 0; i < localData.Length; i++)
                {
                    if (localData[i] != serverData[i])
                        return true;
                }

                return false;
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"[ClientProfilePatches] Could not determine if profile data diverged: {ex.Message}");
                return false;
            }
        }

        public static void ShowYesNoPrompt(string title, string text, Action onYes, Action onNo)
        {
            try
            {
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;

                Type? popupType = AccessTools.TypeByName("UnifiedPopup");
                if (popupType != null)
                {
                    object? instance = AccessTools.Property(popupType, "instance")?.GetValue(null, null)
                                    ?? AccessTools.Field(popupType, "instance")?.GetValue(null);

                    Type? popupDataClass = popupType.GetNestedType("Popup") ?? popupType.GetNestedType("PopupData");
                    if (instance != null && popupDataClass != null)
                    {
                        object? popupObj = null;
                        foreach (var ctor in popupDataClass.GetConstructors())
                        {
                            var parameters = ctor.GetParameters();
                            if (parameters.Length >= 4 &&
                                parameters[0].ParameterType == typeof(string) &&
                                parameters[1].ParameterType == typeof(string) &&
                                typeof(Delegate).IsAssignableFrom(parameters[2].ParameterType) &&
                                typeof(Delegate).IsAssignableFrom(parameters[3].ParameterType))
                            {
                                object[] args = new object[parameters.Length];
                                args[0] = title;
                                args[1] = text;
                                args[2] = onYes;
                                args[3] = onNo;
                                for (int i = 4; i < parameters.Length; i++)
                                {
                                    args[i] = parameters[i].DefaultValue ?? (parameters[i].ParameterType.IsValueType ? Activator.CreateInstance(parameters[i].ParameterType) : null)!;
                                }
                                popupObj = ctor.Invoke(args);
                                break;
                            }
                        }

                        if (popupObj == null)
                        {
                            try
                            {
                                popupObj = Activator.CreateInstance(popupDataClass);
                                SetMember(popupObj, "m_title", title);
                                SetMember(popupObj, "Title", title);
                                SetMember(popupObj, "m_text", text);
                                SetMember(popupObj, "Text", text);
                                SetMember(popupObj, "m_onOk", onYes);
                                SetMember(popupObj, "m_onYes", onYes);
                                SetMember(popupObj, "m_onCancel", onNo);
                                SetMember(popupObj, "m_onNo", onNo);
                            }
                            catch { }
                        }

                        if (popupObj != null)
                        {
                            MethodInfo? method = AccessTools.Method(popupType, "AddPopup", new[] { popupDataClass })
                                              ?? AccessTools.Method(popupType, "Push", new[] { popupDataClass })
                                              ?? AccessTools.Method(popupType, "ShowPopup", new[] { popupDataClass })
                                              ?? popupType.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static)
                                                          .FirstOrDefault(m => (m.Name == "AddPopup" || m.Name == "Push" || m.Name == "ShowPopup") &&
                                                                               m.GetParameters().Length == 1 &&
                                                                               m.GetParameters()[0].ParameterType.IsAssignableFrom(popupDataClass));

                            if (method != null)
                            {
                                method.Invoke(method.IsStatic ? null : instance, new[] { popupObj });
                                Plugin.Log.LogInfo("[ClientProfilePatches] Displayed offline progress confirmation dialog via UnifiedPopup.");
                                return;
                            }
                        }
                    }
                }

                // Fallback to GuiPopup if UnifiedPopup is unavailable
                Type? guiPopupType = AccessTools.TypeByName("GuiPopup");
                if (guiPopupType != null)
                {
                    object? guiInstance = AccessTools.Property(guiPopupType, "instance")?.GetValue(null, null)
                                       ?? AccessTools.Field(guiPopupType, "instance")?.GetValue(null);
                    if (guiInstance != null)
                    {
                        var showMethod = guiPopupType.GetMethods(BindingFlags.Public | BindingFlags.Instance)
                            .FirstOrDefault(m => m.Name == "Show" && m.GetParameters().Length >= 3);
                        if (showMethod != null)
                        {
                            var p = showMethod.GetParameters();
                            if (p.Length == 4)
                            {
                                showMethod.Invoke(guiInstance, new object[] { title, text, onYes, onNo });
                            }
                            else if (p.Length == 3)
                            {
                                showMethod.Invoke(guiInstance, new object[] { text, onYes, onNo });
                            }
                            Plugin.Log.LogInfo("[ClientProfilePatches] Displayed offline progress confirmation dialog via GuiPopup.");
                            return;
                        }
                    }
                }

                Plugin.Log.LogWarning("[ClientProfilePatches] UnifiedPopup / GuiPopup not available. Defaulting to proceed with join.");
                onYes();
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"[ClientProfilePatches] Failed to show confirmation prompt: {ex}");
                onYes();
            }
        }

        private static void SetMember(object target, string name, object value)
        {
            var field = AccessTools.Field(target.GetType(), name);
            if (field != null && field.FieldType.IsAssignableFrom(value.GetType()))
            {
                field.SetValue(target, value);
                return;
            }
            var prop = AccessTools.Property(target.GetType(), name);
            if (prop != null && prop.CanWrite && prop.PropertyType.IsAssignableFrom(value.GetType()))
            {
                prop.SetValue(target, value, null);
            }
        }

        /// <summary>
        /// Called at the start of a server join flow to prime the wait state.
        /// </summary>
        public static void ExpectServerProfile()
        {
            _serverProfileData = null;
            _waitingForProfile = true;
            Plugin.Log.LogInfo("[ClientProfilePatches] Waiting for server profile data...");

            if (_profileTimeoutCoroutine != null && NetworkManager.Instance != null)
            {
                NetworkManager.Instance.StopCoroutine(_profileTimeoutCoroutine);
            }

            if (NetworkManager.Instance != null)
            {
                _profileTimeoutCoroutine = NetworkManager.Instance.StartCoroutine(ProfileTimeoutCoroutine());
            }
        }

        private static IEnumerator ProfileTimeoutCoroutine()
        {
            float timeout = ModConfig.ProfileSyncTimeoutSeconds.Value;
            yield return new WaitForSecondsRealtime(timeout);

            if (_waitingForProfile)
            {
                Plugin.Log.LogWarning($"[ClientProfilePatches] Timed out after {timeout}s waiting for server profile data. Disconnecting.");
                _waitingForProfile = false;
                ConnectionRejectionManager.SetReason("CharactersVault sync timeout: server did not send profile data in time.");
                Game.instance?.Disconnect();
            }
        }

        public static bool IsWaitingForProfile() => _waitingForProfile;
        public static bool IsInitializingFirstJoin() => IsFirstJoinInitializationActive;
        public static byte[]? GetServerProfile() => _serverProfileData;

        public static void Reset()
        {
            if (_profileTimeoutCoroutine != null && NetworkManager.Instance != null)
            {
                try { NetworkManager.Instance.StopCoroutine(_profileTimeoutCoroutine); } catch { }
                _profileTimeoutCoroutine = null;
            }
            _serverProfileData = null;
            _waitingForProfile = false;
            _serverImportAllowed = false;
            FirstJoinInitializationPending = false;
            IsFirstJoinInitializationActive = false;
        }

        /// <summary>
        /// Safely triggers a player profile save on Game.instance across different Valheim game versions.
        /// Handles both the 2-parameter signature SavePlayerProfile(bool setLogoutPoint, bool isFromRpc = false)
        /// introduced in recent Valheim updates and the legacy 1-parameter signature SavePlayerProfile(bool setLogoutPoint)
        /// on older server/game builds via reflection fallback.
        /// </summary>
        public static void SafeSavePlayerProfile(bool setLogoutPoint)
        {
            if (Game.instance == null) return;

            try
            {
                // Native compiled call against updated assembly: SavePlayerProfile(bool, bool)
                Game.instance.SavePlayerProfile(setLogoutPoint, false);
            }
            catch (MissingMethodException)
            {
                // Fallback for older server/client builds with SavePlayerProfile(bool)
                try
                {
                    var legacyMethod = typeof(Game).GetMethod("SavePlayerProfile", new[] { typeof(bool) });
                    if (legacyMethod != null)
                    {
                        legacyMethod.Invoke(Game.instance, new object[] { setLogoutPoint });
                        return;
                    }
                }
                catch (Exception ex)
                {
                    Plugin.Log.LogError($"[ClientProfilePatches] Legacy SavePlayerProfile invocation failed: {ex}");
                }
                throw;
            }
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
                var writer = new FileWriter(path, Splatform.CloudStorageFileGrouping.SameFileEnding, FileHelpers.FileHelperType.Binary, source);
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

        internal static void HandleFirstJoin(PlayerProfile profile, Player player)
        {
            if (profile == null || player == null) return;

            // Always create a safety net backup of the local .fch file before anything else
            DataStore.BackupLocalProfile(profile);

            if (_serverImportAllowed)
            {
                Plugin.Log.LogInfo($"[ClientProfilePatches] First-join character import permitted: adopting character '{profile.GetName()}' with current gear and skills.");
                profile.SavePlayerData(player);
                byte[] playerData = (byte[])Traverse.Create(profile).Field("m_playerData").GetValue();
                NetworkManager.Instance.SendProfileDataToServer(playerData, isPlayerData: true);
                return;
            }

            if (ModConfig.ProtectExistingCharacters.Value && HasExistingProgression(player))
            {
                Plugin.Log.LogWarning($"[ClientProfilePatches] Aborting join for '{profile.GetName()}': character has existing progression. Disconnecting to protect local save.");
                ConnectionRejectionManager.SetReason(
                    $"<color=#33CCFF>CharactersVault</color>\n\n" +
                    $"Character <b><color=#FFCC00>{profile.GetName()}</color></b> has existing progression!\n\n" +
                    $"To protect your character from being wiped, please create a new character to join this server."
                );
                Game.instance?.Disconnect();
                return;
            }

            ExecuteFirstJoinReset(profile, player);
        }

        public static bool HasExistingProgression(Player player)
        {
            if (player == null) return false;

            // 1. Check skills: any skill with level > 0.05 indicates played progress
            try
            {
                var skills = Traverse.Create(player).Field("m_skills").GetValue<Skills>();
                if (skills != null)
                {
                    var skillList = skills.GetSkillList();
                    if (skillList != null && skillList.Any(s => s.m_level > 0.05f))
                    {
                        Plugin.Log.LogInfo("[ClientProfilePatches] Existing progression detected: character has leveled skills.");
                        return true;
                    }
                }
            }
            catch { }

            // 2. Check inventory: starter character only has rags (ArmorRagsChest / ArmorRagsLegs)
            try
            {
                var inv = player.GetInventory();
                if (inv != null)
                {
                    var items = inv.GetAllItems();
                    if (items != null && items.Count > 0)
                    {
                        if (items.Count > 2)
                        {
                            Plugin.Log.LogInfo($"[ClientProfilePatches] Existing progression detected: inventory has {items.Count} items.");
                            return true;
                        }

                        foreach (var item in items)
                        {
                            string prefabName = item.m_dropPrefab?.name ?? "";
                            string sharedName = item.m_shared?.m_name ?? "";
                            bool isStarterRag = prefabName.IndexOf("ArmorRags", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                                sharedName.IndexOf("rag", StringComparison.OrdinalIgnoreCase) >= 0;
                            if (!isStarterRag)
                            {
                                Plugin.Log.LogInfo($"[ClientProfilePatches] Existing progression detected: non-starter item '{prefabName}'.");
                                return true;
                            }
                        }
                    }
                }
            }
            catch { }

            // 3. Check trophies, known materials, or known biomes
            try
            {
                var trophies = Traverse.Create(player).Field("m_trophies").GetValue<List<string>>();
                if (trophies != null && trophies.Count > 0)
                {
                    Plugin.Log.LogInfo("[ClientProfilePatches] Existing progression detected: trophies found.");
                    return true;
                }

                var materials = Traverse.Create(player).Field("m_knownMaterial").GetValue<HashSet<string>>();
                if (materials != null && materials.Count > 0)
                {
                    Plugin.Log.LogInfo("[ClientProfilePatches] Existing progression detected: known materials found.");
                    return true;
                }

                var biomes = Traverse.Create(player).Field("m_knownBiome").GetValue<HashSet<Heightmap.Biome>>();
                if (biomes != null && biomes.Count > 1)
                {
                    Plugin.Log.LogInfo("[ClientProfilePatches] Existing progression detected: explored biomes found.");
                    return true;
                }
            }
            catch { }

            return false;
        }

        internal static void ExecuteFirstJoinReset(PlayerProfile profile, Player player)
        {
            if (player == null || profile == null) return;

            IsFirstJoinInitializationActive = true;
            try
            {
                player.UnequipAllItems();
                player.GetInventory()?.RemoveAll();
                player.GiveDefaultItems();
                player.SetGuardianPower(string.Empty);
                Traverse.Create(player).Field("m_skills").GetValue<Skills>()?.Clear();
                player.m_customData?.Clear();
                ClearPlayerCollection(player, "m_foods");
                ClearPlayerCollection(player, "m_knownRecipes");
                ClearPlayerCollection(player, "m_knownStations");
                ClearPlayerCollection(player, "m_knownMaterial");
                ClearPlayerCollection(player, "m_shownTutorials");
                ClearPlayerCollection(player, "m_uniques");
                ClearPlayerCollection(player, "m_trophies");
                ClearPlayerCollection(player, "m_knownBiome");
                ClearPlayerCollection(player, "m_knownTexts");

                profile.SavePlayerData(player);
                byte[] playerData = (byte[])Traverse.Create(profile).Field("m_playerData").GetValue();
                NetworkManager.Instance.SendProfileDataToServer(playerData, isPlayerData: true);
                Plugin.Log.LogInfo("[ClientProfilePatches] Created initial clean player snapshot while preserving local appearance.");
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"[ClientProfilePatches] Failed to initialize first-join player state: {ex}");
            }
            finally
            {
                IsFirstJoinInitializationActive = false;
            }
        }

        private static void ClearPlayerCollection(Player player, string fieldName)
        {
            Traverse.Create(player).Field(fieldName).Method("Clear").GetValue();
        }

        private static FileHelpers.FileSource GetFileSource(PlayerProfile profile)
        {
            return (FileHelpers.FileSource)Traverse.Create(profile).Field("m_fileSource").GetValue();
        }

    }

    // ── Patch 0: Early RPC Registration on ZRoutedRpc ─────────────────────────

    [HarmonyPatch(typeof(ZRoutedRpc), "Awake")]
    public static class ZRoutedRpc_Awake_Patch
    {
        [HarmonyPostfix]
        public static void Postfix()
        {
            NetworkManager.Instance?.RegisterRPCs();
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
            // CRITICAL: Prevent wiping player's single-player or local server character!
            if (ZNet.instance == null || ZNet.instance.IsServer())
            {
                ClientProfilePatches.FirstJoinInitializationPending = false;
                return;
            }

            if (!ClientProfilePatches.FirstJoinInitializationPending) return;

            ClientProfilePatches.FirstJoinInitializationPending = false;
            ClientProfilePatches.HandleFirstJoin(__instance, player);
        }

        private static void ClearPlayerCollection(Player player, string fieldName)
        {
            Traverse.Create(player).Field(fieldName).Method("Clear").GetValue();
        }
    }

    // Hook Menu logout to initiate flush before socket teardown begins
    [HarmonyPatch]
    public static class Menu_OnQuit_SaveProfile_Patch
    {
        public static IEnumerable<MethodBase> TargetMethods()
        {
            foreach (MethodInfo method in AccessTools.GetDeclaredMethods(typeof(Menu)))
            {
                if (method.Name == "OnQuitConfirm" || method.Name == "OnQuitYes" || method.Name == "OnAbort")
                    yield return method;
            }
        }

        [HarmonyPrefix]
        public static void Prefix()
        {
            if (ZNet.instance != null && !ZNet.instance.IsServer() && Player.m_localPlayer != null)
            {
                try
                {
                    Plugin.Log.LogInfo("[ClientProfilePatches] Menu quit detected — flushing profile before disconnect.");
                    ClientSyncManager.Instance?.FlushImmediate("menu quit");
                    ClientProfilePatches.SafeSavePlayerProfile(true);
                }
                catch (Exception ex)
                {
                    Plugin.Log.LogError($"[ClientProfilePatches] Failed to save profile on menu quit: {ex}");
                }
            }
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
                ClientSyncManager.Instance?.FlushImmediate("disconnect flush");
                ClientProfilePatches.SafeSavePlayerProfile(true);
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

    // ── Instant Death Profile Flush (Anti-Dupe) ───────────────────────────────
    //
    // Closes the Alt+F4 tombstone duplication exploit: Player.OnDeath clears inventory
    // and drops a tombstone. Because live inventory sync is debounced by 2.5s, an Alt+F4
    // during death animation previously left the server with the pre-death snapshot.
    // Hooking OnDeath.Postfix flushes the empty inventory snapshot instantly (<1ms).
    [HarmonyPatch(typeof(Player), "OnDeath")]
    public static class Player_OnDeath_Flush_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(Player __instance)
        {
            if (ZNet.instance == null || ZNet.instance.IsServer() || __instance != Player.m_localPlayer)
                return;

            try
            {
                Plugin.Log.LogInfo("[ClientProfilePatches] Local player died — executing IMMEDIATE profile flush to prevent tombstone dupe.");
                ClientSyncManager.Instance?.FlushImmediate("player death");
                ClientProfilePatches.SafeSavePlayerProfile(false);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"[ClientProfilePatches] Failed to flush profile on player death: {ex}");
            }
        }
    }

    // ── Instant Tombstone Loot Flush (Anti-Void) ──────────────────────────────
    //
    // Prevents item voiding if the client crashes or disconnects right after looting
    // a tombstone. Flushes the newly recovered gear immediately so the server snapshot
    // matches the player's recovered inventory before the tombstone vanishes.
    [HarmonyPatch]
    public static class TombStone_Interact_Flush_Patch
    {
        public static IEnumerable<MethodBase> TargetMethods()
        {
            foreach (MethodInfo method in AccessTools.GetDeclaredMethods(typeof(TombStone)))
            {
                if (method.Name == "Interact")
                    yield return method;
            }
        }

        [HarmonyPostfix]
        public static void Postfix(TombStone __instance, Humanoid character)
        {
            if (ZNet.instance == null || ZNet.instance.IsServer() || character != Player.m_localPlayer)
                return;

            try
            {
                Plugin.Log.LogInfo("[ClientProfilePatches] Tombstone interact — executing IMMEDIATE profile flush to prevent item voiding.");
                ClientSyncManager.Instance?.FlushImmediate("tombstone interact");
                ClientProfilePatches.SafeSavePlayerProfile(false);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"[ClientProfilePatches] Failed to flush profile on tombstone interact: {ex}");
            }
        }
    }

    [HarmonyPatch]
    public static class Container_TakeAll_Tombstone_Flush_Patch
    {
        public static IEnumerable<MethodBase> TargetMethods()
        {
            foreach (MethodInfo method in AccessTools.GetDeclaredMethods(typeof(Container)))
            {
                if (method.Name == "TakeAll")
                    yield return method;
            }
        }

        [HarmonyPostfix]
        public static void Postfix(Container __instance, Humanoid character)
        {
            if (ZNet.instance == null || ZNet.instance.IsServer() || character != Player.m_localPlayer)
                return;

            if (__instance is TombStone)
            {
                try
                {
                    Plugin.Log.LogInfo("[ClientProfilePatches] Tombstone TakeAll — executing IMMEDIATE profile flush.");
                    ClientSyncManager.Instance?.FlushImmediate("tombstone take all");
                    ClientProfilePatches.SafeSavePlayerProfile(false);
                }
                catch (Exception ex)
                {
                    Plugin.Log.LogError($"[ClientProfilePatches] Failed to flush profile on tombstone take all: {ex}");
                }
            }
        }
    }
}
