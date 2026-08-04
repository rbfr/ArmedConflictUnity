using System.Collections.Generic;
using UnityEngine;

namespace ArmedConflict.Game
{
    /// <summary>
    /// Port of CameraFraming.kt. ONE shared function so every phase's framing decision reduces
    /// to "which points are in the set", instead of each phase reimplementing its own
    /// span/anchor/margin math — that duplication is exactly what let a settled melee unit
    /// balloon the enemy-windup zoom, because two hand-written formulas quietly disagreed about
    /// who was included.
    /// </summary>
    public static class CameraFraming
    {
        /// <summary>
        /// Half-width (world units, no margin) needed so every focus point stays in frame with
        /// the camera centred at anchorX. Also covers the set's own span/2 around its OWN centre,
        /// since the anchor is not always the centroid. Returns 0 for an empty set — callers add
        /// their own margin.
        /// </summary>
        public static float HalfWidth(float anchorX, IReadOnlyList<float> focusXs)
        {
            if (focusXs == null || focusXs.Count == 0) return 0f;
            float fromAnchor = 0f, min = float.MaxValue, max = float.MinValue;
            for (int i = 0; i < focusXs.Count; i++)
            {
                float v = focusXs[i];
                fromAnchor = Mathf.Max(fromAnchor, Mathf.Abs(v - anchorX));
                min = Mathf.Min(min, v);
                max = Mathf.Max(max, v);
            }
            return Mathf.Max(fromAnchor, (max - min) / 2f);
        }
    }
}
