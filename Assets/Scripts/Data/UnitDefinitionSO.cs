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
