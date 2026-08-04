using UnityEngine;

namespace ArmedConflict.Game
{
    /// <summary>Port of EnemyAI.kt — pure targeting math, no engine dependencies.</summary>
    public static class EnemyAI
    {
        /// <summary>MUST match TrajectoryPhysics.Gravity — AI accuracy depends on it.</summary>
        const float Gravity = 4.0f;

        /// <summary>
        /// Applied to the TARGET POINT before solving, so it is the only source of inaccuracy.
        /// Keep it big enough that the player never feels the AI shoots with perfect precision.
        /// </summary>
        const float AimJitterRadius = 2.0f;

        /// <summary>
        /// Caps launch speed so fast volleys do not look unfair. Clamping lets steep/close
        /// solves fall short, which reads as "AI missed" rather than "AI fired a laser".
        /// </summary>
        const float MaxEnemyLaunchSpeed = 12.0f;

        // Randomised rather than solved: solving for the minimum angle that reaches the target
        // gives the FLATTEST possible shot, which reads as "shooting directly at them". A real
        // lobbed arc looks fairer.
        const float MinLaunchAngleDegrees = 35f;
        const float MaxLaunchAngleDegrees = 60f;

        const float OverwatchFlareMultiplier = 0.5f;

        /// <summary>Smoke Screen doubles this — the next volley fires through smoke, i.e. worse
        /// accuracy, not less damage.</summary>
        public static float JitterRadius(float multiplier) => AimJitterRadius * multiplier;

        /// <summary>Overwatch Flare halves the banked advance budget — the anti-melee-rush counter.</summary>
        public static float AdvanceBudget(float basePerTurn, bool halved)
            => halved ? basePerTurn * OverwatchFlareMultiplier : basePerTurn;

        /// <summary>
        /// Lobs from origin toward target at a randomised arc angle with slight inaccuracy.
        /// Targets are stationary, so this solves for the SPEED that makes a fixed launch angle
        /// land exactly on the jittered target.
        /// </summary>
        public static Vector3 AimAt(Vector3 origin, Vector3 target, float jitterMultiplier = 1f)
        {
            float radius = JitterRadius(jitterMultiplier);
            var jittered = new Vector3(
                target.x + (Random.value * 2f - 1f) * radius,
                target.y + (Random.value * 2f - 1f) * radius,
                target.z);

            float dx = jittered.x - origin.x;
            float dy = jittered.y - origin.y;
            float absDx = Mathf.Max(Mathf.Abs(dx), 0.05f);

            float angleDeg = MinLaunchAngleDegrees
                           + Random.value * (MaxLaunchAngleDegrees - MinLaunchAngleDegrees);
            float a = angleDeg * Mathf.Deg2Rad;
            float cosA = Mathf.Cos(a), sinA = Mathf.Sin(a);

            // dy = dx*tan(a) - 0.5*g*dx^2 / (v0*cos(a))^2, solved for v0.
            float denom = Mathf.Max(absDx * Mathf.Tan(a) - dy, 0.05f);
            float v0 = Mathf.Min(Mathf.Sqrt(0.5f * Gravity * absDx * absDx / (cosA * cosA * denom)),
                                 MaxEnemyLaunchSpeed);

            return new Vector3(v0 * cosA * (dx < 0f ? -1f : 1f), v0 * sinA, 0f);
        }
    }
}
