using UnityEngine;

namespace ArmedConflict.Data
{
    /// <summary>
    /// The enemy army's identity for one stage — PRODUCT_DIRECTION Tier 2.1, DYNAMISM_DESIGN
    /// Phase D1. "I hate that army" is the beat: the same soldiers in a different uniform, so a
    /// stage change is visible in one frame rather than only in the level name.
    ///
    /// WHAT A FACTION MAY TOUCH IS DELIBERATELY NARROW, and it is the enemy's SIDE colours only:
    /// the uniform and the gear. It carries no stats and no behaviour.
    ///  - the per-class TRIM is shared across both armies on purpose — the uniform says which
    ///    side a soldier is on and the trim says which class he is, and letting a faction repaint
    ///    the trim collapses the two readings into one (see SpikeSceneBattle.TrimMat)
    ///  - SKIN is shared flesh, for the obvious reason
    ///  - STRUCTURES are untouched: their palette already carries more information (building TYPE)
    ///    than a flat faction tint would add
    ///  - the PLAYER never changes. Player colour is the cosmetics feature (Tier 2.4), and two
    ///    systems repainting the same army is how the Kotlin build got a permanently stale uniform
    ///
    /// The colours are authored for LIT 3D materials, which is why <see cref="bannerColor"/>
    /// exists separately: a uniform tone chosen to read under the directional light comes out
    /// muddy as flat HUD text.
    /// </summary>
    [CreateAssetMenu(menuName = "ArmedConflict/Faction", fileName = "Faction")]
    public class FactionDefinitionSO : ScriptableObject
    {
        public string id;

        /// <summary>Shown to the player as "Enemy: &lt;this&gt;" on the level card.</summary>
        public string displayName;

        /// <summary>The tone every mesh that is not skin, trim or accent wears.</summary>
        public Color uniformColor = new Color(0.52f, 0.20f, 0.18f);

        /// <summary>The `accent*` meshes — webbing, helmet, boots. Darker than the uniform.</summary>
        public Color gearColor = new Color(0.20f, 0.14f, 0.13f);

        /// <summary>HUD-only. Brighter than the uniform, because flat text is unlit.</summary>
        public Color bannerColor = new Color(0.90f, 0.36f, 0.32f);
    }
}
