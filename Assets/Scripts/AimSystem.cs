using UnityEngine;

/// <summary>
/// Port of the aim power scale. These two constants are DERIVED FROM LEVEL GEOMETRY and must be
/// re-derived TOGETHER if either moves (CLAUDE.md is explicit): MaxAimMagnitude is the deepest
/// shot the campaign needs, and SpeedScale is then whatever puts that speed at a comfortable
/// full-length drag.
///
/// Self-consistency check, verified: drag pixels convert at ppu = width * 0.0208 (22.46 px per
/// drag-unit at 1080 wide), so a ~525 px full-length drag is 23.4 drag-units, and
/// 23.4 * (0.0064 * 60) = 8.99 — landing exactly on MaxAimMagnitude.
/// </summary>
public static class AimSystem
{
    public const float MaxAimMagnitude = 9f;
    public const float ProjectileSpeedScale = 0.0064f;
    /// <summary>The Kotlin passes PROJECTILE_SPEED_SCALE * 60f into velocityFromDrag.</summary>
    public const float DragSpeedScale = ProjectileSpeedScale * 60f;

    const float PixelsPerUnitFraction = 0.0208f;
    const float PixelsPerUnitMin = 18f;

    /// <summary>Screen-pixel drag delta -> the drag-space vector the Kotlin's aim path consumes.</summary>
    public static Vector2 DragToWorld(Vector2 deltaPixels)
    {
        float ppu = Mathf.Max(Screen.width * PixelsPerUnitFraction, PixelsPerUnitMin);
        // Unity's touch y is bottom-up where Compose's is top-down, so dragging DOWN must still
        // produce a negative worldY (and therefore a positive, upward launch velocity).
        return new Vector2(deltaPixels.x / ppu, deltaPixels.y / ppu);
    }

    /// <summary>
    /// THE CLAMP LIVES HERE, not at the readout. In the Android build the readout once clamped
    /// while the velocity did not, so "100%" was a floor with invisible power above it. The
    /// preview and the shot both go through this function so the hint cannot describe a
    /// different round than the one that flies.
    /// </summary>
    public static Vector3 AimVelocity(float dragX, float dragY)
    {
        var v = TrajectoryPhysics.VelocityFromDrag(dragX, dragY, DragSpeedScale);
        float speed = Mathf.Sqrt(v.x * v.x + v.y * v.y);
        if (speed <= MaxAimMagnitude) return v;
        float k = MaxAimMagnitude / speed;
        return new Vector3(v.x * k, v.y * k, v.z);
    }

    public static float StrengthPercent(Vector3 velocity)
        => Mathf.Clamp01(Mathf.Sqrt(velocity.x * velocity.x + velocity.y * velocity.y)
                         / MaxAimMagnitude) * 100f;

    public static float AngleDegrees(Vector3 velocity)
        => Mathf.Atan2(velocity.y, velocity.x) * Mathf.Rad2Deg;

    /// <summary>Maximum range at 45 degrees, the reach check every level must satisfy.</summary>
    public static float MaxRange45 => MaxAimMagnitude * MaxAimMagnitude / TrajectoryPhysics.Gravity;
}
