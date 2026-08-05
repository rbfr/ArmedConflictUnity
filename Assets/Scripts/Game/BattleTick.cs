using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using ArmedConflict.Data;

namespace ArmedConflict.Game
{
    /// <summary>
    /// The assembly: one state transition, running the ported systems in order.
    ///
    /// ORDER IS THE CONTRACT, and most of it is load bearing:
    ///   1. step projectiles          (never cull yet)
    ///   2. resolve collisions        (so a round crossing y=0 through a target still registers)
    ///   3. apply damage / deaths
    ///   4. structures + collapse propagation
    ///   5. cull spent rounds
    ///   6. cosmetics (ragdolls, shake)
    ///   7. turn flow
    ///   8. camera
    ///
    /// Steps 6-8 run on EVERY path, including once the battle is over. That is not tidiness:
    /// shake decayed only inside the combat block once, so a level ending on a killing volley
    /// froze it and jittered the whole victory screen.
    /// </summary>
    public static class BattleTick
    {
        /// <summary>Verbose per-tick hit logging. Leave off outside an investigation.</summary>
        public static bool TraceHits = false;

        /// <summary>
        /// BOUNDED pools, never monotonic ids. Zero-disposal registries that grow for the whole
        /// battle were a real lag bug on Filament; the cap is what keeps a long level flat.
        /// </summary>
        public const int ScorchSlots = 36;
        public const int DebrisSlots = 96;
        /// <summary>Shared empty map, so clearing the enemy pose never allocates.</summary>
        static readonly Dictionary<int, float> EmptyAim = new();


        public static GameState Step(GameState s, float rawDt, LevelDefinitionSO level,
                                     System.Random random)
        {
            float dt = ProjectileSystem.ClampDt(rawDt);

            // --- 1. physics, always ------------------------------------------------------
            var stepped = ProjectileSystem.StepAll(s.Projectiles, dt, s.WindAccelZ);
            var explosions = ProjectileSystem.AdvanceExplosions(s.Explosions, dt);

            var helicopter = s.Helicopter;
            var heliResult = HelicopterSystem.Step(helicopter, dt, s.Phase,
                                                   s.PlayerUnits, s.EnemyUnits, s.Structures);
            helicopter = heliResult.Heli;
            int nextExplosionSlot = s.NextExplosionSlot;
            if (heliResult.SpawnedCrashFireball)
            {
                explosions = new List<ExplosionEntity>(explosions)
                {
                    new(nextExplosionSlot++, s.Helicopter.X, 0.3f, 0f)
                        { Scale = 1.1f, IsStructureHit = true },
                };
            }

            // Ragdolls and shake advance regardless of phase.
            var dyingUnits = StepRagdolls(s.DyingUnits, dt);
            float shake = CosmeticSystems.DecayShake(s.ShakeIntensity, dt);

            if (s.Phase != GamePhase.Playing)
            {
                // Cosmetic-only path. Still advances everything above, so a finished battle
                // settles instead of freezing mid-motion.
                //
                // It also RE-FRAMES. Leaving the camera on its last gameplay target pointed it at
                // the enemy anchor — a stable per-level value that, once the enemy roster and its
                // structures are gone, is simply a patch of empty ground. The battle ended on the
                // most legible shot available and then panned away from it. Frame the survivors
                // instead: they are what the player wants to look at, win or lose.
                var survivors = s.PlayerUnits.Count > 0 ? s.PlayerUnits : s.EnemyUnits;
                float? endX = s.CameraFollowX;
                float endXVel = s.CameraFollowXVelocity;
                float endZ = s.CameraFollowZ ?? 11f;
                float endZVel = s.CameraFollowZVelocity;

                if (survivors.Count > 0)
                {
                    float mean = survivors.Average(u => u.X);
                    float half = Mathf.Max((survivors.Max(u => u.X) - survivors.Min(u => u.X)) / 2f, 1.5f);
                    float x = endX ?? mean;
                    SpringFollow.Step(ref x, ref endXVel, mean, dt, 0.35f);
                    endX = x;
                    SpringFollow.Step(ref endZ, ref endZVel,
                                      CameraDirector.TargetZ(half + 1.2f, s.StaticCamera, s.StaticCamZ),
                                      dt, 0.35f);
                }

                return s with
                {
                    Projectiles = ProjectileSystem.Cull(stepped, new HashSet<int>(),
                                                        s.PlayerUnits, s.EnemyUnits, s.Structures),
                    Explosions = explosions,
                    DyingUnits = dyingUnits,
                    Debris = StepDebris(s.Debris, dt),
                    Helicopter = helicopter,
                    NextExplosionSlot = nextExplosionSlot,
                    ShakeIntensity = shake,
                    CameraFollowX = endX,
                    CameraFollowXVelocity = endXVel,
                    CameraFollowZ = endZ,
                    CameraFollowZVelocity = endZVel,
                };
            }

            // --- 2. collisions BEFORE any cull -------------------------------------------
            var hits = CollisionSystem.ResolveHits(stepped, s.EnemyUnits, s.PlayerUnits, s.Structures);

            // Per-tick hit tracing. Off by default — it is the instrument that settled "why did
            // 19 rounds kill nobody" (they were wounding: 32 HP against 8 damage is four hits,
            // and most of the volley was being absorbed by the outpost's collision box).
            if (TraceHits)
            {
                if (hits.UnitDamage.Count > 0 || hits.StructureDamage.Count > 0)
                    Debug.Log($"[Tick] hits: units={hits.UnitDamage.Count} " +
                              $"structs={hits.StructureDamage.Count} spent={hits.HitProjectileIds.Count}");
                var landed = ProjectileSystem.GroundImpacts(stepped, hits.HitProjectileIds);
                if (landed.Count > 0)
                    Debug.Log($"[Tick] {landed.Count} rounds hit dirt at x=" +
                              string.Join(",", landed.Take(3).Select(p2 => p2.X.ToString("F2"))));
            }

            // --- 3. damage and deaths ----------------------------------------------------
            var enemyUnits = ApplyDamage(s.EnemyUnits, hits, out var enemyKilled);
            var playerUnits = ApplyDamage(s.PlayerUnits, hits, out var playerKilled);

            // --- 4. structures and collapse ----------------------------------------------
            var structures = s.Structures.ToList();
            if (hits.StructureDamage.Count > 0)
            {
                structures = structures
                    .Select(st => hits.StructureDamage.TryGetValue(st.Id, out int d)
                                  ? st with { Hp = st.Hp - d } : st)
                    .ToList();
            }
            var surviving = structures.Where(st => st.Hp > 0).ToList();
            var directlyDestroyed = structures.Where(st => st.Hp <= 0).Select(st => st.Id);
            var destroyedIds = CollisionSystem.PropagateCollapse(surviving, directlyDestroyed);
            structures = surviving.Where(st => !destroyedIds.Contains(st.Id)).ToList();

            // A garrison dies with what it stands on, however much HP it personally has left.
            if (destroyedIds.Count > 0)
            {
                var fell = enemyUnits.Where(u => u.StandingOnStructureId is int id
                                                 && destroyedIds.Contains(id)).ToList();
                if (fell.Count > 0)
                {
                    enemyKilled += fell.Count;
                    dyingUnits = dyingUnits.Concat(fell.Select(RagdollFrom)).ToList();
                    enemyUnits = enemyUnits.Where(u => !(u.StandingOnStructureId is int id2
                                                         && destroyedIds.Contains(id2))).ToList();
                }
            }

            dyingUnits = dyingUnits
                .Concat(hits.UnitDamage.Keys
                    .Select(id => s.EnemyUnits.FirstOrDefault(u => u.Id == id)
                               ?? s.PlayerUnits.FirstOrDefault(u => u.Id == id))
                    .Where(u => u != null && u.Hp - hits.UnitDamage[u.Id] <= 0)
                    .Select(RagdollFrom))
                .ToList();

            // Tally what actually happened this tick, so the presentation layer reads FACTS
            // rather than guessing from list-length deltas. A round leaving the field on the
            // side bounds is not a ground impact, and a bullet striking a soldier is not a blast
            // — inferring both from "the projectile list got shorter" got both wrong.
            int groundImpactsThisTick = ProjectileSystem.GroundImpacts(stepped, hits.HitProjectileIds).Count;
            int blastsThisTick = 0, structureImpactsThisTick = 0;
            foreach (var d in hits.Detonations)
            {
                bool splash = d.IsGroundBurst || d.HitStructureId != null;
                if (d.HitStructureId != null) structureImpactsThisTick++;
                if (splash) blastsThisTick++;
            }
            int woundedThisTick = 0;
            foreach (var kv in hits.UnitDamage)
            {
                var target = s.EnemyUnits.FirstOrDefault(u => u.Id == kv.Key)
                          ?? s.PlayerUnits.FirstOrDefault(u => u.Id == kv.Key);
                if (target != null && target.Hp - kv.Value > 0) woundedThisTick++;
            }

            // Detonations become explosions. The renderer needs them, and so does the audio —
            // a hit with no blast reads as the round having vanished.
            if (hits.Detonations.Count > 0)
            {
                var withBlasts = new List<ExplosionEntity>(explosions);
                foreach (var d in hits.Detonations)
                {
                    withBlasts.Add(new ExplosionEntity(nextExplosionSlot++, d.X, d.Y, d.Z)
                    {
                        // A structure hit throws a bigger blast than a body hit; a ground burst
                        // sits between the two. Scale is the only cue the renderer has.
                        Scale = d.HitStructureId != null ? 0.9f : d.IsGroundBurst ? 0.6f : 0.45f,
                        IsEnemyFire = !d.ByPlayer,
                        IsStructureHit = d.HitStructureId != null,
                    });
                }
                explosions = withBlasts;
            }

            // --- 4b. lasting marks: scorch and rubble -------------------------------------
            var scorches = s.Scorches;
            int nextScorch = s.NextScorchSlot;
            if (groundImpactsThisTick > 0)
            {
                var marks = new List<ScorchMark>(scorches);
                foreach (var p2 in ProjectileSystem.GroundImpacts(stepped, hits.HitProjectileIds))
                {
                    // Merge into a nearby scar rather than stacking identical decals in the same
                    // patch — that both looks wrong and burns slots from a bounded pool.
                    int hit = CosmeticSystems.FindMergeTarget(marks, p2.X, p2.Z);
                    if (hit >= 0)
                    {
                        marks[hit] = marks[hit] with { Scale = CosmeticSystems.GrowScorch(marks[hit].Scale) };
                    }
                    else if (marks.Count < ScorchSlots)
                    {
                        marks.Add(new ScorchMark(nextScorch++, p2.X, p2.Z));
                    }
                    else
                    {
                        // Bounded round-robin, never a monotonic id — the pool size caps the
                        // registry rather than letting it grow for the whole battle.
                        marks[nextScorch % ScorchSlots] = new ScorchMark(nextScorch, p2.X, p2.Z);
                        nextScorch++;
                    }
                }
                scorches = marks;
            }

            var debris = StepDebris(s.Debris, dt);
            int nextDebris = s.NextDebrisSlot;
            if (destroyedIds.Count > 0)
            {
                var pieces = new List<DebrisPiece>(debris);
                foreach (var id in destroyedIds)
                {
                    var st = s.Structures.FirstOrDefault(x => x.Id == id);
                    if (st == null) continue;
                    float halfW = (st.Definition.hasHitWidth ? st.Definition.hitWidth
                                                             : st.Definition.size) / 2f;
                    // Rubble PERSISTS for the rest of the level (ttl = MaxValue) and sleeps once
                    // settled. A wrecked structure that leaves nothing behind reads as if it was
                    // deleted rather than destroyed.
                    for (int i = 0; i < 10 && pieces.Count < DebrisSlots; i++)
                    {
                        float ang = (float)random.NextDouble() * Mathf.PI * 2f;
                        float speed = 1.5f + (float)random.NextDouble() * 2.5f;
                        pieces.Add(new DebrisPiece(
                            Id: nextDebris++,
                            DefinitionId: st.Definition.id,
                            Accent: i % 3 == 0,
                            X: st.X + ((float)random.NextDouble() - 0.5f) * halfW * 1.6f,
                            Y: st.Y + (float)random.NextDouble() * st.Definition.size * 0.6f,
                            Z: st.Z,
                            Vx: Mathf.Cos(ang) * speed,
                            Vy: 1.5f + (float)random.NextDouble() * 2.5f,
                            Rotation: (float)random.NextDouble() * 360f,
                            RotationSpeed: ((float)random.NextDouble() - 0.5f) * 500f,
                            Size: st.Definition.size * (0.10f + 0.10f * (float)random.NextDouble()),
                            Ttl: CosmeticSystems.DebrisRubbleTtl));
                    }
                }
                debris = pieces;
            }

            // --- 5. cull ------------------------------------------------------------------
            var projectiles = ProjectileSystem.Cull(stepped, hits.HitProjectileIds,
                                                    playerUnits, enemyUnits, structures);

            // --- 6. cosmetics -------------------------------------------------------------
            shake = CosmeticSystems.DecayShake(
                CosmeticSystems.AddShakeForKills(s.ShakeIntensity, enemyKilled + playerKilled), dt);

            // --- 7. turn flow -------------------------------------------------------------
            var phase = TurnFlow.ResolvePhase(playerUnits.Count, enemyUnits.Count);
            var turnSide = s.TurnSide;
            var turnPhase = s.TurnPhase;
            var enemyAim = s.EnemyAimDegrees;
            float handover = s.TurnHandoverDelay;
            int turnNumber = s.TurnNumber;

            if (phase == GamePhase.Playing && turnPhase == TurnPhase.Resolving)
            {
                var gate = TurnFlow.EvaluateVolley(
                    projectiles.Count, s.Projectiles.Count, handover, turnSide,
                    helicopter?.BurstsLeft ?? 0, s.Skirmishes.Count);

                switch (gate)
                {
                    case TurnFlow.VolleyGate.JustLanded:
                        handover = TurnFlow.PostVolleyPauseSeconds;
                        break;
                    case TurnFlow.VolleyGate.Pausing:
                        handover -= dt;
                        break;
                    case TurnFlow.VolleyGate.ReadyToHandOver:
                        if (turnSide == TurnSide.Player)
                        {
                            turnSide = TurnSide.Enemy;
                            turnPhase = TurnPhase.EnemyWindup;
                        }
                        else
                        {
                            turnSide = TurnSide.Player;
                            turnPhase = TurnPhase.Aiming;
                            turnNumber++;
                            // The enemy volley is over, so its raised rifles are over with it.
                            // Left set, the line would hold last turn's elevation for the whole
                            // battle — and units are POOLED, so it would outlive the shooters.
                            enemyAim = EmptyAim;
                        }
                        handover = 0f;
                        break;
                }
            }

            // --- 8. camera ----------------------------------------------------------------
            // CAMERA X IS ALWAYS A SPRING. It used to be nulled outside a volley, and the
            // renderer then fell back to a phase anchor — so every phase change TELEPORTED the
            // camera across the field instead of panning. Keeping one continuous spring and only
            // changing its TARGET is what makes the whole choreography read as camera work.
            var groundVolley = projectiles.Where(p => !p.IsHeliShot).ToList();
            bool chasing = phase == GamePhase.Playing
                        && turnPhase == TurnPhase.Resolving
                        && groundVolley.Count > 0;

            float followXVel = s.CameraFollowXVelocity;
            float followX;
            if (chasing)
            {
                followX = CameraDirector.FollowVolley(s.CameraFollowX, followXVel, groundVolley,
                                                      playerUnits, enemyUnits, structures,
                                                      false, dt, out followXVel);
            }
            else
            {
                // Pan to the phase's anchor rather than snapping to it. Slower than the bullet
                // cam by design: a chase should feel urgent, a reposition should not.
                float anchorTarget = turnPhase switch
                {
                    TurnPhase.Aiming => s.PlayerCamXAnchor,
                    TurnPhase.PlayerScout => s.EnemyCamXAnchor,
                    TurnPhase.EnemyWindup => s.EnemyCamXAnchor,
                    TurnPhase.Resolving => turnSide == TurnSide.Enemy ? s.PlayerCamXAnchor
                                                                     : s.EnemyCamXAnchor,
                    _ => s.PlayerCamXAnchor,
                };
                followX = s.CameraFollowX ?? anchorTarget;
                SpringFollow.Step(ref followX, ref followXVel, anchorTarget, dt,
                                  CameraDirector.MarchEscortSmoothTime);
            }

            // STABLE half-widths, captured at level load. Deriving these from live spans made the
            // zoom twitch on every casualty, because the span of a shrinking set changes
            // discontinuously even when no survivor has moved.
            float halfWidth = CameraDirector.PhaseHalfWidth(
                turnPhase, turnSide,
                s.PlayerCamHalfWidth, s.EnemyCamHalfWidth, s.EnemyCamHalfWidth,
                0f, false, s.PlayerCamHalfWidth, false);
            float targetZ = CameraDirector.TargetZ(halfWidth + 1.2f, s.StaticCamera, s.StaticCamZ);
            float followZ = s.CameraFollowZ ?? targetZ;
            float followZVel = s.CameraFollowZVelocity;
            SpringFollow.Step(ref followZ, ref followZVel, targetZ, dt, 0.12f);

            return s with
            {
                Projectiles = projectiles,
                Explosions = explosions,
                PlayerUnits = playerUnits,
                EnemyUnits = enemyUnits,
                Structures = structures,
                DyingUnits = dyingUnits,
                Helicopter = helicopter,
                NextExplosionSlot = nextExplosionSlot,
                ShakeIntensity = shake,
                Phase = phase,
                TurnSide = turnSide,
                TurnPhase = turnPhase,
                EnemyAimDegrees = enemyAim,
                TurnHandoverDelay = handover,
                TurnNumber = turnNumber,
                CameraFollowX = followX,
                CameraFollowXVelocity = followXVel,
                CameraFollowZ = followZ,
                CameraFollowZVelocity = followZVel,
                TotalPlayerKills = s.TotalPlayerKills + enemyKilled,
                TotalEnemyKills = s.TotalEnemyKills + playerKilled,
                TotalGroundImpacts = s.TotalGroundImpacts + groundImpactsThisTick,
                TotalStructureImpacts = s.TotalStructureImpacts + structureImpactsThisTick,
                TotalWoundedHits = s.TotalWoundedHits + woundedThisTick,
                TotalBlasts = s.TotalBlasts + blastsThisTick,
                Scorches = scorches,
                NextScorchSlot = nextScorch,
                Debris = debris,
                NextDebrisSlot = nextDebris,
            };
        }

        static List<UnitEntity> ApplyDamage(IReadOnlyList<UnitEntity> units, HitResult hits,
                                            out int killed)
        {
            killed = 0;
            var outp = new List<UnitEntity>(units.Count);
            foreach (var u in units)
            {
                if (!hits.UnitDamage.TryGetValue(u.Id, out int dmg)) { outp.Add(u); continue; }
                int hp = u.Hp - dmg;
                if (hp <= 0) { killed++; continue; }
                outp.Add(u with
                {
                    Hp = hp,
                    KnockbackAge = hits.ExplosiveHitUnitIds.Contains(u.Id) ? 0f : u.KnockbackAge,
                });
            }
            return outp;
        }

        static DyingUnitEntity RagdollFrom(UnitEntity u)
            => new(u.Id, u.Definition, u.IsPlayerSide, u.X, u.Y, u.Z,
                   Vx: u.IsPlayerSide ? -1.5f : 1.5f, Vy: 2.5f, RotationSpeed: 220f);

        /// <summary>
        /// Advances debris. Grounded rubble that has stopped moving is put to SLEEP: its motion
        /// is zeroed and it is flagged, so nothing keeps integrating it and IsVisuallyIdle can
        /// still go true on a field littered with permanent rubble.
        /// </summary>
        static List<DebrisPiece> StepDebris(IReadOnlyList<DebrisPiece> debris, float dt)
        {
            var outp = new List<DebrisPiece>(debris.Count);
            foreach (var d in debris)
            {
                if (d.Asleep) { outp.Add(d); continue; }

                float ttl = d.IsRubble ? d.Ttl : d.Ttl - dt;
                if (!d.IsRubble && ttl <= 0f) continue;

                float vy = d.Vy - TrajectoryPhysics.Gravity * dt;
                float y = d.Y + vy * dt;
                float x = d.X + d.Vx * dt;
                float vx = d.Vx;
                float rotSpeed = d.RotationSpeed;
                bool grounded = false;

                float rest = d.Size * 0.5f;
                if (y <= rest)
                {
                    y = rest;
                    grounded = true;
                    vy = -vy * 0.25f;                                   // a chunk of masonry barely bounces
                    vx *= CosmeticSystems.DecayPerTick60(0.86f, dt);
                    rotSpeed *= CosmeticSystems.DecayPerTick60(0.80f, dt);
                    if (Mathf.Abs(vy) < 0.15f) vy = 0f;
                }

                if (CosmeticSystems.ShouldSleep(d.IsRubble, grounded, vx, vy, rotSpeed))
                {
                    outp.Add(d with { X = x, Y = y, Vx = 0f, Vy = 0f, RotationSpeed = 0f, Asleep = true });
                    continue;
                }

                outp.Add(d with
                {
                    X = x, Y = y, Vx = vx, Vy = vy, Ttl = ttl,
                    Rotation = d.Rotation + rotSpeed * dt,
                    RotationSpeed = rotSpeed,
                });
            }
            return outp;
        }

        static List<DyingUnitEntity> StepRagdolls(IReadOnlyList<DyingUnitEntity> dying, float dt)
        {
            var outp = new List<DyingUnitEntity>(dying.Count);
            foreach (var d in dying)
            {
                float age = d.Age + dt;
                if (CosmeticSystems.RagdollExpired(age)) continue;

                float vy = d.Vy - TrajectoryPhysics.Gravity * dt;
                float y = d.Y + vy * dt;
                float rest = CosmeticSystems.RagdollRestY(d.Rotation);

                if (y <= rest)
                {
                    if (CosmeticSystems.ShouldRoll(d.Vx))
                    {
                        CosmeticSystems.StepRoll(d.Vx, dt, out float nvx, out float rollSpeed);
                        outp.Add(d with
                        {
                            X = d.X + nvx * dt, Y = CosmeticSystems.RagdollRestY(d.Rotation),
                            Vx = nvx, Vy = 0f,
                            Rotation = d.Rotation + rollSpeed * dt,
                            RotationSpeed = rollSpeed, Age = age,
                        });
                    }
                    else
                    {
                        CosmeticSystems.StepFlop(d.Rotation, d.RotationSpeed, dt,
                                                 out float rot, out float rotSpeed);
                        outp.Add(d with
                        {
                            Y = CosmeticSystems.RagdollRestY(rot), Vx = 0f, Vy = 0f,
                            Rotation = rot, RotationSpeed = rotSpeed, Age = age,
                        });
                    }
                }
                else
                {
                    outp.Add(d with
                    {
                        X = d.X + d.Vx * dt, Y = y, Vy = vy,
                        Rotation = d.Rotation + d.RotationSpeed * dt, Age = age,
                    });
                }
            }
            return outp;
        }

        /// <summary>Fires the player's volley — one round per living player unit.</summary>
        public static GameState FireVolley(GameState s, Vector3 aimVelocity, System.Random random)
        {
            if (s.Phase != GamePhase.Playing || s.TurnPhase != TurnPhase.Aiming) return s;
            if (s.PlayerUnits.Count == 0) return s;

            var rounds = new List<ProjectileEntity>(s.Projectiles);
            int slot = s.NextBulletSlot;
            foreach (var u in s.PlayerUnits)
            {
                // A little spread per shooter, so a volley reads as many soldiers firing rather
                // than one round drawn N times.
                float jitter = ((float)random.NextDouble() - 0.5f) * 0.25f;
                rounds.Add(new ProjectileEntity(
                    Id: 10000 + slot++,
                    X: u.X, Y: u.Y + 0.35f, Z: u.Z,
                    Vx: aimVelocity.x + jitter, Vy: aimVelocity.y + jitter, Vz: 0f,
                    Damage: u.Definition != null ? u.Definition.damage : 8,
                    OwnerIsPlayer: true));
            }

            return s with
            {
                Projectiles = rounds,
                NextBulletSlot = slot,
                TurnPhase = TurnPhase.Resolving,
                TurnSide = TurnSide.Player,
            };
        }

        /// <summary>
        /// AUTO FIRE — the debug volley. Every unit independently targets its NEAREST enemy and
        /// solves for a fixed 50-degree arc with NO jitter, so it lands roughly every round on
        /// target.
        ///
        /// This is a test harness, not the player, and the distinction matters: it is the fastest
        /// way to drive a level from adb, and it is useless for judging balance. Difficulty has
        /// to be measured with real drags, which spread.
        /// </summary>
        public static GameState AutoFire(GameState s)
        {
            if (s.Phase != GamePhase.Playing || s.TurnPhase != TurnPhase.Aiming) return s;
            if (s.PlayerUnits.Count == 0 || s.EnemyUnits.Count == 0) return s;

            var rounds = new List<ProjectileEntity>(s.Projectiles);
            int slot = s.NextBulletSlot;

            foreach (var u in s.PlayerUnits)
            {
                UnitEntity target = null;
                float best = float.MaxValue;
                foreach (var e in s.EnemyUnits)
                {
                    float dx = e.X - u.X, dy = e.Y - u.Y;
                    float d = dx * dx + dy * dy;
                    if (d < best) { best = d; target = e; }
                }
                if (target == null) continue;

                float muzzleY = u.Y + 0.35f;
                var v = TrajectoryPhysics.SolveVelocity(
                    new Vector3(u.X, muzzleY, u.Z),
                    new Vector3(target.X, target.Y, target.Z),
                    angleDegrees: 50f);

                int shots = u.Definition != null ? Mathf.Max(u.Definition.projectilesPerVolley, 1) : 1;
                for (int i = 0; i < shots; i++)
                {
                    rounds.Add(new ProjectileEntity(
                        Id: 10000 + slot++,
                        X: u.X, Y: muzzleY, Z: u.Z,
                        Vx: v.x, Vy: v.y, Vz: 0f,
                        Damage: u.Definition != null ? u.Definition.damage : 8,
                        OwnerIsPlayer: true)
                    {
                        Type = u.Definition != null ? u.Definition.projectileType : ProjectileType.Bullet,
                        SplashRadius = u.Definition != null ? u.Definition.splashRadius : 0f,
                        StructureDamageMultiplier =
                            u.Definition != null ? u.Definition.structureDamageMultiplier : 1f,
                    });
                }
            }

            return s with
            {
                Projectiles = rounds,
                NextBulletSlot = slot,
                TurnPhase = TurnPhase.Resolving,
                TurnSide = TurnSide.Player,
            };
        }

        /// <summary>The enemy's answering volley, aimed with jitter at random player units.</summary>
        public static GameState FireEnemyVolley(GameState s, System.Random random)
        {
            if (s.EnemyUnits.Count == 0 || s.PlayerUnits.Count == 0) return s;

            var rounds = new List<ProjectileEntity>(s.Projectiles);
            var aim = new Dictionary<int, float>(s.EnemyUnits.Count);
            int slot = s.NextBulletSlot;
            foreach (var e in s.EnemyUnits)
            {
                var target = s.PlayerUnits[random.Next(s.PlayerUnits.Count)];
                var v = EnemyAI.AimAt(new Vector3(e.X, e.Y + 0.35f, e.Z),
                                      new Vector3(target.X, target.Y, target.Z));

                // The elevation this unit is ABOUT TO FIRE at, read back off the velocity rather
                // than drawn again — AimAt picks its arc at random, so a second draw would pose
                // the rifle at an angle no round takes. Measured off |Vx| so a unit shooting
                // leftward still reports a positive elevation.
                aim[e.Id] = Mathf.Atan2(v.y, Mathf.Abs(v.x)) * Mathf.Rad2Deg;

                rounds.Add(new ProjectileEntity(
                    Id: 20000 + slot++,
                    X: e.X, Y: e.Y + 0.35f, Z: e.Z,
                    Vx: v.x, Vy: v.y, Vz: 0f,
                    Damage: e.Definition != null ? e.Definition.damage : 8,
                    OwnerIsPlayer: false));
            }

            return s with
            {
                Projectiles = rounds,
                NextBulletSlot = slot,
                EnemyAimDegrees = aim,
                TurnPhase = TurnPhase.Resolving,
                TurnSide = TurnSide.Enemy,
            };
        }
    }
}
