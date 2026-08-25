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
        /// <summary>Extra punch once a tick drops this many bodies. One scream is not a volley.</summary>
        public const int MultiKillAt = 3;
        public const float ShakeMultiKillBonus = 0.35f;

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
            => shake + kills * ShakePerKill
             + (kills >= MultiKillAt ? ShakeMultiKillBonus : 0f);

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
        /// How close to a roof edge a body must be to drape off instead of sitting.
        /// About half a standing width: end-of-row garrisons sit ~0.1–0.3 inside
        /// <c>hitWidth</c> (Barracks 0.125, GarrisonPost 0.31) and those are the
        /// ones that read as "stuck on the lip". A centre-deck body on a wide
        /// post stays. A comms mast (hitWidth 0.9) is all lip, which is right.
        /// </summary>
        public const float RagdollLipMargin = 0.55f;

        /// <summary>
        /// Slack, and leftover speed, at which a supported body still counts as
        /// airborne for the flail. Same numbers the renderer used to hard-code
        /// against ground rest — now they compare to <see cref="DyingUnitEntity.SupportY"/>.
        /// </summary>
        public const float RagdollAirborneSlack = 0.12f;
        public const float RagdollAirborneVy = 0.4f;

        /// <summary>
        /// Per-body launch. The tick used to throw every corpse with the same plank
        /// (<c>Vx = ±1.5, Vy = 2.5, spin = 220</c>), so a volley fell as one chorus line at one
        /// angle — Rob, 2026-08-13. These ranges keep that throw as the MIDPOINT and fan around
        /// it. Keyed on the unit id, same family as <see cref="FlamePhase"/>: consecutive ids
        /// (a rank dying together) must not share a path, and a replay of the same body is
        /// stable.
        /// </summary>
        public const float RagdollVxMin = 0.90f;
        public const float RagdollVxMax = 2.10f;
        public const float RagdollVyMin = 1.60f;
        public const float RagdollVyMax = 3.40f;
        /// <summary>
        /// Depth fan on launch. Was 0.70 — a rank still shared one plane
        /// and then stacked on the same face. Wider so they leave as a
        /// cloud, not a chorus line.
        /// </summary>
        public const float RagdollVzHalf = 1.20f;

        /// <summary>
        /// Fraction of inbound speed dumped into Z when a body hits a
        /// wall. Kills the "glass pane" stack: leftover travel becomes
        /// a slide along the face while they fall.
        /// </summary>
        public const float RagdollWallDeflect = 0.55f;
        /// <summary>
        /// Airborne tumble. The die clip is NOT applied in the air (that was
        /// the sitting-with-legs-out pose), so the GO can actually spin.
        /// 120–200 deg/s crosses 90–160° in a typical throw — a flip, not a tip.
        /// </summary>
        public const float RagdollSpinMin = 120f;
        public const float RagdollSpinMax = 200f;
        public const float RagdollYawSpinHalf = 90f;
        public const float RagdollTiltSpinHalf = 70f;
        /// <summary>
        /// Kept for the old lean-cap checks. Airborne draw no longer uses it —
        /// they are allowed to go past horizontal.
        /// </summary>
        public const float RagdollAirborneTiltMax = 55f;
        public const float RagdollTiltHalf = 15f;

        /// <summary>
        /// THE GROUND DEATH — knocked back, not tipped over (2026-08-25). Rob: *"when a unit is
        /// on the ground and they die, they just tip over. let's make them get blown back but not
        /// as dramatic as the one falling off the building."*
        ///
        /// It used to be a 0.35-0.80 shove with a 0.05-0.20 hop, and it went the WRONG WAY: the
        /// old branch threw at `-sign`, i.e. TOWARD the enemy, on reasoning about shoving bodies
        /// off a building — a comment that no longer described this branch at all, since a body
        /// on a structure takes the tumble path. So a man shot on the dirt fell forwards, into
        /// the fire that killed him.
        ///
        /// These sit deliberately BETWEEN the old tip-over and the deck tumble (Vx 0.90-2.10,
        /// Vy 1.60-3.40, spin 120-200): far enough to read as a hit, nowhere near a body thrown
        /// off a roof. At `RollFrictionPerTick` the throw carries about `Vx / 2.3` units, so this
        /// range slides a body roughly 0.3-0.6 — a few body widths.
        ///
        /// NO yaw or tilt spin, unlike the tumble. The 3-axis cartwheel is most of what makes the
        /// deck fall dramatic, and it is the half not wanted here.
        /// </summary>
        public const float RagdollKnockVxMin = 0.75f;
        public const float RagdollKnockVxMax = 1.35f;
        public const float RagdollKnockVyMin = 0.30f;
        public const float RagdollKnockVyMax = 0.70f;
        public const float RagdollKnockLeanMin = 12f;
        public const float RagdollKnockLeanMax = 24f;
        public const float RagdollKnockSpinMin = 100f;
        public const float RagdollKnockSpinMax = 150f;

        public readonly struct RagdollImpulse
        {
            public readonly float Vx, Vy, Vz, Rotation, RotationSpeed;
            public readonly float YawSpeed, TiltSpeed;
            public RagdollImpulse(float vx, float vy, float vz, float rotation, float rotationSpeed,
                                  float yawSpeed, float tiltSpeed)
            {
                Vx = vx; Vy = vy; Vz = vz; Rotation = rotation; RotationSpeed = rotationSpeed;
                YawSpeed = yawSpeed; TiltSpeed = tiltSpeed;
            }
        }

        /// <summary>
        /// A deck fall gets the full tumble. Dirt deaths do not — they tip over
        /// where they stood. Tank deck is 0.60; anything at or above that came
        /// off a structure.
        /// </summary>
        public const float RagdollTumbleMinY = 0.40f;
        /// <summary>Kick the limbs only on the throw, not while they sit in the wreck.</summary>
        public const float RagdollFlailSeconds = 0.35f;
        /// <summary>Collapsed ruin is a low mound, not the standing box.</summary>
        public const float WreckRestY = 0.32f;

        /// <summary>
        /// Where a corpse sits on a wreck. Wreck.Y is the visual BASE
        /// (same as the wreck GO: structure centre minus size/2). Adding
        /// the standing centre left bodies parked at ~1.3 while the
        /// collapse mesh sat on the dirt — Rob, 2026-08-18: a body can
        /// stop in midair after being blown off the structure.
        /// </summary>
        public static float WreckLidY(WreckEntity w)
            => w.Y + WreckRestY;

        public static bool DiesInATumble(float y, int? standingOnStructureId)
            => standingOnStructureId != null || y > RagdollTumbleMinY;

        /// <summary>Throw for this body. Player is flung -X, enemy +X — still "backwards".</summary>
        public static RagdollImpulse ImpulseFor(int unitId, bool isPlayerSide)
            => ImpulseFor(unitId, isPlayerSide, tumble: true);

        public static RagdollImpulse ImpulseFor(int unitId, bool isPlayerSide, bool tumble)
        {
            float sign = isPlayerSide ? -1f : 1f;
            if (!tumble)
            {
                // KNOCKED BACK. `sign`, not `-sign`: the same "still backwards" convention the
                // tumble uses, so a body goes away from whatever shot it instead of toward it.
                return new RagdollImpulse(
                    vx: sign * Mathf.Lerp(RagdollKnockVxMin, RagdollKnockVxMax, Hash01(unitId, 1u)),
                    vy: Mathf.Lerp(RagdollKnockVyMin, RagdollKnockVyMax, Hash01(unitId, 2u)),
                    vz: Mathf.Lerp(-0.15f, 0.15f, Hash01(unitId, 3u)),
                    rotation: sign * Mathf.Lerp(RagdollKnockLeanMin, RagdollKnockLeanMax,
                                                Hash01(unitId, 4u)),
                    rotationSpeed: sign * Mathf.Lerp(RagdollKnockSpinMin, RagdollKnockSpinMax,
                                                     Hash01(unitId, 5u)),
                    yawSpeed: 0f,
                    tiltSpeed: 0f);
            }
            float yawSign = Hash01(unitId, 6u) < 0.5f ? -1f : 1f;
            float tiltSign = Hash01(unitId, 7u) < 0.5f ? -1f : 1f;
            return new RagdollImpulse(
                vx: sign * Mathf.Lerp(RagdollVxMin, RagdollVxMax, Hash01(unitId, 1u)),
                vy: Mathf.Lerp(RagdollVyMin, RagdollVyMax, Hash01(unitId, 2u)),
                vz: Mathf.Lerp(-RagdollVzHalf, RagdollVzHalf, Hash01(unitId, 3u)),
                rotation: Mathf.Lerp(-RagdollTiltHalf, RagdollTiltHalf, Hash01(unitId, 4u)),
                rotationSpeed: sign * Mathf.Lerp(RagdollSpinMin, RagdollSpinMax, Hash01(unitId, 5u)),
                yawSpeed: yawSign * Mathf.Lerp(20f, RagdollYawSpinHalf, Hash01(unitId, 8u)),
                tiltSpeed: tiltSign * Mathf.Lerp(15f, RagdollTiltSpinHalf, Hash01(unitId, 9u)));
        }

        /// <summary>
        /// Unit-id hash in <c>[0, 1)</c>. Consecutive ids (a dying rank) must not walk a
        /// ramp — a linear map is a travelling wave, which is the chorus line this exists to
        /// break. Same mixer as <see cref="FlamePhase"/>, salted so each channel is independent.
        /// </summary>
        static float Hash01(int unitId, uint salt)
        {
            unchecked
            {
                uint h = (uint)unitId * 2654435761u ^ salt * 2246822519u;
                h ^= h >> 15;
                return (h & 0xFFFFu) / 65536f;
            }
        }

        /// <summary>
        /// The lean to draw for an animated corpse. Signed by the side, because a body tips the
        /// way it is thrown and the two sides are thrown in opposite directions.
        ///
        /// Measured from the nearest LYING pose (0 or 180), not from abs(rotation). Flop settles
        /// at either; abs(180)*0.32 is the 38° cap, so every body that fell the long way sat
        /// propped off the horizon forever. Rob, 2026-08-13: "they don't always come to rest
        /// on the horizon" — the "always" is which way they flop, which is the launch hash.
        /// </summary>
        public static float RagdollLeanDegrees(float rotation, bool isPlayerSide)
        {
            float toFlat = Mathf.Abs(Mathf.DeltaAngle(rotation, 0f));
            if (toFlat > 90f) toFlat = 180f - toFlat;
            float lean = Mathf.Min(toFlat * RagdollLeanFraction, RagdollLeanMaxDegrees);
            return isPlayerSide ? -lean : lean;
        }

        /// <summary>
        /// Clamp airborne tumble so a long throw cannot roll them past vertical
        /// before they land.
        /// </summary>
        public static float ClampAirborneTilt(float rotation)
        {
            float tip = Mathf.DeltaAngle(0f, rotation);
            return Mathf.Clamp(tip, -RagdollAirborneTiltMax, RagdollAirborneTiltMax);
        }

        /// <summary>
        /// What the renderer pitches/yaws/rolls the corpse by.
        ///
        /// Airborne: the live 3-axis tumble. Grounded: the nearest side-lie
        /// (±90° on Z — horizontal at this camera) plus leftover twist. The
        /// die clip is not in this path; it is a sit-down pose and is what
        /// made them look seated in the air.
        /// </summary>
        public static Vector3 RagdollVisualEuler(DyingUnitEntity d)
            => new Vector3(d.SettleTilt, d.Yaw, -d.Rotation);

        /// <summary>
        /// Spring toward the nearest side-lie (±90). 0 and 180 are standing /
        /// upside-down in the renderer; those were the sit-up on landing.
        /// </summary>
        public static void StepFlopToSide(float rotation, float rotationSpeed, float dt,
                                          out float newRotation, out float newRotationSpeed)
        {
            float r = Mathf.DeltaAngle(0f, rotation);
            float nearest = Mathf.Abs(Mathf.DeltaAngle(r, 90f)) <= Mathf.Abs(Mathf.DeltaAngle(r, -90f))
                ? 90f : -90f;
            float error = Mathf.DeltaAngle(r, nearest);
            float accel = error * FlopSpring - rotationSpeed * FlopDamping;
            newRotationSpeed = Mathf.Clamp(rotationSpeed + accel * dt,
                                           -FlopMaxSettleSpeed, FlopMaxSettleSpeed);
            newRotation = r + newRotationSpeed * dt;
        }

        /// <summary>
        /// Speed to run Kenney's <c>die</c> at. The clip is 0.33s and ends lying
        /// down — at speed 1 they finish the fold in the air. Frozen at a
        /// mid-crumple until contact, then play through. Hold at 0 is a standing
        /// statue (seen on device); hold at 1 is already horizontal in the air.
        /// </summary>
        public const float DieAirborneHold = 0.38f;

        public static float DieClipSpeed(bool airborne, float normalizedTime)
        {
            return airborne ? 0f : 1f;
        }

        /// <summary>
        /// True while the body is still in the air — flail on, slump off. A roof
        /// counts as the ground. Comparing to <see cref="RagdollRestY"/> here is
        /// what left every garrison twitching on its deck.
        /// </summary>
        public static bool RagdollAirborne(DyingUnitEntity d)
        {
            if (d.SupportY < 0f) return true;
            // Vx is not airborne. A body sliding on dirt is ON the dirt, and
            // keying the flail off leftover speed was the twitch Rob saw
            // after the sink landed (2026-08-16).
            return d.Y > d.SupportY + RagdollAirborneSlack
                || Mathf.Abs(d.Vy) > RagdollAirborneVy;
        }

        /// <summary>
        /// Inside the footprint and within <see cref="RagdollLipMargin"/> of a
        /// face. A box narrower than two margins is all lip.
        /// </summary>
        public static bool RagdollOnLip(float x, float minX, float maxX)
        {
            if (x <= minX || x >= maxX) return false;
            return (x - minX) <= RagdollLipMargin || (maxX - x) <= RagdollLipMargin;
        }

        /// <summary>
        /// Depth kick when a body hits a face. Sign is hashed so two
        /// soldiers who reach the same wall peel opposite ways.
        /// </summary>
        public static float RagdollWallScatterVz(int unitId, float inboundSpeed)
        {
            float side = Hash01(unitId, 11u) < 0.5f ? -1f : 1f;
            return side * inboundSpeed * RagdollWallDeflect;
        }

        public const float RagdollMaxAgeSeconds = 5f;
        public const float RagdollBodyHeight = 0.5f;
        public const float RagdollBodyHalfWidth = 0.05f;

        /// <summary>
        /// Last stretch of the TTL: a body on the DIRT eases under the ground
        /// instead of vanishing in one frame. Same family as the health-bar fade.
        /// Render-only — the tick must not move, or rest no longer agrees with
        /// where they fell. Roofs do not sink (that is into masonry). Depth is
        /// enough to bury a lying mesh at this camera's ~6°, so a recycled slot
        /// cannot pop a half-buried body back to standing.
        /// </summary>
        public const float RagdollSinkSeconds = 0.9f;
        public const float RagdollSinkDepth = 0.80f;

        /// <summary>
        /// Downward render offset at this age. Zero while airborne, on a roof,
        /// or before the last stretch. Smoothstep so it eases, not a linear drop.
        /// </summary>
        public static float RagdollSinkY(float age, float supportY)
        {
            if (supportY < 0f) return 0f;
            // Dirt rest is at most the upright-box height. A tank deck is 0.60
            // and every building is taller — those must not sink.
            if (supportY > RagdollBodyHeight + 0.02f) return 0f;
            float remaining = RagdollMaxAgeSeconds - age;
            if (remaining >= RagdollSinkSeconds) return 0f;
            float t = 1f - Mathf.Clamp01(remaining / RagdollSinkSeconds);
            t = t * t * (3f - 2f * t);
            return -RagdollSinkDepth * t;
        }
        public const float RollMinSpeed = 0.30f;

        /// <summary>
        /// Ceiling on how fast the settle spring may turn a body, in deg/s.
        ///
        /// Rob, 2026-08-25: *"when they are at/near the ground, they seem to start
        /// glitching/going into some kind of animation loop before they finally disappear."*
        /// Measured on a real deck fall, that is the roll-to-settle handover: the body rolls to
        /// an ARBITRARY angle, and `StepFlopToSide` then has to cross up to 90 degrees to the
        /// nearest side-lie. At FlopSpring 140 it crosses it in about an eighth of a second and
        /// REVERSES on the way — +57 deg/s of roll became -158 in the same fifth of a second,
        /// which is the snap being reported.
        ///
        /// The spring is not the wrong model and its constants are right for the small errors a
        /// DIRT death hands it (a ~20 degree lean). What it needed was a speed limit, so a large
        /// error eases over ~0.3s instead of snapping. Small errors never reach this cap, so the
        /// tip-over is untouched.
        ///
        /// Two things tried first and reverted, because measurement said they missed: dropping
        /// RollMinSpeed to bleed the roll off (it made the handover error WORSE, 32 -> 49
        /// degrees) and bleeding the opposing inherited spin (the spring's own error term
        /// dominates it).
        /// </summary>
        public const float FlopMaxSettleSpeed = 120f;
        public const float RollFrictionPerTick = 0.962f;   // per tick at 60Hz — see DecayPerTick60
        public const float RollDegPerUnit = 150f;
        public const float FlopSpring = 140f;
        public const float FlopDamping = 23f;

        /// <summary>
        /// Resting height for a body at this rotation. 0 and 180 are both lying flat — the
        /// origin-at-the-feet box used to report 0.5 at 180 (the top of the box had flipped
        /// below the origin), so a flop that settled the long way left the hips half a unit
        /// off the ground. Lift is the sine of the angle: max at 90, none on either flat.
        /// </summary>
        public static float RagdollRestY(float rotationDegrees)
        {
            float r = rotationDegrees * Mathf.Deg2Rad;
            return Mathf.Abs(Mathf.Sin(r)) * RagdollBodyHeight
                 + Mathf.Abs(Mathf.Cos(r)) * RagdollBodyHalfWidth;
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

        /// <summary>Long enough to still be on the dirt when the next
        /// aim starts. 2.6s vanished during the volley follow.</summary>
        public const float DebrisTtlSeconds = 8.0f;
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
        /// <summary>
        /// Same projection argument as unit shadows: at ~6° a round decal is a smear, and
        /// WIDTH cannot buy screen height. Stretch along depth so a miss still reads.
        /// </summary>
        public const float ScorchDepthStretch = 3.2f;
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
