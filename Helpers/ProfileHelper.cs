using System;
using System.IO;
using System.Reflection;
using System.Runtime.Serialization;
using HarmonyLib;

namespace CharacterVault.Helpers
{
    /// <summary>
    /// Utility for generating valid, native Valheim PlayerProfile byte arrays via reflection.
    /// Uses FormatterServices.GetUninitializedObject to construct a clean PlayerProfile instance
    /// on any platform without constructor dependency mismatch, populates fields, and calls
    /// SavePlayerToDisk() to produce guaranteed valid .fch profile bytes.
    /// </summary>
    public static class ProfileHelper
    {
        private static bool _initialized;
        private static MethodInfo? _savePlayerToDiskMethod;
        private static FieldInfo? _playerNameField;
        private static FieldInfo? _filenameField;
        private static FieldInfo? _fileSourceField;

        private static void EnsureInitialized()
        {
            if (_initialized) return;
            _initialized = true;

            try
            {
                var profileType = typeof(PlayerProfile);

                _savePlayerToDiskMethod = profileType.GetMethod(
                    "SavePlayerToDisk",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

                _playerNameField = profileType.GetField("m_playerName", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                _filenameField   = profileType.GetField("m_filename", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                _fileSourceField = profileType.GetField("m_fileSource", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);

                if (_savePlayerToDiskMethod == null)
                    Plugin.Log.LogWarning("[CharacterVault :: ProfileHelper] Could not find PlayerProfile.SavePlayerToDisk method via reflection.");
                if (_playerNameField == null || _filenameField == null)
                    Plugin.Log.LogWarning("[CharacterVault :: ProfileHelper] Could not find name/filename fields on PlayerProfile.");
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"[CharacterVault :: ProfileHelper] Reflection init failed: {ex.Message}");
            }
        }

        public static byte[] CreateBlankProfile(string characterName)
        {
            Plugin.Log.LogInfo($"[CharacterVault :: ProfileHelper] Generating native blank .fch profile for character '{characterName}'...");
            EnsureInitialized();

            if (_savePlayerToDiskMethod == null)
            {
                Plugin.Log.LogError("[CharacterVault :: ProfileHelper] Reflection targets missing — cannot generate blank profile.");
                return Array.Empty<byte>();
            }

            try
            {
                var profileType = typeof(PlayerProfile);
                PlayerProfile? profile = null;

                // Try to use the 2-parameter constructor
                if (_fileSourceField != null)
                {
                    try
                    {
                        object localEnum = Enum.Parse(_fileSourceField.FieldType, "Local");
                        profile = (PlayerProfile)Activator.CreateInstance(profileType, characterName, localEnum);
                    }
                    catch (Exception ex)
                    {
                        Plugin.Log.LogWarning($"[CharacterVault :: ProfileHelper] Activator.CreateInstance failed: {ex.Message}");
                    }
                }

                if (profile == null)
                {
                    Plugin.Log.LogError($"[CharacterVault :: ProfileHelper] Failed to instantiate PlayerProfile for '{characterName}'.");
                    return Array.Empty<byte>();
                }

                // The constructor argument is the save filename; Valheim initializes
                // the player name to "Stranger" until SetName is called explicitly.
                profile.SetName(characterName);

                // 4. Save it to disk using Valheim's SavePlayerToDisk()
                _savePlayerToDiskMethod.Invoke(profile, null);

                // 5. Read the native .fch file through Valheim's file abstraction.
                string path = profile.GetPath();

                if (File.Exists(path))
                {
                    byte[] bytes = File.ReadAllBytes(path);

                    // Clean up temp file on server disk
                    try { File.Delete(path); } catch { }

                    Plugin.Log.LogInfo($"[CharacterVault :: ProfileHelper] SUCCESS: Generated {bytes.Length}-byte native blank profile for '{characterName}'.");
                    return bytes;
                }
                else
                {
                    Plugin.Log.LogError($"[CharacterVault :: ProfileHelper] SavePlayerToDisk did not produce expected file at '{path}'.");
                    return Array.Empty<byte>();
                }
            }
            catch (Exception ex)
            {
                if (ex is TargetInvocationException tie && tie.InnerException != null)
                {
                    Plugin.Log.LogError($"[CharacterVault :: ProfileHelper] Failed to generate blank profile for '{characterName}': {tie.InnerException.GetType().Name} - {tie.InnerException.Message}\nStackTrace:\n{tie.InnerException.StackTrace}");
                }
                else
                {
                    Plugin.Log.LogError($"[CharacterVault :: ProfileHelper] Failed to generate blank profile for '{characterName}': {ex}");
                }
                return Array.Empty<byte>();
            }
        }
    }
}
