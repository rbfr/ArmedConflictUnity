using System.Collections.Generic;
using UnityEngine;

namespace ArmedConflict.Data
{
    /// <summary>
    /// One ammo type: what it costs to own, and what it does to a volley.
    ///
    /// Every gameplay number lives HERE rather than in the tick — the locked convention is that
    /// stats live in Definition classes. `AmmoModifiers` in ArmedConflict.Game is the pure,
    /// testable projection of this, so the damage math never has to touch a ScriptableObject.
    /// </summary>
    [System.Serializable]
    public class AmmoSlot
    {
        public AmmoType type = AmmoType.Standard;
        public string displayName = "";

        [TextArea(2, 3)]
        public string oneLiner = "";

        /// <summary>Coins to unlock permanently. 0 = free from the start (Standard only).</summary>
        public int coinPrice = 0;

        /// <summary>
        /// Multiplies each round's damage AGAINST UNITS. Below 1 for the types that buy their
        /// advantage elsewhere — Cluster spreads wider and AP trades soft-target damage for
        /// masonry. `PRODUCT_DIRECTION.md` is explicit that no ammo may be REQUIRED to clear a
        /// level, so nothing here may make Standard strictly worse.
        /// </summary>
        public float unitDamageScale = 1f;

        /// <summary>
        /// Multiplies the round's structureDamageMultiplier. This is AP's whole point, and it
        /// stacks on the UNIT's own multiplier — a rocket trooper firing AP is deliberately the
        /// best masonry answer in the game.
        /// </summary>
        public float structureDamageScale = 1f;

        /// <summary>
        /// Scales the per-shooter jitter FireVolley already applies, which is what "wider target
        /// zone" means mechanically. It is still convergent fire at real targets — a blind fan is
        /// forbidden by the lock, and widening the existing spread is the shipped way to say
        /// "more enemies hit, each one lighter".
        /// </summary>
        public float spreadScale = 1f;

        /// <summary>
        /// Incendiary only. Damage applied ONCE to each surviving unit hit this turn, at the
        /// handover into the enemy's windup. A single legible tick, deliberately not a
        /// per-second DoT: `DYNAMISM_DESIGN.md` asks for one damage event the player can read,
        /// and a DoT would also need a new per-tick damage pipeline.
        ///
        /// NOTE the trap recorded in HANDOVER: 6 was calibrated to finish an 8hp Sniper that
        /// no longer exists (the roster cut gave the Sniper 16hp), so a tick is a chip rather
        /// than a kill. Judge it against the frailest unit in the CURRENT roster.
        /// </summary>
        public int burnDamage = 0;
    }

    /// <summary>
    /// The ammo selector's menu, and the source of truth for what each type does.
    ///
    /// One free permanent choice per turn (`DYNAMISM_DESIGN.md` Phase A): the player switches
    /// before a drag, the selection persists across turns and battles, and the drag itself is
    /// completely unchanged — the mechanic stays "guess the angle and power", and ammo changes
    /// what the volley DOES when it arrives.
    ///
    /// The ENEMY always fires Standard. Giving a faction a signature ammo is a later phase.
    /// </summary>
    [CreateAssetMenu(menuName = "ArmedConflict/Ammo Catalog", fileName = "AmmoCatalog")]
    public class AmmoCatalogSO : ScriptableObject
    {
        public List<AmmoSlot> slots = new();

        public AmmoSlot Find(AmmoType type)
        {
            foreach (var s in slots) if (s.type == type) return s;
            return null;
        }
    }
}
