using System.Collections.Generic;
using UnityEngine;

namespace ArmedConflict.Game
{
    /// <summary>
    /// Sixth slice of the GameViewModel port: the cosmetic layers — ragdolls, debris, scorch
    /// marks and camera shake.
    ///
    /// None of this changes who wins. All of it changes whether the game looks alive, and every
    /// piece here carries a trap that once shipped as a visible bug.
    /// </summary>
    public static class CosmeticSystems
    {
        // ---- rate independence ----------------------------------------------------------

        /// <summary>
        /// Converts a per-tick-at-60Hz decay factor into one correct for THIS tick's dt.
        ///
        /// dt VARIES, so a bare per-tick multiply silently changes rate with the frame rate:
        /// the same "0.962 friction" is a different deceleration at 30, 60 and 120Hz. Every
        /// decay constant expressed per-tick must go through here, or a ragdoll that rolls
        /// convincingly on one device skids on another.
        /// </summary>
        public static float DecayPerTick60(float perTick, float dt)
            => Mathf.Pow(perTick, dt * 60f);

        // ---- health bars ----------------------------------------------------------------

        /// <summary>
        /// How long a unit's health bar stays up after it is hit, and how much of that is spent
        /// fading out. It does NOT persist for as long as the unit is wounded: the player has
        /// already read the hit by then, and a line of permanent bars is a second HUD sitting on
        /// top of the army.
        ///
        /// Re-armed from zero on every hit, so a unit under sustained fire keeps its bar up
        /// rather than having it expire mid-bombardment.
        /// </summary>
        public const float HealthBarSeconds = 3f;
        public const float HealthBarFadeSeconds = 0.7f;

        /// <summary>
        /// Advances the since-hit clock. -1 means "not showing", which is also what this returns
        /// once the bar has run its course — the renderer tests for >= 0 and nothing else has to
        /// remember whether a given unit was ever hit.
        /// </summary>
        public static float StepHitAge(float age, float dt)
        {
            if (age < 0f) return -1f;
            float next = age + dt;
            return next >= HealthBarSeconds ? -1f : next;
        }

        /// <summary>Opacity for a bar at this age: solid, then fading over the last stretch.</summary>
        public static float HealthBarAlpha(float age)
        {
            if (age < 0f) return 0f;
            float remaining = HealthBarSeconds - age;
            return remaining >= HealthBarFadeSeconds ? 1f
                 : Mathf.Clamp01(remaining / HealthBarFadeSeconds);
        }

        /// <summary>
        /// Opacity for the EMPTY TRACK, which fades FASTER than the fill on top of it.
        ///
        /// Equal alpha is not equal legibility. The track is near-black and the fill is a
        /// saturated colour, so against any of this game's grounds — pale winter, tan desert —
        /// the dark track keeps far more contrast at the same alpha than the colour does. Faded
        /// together, the colour washes out first and the bar spends its last half-second as a
        /// DARK HUSK: a black rectangle over a soldier's head, which is exactly what "black means
        /// dead, right?" was reporting.
        ///
        /// Squaring makes the track the first thing to go, so a bar always dissolves down to its
        /// COLOUR and never down to a black bar.
        /// </summary>
        public static float HealthBarTrackAlpha(float age)
        {
            float a = HealthBarAlpha(age);
            return a * a;
        }

        // ---- the incendiary flame -------------------------------------------------------

        /// <summary>
        /// How the flame on a burning unit flickers. Two tongues per unit, each a scaled quad,
        /// and this is the only thing that moves them.
        ///
        /// A SINE OF ABSOLUTE TIME rather than anything integrated per tick. dt VARIES here, so a
        /// flicker advanced by dt would run at a different rate on a stuttering frame and a
        /// phase accumulated per slot would have to be reset on recycle; sampling a function of
        /// the clock has neither problem and is exactly reproducible in a test.
        ///
        /// The two tongues run at DIFFERENT rates, deliberately not harmonically related. At the
        /// same rate they scale in lockstep and the pair reads as one shape breathing, which is a
        /// UI pulse rather than fire.
        /// </summary>
        public const float FlameOuterHz = 6.3f;
        public const float FlameInnerHz = 9.1f;
        /// <summary>How far the tongue's height swings either side of its nominal size.</summary>
        public const float FlameHeightSwing = 0.22f;
        /// <summary>Width swings LESS than height — fire licks upward, it does not pump sideways.</summary>
        public const float FlameWidthSwing = 0.09f;

        /// <summary>
        /// A stable per-unit phase offset, so a line of burning soldiers does not flicker as one
        /// chorus. Keyed on the unit's ID rather than on its render slot: slots are handed out in
        /// roster order and shift down as men die, so a slot-keyed phase would make every
        /// surviving flame jump the instant one of their neighbours fell.
        ///
        /// The same reasoning, and the same fix, as UnitAnim.Desync.
        /// </summary>
        public static float FlamePhase(int unitId)
        {
            // A cheap integer hash rather than id * k: consecutive IDs are the common case (a
            // group is spawned in a run), and a linear phase across a rank is a travelling wave —
            // which reads as deliberate choreography, the thing the offset exists to avoid.
            unchecked
            {
                uint h = (uint)unitId * 2654435761u;
                h ^= h >> 15;
                return (h % 10000u) / 10000f * (Mathf.PI * 2f);
            }
        }

        /// <summary>
        /// The scale multiplier for one tongue: x is width, y is height.
        ///
        /// Height and width swing in ANTIPHASE (note the minus). A flame conserves roughly its
        /// volume as it licks — it narrows as it stretches — and swinging both together just
        /// makes the whole tongue zoom in and out, which reads as a throbbing sticker.
        /// </summary>
        public static Vector2 FlameScale(float time, float phase, bool inner)
        {
            float s = Mathf.Sin(time * (inner ? FlameInnerHz : FlameOuterHz) * Mathf.PI * 2f
                                + phase + (inner ? Mathf.PI * 0.5f : 0f));
            return new Vector2(1f - s * FlameWidthSwing, 1f + s * FlameHeightSwing);
        }

        // ---- camera shake ---------------------------------------------------------------

        public const float ShakeDecayPerSecond = 2.5f;
        public const float ShakePerKill = 0.15f;

        /// <summary>
        /// Decays camera shake. This MUST run on every tick path, including the one taken once
        /// the battle is over.
        ///
        /// In the Android build shake was only computed inside the combat block, which the
        /// non-Playing early return skipped — so a level that ended on a killing volley (i.e.
        /// every level) froze its shake wherever the decay had got to, permanently. The renderer
        /// re-rolls a random offset from it every frame it is above zero, so the whole scene
        /// jittered for the rest of the victory screen. It was reported as "the destroyed
        /// structures are jittering when the level is over" — the camera was jittering, and the
        /// wrecks were simply the only things left to see it against.
        /// </summary>
        public static float DecayShake(float shake, float dt)
            => Mathf.Max(shake - dt * ShakeDecayPerSecond, 0f);

        public static float AddShakeForKills(float shake, int kills)
            => shake + kills * ShakePerKill;

        // ---- ragdolls -------------------------------------------------------------------

        /// <summary>
        /// How much of a ragdoll's tumble an ANIMATED body actually shows, and the cap on it.
        ///
        /// The tick spins a corpse at 220 deg/s, which is right for the un-animated fallback — a
        /// rigid plank has nothing else to say it is dying. A rigged body already has a death
        /// CLIP folding it, so applying the full spin on top made it fold AND cartwheel, which is
        /// why the renderer used to discard the rotation entirely. Discarding it went too far the
        /// other way: a body flew backwards perfectly upright, like a statue on rails.
        ///
        /// A FRACTION of the tumble, capped, is the middle: the body pitches back as it is thrown
        /// and then holds that lean while the clip does the folding. At 220 deg/s the cap is
        /// reached about a third of a second in, so the lean rises and settles rather than
        /// winding up.
        /// </summary>
        public const float RagdollLeanFraction = 0.32f;
        public const float RagdollLeanMaxDegrees = 38f;

        /// <summary>
        /// The lean to draw for an animated corpse. Signed by the side, because a body tips the
        /// way it is thrown and the two sides are thrown in opposite directions.
        /// </summary>
        public static float RagdollLeanDegrees(float rotation, bool isPlayerSide)
        {
            float lean = Mathf.Min(Mathf.Abs(rotation) * RagdollLeanFraction, RagdollLeanMaxDegrees);
            return isPlayerSide ? -lean : lean;
        }

        public const float RagdollMaxAgeSeconds = 5f;
        public const float RagdollBodyHeight = 0.5f;
        public const float RagdollBodyHalfWidth = 0.05f;
        public const float RollMinSpeed = 0.30f;
        public const float RollFrictionPerTick = 0.962f;   // per tick at 60Hz — see DecayPerTick60
        public const float RollDegPerUnit = 150f;
        public const float FlopSpring = 140f;
        public const float FlopDamping = 23f;

        /// <summary>
        /// Resting height for a body at this rotation — the lift needed so no corner of the
        /// body box sinks below the ground. A body lying flat rests lower than one propped at an
        /// angle, which is what stops a tumbling corpse from clipping through the floor
        /// mid-roll.
        /// </summary>
        public static float RagdollRestY(float rotationDegrees)
        {
            float r = rotationDegrees * Mathf.Deg2Rad;
            float sin = Mathf.Sin(r), cos = Mathf.Cos(r);
            float lowest = 0f;
            foreach (float cx in new[] { -RagdollBodyHalfWidth, RagdollBodyHalfWidth })
            foreach (float cy in new[] { 0f, RagdollBodyHeight })
            {
                float rotatedY = cx * sin + cy * cos;
                if (rotatedY < lowest) lowest = rotatedY;
            }
            return -lowest;
        }

        /// <summary>
        /// Rolling contact: while a grounded body still has real horizontal speed it tumbles
        /// along the ground with rotation locked to travel, like a log, before the flop spring
        /// takes over. Returns the new horizontal speed and the rotation rate that matches it.
        /// </summary>
        public static void StepRoll(float vx, float dt, out float newVx, out float rollSpeed)
        {
            newVx = vx * DecayPerTick60(RollFrictionPerTick, dt);
            rollSpeed = -newVx * RollDegPerUnit;
        }

        public static bool ShouldRoll(float vx) => Mathf.Abs(vx) > RollMinSpeed;

        /// <summary>
        /// Angular spring pulling a grounded body toward the nearest lying pose, near-critically
        /// damped (damping ~ 2*sqrt(spring)), so flop-to-rest takes about 0.4s with a slight rock.
        /// </summary>
        public static void StepFlop(float rotation, float rotationSpeed, float dt,
                                    out float newRotation, out float newRotationSpeed)
        {
            float nearestLying = Mathf.Round(rotation / 180f) * 180f;
            float error = nearestLying - rotation;
            float accel = error * FlopSpring - rotationSpeed * FlopDamping;
            newRotationSpeed = rotationSpeed + accel * dt;
            newRotation = rotation + newRotationSpeed * dt;
        }

        /// <summary>
        /// Bodies lie on the field for a short beat, then cull. Longer lifetimes were tried (30s)
        /// and keep the dying list long across whole turn cycles, which the renderer then scans
        /// per unit slot per tick — it read as sluggishness during heavy volleys.
        /// </summary>
        public static bool RagdollExpired(float age) => age >= RagdollMaxAgeSeconds;

        // ---- debris ---------------------------------------------------------------------

        public const float DebrisTtlSeconds = 2.6f;
        /// <summary>Structure rubble persists for the WHOLE level rather than ageing out.</summary>
        public const float DebrisRubbleTtl = float.MaxValue;

        /// <summary>
        /// A grounded, nearly-motionless piece of RUBBLE is put to sleep: its motion stops and it
        /// is flagged so nothing has to keep integrating it.
        ///
        /// Only rubble sleeps — transient spatter is never slept, it ages out on ttl instead.
        /// The flag is also what lets IsVisuallyIdle stay usable: rubble never disappears, so a
        /// plain "is the debris list empty" test would be false forever once anything was
        /// destroyed, silently disabling the idle path for the rest of the level.
        /// </summary>
        public static bool ShouldSleep(bool isRubble, bool grounded,
                                       float vx, float vy, float rotationSpeed)
            => isRubble && grounded
               && Mathf.Abs(vx) < 0.05f && Mathf.Abs(vy) < 0.05f
               && Mathf.Abs(rotationSpeed) < 12f;

        // ---- scorch ---------------------------------------------------------------------

        public const float ScorchWorldRadius = 0.30f;
        /// <summary>A new mark within this fraction of an existing one merges into it.</summary>
        public const float ScorchMergeFraction = 0.75f;
        public const float ScorchMergeGrowth = 1.07f;
        public const float ScorchMaxScale = 1.8f;

        /// <summary>
        /// Finds an existing scorch close enough to absorb a new one, so a heavily-shelled patch
        /// grows one bigger scar instead of stacking dozens of identical decals in the same
        /// place — which both looks wrong and burns slots from a bounded pool.
        /// </summary>
        public static int FindMergeTarget(IReadOnlyList<ScorchMark> scorches, float x, float z)
        {
            float mergeDist = ScorchWorldRadius * ScorchMergeFraction;
            float bestSq = mergeDist * mergeDist;
            int best = -1;
            for (int i = 0; i < scorches.Count; i++)
            {
                float dx = scorches[i].X - x, dz = scorches[i].Z - z;
                float d = dx * dx + dz * dz;
                if (d <= bestSq) { bestSq = d; best = i; }
            }
            return best;
        }

        /// <summary>Growth is capped so a long bombardment cannot produce one enormous blot.</summary>
        public static float GrowScorch(float scale)
            => Mathf.Min(scale * ScorchMergeGrowth, ScorchMaxScale);

        // ---- knockback ------------------------------------------------------------------

        public const float KnockbackDurationSeconds = 0.42f;

        /// <summary>
        /// Advances a survivor's knockback hop. -1 means inactive; the counter runs up and then
        /// returns to -1, snapping the unit back to its formation slot. It is deliberately an
        /// AGE rather than a position: the renderer derives a sine arc from it, so the unit's
        /// real x/y never leave the formation and collision is unaffected.
        /// </summary>
        public static float StepKnockback(float knockbackAge, float dt)
        {
            if (knockbackAge < 0f) return -1f;
            float next = knockbackAge + dt;
            return next >= KnockbackDurationSeconds ? -1f : next;
        }
    }
}
