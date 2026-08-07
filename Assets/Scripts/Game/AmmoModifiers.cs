using UnityEngine;
using ArmedConflict.Data;

namespace ArmedConflict.Game
{
    /// <summary>
    /// What an ammo type does to a volley, as plain numbers.
    ///
    /// `DYNAMISM_DESIGN.md` Phase A is explicit that the damage math must live in pure functions
    /// with tests rather than inside the untestable renderer class. This is that layer: a struct
    /// the tick can apply, projected from `AmmoSlot` once at fire time, so nothing in
    /// `BattleTick` has to hold a ScriptableObject or branch on an enum.
    ///
    /// `Standard` is the identity, which is what makes "no ammo is ever REQUIRED to clear a
    /// level" checkable rather than a promise — a level cleared with Standard is a level cleared
    /// with the identity modifier.
    /// </summary>
    public readonly struct AmmoModifiers
    {
        public readonly AmmoType Type;
        public readonly float UnitDamageScale;
        public readonly float StructureDamageScale;
        public readonly float SpreadScale;
        public readonly int BurnDamage;

        public AmmoModifiers(AmmoType type, float unitDamageScale, float structureDamageScale,
                             float spreadScale, int burnDamage)
        {
            Type = type;
            UnitDamageScale = unitDamageScale;
            StructureDamageScale = structureDamageScale;
            SpreadScale = spreadScale;
            BurnDamage = burnDamage;
        }

        /// <summary>The identity. Every field is a no-op, so Standard cannot change a volley.</summary>
        public static readonly AmmoModifiers Standard =
            new(AmmoType.Standard, 1f, 1f, 1f, 0);

        /// <summary>
        /// Read the catalogue, falling back to the identity. A MISSING slot must behave exactly
        /// like Standard rather than throwing: the catalogue is data, and a half-authored asset
        /// should make the ammo do nothing, not take the battle down.
        /// </summary>
        public static AmmoModifiers From(AmmoCatalogSO catalog, AmmoType type)
        {
            if (catalog == null) return Standard;
            var slot = catalog.Find(type);
            if (slot == null) return Standard;
            return new AmmoModifiers(slot.type,
                                     Mathf.Max(slot.unitDamageScale, 0f),
                                     Mathf.Max(slot.structureDamageScale, 0f),
                                     Mathf.Max(slot.spreadScale, 0f),
                                     Mathf.Max(slot.burnDamage, 0));
        }

        /// <summary>
        /// A round's damage against UNITS after this ammo.
        ///
        /// Rounded but FLOORED AT 1: a scale that rounded to zero would make a legally purchased
        /// ammo type do literally nothing against a soft target, which reads as a broken unlock
        /// rather than as a trade-off.
        /// </summary>
        public int UnitDamage(int baseDamage)
            => Mathf.Max(1, Mathf.RoundToInt(baseDamage * UnitDamageScale));

        /// <summary>
        /// A round's structure multiplier after this ammo. STACKS on the unit's own multiplier,
        /// so a rocket trooper firing AP is deliberately the best masonry answer in the game —
        /// that combination is the reward for owning both.
        ///
        /// DIVIDING BY UnitDamageScale IS LOAD BEARING, and it is not a fudge. The engine
        /// computes structure damage as `Damage * StructureDamageMultiplier`
        /// (`CollisionSystem`), and this ammo has already scaled `Damage` down for soft targets
        /// — so without the division AP's two knobs MULTIPLY instead of acting independently and
        /// its real masonry effect is 0.6 * 2 = 1.2x, not 2x.
        ///
        /// Caught on device, not in the tests that passed: firing AP at L12's 165hp citadel took
        /// 128 off it where the intent was ~192. The two scales are meant to be read against the
        /// BASE round — "0.6x to men, 2x to walls" — and this is what makes that true.
        /// </summary>
        public float StructureMultiplier(float baseMultiplier)
            => baseMultiplier * StructureDamageScale / Mathf.Max(UnitDamageScale, 0.01f);
    }
}
