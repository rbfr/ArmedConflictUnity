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
        // Shed-piece sizing band, shared with the destruction rubble so the two kinds of
        // wreckage read as one material.
        const float ChunkPieceMinSize = 0.10f;
        const float ChunkPieceMaxSize = 0.30f;
        const float ChunkShedVy = 0.5f;
        const float ChunkShedSpreadVx = 0.9f;

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
            var dyingUnits = StepRagdolls(s.DyingUnits, dt, s.Structures);
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
            var enemyUnits = ApplyDamage(s.EnemyUnits, hits, dt, out var enemyKilled);
            var playerUnits = ApplyDamage(s.PlayerUnits, hits, dt, out var playerKilled);

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

            // --- 4b. STRUCTURES SHED THEIR OWN GEOMETRY ------------------------------------
            //
            // A damaged structure loses named `chunk_N` groups from its model in ascending N,
            // and the tick drops the SAME group as falling rubble from exactly where that
            // geometry stood. The gap in the silhouette plus the pile at its foot is the damage
            // read — it needs no decal, and it persists for the battle.
            //
            // Both halves derive from ShedChunkCount, so the geometry that vanishes and the
            // rubble that appears can never be different groups. The port had the data, the
            // entity field and the curve, and nothing called any of them: destruction threw ten
            // random cubes sized off `size`, which is why a hit building shed bricks that had
            // never been part of it.
            if (structures.Any(st => st.Definition.damageChunks.Count > 0
                                  && StructureDamage.ShedChunkCount(
                                         st.HpFraction, st.Definition.damageChunks.Count) > st.ShedChunks))
            {
                var shedPieces = new List<DebrisPiece>(debris);
                var after = new List<StructureEntity>(structures.Count);
                foreach (var st in structures)
                {
                    int groups = st.Definition.damageChunks.Count;
                    int shed = groups == 0 ? 0
                             : StructureDamage.ShedChunkCount(st.HpFraction, groups);
                    if (groups == 0 || shed <= st.ShedChunks) { after.Add(st); continue; }

                    // The model origin, where every chunk offset is measured from.
                    float baseY = st.Y - st.Definition.size / 2f;
                    for (int index = st.ShedChunks; index < shed; index++)
                    {
                        var chunk = st.Definition.damageChunks[index];

                        // Split the group along its LONGEST axis, which is how these groups are
                        // built — a sandbag course is a row of bags, a wall plate is one slab —
                        // so a row scatters as a row instead of dropping as one long bar.
                        var dims = new[] { chunk.sizeX, chunk.sizeY, chunk.sizeZ };
                        int longest = dims[0] >= dims[1] && dims[0] >= dims[2] ? 0
                                    : dims[1] >= dims[2] ? 1 : 2;
                        float span = dims[longest];
                        int pieces = Mathf.Max(chunk.pieces, 1);
                        dims[longest] = span / pieces;

                        // Debris renders as a CUBE of one edge, so a piece is sized from its
                        // VOLUME, never the mean of its dimensions. The mean is dominated by the
                        // long axis of a flat plate: a wide tier's wall plate is 1.25 x 0.75 x
                        // 0.20, mean 0.73 — three times the largest destruction chunk, which read
                        // on device as slabs leaning against a wall bigger than the wall.
                        float size = Mathf.Clamp(
                            Mathf.Pow(Mathf.Max(dims[0] * dims[1] * dims[2], 1e-6f), 1f / 3f),
                            ChunkPieceMinSize, ChunkPieceMaxSize);

                        for (int k = 0; k < pieces && shedPieces.Count < DebrisSlots; k++)
                        {
                            float along = pieces == 1 ? 0f : ((k + 0.5f) / pieces - 0.5f) * span;
                            float sz = size * (0.8f + 0.35f * (float)random.NextDouble());
                            shedPieces.Add(new DebrisPiece(
                                Id: nextDebris++,
                                DefinitionId: st.Definition.id,
                                Accent: (index + k) % 3 == 0,
                                X: st.X + chunk.offsetX + (longest == 0 ? along : 0f),
                                Y: baseY + chunk.offsetY + (longest == 1 ? along : 0f),
                                Z: st.Z + chunk.offsetZ + (longest == 2 ? along : 0f),
                                // Barely thrown: it is coming loose under its own weight, so it
                                // reads as falling OFF the building rather than being launched.
                                Vx: ((float)random.NextDouble() - 0.5f) * 2f * ChunkShedSpreadVx,
                                Vy: ChunkShedVy * (1f + (float)random.NextDouble()),
                                Rotation: (float)random.NextDouble() * 360f,
                                RotationSpeed: ((float)random.NextDouble() - 0.5f) * 240f,
                                Size: sz,
                                Ttl: CosmeticSystems.DebrisRubbleTtl));
                        }
                    }
                    after.Add(st with { ShedChunks = shed });
                }
                structures = after;
                debris = shedPieces;
            }
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

            // --- 7b. MID-BATTLE EVENTS ----------------------------------------------------
            //
            // Boss phases and reinforcement waves. EventSystems has decided these correctly since
            // the port and NOTHING EVER ASKED IT — `bossPhases` and `reinforcementWaves` were read
            // only by BattleRunner, and only to size the pools. Three campaign levels are authored
            // around them, so without this the Sovereign never appears and the "reinforcements"
            // never arrive.
            var triggeredBoss = new HashSet<int>(s.TriggeredBossPhases);
            var triggeredWaves = new HashSet<int>(s.TriggeredReinforcementWaves);
            string bossAnnouncement = s.BossAnnouncement;
            float bossTimer = Mathf.Max(0f, s.BossAnnouncementTimer - dt);
            if (bossTimer <= 0f) bossAnnouncement = null;
            string telegraph = s.TelegraphText;

            if (phase == GamePhase.Playing && level != null)
            {
                // A structure counts as destroyed once it has left the live list — collapse
                // propagation already removed it there, so no separate "destroyed ever" set has
                // to be carried on the state and kept in sync.
                var liveStructureIds = new HashSet<int>(structures.Select(st => st.Id));
                var runtimeIdByLevelId = new Dictionary<string, int>();
                var destroyedEver = new HashSet<int>();
                for (int i = 0; i < level.structures.Count; i++)
                {
                    string lid = level.structures[i].id;
                    if (string.IsNullOrEmpty(lid)) continue;
                    int runtimeId = LevelBuilder.StructureIdBase + i;
                    runtimeIdByLevelId[lid] = runtimeId;
                    if (!liveStructureIds.Contains(runtimeId)) destroyedEver.Add(runtimeId);
                }

                for (int i = 0; i < level.bossPhases.Count; i++)
                {
                    var trigger = level.bossPhases[i];
                    if (!EventSystems.ShouldTriggerBossPhase(
                            i, trigger, triggeredBoss,
                            lid => EventSystems.IsTriggerDefeated(lid, runtimeIdByLevelId,
                                                                 destroyedEver, level, enemyUnits)))
                        continue;

                    enemyUnits = Spawn(enemyUnits, level, trigger.spawnGroups,
                                       EventSystems.BossWaveIdBase + i * 100, random);
                    triggeredBoss.Add(i);
                    bossAnnouncement = trigger.announcement;
                    bossTimer = EventSystems.BossAnnouncementSeconds;
                }

                // The telegraph is recomputed from scratch every tick rather than latched, so it
                // clears itself the moment the wave lands or the turn moves on. A latched warning
                // is one that eventually gets left on screen.
                telegraph = null;
                for (int i = 0; i < level.reinforcementWaves.Count; i++)
                {
                    var w = level.reinforcementWaves[i];
                    if (triggeredWaves.Contains(i)) continue;
                    if (EventSystems.ReinforcementWaveBeat(w.arrivesOnTurn, turnNumber)
                        == EventSystems.WaveTriggerBeat.Telegraph)
                        telegraph = w.telegraphText;
                }

                for (int i = 0; i < level.reinforcementWaves.Count; i++)
                {
                    var wave = level.reinforcementWaves[i];
                    if (triggeredWaves.Contains(i)) continue;
                    // ARRIVE only. The telegraph beat is a HUD concern (Phase F) and must not
                    // consume the wave — spending it on the warning is how a telegraphed wave
                    // ends up never arriving.
                    if (EventSystems.ReinforcementWaveBeat(wave.arrivesOnTurn, turnNumber)
                        != EventSystems.WaveTriggerBeat.Arrive) continue;

                    enemyUnits = Spawn(enemyUnits, level, wave.spawnGroups,
                                       EventSystems.ReinforcementWaveIdBase + i * 100, random);
                    triggeredWaves.Add(i);
                    bossAnnouncement = wave.announcement;
                    bossTimer = EventSystems.BossAnnouncementSeconds;
                }
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
                TriggeredBossPhases = triggeredBoss,
                TelegraphText = telegraph,
                TriggeredReinforcementWaves = triggeredWaves,
                BossAnnouncement = bossAnnouncement,
                BossAnnouncementTimer = bossTimer,
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
                                            float dt, out int killed)
        {
            killed = 0;
            var outp = new List<UnitEntity>(units.Count);
            foreach (var u in units)
            {
                // The since-hit clock has to advance for EVERY unit, hit or not — this is the one
                // place per tick that sees them all. Advancing it only in the damaged branch
                // starts a bar that nothing ever takes down, and the unit wears it for the rest
                // of the battle.
                float age = CosmeticSystems.StepHitAge(u.LastHitAge, dt);
                if (!hits.UnitDamage.TryGetValue(u.Id, out int dmg))
                {
                    outp.Add(age == u.LastHitAge ? u : u with { LastHitAge = age });
                    continue;
                }
                int hp = u.Hp - dmg;
                if (hp <= 0) { killed++; continue; }
                outp.Add(u with
                {
                    Hp = hp,
                    KnockbackAge = hits.ExplosiveHitUnitIds.Contains(u.Id) ? 0f : u.KnockbackAge,
                    // Re-armed from zero on every hit, so a unit under sustained fire keeps its
                    // bar up instead of having it expire mid-bombardment.
                    LastHitAge = 0f,
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

        /// <summary>
        /// Stops a thrown body at a structure's face and rests it on a structure's roof.
        ///
        /// Corpses used to sail straight THROUGH buildings, which is the one place a purely
        /// cosmetic system stops looking cosmetic: a body passing through a bunker says the
        /// bunker is not there. Blocks on EVERY structure, not just the opposing side's —
        /// projectiles are allowed through friendly walls so a garrison can fire over its own
        /// fortress, but a body has no such excuse.
        /// </summary>
        static void BlockOnStructures(IReadOnlyList<StructureEntity> structures,
                                      float fromX, float y, ref float x, ref float vx,
                                      ref float restY)
        {
            foreach (var st in structures)
            {
                CollisionSystem.StructureBox(st, out float minX, out float maxX,
                                             out float baseY, out float topY);

                // Resting ON it: horizontally over the box and at or below its roof. Checked
                // first, because a body that cleared the wall should land on the roof rather than
                // be shoved back off the face it already passed.
                if (x > minX && x < maxX && y >= baseY) restY = Mathf.Max(restY, topY);

                // Stopped BY it: only while the body is inside the box's vertical span. Above the
                // roof it is flying over, which is a real trajectory and not a miss.
                if (y > topY || y < baseY) continue;
                if (x <= minX || x >= maxX) continue;

                // Put it back against the face it came in through, and kill the horizontal
                // travel. Approaching from the right means resting against the right-hand face.
                if (fromX >= maxX) { x = maxX; vx = 0f; }
                else if (fromX <= minX) { x = minX; vx = 0f; }
                else vx = 0f;                      // spawned inside: just stop, do not teleport
            }
        }

        static List<DyingUnitEntity> StepRagdolls(IReadOnlyList<DyingUnitEntity> dying, float dt,
                                                  IReadOnlyList<StructureEntity> structures)
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
                        float rx = d.X + nvx * dt;
                        float restRolled = CosmeticSystems.RagdollRestY(d.Rotation);
                        BlockOnStructures(structures, d.X, y, ref rx, ref nvx, ref restRolled);
                        outp.Add(d with
                        {
                            X = rx, Y = restRolled,
                            Vx = nvx, Vy = 0f,
                            Rotation = d.Rotation + rollSpeed * dt,
                            RotationSpeed = rollSpeed, Age = age,
                        });
                    }
                    else
                    {
                        CosmeticSystems.StepFlop(d.Rotation, d.RotationSpeed, dt,
                                                 out float rot, out float rotSpeed);
                        float sx = d.X, svx = 0f;
                        float restFlop = CosmeticSystems.RagdollRestY(rot);
                        BlockOnStructures(structures, d.X, y, ref sx, ref svx, ref restFlop);
                        outp.Add(d with
                        {
                            X = sx, Y = restFlop, Vx = 0f, Vy = 0f,
                            Rotation = rot, RotationSpeed = rotSpeed, Age = age,
                        });
                    }
                }
                else
                {
                    float ax = d.X + d.Vx * dt, avx = d.Vx, aRest = rest;
                    BlockOnStructures(structures, d.X, y, ref ax, ref avx, ref aRest);
                    // A body that hit a wall mid-flight keeps falling — it just stops travelling.
                    // Clamping to the roof here is what rests it on top when it cleared the wall.
                    outp.Add(d with
                    {
                        X = ax, Y = Mathf.Max(y, aRest), Vy = y <= aRest ? 0f : vy, Vx = avx,
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
                    OwnerIsPlayer: true)
                {
                    // The PLAYER's volley used to leave all three of these at their defaults, so
                    // every round the player fired was a plain bullet with no splash and a 1x
                    // structure multiplier — while AutoFire, three methods down, set them
                    // correctly. The rocket trooper's 6x against buildings and the grenadier's 2x
                    // existed only under the debug driver, and a rocket rendered as a tracer.
                    Type = u.Definition != null ? u.Definition.projectileType : ProjectileType.Bullet,
                    SplashRadius = u.Definition != null ? u.Definition.splashRadius : 0f,
                    StructureDamageMultiplier =
                        u.Definition != null ? u.Definition.structureDamageMultiplier : 1f,
                });
            }

            int shellSlot = s.NextShellSlot;
            // velocityBoost, straight from the data. The tank sits BEHIND the infantry line, so a
            // shell thrown at the line's own velocity lands short of where the volley does; the
            // boost is what puts the heavy round with the rest of the shot. Z is NOT boosted —
            // the boost exists to buy range, and scaling the cross-field component with it would
            // also throw the shell sideways.
            var shells = CannonShells(s, ref shellSlot,
                (muzzle, c) => new Vector3(aimVelocity.x * c.velocityBoost,
                                           aimVelocity.y * c.velocityBoost,
                                           aimVelocity.z));
            rounds.AddRange(shells);

            return s with
            {
                Projectiles = rounds,
                NextBulletSlot = slot,
                NextShellSlot = shellSlot,
                TankShellsRemaining = s.TankShellsRemaining - shells.Count,
                TurnPhase = TurnPhase.Resolving,
                TurnSide = TurnSide.Player,
            };
        }

        /// <summary>Shell ids sit in their own band, like the bullets' 10000 and the enemy's
        /// 20000. Raw ids have to stay globally unique — hit tracking keys off them.</summary>
        const int ShellIdBase = 30000;

        /// <summary>
        /// The player tank's contribution: one heavy shell per player-side structure that mounts
        /// a cannon, added to the volley the infantry just threw.
        ///
        /// It is OFF-ROSTER — there is no UnitEntity behind it, which is why it is built from the
        /// STRUCTURE rather than in the unit loop. Losing every soldier does not silence the tank,
        /// and the tank is not a body the enemy can shoot at.
        ///
        /// Ammo is FINITE (`CannonSpec.ammoPerBattle`, totalled into TankShellsRemaining when the
        /// level is built) and `CannonArmed` gates it, so a level can field a tank with a cold gun
        /// and no battle can be won by leaning on the heavy round every turn.
        ///
        /// NO JITTER, unlike the infantry. They get a random spread because a volley of identical
        /// arcs reads as one round drawn N times; a rifled gun puts its round where it is pointed,
        /// and a wandering tank shell reads as a bug rather than as spread.
        /// </summary>
        /// <param name="solve">Muzzle position and cannon spec in, launch velocity out. The two
        /// callers want different things — the player's drag scaled by the gun's boost, and Auto's
        /// own solve at a target — and passing the velocity in ready-made cannot work, because it
        /// is the MUZZLE that Auto has to solve from and only this method knows where that is.
        /// </param>
        static List<ProjectileEntity> CannonShells(GameState s, ref int slot,
                                                  System.Func<Vector3, CannonSpec, Vector3> solve)
        {
            var shells = new List<ProjectileEntity>();
            int allowed = s.CannonArmed ? s.TankShellsRemaining : 0;
            if (allowed <= 0) return shells;

            foreach (var st in s.Structures)
            {
                if (shells.Count >= allowed) break;
                var def = st.Definition;
                if (def == null || !def.isPlayerSide || !def.hasCannon || def.cannon == null) continue;
                var c = def.cannon;

                // A structure entity's Y is the CENTRE of its box (placement.y * worldScale +
                // size/2), so the muzzle offset is taken from the BASE. Read off the centre it
                // would hang the muzzle half a tank in the air.
                var muzzle = new Vector3(st.X + c.muzzleOffsetX,
                                         st.Y - def.size / 2f + c.muzzleOffsetY,
                                         st.Z);
                var v = solve(muzzle, c);

                shells.Add(new ProjectileEntity(
                    Id: ShellIdBase + slot++,
                    X: muzzle.x, Y: muzzle.y, Z: muzzle.z,
                    Vx: v.x, Vy: v.y, Vz: v.z,
                    Damage: c.damage,
                    OwnerIsPlayer: true)
                {
                    Type = ProjectileType.Shell,
                    SplashRadius = c.splashRadius,
                    StructureDamageMultiplier = c.structureDamageMultiplier,
                });
            }
            return shells;
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
        /// <summary>
        /// Adds a mid-battle group to the enemy line.
        ///
        /// Goes through LevelBuilder.BuildUnits so an arrival is constructed exactly like the
        /// opening roster — same formation, same jitter, same garrison resolution. Rebuilding
        /// that here would be a second definition of what a unit is, and the two would drift.
        ///
        /// The id base is spaced per wave (EventSystems' BossWaveIdBase / ReinforcementWaveIdBase,
        /// stepped by 100) because ids must never collide with the living: PortSelfTest asserts
        /// unit and structure ids never overlap, and a reused id would retarget an existing unit's
        /// damage onto the newcomer.
        /// </summary>
        static List<UnitEntity> Spawn(List<UnitEntity> enemyUnits, LevelDefinitionSO level,
                                      List<EnemyGroup> groups, int idBase, System.Random random)
        {
            if (groups == null || groups.Count == 0) return enemyUnits;
            var arrivals = LevelBuilder.BuildUnits(level, groups, isPlayerSide: false,
                                                   startId: idBase, random: random);
            if (arrivals.Count == 0) return enemyUnits;
            var combined = new List<UnitEntity>(enemyUnits);
            combined.AddRange(arrivals);
            return combined;
        }

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

            // The tank fires under Auto too, solving from its OWN muzzle at the enemy nearest to
            // it. Note the standing caveat: Auto targets enemy UNITS, so on a rig whose only
            // enemies are off-screen immortals it throws the shell past the buildings and
            // structure HP never moves. Judge a tank round against a structure with a real drag.
            int shellSlot = s.NextShellSlot;
            var shells = CannonShells(s, ref shellSlot, (muzzle, c) =>
            {
                UnitEntity nearest = null;
                float best = float.MaxValue;
                foreach (var e in s.EnemyUnits)
                {
                    float dx = e.X - muzzle.x, dy = e.Y - muzzle.y;
                    float d = dx * dx + dy * dy;
                    if (d < best) { best = d; nearest = e; }
                }
                if (nearest == null) return Vector3.zero;
                return TrajectoryPhysics.SolveVelocity(
                    muzzle, new Vector3(nearest.X, nearest.Y, nearest.Z), angleDegrees: 50f);
            });
            rounds.AddRange(shells);

            return s with
            {
                Projectiles = rounds,
                NextBulletSlot = slot,
                NextShellSlot = shellSlot,
                TankShellsRemaining = s.TankShellsRemaining - shells.Count,
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
