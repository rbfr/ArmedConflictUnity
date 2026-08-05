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
                return s with
                {
                    Projectiles = ProjectileSystem.Cull(stepped, new HashSet<int>(),
                                                        s.PlayerUnits, s.EnemyUnits, s.Structures),
                    Explosions = explosions,
                    DyingUnits = dyingUnits,
                    Helicopter = helicopter,
                    NextExplosionSlot = nextExplosionSlot,
                    ShakeIntensity = shake,
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
                        }
                        handover = 0f;
                        break;
                }
            }

            // --- 8. camera ----------------------------------------------------------------
            float? followX = s.CameraFollowX;
            float followXVel = s.CameraFollowXVelocity;
            var groundVolley = projectiles.Where(p => !p.IsHeliShot).ToList();
            if (phase == GamePhase.Playing && turnPhase == TurnPhase.Resolving && groundVolley.Count > 0)
            {
                followX = CameraDirector.FollowVolley(followX, followXVel, groundVolley,
                                                      playerUnits, enemyUnits, structures,
                                                      false, dt, out followXVel);
            }
            else if (turnPhase != TurnPhase.Resolving)
            {
                followX = null;
                followXVel = 0f;
            }

            float halfWidth = FrameHalfWidth(turnPhase, turnSide, playerUnits, enemyUnits, structures);
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
                TurnHandoverDelay = handover,
                TurnNumber = turnNumber,
                CameraFollowX = followX,
                CameraFollowXVelocity = followXVel,
                CameraFollowZ = followZ,
                CameraFollowZVelocity = followZVel,
                TotalPlayerKills = s.TotalPlayerKills + enemyKilled,
                TotalEnemyKills = s.TotalEnemyKills + playerKilled,
            };
        }

        static float FrameHalfWidth(TurnPhase turnPhase, TurnSide turnSide,
                                    IReadOnlyList<UnitEntity> playerUnits,
                                    IReadOnlyList<UnitEntity> enemyUnits,
                                    IReadOnlyList<StructureEntity> structures)
        {
            float playerHalf = HalfSpan(playerUnits.Select(u => u.X).ToList());
            var enemyXs = enemyUnits.Select(u => u.X)
                .Concat(structures.Where(st => !st.Definition.isPlayerSide)
                    .SelectMany(st => new[]
                    {
                        st.X - (st.Definition.hasHitWidth ? st.Definition.hitWidth : st.Definition.size) / 2f,
                        st.X + (st.Definition.hasHitWidth ? st.Definition.hitWidth : st.Definition.size) / 2f,
                    }))
                .ToList();
            float enemyHalf = HalfSpan(enemyXs);
            return CameraDirector.PhaseHalfWidth(turnPhase, turnSide, playerHalf, enemyHalf,
                                                 enemyHalf, 0f, false, playerHalf, false);
        }

        static float HalfSpan(IReadOnlyList<float> xs)
            => xs.Count == 0 ? 3f : Mathf.Max((xs.Max() - xs.Min()) / 2f, 1f);

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

        /// <summary>The enemy's answering volley, aimed with jitter at random player units.</summary>
        public static GameState FireEnemyVolley(GameState s, System.Random random)
        {
            if (s.EnemyUnits.Count == 0 || s.PlayerUnits.Count == 0) return s;

            var rounds = new List<ProjectileEntity>(s.Projectiles);
            int slot = s.NextBulletSlot;
            foreach (var e in s.EnemyUnits)
            {
                var target = s.PlayerUnits[random.Next(s.PlayerUnits.Count)];
                var v = EnemyAI.AimAt(new Vector3(e.X, e.Y + 0.35f, e.Z),
                                      new Vector3(target.X, target.Y, target.Z));
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
                TurnPhase = TurnPhase.Resolving,
                TurnSide = TurnSide.Enemy,
            };
        }
    }
}
