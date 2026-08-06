using System.Collections.Generic;
using UnityEngine;

namespace ArmedConflict.Data
{
    /// <summary>One pickable unit: what it costs to field, and what it costs to own.</summary>
    [System.Serializable]
    public class RosterSlot
    {
        public UnitDefinitionSO unit;

        /// <summary>
        /// Points to field ONE of these, spent against the level's `deployBudget`.
        ///
        /// The Rifleman is 1 and everything else is dearer, which is what makes the budget a
        /// QUALITY decision rather than a quantity one — see the class comment.
        /// </summary>
        public int pointCost = 1;

        /// <summary>Coins to unlock permanently. 0 = free from the start.</summary>
        public int coinPrice = 0;

        [TextArea(2, 4)]
        public string oneLiner = "";
    }

    /// <summary>
    /// The loadout picker's menu.
    ///
    /// SLOTS AND POINTS ARE SEPARATE, and that separation is load bearing:
    ///
    /// - **Slots** = the number of ground troops the level was authored with. Fixed, because
    ///   composition rule 1 measures the PLAYER LINE'S WIDTH and the aiming camera is framed on
    ///   it. A loadout that could field more bodies than the level was drawn for would zoom the
    ///   camera out, and no other part of the layout can compensate for that.
    /// - **Points** = `LevelDefinition.deployBudget`, and they buy QUALITY. Eight slots with eight
    ///   points is eight riflemen; eight slots with sixteen is four heavies and four riflemen, or
    ///   two snipers and six riflemen, and so on.
    ///
    /// So the squad never gets wider as the campaign goes on — it gets better. That keeps every
    /// authored level framed exactly as it was measured, while still making the budget a real
    /// decision, and it stays inside the locked 7-30 scale by construction.
    ///
    /// The tank crew is NOT part of this. It is level geometry — it stands on a structure at a
    /// fixed anchor — and swapping it would move a garrison, not a squad.
    /// </summary>
    [CreateAssetMenu(menuName = "ArmedConflict/Roster", fileName = "Roster")]
    public class RosterDefinitionSO : ScriptableObject
    {
        public List<RosterSlot> slots = new();

        /// <summary>Costs for tier 1, 2, 3. Shared by every unit, matching the ported EconomyStore.</summary>
        public List<int> tierCosts = new() { 150, 350, 700 };
    }
}
