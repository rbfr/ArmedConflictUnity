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

        /// <summary>
        /// A FLAT shooter's cap. Higher than the lobbing one on purpose: covering the same ground
        /// on a shallow arc simply costs more speed, and at 12 the solve was being clamped —
        /// PortSelfTest caught rounds pitching into the dirt ~3 units short of the line, which
        /// reads as a broken gun rather than as a miss. It is also the right CHARACTER: a sniper
        /// round is supposed to be fast and flat, where the "do not look unfair" reasoning behind
        /// the 12 was written about a sky full of lobbed volley fire.
        /// </summary>
        const float MaxFlatLaunchSpeed = 16.0f;

        // Randomised rather than solved: solving for the minimum angle that reaches the target
        // gives the FLATTEST possible shot, which reads as "shooting directly at them". A real
        // lobbed arc looks fairer.
        const float MinLaunchAngleDegrees = 35f;
        const float MaxLaunchAngleDegrees = 60f;

        /// <summary>
        /// The band for a FLAT shooter (`UnitDefinitionSO.flatTrajectory`) — a sniper, whose
        /// round should read as aimed straight at you rather than lobbed.
        ///
        /// Not zero, and not "solve the minimum": at g=4 a genuinely flat shot needs a speed
        /// `MaxEnemyLaunchSpeed` will not give it, so the round falls short and the shooter stops
        /// being a threat at all. This band is shallow enough to read as direct fire and still
        /// solve under the cap at campaign separations. It is still a BAND, not one angle, so a
        /// flat shooter does not fire a metronome.
        /// </summary>
        /// 12-20 measured against L5's tower-to-line separation (dx ~16-18, firing down off the
        /// platform): every round solves under MaxEnemyLaunchSpeed and NONE falls short, which is
        /// the failure this band exists to avoid. PortSelfTest asserts that directly.
        ///
        /// What it does NOT fix, and what a flat shot cannot: the +/-2 AIM JITTER is applied to
        /// the target POINT, including its height, and a shallow trajectory travels a long way
        /// horizontally while dropping 2 units — so a flat shooter's misses are LONG, sailing
        /// past the line rather than dropping short of it. About 1 round in 6 on L5. That is the
        /// price of the characterisation, not a bug; tightening it means giving snipers their own
        /// jitter, which is an accuracy change nobody has asked for.
        const float FlatMinLaunchAngleDegrees = 12f;
        const float FlatMaxLaunchAngleDegrees = 20f;

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
        public static Vector3 AimAt(Vector3 origin, Vector3 target, float jitterMultiplier = 1f,
                                    bool flat = false)
        {
            float radius = JitterRadius(jitterMultiplier);
            var jittered = new Vector3(
                target.x + (Random.value * 2f - 1f) * radius,
                target.y + (Random.value * 2f - 1f) * radius,
                target.z);

            float dx = jittered.x - origin.x;
            float dy = jittered.y - origin.y;
            float absDx = Mathf.Max(Mathf.Abs(dx), 0.05f);

            float lo = flat ? FlatMinLaunchAngleDegrees : MinLaunchAngleDegrees;
            float hi = flat ? FlatMaxLaunchAngleDegrees : MaxLaunchAngleDegrees;
            float angleDeg = lo + Random.value * (hi - lo);
            float a = angleDeg * Mathf.Deg2Rad;
            float cosA = Mathf.Cos(a), sinA = Mathf.Sin(a);

            // dy = dx*tan(a) - 0.5*g*dx^2 / (v0*cos(a))^2, solved for v0.
            float denom = Mathf.Max(absDx * Mathf.Tan(a) - dy, 0.05f);
            float v0 = Mathf.Min(Mathf.Sqrt(0.5f * Gravity * absDx * absDx / (cosA * cosA * denom)),
                                 flat ? MaxFlatLaunchSpeed : MaxEnemyLaunchSpeed);

            return new Vector3(v0 * cosA * (dx < 0f ? -1f : 1f), v0 * sinA, 0f);
        }
    }
}
