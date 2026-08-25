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
        /// Shoots on a FLAT, direct line instead of lobbing (2026-08-24, Rob on L5's tower
        /// sniper: *"make their shot more on a direct line instead of an arc"*).
        ///
        /// `EnemyAI.AimAt` normally rolls a 35-60 degree arc for everyone, and its own comment
        /// explains why: the flattest solve "reads as shooting directly at them", and a lobbed
        /// arc looks fairer. That reasoning is right for a rifleman and WRONG for a sniper —
        /// reading as a shot aimed straight at you is precisely what a sniper is supposed to be,
        /// and a marksman dropping mortar arcs on you is the wrong character entirely.
        ///
        /// It is a per-class trait rather than a per-level tweak because it is characterisation,
        /// not balance: any sniper anywhere should shoot like this.
        ///
        /// COSTS RANGE. A flat shot needs far more speed to cover the same ground, and
        /// `MaxEnemyLaunchSpeed` caps it — so a flat shooter placed too far back simply cannot
        /// reach and quietly stops mattering. PortSelfTest measures where the round actually
        /// LANDS, not the angle it left at, for exactly that reason.
        /// </summary>
        public bool flatTrajectory = false;

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
