using System;
using HarmonyLib;
using UnityEngine;

namespace CharacterVault.Patches
{
    /// <summary>
    /// Displays a prominent warning banner on the character selection screen to remind
    /// players that joining a CharactersVault server will wipe existing character progression.
    /// </summary>
    [HarmonyPatch(typeof(FejdStartup))]
    public static class MenuWarningPatch
    {
        private static GameObject? _warningBanner;

        private const string WarningText =
            "<color=#FFCC00><b>CharactersVault:</b> Joining a server with this mod will wipe your selected character's items & skills!\n" +
            "Create a new character, and use that character on the server.</color>";

        [HarmonyPatch("SetupGui")]
        [HarmonyPostfix]
        public static void SetupGui_Postfix(FejdStartup __instance)
        {
            EnsureWarningBanner(__instance);
        }

        [HarmonyPatch("ShowCharacterSelection")]
        [HarmonyPostfix]
        public static void ShowCharacterSelection_Postfix(FejdStartup __instance)
        {
            EnsureWarningBanner(__instance);
        }

        private static void EnsureWarningBanner(FejdStartup startup)
        {
            try
            {
                if (!ModConfig.ShowCharacterSelectWarning.Value)
                {
                    if (_warningBanner != null)
                    {
                        _warningBanner.SetActive(false);
                    }
                    return;
                }

                if (_warningBanner != null)
                {
                    _warningBanner.SetActive(true);
                    return;
                }

                if (startup == null || startup.m_selectCharacterPanel == null)
                {
                    return;
                }

                // Locate a template TMP_Text component to clone font, material, and canvas setup
                Component? templateComponent = Traverse.Create(startup).Field("m_csSourceInfo").GetValue<Component>()
                    ?? Traverse.Create(startup).Field("m_csName").GetValue<Component>()
                    ?? Traverse.Create(startup).Field("m_versionLabel").GetValue<Component>();

                if (templateComponent == null)
                {
                    Plugin.Log?.LogWarning("[MenuWarningPatch] Could not find reference text component to create character select warning banner.");
                    return;
                }

                Transform parentTransform = startup.m_characterSelectScreen != null
                    ? startup.m_characterSelectScreen.transform
                    : startup.m_selectCharacterPanel.transform;

                _warningBanner = UnityEngine.Object.Instantiate(templateComponent.gameObject, parentTransform, false);
                _warningBanner.name = "CharactersVault_SelectWarningBanner";

                RectTransform? rt = _warningBanner.GetComponent<RectTransform>();
                if (rt != null)
                {
                    rt.anchorMin = new Vector2(0.5f, 1f);
                    rt.anchorMax = new Vector2(0.5f, 1f);
                    rt.pivot = new Vector2(0.5f, 1f);
                    rt.anchoredPosition = new Vector2(0f, -40f);
                    rt.sizeDelta = new Vector2(950f, 60f);
                }

                Component? tmpText = _warningBanner.GetComponent("TMP_Text") ?? _warningBanner.GetComponent("TextMeshProUGUI");
                if (tmpText != null)
                {
                    var trav = Traverse.Create(tmpText);
                    trav.Property("text").SetValue(WarningText);
                    trav.Property("fontSize").SetValue(15f);
                    trav.Property("alignment").SetValue(2); // Center
                    trav.Property("enableWordWrapping").SetValue(true);
                    trav.Property("richText").SetValue(true);
                }

                _warningBanner.SetActive(true);
                Plugin.Log?.LogInfo("[MenuWarningPatch] Character select warning banner created successfully.");
            }
            catch (Exception ex)
            {
                Plugin.Log?.LogWarning($"[MenuWarningPatch] Failed to create character select warning banner: {ex.Message}");
            }
        }
    }
}
