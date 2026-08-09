using System;

namespace CharacterVault.Models
{
    /// <summary>
    /// Represents the permanent binding between a platform ID and a registered character name.
    /// Once registered, the player may only join with this character name.
    /// </summary>
    public class CharacterRecord
    {
        /// <summary>Platform ID reported by Valheim, such as Steam_... or Xbox_....</summary>
        public string PlayerId { get; set; }

        /// <summary>The character name bound to this platform ID.</summary>
        public string CharacterName { get; set; } = string.Empty;

        /// <summary>UTC timestamp of when this binding was first created.</summary>
        public DateTime RegisteredAt { get; set; } = DateTime.UtcNow;

        /// <summary>UTC timestamp of the last successful join with this character.</summary>
        public DateTime LastSeenAt { get; set; } = DateTime.UtcNow;
    }
}
