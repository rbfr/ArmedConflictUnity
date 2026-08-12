using UnityEngine;

namespace ArmedConflict.Data
{
    /// <summary>
    /// Port of UnitDefinition.kt. Stats live HERE, never hardcoded at a call site — that is a
    /// standing convention in this project, and ScriptableObjects enforce it better than a
    /// Kotlin object did.
    /// </summary>
    [CreateAssetMenu(menuName = "ArmedConflict/Unit", fileName = "Unit")]
    public class UnitDefinitionSO : ScriptableObject
    {
        public string id;
        public string displayName;
        public string modelAsset;
        public string gunModelAsset;

        public int maxHp;
        public int damage;
        public ProjectileType projectileType = ProjectileType.Bullet;
        public BulletVariant bulletVariant = BulletVariant.Standard;
        public int projectilesPerVolley = 1;
        public float splashRadius = 0f;
        public float structureDamageMultiplier = 1f;
        public int meleeDamage = 0;

        /// <summary>
        /// ARMOUR. What fraction of an incoming round's damage this unit actually takes — 1 is
        /// unarmoured and every class but one is 1. It is the shield bearer's whole mechanic:
        /// `meleeDamage` is unported (nothing reads it, no SkirmishEntity is ever built, and a
        /// PLAYER unit's AdvancePerTurn is pinned to 0), so the class the store sold as "soaks
        /// the charge" was measurably a rifleman with more hp and less damage — the Tier 2.3
        /// audit's one real duplicate. This gives it a mechanic that does not wait on melee.
        ///
        /// Applied in CollisionSystem, the ONE place unit damage is written, and floored at 1 so
        /// armour can never make a unit immortal by rounding.
        /// </summary>
        public float damageTakenMultiplier = 1f;

        /// <summary>
        /// Hero-scale multiplier on the crowd unit size. 1.9 for heroes (was 1.35, which read as
        /// "a slightly big soldier"). Formation spacing scales with this so a bigger unit gets
        /// proportionally more room and does not overlap its neighbours.
        /// </summary>
        public float renderScale = 1f;
    }

    /// <summary>
    /// Port of UnitGeometry. UNIT_SCALE_UNITS is the SINGLE SOURCE OF TRUTH for crowd-unit size,
    /// in the same spirit as STRUCTURE_SCALE. Never re-author a derived value against a literal.
    /// </summary>
    public static class UnitGeometry
    {
        public const float UnitScaleUnits = 0.48f;      // was 0.77 before 2026-08-02
        public const float LegacyScaleRatio = UnitScaleUnits / 0.77f;
    }
}
