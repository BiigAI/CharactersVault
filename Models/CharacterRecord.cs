using System;

namespace ServerCharacters.Models
{
    /// <summary>
    /// Represents the permanent binding between a Steam ID and a registered character name.
    /// Once registered, the player may only join with this character name.
    /// </summary>
    public class CharacterRecord
    {
        /// <summary>Steam ID (SteamID64) of the player.</summary>
        public string PlayerId { get; set; }

        /// <summary>The character name bound to this Steam ID.</summary>
        public string CharacterName { get; set; } = string.Empty;

        /// <summary>UTC timestamp of when this binding was first created.</summary>
        public DateTime RegisteredAt { get; set; } = DateTime.UtcNow;

        /// <summary>UTC timestamp of the last successful join with this character.</summary>
        public DateTime LastSeenAt { get; set; } = DateTime.UtcNow;
    }
}
