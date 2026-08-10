using UnityEngine;
using ArmedConflict.Data;
using ArmedConflict.Game;

namespace ArmedConflict.Render
{
    /// <summary>
    /// Where a burning unit's flame goes and how big it is — the placement half of the incendiary
    /// cue, with the flicker itself in <see cref="CosmeticSystems"/>.
    ///
    /// It lives here rather than inside BattleRunner so that FlamePreview can render the real
    /// thing. A preview that re-implements the placement is testing a different flame than the one
    /// that ships, and this project has already paid for exactly that: BackdropPreview was a
    /// hand-copied second implementation and spent a whole session rendering plausible,
    /// wrong pictures. Same code, or no preview.
    /// </summary>
    public static class FlameRig
    {
        /// <summary>
        /// Body-relative, like everything else here. It stands slightly TALLER than the unit so
        /// the tips lick past the helmet — a flame capped at body height reads as a coat.
        /// </summary>
        public const float Height = 1.05f * UnitGeometry.UnitScaleUnits;
        public const float Width = 0.76f * UnitGeometry.UnitScaleUnits;

        /// <summary>
        /// Toward the camera (game z is larger nearer), so the fire is IN FRONT of the man rather
        /// than z-fighting his chest.
        /// </summary>
        public const float ZOffset = 0.03f;

        /// <summary>The inner tongue, as a fraction of the outer.</summary>
        public const float InnerScale = 0.54f;

        /// <summary>
        /// How long a flame takes to die once its unit stops burning.
        ///
        /// It does not simply stop being drawn. The burn resolves on a single frame, so membership
        /// of the burning set ends on one — and a bright orange object vanishing in one frame is
        /// the exact artefact this project has already paid for twice (the health bar and the
        /// backdrop layer both held full strength and then blinked out). Fire also dies
        /// asymmetrically: it catches instantly and gutters out, so there is no matching fade IN.
        /// </summary>
        public const float OutSeconds = 0.5f;

        /// <summary>
        /// Poses one flame. `root` carries a 180-degree turn about Y that faces the shared quad at
        /// the camera; being about Y it mirrors the HORIZONTAL only, so local up is still world up.
        /// </summary>
        public static void Place(Transform root, Transform outer, Transform inner,
                                 float gameX, float gameY, float gameZ,
                                 float unitScale, float time, int unitId)
        {
            // At the unit's FEET — the fire is standing where he is, and a garrison stands on a
            // deck, so this follows the entity's own y rather than the world floor.
            root.position = GameSpace.ToUnity(gameX, gameY, gameZ + ZOffset);

            float phase = CosmeticSystems.FlamePhase(unitId);
            Tongue(outer, CosmeticSystems.FlameScale(time, phase, false), unitScale, 1f);
            Tongue(inner, CosmeticSystems.FlameScale(time, phase, true), unitScale, InnerScale);
        }

        static void Tongue(Transform t, Vector2 flicker, float unitScale, float tongue)
        {
            float h = Height * unitScale * tongue * flicker.y;
            t.localScale = new Vector3(Width * unitScale * tongue * flicker.x, h, 1f);
            // The quad's pivot is its CENTRE, so anchoring the flame at the unit's feet means
            // lifting it by half its own height — and re-lifting it every frame, because the
            // flicker changes that height. Left at a fixed offset, a stretching tongue would grow
            // downward into the ground as much as upward.
            t.localPosition = new Vector3(0f, h * 0.5f, t.localPosition.z);
        }
    }
}
