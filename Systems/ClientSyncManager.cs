using System;
using System.Collections;
using CharacterVault.Models;
using UnityEngine;

namespace CharacterVault.Systems
{
    public class ClientSyncManager : MonoBehaviour
    {
        public static ClientSyncManager Instance { get; private set; } = null!;

        private Coroutine? _syncCoroutine;

        public static void Initialize()
        {
            var go = new GameObject("CharacterVault_ClientSyncManager");
            Instance = go.AddComponent<ClientSyncManager>();
            DontDestroyOnLoad(go);
        }

        private void Start()
        {
            _syncCoroutine = StartCoroutine(PeriodicSyncCoroutine());
        }

        private void OnDestroy()
        {
            if (_syncCoroutine != null)
                StopCoroutine(_syncCoroutine);

        }

        public void QueueSnapshotUpdate(string reason)
        {
            if (ZNet.instance == null || ZNet.instance.IsServer() ||
                Patches.ClientProfilePatches.IsWaitingForProfile() ||
                Patches.ClientProfilePatches.IsInitializingFirstJoin())
                return;

            try
            {
                byte[] playerData = Patches.ClientProfilePatches.CaptureLivePlayerData();
                if (playerData.Length == 0) return;

                Plugin.Log.LogInfo($"[ClientSyncManager] Uploading live player-data checkpoint: {reason}.");
                NetworkManager.Instance.SendProfileDataToServer(playerData, isPlayerData: true);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"[ClientSyncManager] Failed to upload live player-data checkpoint: {ex}");
            }
        }

        private IEnumerator PeriodicSyncCoroutine()
        {
            while (true)
            {
                float intervalSeconds = ModConfig.AutoSaveIntervalMinutes.Value * 60f;
                yield return new WaitForSeconds(intervalSeconds);

                try
                {
                    // Only run on the client when connected to a server
                    if (ZNet.instance == null || ZNet.instance.IsServer() || Game.instance == null)
                        continue;

                    Plugin.Log.LogInfo("[ClientSyncManager] Running periodic profile sync to server.");
                    
                    // Game.instance.SavePlayerProfile(true) triggers the SavePlayerToDisk which we intercept
                    Game.instance.SavePlayerProfile(true);
                }
                catch (Exception ex)
                {
                    Plugin.Log.LogError($"[ClientSyncManager] Exception during periodic sync: {ex}");
                }
            }
        }
    }
}
