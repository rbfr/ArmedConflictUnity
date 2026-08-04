using UnityEngine;

/// <summary>
/// Port of ArmedConflict's TrajectoryPhysics.kt. Pure ballistics, no engine dependency.
///
/// Runs in the TICK, never in Unity's Rigidbody system — the same locked decision as the
/// Android build (GAME_DESIGN_LOCKS.md -> Physics). Built-in physics fights an immutable-state
/// architecture and writes transforms from its own thread. Unity's scene graph removes the
/// LIFECYCLE problems that motivated the spike; it does not change this call.
///
/// All quantities are in GAME space (right-handed, +X toward the enemy). Convert to Unity
/// coordinates only at render time, via GameSpace.
/// </summary>
public static class TrajectoryPhysics
{
    public const float Gravity = 4.0f;

    /// <summary>
    /// Semi-implicit Euler: velocity first, then position from the NEW velocity. Note this is
    /// not the analytic parabola — it lands very slightly long, and the error scales with dt.
    /// </summary>
    public static void Step(ref Vector3 position, ref Vector3 velocity, float dt, float windAccelZ = 0f)
    {
        velocity += new Vector3(0f, -Gravity * dt, windAccelZ * dt);
        position += velocity * dt;
    }

    /// <summary>Time until a shot launched from origin returns to the y=0 floor.</summary>
    public static float FlightTime(Vector3 origin, Vector3 velocity)
    {
        float y0 = Mathf.Max(origin.y, 0f);
        float disc = velocity.y * velocity.y + 2f * Gravity * y0;
        return (velocity.y + Mathf.Sqrt(disc)) / Gravity;
    }

    /// <summary>Analytic landing point on the y=0 floor.</summary>
    public static Vector3 LandingPoint(Vector3 origin, Vector3 velocity)
        => new Vector3(origin.x + velocity.x * FlightTime(origin, velocity), 0f, origin.z);

    /// <summary>
    /// Drag vector -> launch velocity. Horizontal direction is ALWAYS toward the enemy side
    /// regardless of which way the player dragged; only drag distance (power) matters for X.
    /// Vertical drag controls arc height as a normal slingshot.
    /// </summary>
    public static Vector3 VelocityFromDrag(float dragX, float dragY, float speedScale)
        => new Vector3(Mathf.Abs(dragX) * speedScale, -dragY * speedScale, 0f);

    /// <summary>Samples the arc for the aim preview; stops when the shot returns to launch height.</summary>
    public static void SampleArc(Vector3 origin, Vector3 initialVelocity, int sampleCount,
                                 float dt, System.Collections.Generic.List<Vector3> into)
    {
        into.Clear();
        into.Add(origin);
        var p = origin;
        var v = initialVelocity;
        for (int i = 0; i < sampleCount; i++)
        {
            Step(ref p, ref v, dt);
            into.Add(p);
            if (v.y <= 0f && p.y <= origin.y) return;
        }
    }
}
