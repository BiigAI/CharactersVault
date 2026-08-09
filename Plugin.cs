using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using CharacterVault.Helpers;
using CharacterVault.Systems;
using UnityEngine;

namespace CharacterVault
{
    [BepInPlugin(Plugin.ModGuid, Plugin.ModName, Plugin.ModVersion)]
    public class Plugin : BaseUnityPlugin
    {
        public const string ModGuid    = "com.charactervault.valheim";
        public const string ModName    = "CharactersVault";
        public const string ModVersion = "2.3.0";

        /// <summary>Static reference for accessing the plugin instance (e.g. for StartCoroutine).</summary>
        public static Plugin Instance { get; private set; } = null!;

        /// <summary>Shared logger. Use Plugin.Log.LogInfo / LogWarning / LogError throughout the mod.</summary>
        public static ManualLogSource Log { get; private set; } = null!;

        private Harmony? _harmony;

        private void Awake()
        {
            Instance = this;
            Log = Logger;

            try
            {
                Log.LogInfo($"══════════════════════════════════════════");
                Log.LogInfo($"  {ModName} v{ModVersion} loading...");
                Log.LogInfo($"══════════════════════════════════════════");

                // 1. Load config (applies on both server and client)
                ModConfig.Initialize(Config);

                // 2. Server-only: initialize file storage and load persisted data
                DataStore.Initialize();
                BindingManager.Load();

                // 3. Apply all Harmony patches
                _harmony = new Harmony(ModGuid);
                _harmony.PatchAll();

                Log.LogInfo($"[{ModName}] All Harmony patches applied.");
                Log.LogInfo($"[{ModName}] Loaded successfully. Waiting for network initialization...");

                // 4. Initialize persistent MonoBehaviour managers
                NetworkManager.Initialize();
                ClientSyncManager.Initialize();
            }
            catch (Exception ex)
            {
                Log.LogError($"[{ModName}] FATAL: Failed to initialize — {ex}");
            }
        }

        private void Start()
        {
            // Initialization is handled in Awake
        }

        private void OnDestroy()
        {
            _harmony?.UnpatchSelf();
            Log.LogInfo($"[{ModName}] Unloaded.");
        }
    }
}
