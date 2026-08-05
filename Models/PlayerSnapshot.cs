using System;

namespace CharacterVault.Models
{
    /// <summary>
    /// A point-in-time snapshot of a player's character state captured from server-side ZDO data.
    /// Stored as raw base64-encoded ZPackage bytes to remain agnostic of Valheim version changes.
    /// </summary>
    public class PlayerSnapshot
    {
        /// <summary>Steam ID (SteamID64) of the player this snapshot belongs to.</summary>
        public string PlayerId { get; set; }

        /// <summary>Character name at snapshot time (for audit / display).</summary>
        public string CharacterName { get; set; } = string.Empty;

        /// <summary>UTC timestamp when this snapshot was captured.</summary>
        public DateTime SnapshotTime { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Base64-encoded raw bytes of the full PlayerProfile.
        /// This is the serialized ZPackage that Valheim uses to store the .fch file.
        /// A null or empty string means no profile data was available (e.g. fresh character).
        /// </summary>
        public string ProfileDataBase64 { get; set; } = string.Empty;

        /// <summary>True when the bytes are Player.Save(ZPackage) data rather than a full .fch file.</summary>
        public bool IsPlayerData { get; set; }

        /// <summary>Convenience: decode profile bytes.</summary>
        public byte[] GetProfileBytes() =>
            string.IsNullOrEmpty(ProfileDataBase64)
                ? Array.Empty<byte>()
                : Convert.FromBase64String(ProfileDataBase64);

        /// <summary>True if this snapshot has any usable data.</summary>
        public bool HasData => !string.IsNullOrEmpty(ProfileDataBase64);
    }
}
