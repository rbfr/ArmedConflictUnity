using System;
using System.Collections.Generic;
using UnityEngine;

namespace ArmedConflict.Data
{
    [Serializable]
    public class CannonSpec
    {
        public int damage;
        public float splashRadius;
        public float structureDamageMultiplier;
        public float muzzleOffsetX;
        public float muzzleOffsetY;
        public float velocityBoost = 1.12f;
        public int ammoPerBattle = 2;
    }

    /// <summary>
    /// One shed geometry group. Structures show damage by SHEDDING THEIR OWN GEOMETRY, not with
    /// a decal — the gap in the silhouette plus the pile at the foot IS the damage read.
    /// Measured with tools/measure_chunks.py, never off a bounding box.
    /// `pieces` records how many separate solids were joined into the group, so a sandbag course
    /// scatters as bags rather than dropping as one bar.
    /// </summary>
    [Serializable]
    public class DamageChunk
    {
        public float offsetX, offsetY, offsetZ;
        public float sizeX, sizeY, sizeZ;
        public int pieces = 1;
    }

    [Serializable]
    public class FlagMount
    {
        public float offsetX;   // toward the owner's rear (sign applied by the renderer)
        public float offsetY;   // up from the base = the visual roof/deck height
        public string model = "models/flag.glb";
        public float scale = 1f;
    }

    /// <summary>
    /// Port of StructureDefinition.kt.
    ///
    /// Every ENEMY structure is multiplied by StructureScale via Scaled(), which moves
    /// size/hitWidth/standWidth/deckStandZOffset/flagMount/cannon offsets/worldScale TOGETHER.
    /// No model carries the factor — tuning the look is one number. The player tank is
    /// deliberately NOT scaled.
    ///
    /// When a length starts scaling, grep for EVERY READER of the field, not just the writer:
    /// flagMount.scale was missed once (flags stayed small on structures that grew 2.5x), and
    /// standingYFor was missed once (garrisons embedded in the masonry of the tier below).
    /// </summary>
    [CreateAssetMenu(menuName = "ArmedConflict/Structure", fileName = "Structure")]
    public class StructureDefinitionSO : ScriptableObject
    {
        public const float StructureScale = 2.5f;

        public string id;
        public string displayName;
        public string modelAsset;
        public int maxHp;

        /// <summary>
        /// A LOGICAL height that also drives the collision box — never free to follow the roof.
        /// Where a garrison actually stands is deckY.
        /// </summary>
        public float size;

        public bool isPlayerSide = false;
        public bool modelAbsoluteScale = false;
        public float modelScaleUnits = 1f;
        public float standWidth = 0.6f;
        public float deckStandZOffset = 0f;

        /// <summary>Falls back to `size` for tall-narrow builds.</summary>
        public float hitWidth = -1f;
        public bool hasHitWidth = false;

        /// <summary>
        /// A garrison stands on deckY, and the deck is the largest UP-FACING surface — measured
        /// with tools/measure_decks.py, NEVER off a node's bounding box. A bbox top is as likely
        /// to be a chimney, a guard rail, a cupola or a damage chunk; that mistake stood four of
        /// five garrisons in mid-air. Falls back to `size` when unset.
        /// </summary>
        public float deckY = -1f;
        public bool hasDeckY = false;

        public float worldScale = 1f;

        public CannonSpec cannon;
        public bool hasCannon = false;
        public FlagMount flagMount;
        public bool hasFlagMount = false;

        public List<DamageChunk> damageChunks = new();
    }
}
