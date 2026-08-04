using UnityEngine;

namespace ArmedConflict.Game
{
    /// <summary>
    /// Port of SpringFollow.kt — critically-damped spring smoothing (the Game Programming Gems 4
    /// "SmoothDamp" algorithm). THE ONE smoothing primitive for every tick-synced value that
    /// follows a possibly-discontinuous target: camera X, camera Z (zoom), volley centroid.
    ///
    /// Deliberately NOT replaced by Unity's Mathf.SmoothDamp even though the algorithm is the
    /// same: the REST DEADBAND below is a local addition and is load bearing (see step()).
    /// Mathf.SmoothDamp has no deadband and would reintroduce the exact bug.
    ///
    /// Three separate bugs in the Android build were each a per-tick value derived from the mean
    /// of a CHANGING SET — discontinuous even though every member moves smoothly, because set
    /// MEMBERSHIP changes non-monotonically. Each got its own hand-rolled decay constant; a real
    /// spring carries velocity state and is dt-parameterised, so it survives a jumping target and
    /// a varying tick rate.
    /// </summary>
    public static class SpringFollow
    {
        const float RestEpsilon = 1e-4f;
        const float RestVelocityEpsilon = 1e-3f;

        public static void Step(ref float current, ref float velocity, float target,
                                float dt, float smoothTime, float maxSpeed = float.MaxValue)
        {
            if (dt <= 0f) return;

            float smoothTimeSafe = Mathf.Max(smoothTime, 0.0001f);
            float omega = 2f / smoothTimeSafe;
            float x = omega * dt;
            // Rational approximation of exp(-x) — avoids a transcendental call every tick.
            float exp = 1f / (1f + x + 0.48f * x * x + 0.235f * x * x * x);

            float originalTarget = target;
            float change = current - target;
            float maxChange = maxSpeed * smoothTimeSafe;
            change = Mathf.Clamp(change, -maxChange, maxChange);
            float adjustedTarget = current - change;

            float temp = (velocity + omega * change) * dt;
            float newVelocity = (velocity - omega * temp) * exp;
            float newValue = adjustedTarget + (change + temp) * exp;

            // Prevent overshooting the (unclamped) original target.
            if ((originalTarget - current > 0f) == (newValue > originalTarget))
            {
                newValue = originalTarget;
                newVelocity = (newValue - originalTarget) / dt;
            }

            // REST DEADBAND — the single most expensive thing ever measured in this codebase.
            // Without it the spring approaches asymptotically and never actually stops, so
            // velocity settles to a tiny non-zero that jitters with dt forever. In the Android
            // build GameState sits behind a StateFlow that conflates by equality, so ONE
            // never-settling float made every tick's state unequal, recomposing the whole scene
            // at full frame rate: measured 30 recompositions/second and ~40% of a core while
            // idle, with a reflective diff showing exactly one changing field
            // (cameraFollowZVelocity -2.1812475E-6 -> -2.1813487E-6).
            //
            // Unity has no StateFlow, so the blast radius is smaller — but a resting spring must
            // still be bit-identical tick to tick, and snapping the VALUE (not just the velocity)
            // is what makes that true. Thresholds sit far below anything observable (~0.01px at
            // gameplay framing) and far above float noise.
            if (Mathf.Abs(newValue - originalTarget) < RestEpsilon &&
                Mathf.Abs(newVelocity) < RestVelocityEpsilon)
            {
                current = originalTarget;
                velocity = 0f;
                return;
            }

            current = newValue;
            velocity = newVelocity;
        }
    }
}
