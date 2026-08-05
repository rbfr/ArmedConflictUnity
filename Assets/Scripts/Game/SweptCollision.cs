using UnityEngine;

namespace ArmedConflict.Game
{
    /// <summary>
    /// Port of the swept-segment collision from CollisionSystem.kt.
    ///
    /// SWEPT, not point-sampled: the tick's dt is clamped but never sub-stepped, so a fast or
    /// steeply-descending round can cover more than a world unit between ticks — well past the hit
    /// radius. A point-only check let a round render clearly above a unit's head one frame and land
    /// already-hit the next, with no visible moment of contact.
    ///
    /// x/y ONLY. z is visual parallax: all projectiles travel with vz=0 and formation z-rows are
    /// spaced further apart than the hit radius, so a 3D check would make half the formation
    /// unhittable.
    /// </summary>
    public static class SweptCollision
    {
        const float LegacyScaleRatio = 0.48f / 0.77f;
        /// <summary>
        /// Deliberately 1.22x body-proportional. Halving the hitbox along with the model would have
        /// turned an art change into a difficulty change — retune this, not the model scale, if
        /// aiming ever feels too strict.
        /// </summary>
        const float HitForgiveness = 1.22f;

        public const float UnitHitRadius = 0.5f * LegacyScaleRatio * HitForgiveness;
        public static readonly float UnitHitRadiusSq = UnitHitRadius * UnitHitRadius;

        static float DistanceSq(float px, float py, float tx, float ty)
        {
            float dx = px - tx, dy = py - ty;
            return dx * dx + dy * dy;
        }

        /// <summary>
        /// Squared distance from point (px,py) to the segment (ax,ay)-(bx,by). Falls back to the
        /// endpoint when the segment is ~zero-length (a round's very first tick).
        /// </summary>
        public static float SegmentDistanceSq(float ax, float ay, float bx, float by, float px, float py)
            => ClosestPointOnSegment(ax, ay, bx, by, px, py, out _, out _);

        /// <summary>
        /// Closest point on the segment to (px,py), plus the squared distance to it.
        ///
        /// The POINT matters as much as the distance: a detonation is placed at the contact
        /// point along this tick's flight path, not at the projectile's tick-end position, which
        /// may have overshot well past the target. Splash blasts are centred there for the same
        /// reason — otherwise a fast round's blast lands behind whatever it hit.
        /// </summary>
        public static float ClosestPointOnSegment(float ax, float ay, float bx, float by,
                                                  float px, float py, out float cx, out float cy)
        {
            float abx = bx - ax, aby = by - ay;
            float lenSq = abx * abx + aby * aby;
            if (lenSq < 0.0001f)
            {
                cx = ax; cy = ay;
                return DistanceSq(px, py, ax, ay);
            }
            float t = Mathf.Clamp01(((px - ax) * abx + (py - ay) * aby) / lenSq);
            cx = ax + abx * t;
            cy = ay + aby * t;
            return DistanceSq(px, py, cx, cy);
        }

        /// <summary>
        /// Structures collide as an axis-aligned box matching their real footprint: hitWidth
        /// wide, and rising from the ground to the top of the ACTUAL BUILDING.
        ///
        /// The top is baseY + deckY where a deck has been measured, NOT baseY + size. `size` is a
        /// logical height, and several structures break the "deck at size" contract outright —
        /// the outpost's roof tops out at 1.4 world units against a size of 2.0. Using `size`
        /// puts 0.6 units of INVISIBLE MASONRY above the roof, which is 1.3 unit-heights of
        /// solid airspace sitting on top of the defenders standing there.
        ///
        /// That is not a cosmetic error. Units are resolved before structures, but a round
        /// descending onto a garrison enters that phantom ceiling near the structure's leading
        /// EDGE — metres from any defender — so it registers as a wall hit and is spent. The
        /// garrison then cannot be shot at all: the only way to kill them is to bring the
        /// building down.
        /// </summary>
        public static bool HitsStructure(float px, float py, float sx, float baseY,
                                         float hitWidth, float height)
            => Mathf.Abs(px - sx) <= hitWidth / 2f && py >= baseY && py <= baseY + height;
    }

}
