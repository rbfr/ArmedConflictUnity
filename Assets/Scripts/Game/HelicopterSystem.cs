using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace ArmedConflict.Game
{
    /// <summary>
    /// Seventh slice of the GameViewModel port: the helicopter state machine.
    ///
    /// The heli is currently gated off in the Android build (HELI_ENABLED = false), and that was
    /// a CAMERA-LOAD decision rather than a sequencing TODO — a hovering gunship adds a reserved
    /// margin to the frame, and the interaction between that margin and the per-phase zoom is
    /// what went wrong. This is ported as a system intended to come back, so the margin's own
    /// slow blend (see GameState.HeliMarginBlend) is part of the design, not an afterthought.
    ///
    /// Modes: Preview (cosmetic pre-battle flyby, not shootable) -> Entering -> Hovering ->
    /// GunRun or Retreating -> gone; Crashing from any shootable state.
    /// </summary>
    public static class HelicopterSystem
    {
        public const float Altitude = 3.0f;
        public const float Speed = 2.6f;
        public const float PreviewSpeed = 3.0f;
        public const int Bursts = 6;
        public const float FireInterval = 0.55f;
        public const float BulletSpeed = 10f;
        public const int BulletDamage = 4;

        public const int MaxHp = 60;
        /// <summary>Generous on purpose — an arc THROUGH the hover disc should reward the player.</summary>
        public const float HitRadius = 1.15f;
        public static readonly float HitRadiusSq = HitRadius * HitRadius;

        public const float CrashSpinDeg = 260f;
        public const float CrashGravity = 3.2f;
        /// <summary>A brief lurch UP at the killing hit, before the fall takes over.</summary>
        public const float CrashLurchVy = 0.6f;
        public const float CrashGroundY = 0.22f;   // hull height — the fireball is at hull contact
        public const float CrashClearMargin = 8.0f;

        /// <summary>Cosmetic altitude bob. Purely visual; nothing reads it for gameplay.</summary>
        public static float BobY(float age) => Altitude + Mathf.Sin(age * 2.3f) * 0.09f;

        /// <summary>Only a REAL gameplay heli is shootable — the pre-battle flyby is scenery.</summary>
        public static bool IsShootable(HeliMode mode)
            => mode != HeliMode.Preview && mode != HeliMode.Crashing;

        /// <summary>
        /// Wounded gunships RETREAT instead of gun-running. That is the shoot-down counter-play:
        /// hitting it at all changes what it does, rather than only mattering if you kill it.
        /// </summary>
        public static bool IsWounded(int hp, int maxHp) => hp < maxHp;

        public readonly struct StepResult
        {
            public readonly HelicopterEntity Heli;     // null once it has left or hit the ground
            public readonly bool SpawnedCrashFireball;

            public StepResult(HelicopterEntity heli, bool fireball)
            {
                Heli = heli;
                SpawnedCrashFireball = fireball;
            }
        }

        /// <summary>
        /// Advances the machine one tick. ALWAYS runs, regardless of game phase: a gunship
        /// mid-exit or mid-crash keeps moving after victory or defeat, and a heli falling when
        /// the battle ends still explodes rather than silently despawning.
        /// </summary>
        public static StepResult Step(HelicopterEntity h, float dt, GamePhase phase,
                                      IReadOnlyList<UnitEntity> playerUnits,
                                      IReadOnlyList<UnitEntity> enemyUnits,
                                      IReadOnlyList<StructureEntity> structures)
        {
            if (h == null) return new StepResult(null, false);

            float age = h.Age + dt;
            float bob = BobY(age);
            float cooled = Mathf.Max(h.FireCooldown - dt, 0f);

            switch (h.Mode)
            {
                case HeliMode.Preview:
                case HeliMode.GunRun:
                {
                    // Crosses right to left, despawning well past the player's edge.
                    float newX = h.X + h.Vx * dt;
                    float exitX = (playerUnits.Count > 0 ? playerUnits.Min(u => u.X) : -8f) - 6f;
                    if (newX < exitX) return new StepResult(null, false);
                    return new StepResult(
                        h with { X = newX, Y = bob, Age = age, FireCooldown = cooled }, false);
                }

                case HeliMode.Entering:
                {
                    float newX = h.X + h.Vx * dt;
                    if (newX <= h.HoverX)
                        return new StepResult(
                            h with { X = h.HoverX, Y = bob, Vx = 0f, Age = age, Mode = HeliMode.Hovering },
                            false);
                    return new StepResult(h with { X = newX, Y = bob, Age = age }, false);
                }

                case HeliMode.Hovering:
                {
                    // Battle ended while it waited: leave cosmetically, with no bursts left so
                    // the turn handover is never held open by a gunship nobody is fighting.
                    if (phase != GamePhase.Playing)
                        return new StepResult(
                            h with { Mode = HeliMode.GunRun, Vx = -Speed, Y = bob, Age = age },
                            false);
                    return new StepResult(h with { Y = bob, Age = age }, false);
                }

                case HeliMode.Retreating:
                {
                    // Wounded bug-out: back off the way it came, firing nothing.
                    float newX = h.X + h.Vx * dt;
                    var rear = enemyUnits.Select(u => u.X)
                        .Concat(structures.Where(s => !s.Definition.isPlayerSide).Select(s => s.X))
                        .ToList();
                    float exitX = (rear.Count > 0 ? rear.Max() : 8f) + 8f;
                    if (newX > exitX) return new StepResult(null, false);
                    return new StepResult(h with { X = newX, Y = bob, Age = age }, false);
                }

                case HeliMode.Crashing:
                {
                    float vy = h.Vy - CrashGravity * dt;
                    float newY = h.Y + vy * dt;
                    if (newY <= CrashGroundY)
                        return new StepResult(null, true);      // fireball at hull contact
                    return new StepResult(
                        h with
                        {
                            X = h.X + h.Vx * dt,
                            Y = newY,
                            Vy = vy,
                            Age = age,
                            Rotation = h.Rotation + CrashSpinDeg * dt,
                        },
                        false);
                }
            }

            return new StepResult(h, false);
        }

        /// <summary>
        /// Applies a hit. Below zero HP it starts CRASHING with a brief upward lurch; otherwise
        /// a wounded hoverer breaks off and retreats rather than completing its gun run.
        /// </summary>
        public static HelicopterEntity ApplyHit(HelicopterEntity h, int damage)
        {
            if (h == null || !IsShootable(h.Mode)) return h;

            int newHp = h.Hp - damage;
            if (newHp <= 0)
            {
                return h with
                {
                    Hp = 0,
                    Mode = HeliMode.Crashing,
                    Vy = CrashLurchVy,
                    BurstsLeft = 0,   // a falling heli must not hold the turn handover open
                };
            }

            if (h.Mode == HeliMode.Hovering || h.Mode == HeliMode.GunRun)
            {
                return h with { Hp = newHp, Mode = HeliMode.Retreating, Vx = Speed, BurstsLeft = 0 };
            }

            return h with { Hp = newHp };
        }

        /// <summary>
        /// Whether a projectile's swept path this tick passed through the hover disc. Uses the
        /// same swept segment as unit collision, for the same reason: a fast round must not
        /// tunnel through the disc between two ticks.
        /// </summary>
        public static bool IsHitBy(HelicopterEntity h, ProjectileEntity p)
        {
            if (h == null || !IsShootable(h.Mode)) return false;
            if (!p.OwnerIsPlayer) return false;          // the enemy never shoots its own gunship
            if (p.IsHeliShot) return false;              // nor do its own door-gunner rounds
            return SweptCollision.SegmentDistanceSq(p.PrevX, p.PrevY, p.X, p.Y, h.X, h.Y)
                   <= HitRadiusSq;
        }

        /// <summary>
        /// Fires a door-gunner burst if the cooldown has elapsed and bursts remain. Returns the
        /// round, or null. Gunner rounds are flagged IsHeliShot so the volley-follow camera
        /// excludes them — otherwise the heli drags the camera off the ground volley.
        /// </summary>
        public static ProjectileEntity TryFire(HelicopterEntity h, IReadOnlyList<UnitEntity> playerUnits,
                                               int projectileId, System.Random random)
        {
            if (h == null || h.Mode != HeliMode.GunRun) return null;
            if (h.BurstsLeft <= 0 || h.FireCooldown > 0f) return null;
            if (playerUnits.Count == 0) return null;

            var target = playerUnits[random.Next(playerUnits.Count)];
            float dx = target.X - h.X;
            float dy = target.Y - h.Y;
            float len = Mathf.Max(Mathf.Sqrt(dx * dx + dy * dy), 0.001f);

            return new ProjectileEntity(
                Id: projectileId,
                X: h.X, Y: h.Y, Z: 0f,
                Vx: dx / len * BulletSpeed,
                Vy: dy / len * BulletSpeed,
                Vz: 0f,
                Damage: BulletDamage,
                OwnerIsPlayer: false)
            {
                IsHeliShot = true,
            };
        }

        /// <summary>Bookkeeping after a burst leaves the gun.</summary>
        public static HelicopterEntity ConsumeBurst(HelicopterEntity h)
            => h with { BurstsLeft = h.BurstsLeft - 1, FireCooldown = FireInterval };
    }
}
