using System;
using System.Collections;
using System.IO;
using BepInEx;
using HarmonyLib;
using ServerCharacters.Models;
using ServerCharacters.Systems;
using UnityEngine;

namespace ServerCharacters.Patches
{
    // ═══════════════════════════════════════════════════════════════════════════════
    // CLIENT PROFILE SYNCHRONISATION
    //
    // Problem: Valheim's loading sequence on the client is:
    //   ZNet.Connect → ZNet.RPC_PeerInfo → Game.Start() → PlayerProfile.Load()
    //
    // Our mod needs to:
    //   1. Client receives handshake request from server after RPC_PeerInfo
    //   2. Client sends version reply → server sends profile bytes (chunked RPC)
    //   3. Client must receive ALL bytes BEFORE PlayerProfile.Load() runs
    //
    // Solution: Patch PlayerProfile.Load() with a Prefix that BLOCKS (returns false)
    // until _serverProfileData is set. A companion coroutine waits for the data and
    // then re-invokes Load() manually once it arrives. If a timeout elapses, we
    // disconnect with a clear error message.
    //
    // This only activates on clients connected to a dedicated server. Host players
    // and solo play are unaffected.
    // ═══════════════════════════════════════════════════════════════════════════════

    public static class ClientProfilePatches
    {
        // Profile data received from the server (null = not yet arrived)
        private static byte[]? _serverProfileData = null;

        // True during the window between requesting the profile and receiving it
        private static bool _waitingForProfile = false;

        // ── Public API (called by NetworkManager) ─────────────────────────────────

        /// <summary>
        /// Called by NetworkManager when the server's full profile bytes arrive.
        /// This unblocks the waiting coroutine so PlayerProfile.Load() can proceed.
        /// </summary>
        public static void ReceiveServerProfile(byte[] profileData)
        {
            Plugin.Log.LogInfo($"[ClientProfilePatches] Received {profileData.Length} bytes from server.");
            _serverProfileData = profileData;
            _waitingForProfile = false;
        }

        /// <summary>
        /// Called at the start of a server join flow to prime the wait state.
        /// Must be called before Game.Start fires.
        /// </summary>
        public static void ExpectServerProfile()
        {
            _serverProfileData = null;
            _waitingForProfile = true;
            Plugin.Log.LogInfo("[ClientProfilePatches] Waiting for server profile data...");
        }

        public static bool IsWaitingForProfile() => _waitingForProfile;
        public static byte[]? GetServerProfile()  => _serverProfileData;

        /// <summary>
        /// Resets all state. Called when the player leaves a server or the join fails.
        /// </summary>
        public static void Reset()
        {
            _serverProfileData = null;
            _waitingForProfile = false;
        }

        // ── Internal helpers ──────────────────────────────────────────────────────

        /// <summary>
        /// Writes the server-provided bytes to the character's .fch file on disk.
        /// PlayerProfile.Load() reads from disk, so we must write first.
        /// </summary>
        private static bool WriteServerDataToDisk(PlayerProfile profile, byte[] data)
        {
            if (data == null || data.Length == 0)
            {
                Plugin.Log.LogWarning("[ClientProfilePatches] Server sent empty profile — using as-is (blank character).");
                // An empty array means the server intentionally sent nothing (shouldn't happen
                // with our new blank profile generation, but handle it gracefully).
                return false;
            }

            try
            {
                string filename = (string)Traverse.Create(profile).Field("m_filename").GetValue();

                // Use Valheim's Utils class to get the correct save path
                var utilsType = typeof(Game).Assembly.GetType("Utils");
                if (utilsType == null) throw new Exception("Could not find Utils type in assembly.");

                string saveDataPath = (string)utilsType.GetMethod("GetSaveDataPath").Invoke(null, null);
                string path = System.IO.Path.Combine(saveDataPath, "characters", filename + ".fch");

                System.IO.File.WriteAllBytes(path, data);
                Plugin.Log.LogInfo($"[ClientProfilePatches] Wrote {data.Length} bytes to '{path}'.");
                return true;
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"[ClientProfilePatches] Failed to write server profile to disk: {ex}");
                return false;
            }
        }
    }

    // ── Patch 1: Game.Start — set up the wait state early ─────────────────────
    //
    // We set up our "waiting" flag HERE, before any RPC can fire, so that by the
    // time PlayerProfile.Load() is called we are already tracking the state.

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

    // ── Patch 2: PlayerProfile.Load — block until server data arrives ──────────
    //
    // If we are waiting for the server profile, we return false (skip the original)
    // and kick off a coroutine that will write the data to disk and re-invoke Load()
    // once it arrives. If the data never comes within the timeout, we disconnect.

    [HarmonyPatch(typeof(PlayerProfile), "Load")]
    public static class PlayerProfile_Load_Patch
    {
        [HarmonyPrefix]
        public static bool Prefix(PlayerProfile __instance)
        {
            // Not a client connecting to a dedicated server — run normally
            if (ZNet.instance == null || ZNet.instance.IsServer())
                return true;

            // Not in a server join flow (e.g. loading locally in main menu)
            if (!ClientProfilePatches.IsWaitingForProfile())
                return true;

            // Data already arrived before Load() was called (fast connection)
            byte[]? data = ClientProfilePatches.GetServerProfile();
            if (data != null)
            {
                Plugin.Log.LogInfo("[PlayerProfile_Load_Patch] Profile data already available — loading now.");
                WriteAndLoad(__instance, data);
                return false; // We called Load() ourselves inside WriteAndLoad
            }

            // Data not here yet — defer: spin up a coroutine and skip this Load() call.
            // The coroutine will call __instance.Load() once data arrives.
            Plugin.Log.LogInfo("[PlayerProfile_Load_Patch] Profile data not yet received. Deferring Load()...");
            Plugin.Instance.StartCoroutine(WaitAndLoadCoroutine(__instance));
            return false; // Skip the original Load() — coroutine will do it
        }

        private static IEnumerator WaitAndLoadCoroutine(PlayerProfile profile)
        {
            float elapsed = 0f;
            float timeout = ModConfig.ProfileSyncTimeoutSeconds.Value;

            Plugin.Log.LogInfo($"[WaitAndLoad] Waiting up to {timeout}s for server profile...");

            while (ClientProfilePatches.IsWaitingForProfile())
            {
                byte[]? incoming = ClientProfilePatches.GetServerProfile();
                if (incoming != null)
                    break; // Data arrived

                elapsed += Time.deltaTime;
                if (elapsed >= timeout)
                {
                    Plugin.Log.LogError($"[WaitAndLoad] Timed out after {timeout}s waiting for server profile. Disconnecting.");
                    DisconnectFromServer();
                    ClientProfilePatches.Reset();
                    yield break;
                }

                yield return null; // Wait one frame
            }

            byte[]? data = ClientProfilePatches.GetServerProfile();
            if (data == null)
            {
                // Shouldn't happen, but be safe
                Plugin.Log.LogError("[WaitAndLoad] Profile data is null after wait loop. Disconnecting.");
                DisconnectFromServer();
                ClientProfilePatches.Reset();
                yield break;
            }

            Plugin.Log.LogInfo($"[WaitAndLoad] Profile data received ({data.Length} bytes). Proceeding with load.");
            WriteAndLoad(profile, data);
        }

        private static void WriteAndLoad(PlayerProfile profile, byte[] data)
        {
            // Write server data to disk (if any)
            WriteServerDataToDisk(profile, data);

            // Now run the original PlayerProfile.Load() to read the file we just wrote
            // (or load the existing file if write was skipped due to empty data)
            try
            {
                profile.Load();
                Plugin.Log.LogInfo("[WaitAndLoad] PlayerProfile.Load() completed successfully.");
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"[WaitAndLoad] PlayerProfile.Load() threw after server data write: {ex}");
            }
        }

        /// <summary>
        /// Disconnects the client from the server. Uses GetServerPeer() to get the
        /// peer to disconnect, since ZNet.Disconnect() requires a ZNetPeer argument.
        /// </summary>
        private static void DisconnectFromServer()
        {
            try
            {
                if (ZNet.instance == null) return;
                var serverPeer = ZNet.instance.GetServerPeer();
                if (serverPeer != null)
                    ZNet.instance.Disconnect(serverPeer);
                else
                    Plugin.Log.LogWarning("[WaitAndLoad] Could not find server peer to disconnect.");
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"[WaitAndLoad] DisconnectFromServer failed: {ex.Message}");
            }
        }

        private static bool WriteServerDataToDisk(PlayerProfile profile, byte[] data)
        {
            if (data == null || data.Length == 0)
            {
                Plugin.Log.LogWarning("[WriteAndLoad] Server sent empty/null profile bytes. Loading existing local file.");
                return false;
            }

            try
            {
                string filename = (string)Traverse.Create(profile).Field("m_filename").GetValue();
                var utilsType = typeof(Game).Assembly.GetType("Utils");
                if (utilsType == null) throw new Exception("Could not find Utils type in assembly.");
                string saveDataPath = (string)utilsType.GetMethod("GetSaveDataPath").Invoke(null, null);
                string path = System.IO.Path.Combine(saveDataPath, "characters", filename + ".fch");

                System.IO.File.WriteAllBytes(path, data);
                Plugin.Log.LogInfo($"[WriteAndLoad] Wrote {data.Length} server bytes to '{path}'.");
                return true;
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"[WriteAndLoad] Failed to write server profile to disk: {ex}");
                return false;
            }
        }
    }

    // ── Patch 3: PlayerProfile.SavePlayerToDisk — upload to server on every save ─
    //
    // After Valheim writes the .fch file locally, we read it back and send it to
    // the server, which stores it as the new authoritative snapshot.

    [HarmonyPatch(typeof(PlayerProfile), "SavePlayerToDisk")]
    public static class PlayerProfile_SavePlayerToDisk_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(PlayerProfile __instance)
        {
            if (ZNet.instance == null || ZNet.instance.IsServer())
                return;

            Plugin.Log.LogInfo("[ClientProfilePatches] Player saved — uploading profile to server.");

            try
            {
                string filename = (string)Traverse.Create(__instance).Field("m_filename").GetValue();
                var utilsType = typeof(Game).Assembly.GetType("Utils");
                string saveDataPath = (string)utilsType.GetMethod("GetSaveDataPath").Invoke(null, null);
                string path = System.IO.Path.Combine(saveDataPath, "characters", filename + ".fch");

                if (File.Exists(path))
                {
                    byte[] profileBytes = File.ReadAllBytes(path);
                    NetworkManager.Instance.SendProfileDataToServer(profileBytes);
                }
                else
                {
                    Plugin.Log.LogWarning($"[ClientProfilePatches] Save file not found at '{path}', skipping upload.");
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"[ClientProfilePatches] Failed to upload saved profile to server: {ex}");
            }
        }
    }
}
