using System.Collections.Generic;
using System.Text;

namespace ServerCharacters.Models
{
    /// <summary>
    /// The result of comparing a player's current ZDO state against their stored snapshot.
    /// A non-null report with HasChanges == true indicates potential offline progression.
    /// </summary>
    public class MismatchReport
    {
        /// <summary>Whether the raw inventory bytes differ from the snapshot.</summary>
        public bool InventoryChanged { get; set; }

        /// <summary>Whether the raw skill bytes differ from the snapshot.</summary>
        public bool SkillsChanged { get; set; }

        /// <summary>True if either inventory or skills changed.</summary>
        public bool HasChanges => InventoryChanged || SkillsChanged;

        /// <summary>Human-readable summary of what changed, for use in logs and kick messages.</summary>
        public string Summary
        {
            get
            {
                var parts = new List<string>();
                if (InventoryChanged) parts.Add("inventory");
                if (SkillsChanged) parts.Add("skills");

                return parts.Count == 0
                    ? "No changes detected"
                    : $"Mismatch detected in: {string.Join(", ", parts)}";
            }
        }

        public override string ToString() => Summary;
    }
}
