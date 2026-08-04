using System;
using System.Reflection;

namespace ServerCharacters.Helpers
{
    /// <summary>
    /// Utility for generating valid Valheim PlayerProfile byte arrays via reflection.
    ///
    /// We cannot call PlayerProfile methods directly at compile time because the
    /// constructor signature depends on FileHelpers types from assembly_utils.dll which
    /// is not in libs. Instead we use reflection to call the same constructor and
    /// serialization method at runtime when all assemblies are loaded by the game.
    ///
    /// The blank profile format is produced by constructing a default PlayerProfile
    /// and serializing it with SavePlayerData(), mirroring exactly what Valheim does
    /// when writing a .fch file. This guarantees the bytes are always valid for the
    /// currently-running version of the game.
    /// </summary>
    public static class ProfileHelper
    {
        // Cached reflection info — resolved once on first call
        private static bool _initialized;
        private static ConstructorInfo? _profileCtor;
        private static MethodInfo? _savePlayerData;

        private static void EnsureInitialized()
        {
            if (_initialized) return;
            _initialized = true;

            try
            {
                var profileType = typeof(PlayerProfile);

                // PlayerProfile(string name) — the simplest constructor
                _profileCtor = profileType.GetConstructor(
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance,
                    null,
                    new[] { typeof(string) },
                    null);

                // void SavePlayerData(ZPackage pkg) — serializes the profile to a ZPackage
                _savePlayerData = profileType.GetMethod(
                    "SavePlayerData",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance,
                    null,
                    new[] { typeof(ZPackage) },
                    null);

                if (_profileCtor == null)
                    Plugin.Log.LogWarning("[ProfileHelper] Could not find PlayerProfile(string) constructor via reflection.");
                if (_savePlayerData == null)
                    Plugin.Log.LogWarning("[ProfileHelper] Could not find PlayerProfile.SavePlayerData(ZPackage) via reflection.");
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"[ProfileHelper] Reflection init failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Creates the raw bytes for a blank, freshly-created character profile
        /// with the given name. Returns an empty array if profile generation fails,
        /// in which case the client falls back to their existing local file.
        /// </summary>
        public static byte[] CreateBlankProfile(string characterName)
        {
            EnsureInitialized();

            if (_profileCtor == null || _savePlayerData == null)
            {
                Plugin.Log.LogWarning("[ProfileHelper] Reflection not available — sending empty profile (client will use local file).");
                return Array.Empty<byte>();
            }

            try
            {
                // Construct a fresh PlayerProfile with the given name
                object profile = _profileCtor.Invoke(new object[] { characterName });

                // Serialize it into a ZPackage (same as what Valheim does for .fch)
                var pkg = new ZPackage();
                _savePlayerData.Invoke(profile, new object[] { pkg });

                byte[] bytes = pkg.GetArray();
                Plugin.Log.LogInfo($"[ProfileHelper] Generated blank profile for '{characterName}' ({bytes.Length} bytes).");
                return bytes;
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"[ProfileHelper] Failed to generate blank profile: {ex.Message}");
                return Array.Empty<byte>();
            }
        }
    }
}
