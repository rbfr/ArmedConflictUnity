using UnityEngine;

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
    {
        float abx = bx - ax, aby = by - ay;
        float lenSq = abx * abx + aby * aby;
        if (lenSq < 0.0001f) return DistanceSq(px, py, ax, ay);
        float t = Mathf.Clamp01(((px - ax) * abx + (py - ay) * aby) / lenSq);
        return DistanceSq(px, py, ax + abx * t, ay + aby * t);
    }

    /// <summary>Structures collide as an axis-aligned box matching their real footprint.</summary>
    public static bool HitsStructure(float px, float py, float sx, float sy,
                                     float hitWidth, float size)
        => Mathf.Abs(px - sx) <= hitWidth / 2f && Mathf.Abs(py - sy) <= size / 2f;
}
