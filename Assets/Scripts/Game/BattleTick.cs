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
        const float ChunkPieceMinSize = 0.05f;
        const float ChunkPieceMaxSize = 0.14f;
        const float ChunkShedVy = 0.5f;
        const float ChunkShedSpreadVx = 0.9f;

        /// <summary>
        /// How flat a settled ruin slab lies. At this camera's ~6° the HEIGHT of a lump is most
        /// of what reads, so a cube of rubble looks like a crate and a slab looks like masonry
        /// that has come down.
        /// </summary>
        const float RuinSquash = 0.3f;

        static readonly Dictionary<int, float> EmptyAim = new();
        static readonly Dictionary<int, Vector3> EmptyLaunch = new();


        /// <param name="ammoCatalog">
        /// Optional, and only the BURN needs it: an incendiary tick is applied on the handover
        /// into the enemy windup, and its damage is data. Null means no burn, which is exactly
        /// Standard's behaviour, so every caller and test written before ammo is unchanged.
        /// </param>
        public static GameState Step(GameState s, float rawDt, LevelDefinitionSO level,
                                     System.Random random, AmmoCatalogSO ammoCatalog = null)
        {
            float dt = ProjectileSystem.ClampDt(rawDt);
            int burnDamage = AmmoModifiers.From(ammoCatalog, s.SelectedAmmo).BurnDamage;

            // --- 1. physics, always ------------------------------------------------------
            var stepped = ProjectileSystem.StepAll(s.Projectiles, dt, s.WindAccelZ);
            var explosions = ProjectileSystem.AdvanceExplosions(s.Explosions, dt);

            // THE AIRCRAFT FLIES ON EVERY TICK PATH, not only during its own phase. It hands the
            // turn to the volley the moment its bomb lands and is still exiting frame while that
            // volley resolves — so motion tied to TurnPhase.AirstrikeRun froze it in mid-air, at
            // the size the camera happened to zoom to, for the rest of the battle. Measured on
            // device 2026-08-10, second build of the beat.
            //
            // The same family as "anything that decays must decay on EVERY tick path".
            AirstrikePlaneEntity planeNow;

            // AND SO DO ITS GUNS AND ITS BOMB, for exactly the same reason. The rake covers the
            // whole enemy position, which routinely reaches PAST the bomb's impact; and now that
            // the volley and the pass are aligned on their impacts, the aircraft is often still
            // short of its own drop point while the phase has already moved on to Resolving. While
            // these lived inside the run's own step the surplus rounds were simply never fired —
            // no error, no log, just a burst that stopped early. A deliberate over-long rake
            // dropped 11 of 28 rounds that way and every check stayed green.
            //
            // THE SPAWN DELAY GATES ALL THREE. Until it expires the aircraft has not been released
            // onto the field: it must not move, must not shoot, and must not drop.
            float spawnDelay = Mathf.Max(0f, s.AirstrikeSpawnDelay - dt);
            int grenadeSlotNow = s.NextGrenadeSlot;
            if (s.AirstrikeSpawnDelay <= 0f)
            {
                planeNow = StepPlane(s.AirstrikePlane, dt);
                (planeNow, stepped) = StepStrafe(planeNow, stepped);
                (planeNow, stepped, grenadeSlotNow) =
                    StepBomb(planeNow, stepped, grenadeSlotNow);
            }
            else
            {
                // Held back. The entity exists so nothing has to be recomputed at release time,
                // but it is not on the field yet — the renderer hides it on the same condition.
                planeNow = s.AirstrikePlane;
            }

            float volleyDelay = Mathf.Max(0f, s.PendingVolleyDelay - dt);
            // Plane just left: start the return beat so the camera can
            // get home to the player line before they fire.
            if (s.AirstrikePlane != null && planeNow == null && s.PendingVolleyAim != null)
                volleyDelay = AirstrikeReturnSeconds;

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
            var dyingUnits = StepRagdolls(s.DyingUnits, dt, s.Structures, s.Wrecks);
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
                //
                // NOT ON THE TICK THE LAST ENEMY FALLS. Rob, 2026-08-13: the killing blow has to
                // sit for a couple of seconds or it never registers. VictoryCamHold is that beat;
                // once it runs out the spring to the survivors is the same as it was.
                float victoryCamHold = Mathf.Max(0f, s.VictoryCamHold - dt);
                float endCollapseHold = Mathf.Max(0f, s.CollapseHold - dt);
                float endCollapseAnchorX = s.CollapseHoldAnchorX;
                float endCollapseHalf = s.CollapseHoldHalfWidth;
                var survivors = s.PlayerUnits.Count > 0 ? s.PlayerUnits : s.EnemyUnits;
                float? endX = s.CameraFollowX;
                float endXVel = s.CameraFollowXVelocity;
                float endZ = s.CameraFollowZ ?? 11f;
                float endZVel = s.CameraFollowZVelocity;

                if (CameraDirector.CollapseIsFollowing(endCollapseHold))
                {
                    // Last-garrison collapse ends the battle the same tick the
                    // bodies launch. Ride them on this path too or the throw
                    // plays as a still frame of the ruin.
                    CameraDirector.CollapseFollowFrame(dyingUnits,
                                                       ref endCollapseAnchorX,
                                                       ref endCollapseHalf);
                    float x = endX ?? endCollapseAnchorX;
                    SpringFollow.Step(ref x, ref endXVel, endCollapseAnchorX, dt,
                                      CameraDirector.CollapseFollowSmoothTime);
                    endX = x;
                    SpringFollow.Step(ref endZ, ref endZVel,
                                      CameraDirector.TargetZ(
                                          endCollapseHalf + CameraDirector.FramePad,
                                          s.StaticCamera, s.StaticCamZ),
                                      dt, CameraDirector.CollapseFollowSmoothTime);
                }
                else if (s.Phase == GamePhase.Victory && victoryCamHold > 0f)
                {
                    // Stay put. A spring that has not been given a new target does not move.
                }
                else if (survivors.Count > 0)
                {
                    float mean = survivors.Average(u => u.X);
                    float half = Mathf.Max((survivors.Max(u => u.X) - survivors.Min(u => u.X)) / 2f, 1.5f);
                    float x = endX ?? mean;
                    SpringFollow.Step(ref x, ref endXVel, mean, dt, 0.35f);
                    endX = x;
                    SpringFollow.Step(ref endZ, ref endZVel,
                                      CameraDirector.TargetZ(half + CameraDirector.FramePad,
                                                             s.StaticCamera, s.StaticCamZ),
                                      dt, 0.35f);
                }

                // The relief squad keeps running even though the battle is over. A jogging man
                // frozen mid-stride the instant victory lands is the same artefact as a value
                // that only decays inside the combat block — and the men are on screen, because
                // this path deliberately re-frames onto the survivors.
                var endMarch = StepMarch(s.PlayerUnits, dt, out bool endMarching);

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
                    PlayerUnits = endMarch,
                    PlayerMarchInProgress = endMarching,
                    CameraFollowX = endX,
                    CameraFollowXVelocity = endXVel,
                    CameraFollowZ = endZ,
                    CameraFollowZVelocity = endZVel,
                    // DECAYS ON EVERY TICK PATH, including the one taken once the battle is over.
                    // A melee mutual kill can be the blow that ENDS the battle, and a hold left
                    // frozen at 1.5 on the victory screen is a value that never decays again —
                    // the exact failure the standing rule in CLAUDE.md is written about.
                    MeleeHold = Mathf.Max(0f, s.MeleeHold - dt),
                    CollapseHold = endCollapseHold,
                    CollapseHoldAnchorX = endCollapseAnchorX,
                    CollapseHoldHalfWidth = endCollapseHalf,
                    VictoryCamHold = victoryCamHold,
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

            // The relief squad jogs in from the player's edge to its formation slots. They are
            // full roster members from the moment they spawn — a volley fired mid-march simply
            // launches from wherever each man has got to.
            playerUnits = StepMarch(playerUnits, dt, out bool marching);

            // INCENDIARY: mark the SURVIVORS of an incendiary hit. CollisionSystem has populated
            // IncendiaryHitUnitIds since the port and nothing has ever read it. The dead are
            // filtered out deliberately — a burn tick landing on a body already falling is a
            // damage event with nothing to damage, and it would inflate the kill tally twice.
            var burning = new HashSet<int>(s.BurningEnemyIds);
            if (hits.IncendiaryHitUnitIds.Count > 0)
                foreach (var u in enemyUnits)
                    if (hits.IncendiaryHitUnitIds.Contains(u.Id)) burning.Add(u.Id);

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
            float collapseHold = s.CollapseHold;
            float collapseHoldAnchorX = s.CollapseHoldAnchorX;
            float collapseHoldHalfWidth = s.CollapseHoldHalfWidth;
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

                    // Hold the camera on the throw. The last round and the launch
                    // are the same tick; without this the follow dies with the
                    // projectile and the bodies fly off-screen. The first beat
                    // rides the falling garrison; after that the hold only
                    // freezes the windup so the spring can pan back to the
                    // live line before they fire.
                    collapseHold = CameraDirector.CollapseHoldSeconds;
                }
            }
            else collapseHold = Mathf.Max(0f, collapseHold - dt);

            if (collapseHold > 0f)
            {
                if (CameraDirector.CollapseIsFollowing(collapseHold))
                    CameraDirector.CollapseFollowFrame(dyingUnits,
                                                       ref collapseHoldAnchorX,
                                                       ref collapseHoldHalfWidth);
                else
                    CameraDirector.CollapseReturnFrame(enemyUnits,
                                                       ref collapseHoldAnchorX,
                                                       ref collapseHoldHalfWidth);
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
                                // TRANSIENT, not permanent. These fall off as the building takes
                                // damage, and there are up to a dozen groups over a structure's
                                // life — kept forever they piled up across the field as loose
                                // blocks with nothing to do with where the building stood, which
                                // is most of what "blocks everywhere" was. The lasting record of
                                // damage is the structure's OWN missing geometry; the lasting
                                // record of destruction is the ruin.
                                Ttl: CosmeticSystems.DebrisTtlSeconds));
                        }
                    }
                    after.Add(st with { ShedChunks = shed });
                }
                structures = after;
                debris = shedPieces;
            }
            // Wrecks age on EVERY path, including after the battle is over — a collapse
            // that only ticks in the combat block freezes mid-fold on the victory screen.
            var wrecks = new List<WreckEntity>(s.Wrecks.Count + destroyedIds.Count);
            foreach (var w in s.Wrecks)
                wrecks.Add(w.Age >= GameState.WreckCollapseSeconds
                    ? w
                    : w with { Age = w.Age + dt });

            if (destroyedIds.Count > 0)
            {
                var pieces = new List<DebrisPiece>(debris);
                foreach (var id in destroyedIds)
                {
                    var st = s.Structures.FirstOrDefault(x => x.Id == id);
                    if (st == null) continue;
                    // Same box the wreck GO uses: BASE on the dirt (or the
                    // tier floor), standWidth not hitWidth. The standing
                    // centre plus hitWidth was an invisible deck in mid-air.
                    float halfW = (st.Definition.standWidth > 0.01f
                                   ? st.Definition.standWidth
                                   : (st.Definition.hasHitWidth ? st.Definition.hitWidth
                                                                : st.Definition.size)) / 2f;

                    wrecks.Add(new WreckEntity(
                        Id: id,
                        DefinitionId: st.Definition.id,
                        X: st.X,
                        Y: st.Y - st.Definition.size * 0.5f,
                        Z: st.Z,
                        Width: halfW * 2f,
                        Height: st.Definition.size));

                    // An authored collapse IS the ruin. Cube slabs under it would
                    // stack a second wreck on the same footprint.
                    bool authored = !string.IsNullOrEmpty(st.Definition.wreckModelAsset);
                    if (authored)
                    {
                        // The moment of collapse still throws chunks — transient.
                        for (int i = 0; i < 6 && pieces.Count < DebrisSlots; i++)
                        {
                            float ang = (float)random.NextDouble() * Mathf.PI * 2f;
                            float speed = 1.0f + (float)random.NextDouble() * 1.6f;
                            pieces.Add(new DebrisPiece(
                                Id: nextDebris++,
                                DefinitionId: st.Definition.id,
                                Accent: i % 3 == 0,
                                X: st.X + ((float)random.NextDouble() - 0.5f) * halfW,
                                Y: st.Y + (float)random.NextDouble() * st.Definition.size * 0.6f,
                                Z: st.Z,
                                Vx: Mathf.Cos(ang) * speed,
                                Vy: 1.5f + (float)random.NextDouble() * 2.5f,
                                Rotation: (float)random.NextDouble() * 360f,
                                RotationSpeed: ((float)random.NextDouble() - 0.5f) * 500f,
                                Size: Mathf.Min(ChunkPieceMaxSize,
                                    st.Definition.size * (0.035f + 0.025f * (float)random.NextDouble())),
                                Ttl: CosmeticSystems.DebrisTtlSeconds));
                        }
                        continue;
                    }

                    // THE RUIN. Placed, not launched: a row of wide flat slabs lying inside the
                    // structure's OWN FOOTPRINT, already asleep, persisting for the rest of the
                    // level.
                    //
                    // This replaces ten cubes fired off at random angles with permanent ttl. That
                    // version had both halves wrong — the building vanished outright, so nothing
                    // marked where it had been, and its masonry ended up strewn across the field
                    // as loose blocks that never went away. A wreck should sit in the hole the
                    // building left.
                    //
                    // Sizes descend across the row and the tallest sits at the centre, so the pile
                    // reads as a collapsed mound rather than a wall of equal lumps.
                    int slabs = Mathf.Clamp(Mathf.RoundToInt(halfW * 2.6f), 3, 6);
                    for (int i = 0; i < slabs && pieces.Count < DebrisSlots; i++)
                    {
                        float t = slabs == 1 ? 0f : i / (float)(slabs - 1);      // 0..1 across
                        float fromCentre = Mathf.Abs(t - 0.5f) * 2f;             // 1 at the edges
                        float width = st.Definition.size * (0.34f - 0.12f * fromCentre)
                                    * (0.85f + 0.3f * (float)random.NextDouble());
                        pieces.Add(new DebrisPiece(
                            Id: nextDebris++,
                            DefinitionId: st.Definition.id,
                            Accent: i % 3 == 1,
                            // Spread across the footprint, never beyond it.
                            X: st.X + (t - 0.5f) * halfW * 1.7f
                                    + ((float)random.NextDouble() - 0.5f) * 0.12f,
                            Y: width * RuinSquash * 0.5f,                        // resting on the ground
                            Z: st.Z + ((float)random.NextDouble() - 0.5f) * 0.25f,
                            Vx: 0f, Vy: 0f,
                            // A few degrees only. Masonry settles askew; it does not stand on end.
                            Rotation: ((float)random.NextDouble() - 0.5f) * 22f,
                            RotationSpeed: 0f,
                            Size: width,
                            Ttl: CosmeticSystems.DebrisRubbleTtl)
                        {
                            Asleep = true,
                            Squash = RuinSquash,
                        });
                    }

                    // The moment of collapse still throws chunks — but they are TRANSIENT now, so
                    // nothing loose is left on the field once the dust settles.
                    for (int i = 0; i < 6 && pieces.Count < DebrisSlots; i++)
                    {
                        float ang = (float)random.NextDouble() * Mathf.PI * 2f;
                        float speed = 1.0f + (float)random.NextDouble() * 1.6f;
                        pieces.Add(new DebrisPiece(
                            Id: nextDebris++,
                            DefinitionId: st.Definition.id,
                            Accent: i % 3 == 0,
                            X: st.X + ((float)random.NextDouble() - 0.5f) * halfW,
                            Y: st.Y + (float)random.NextDouble() * st.Definition.size * 0.6f,
                            Z: st.Z,
                            Vx: Mathf.Cos(ang) * speed,
                            Vy: 1.5f + (float)random.NextDouble() * 2.5f,
                            Rotation: (float)random.NextDouble() * 360f,
                            RotationSpeed: ((float)random.NextDouble() - 0.5f) * 500f,
                            Size: Mathf.Min(ChunkPieceMaxSize,
                                st.Definition.size * (0.035f + 0.025f * (float)random.NextDouble())),
                            Ttl: CosmeticSystems.DebrisTtlSeconds));
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
            var enemyLaunch = s.EnemyLaunch;
            float handover = s.TurnHandoverDelay;
            int turnNumber = s.TurnNumber;
            float scoutTimer = s.ScoutTimer;
            float tankArriveTimer = s.TankArriveTimer;
            if (phase == GamePhase.Playing && turnPhase == TurnPhase.TankArrive)
            {
                tankArriveTimer = Mathf.Max(0f, tankArriveTimer - dt);
                float ease = TurnFlow.TankArriveEase(tankArriveTimer);
                float want = s.TankParkX - TurnFlow.TankArriveDistance * (1f - ease);
                float shift = 0f;
                var moved = new List<StructureEntity>(structures.Count);
                foreach (var st in structures)
                {
                    if (st.Definition != null && st.Definition.isPlayerSide && st.Definition.hasCannon)
                    {
                        if (shift == 0f) shift = want - st.X;
                        moved.Add(st with { X = st.X + shift });
                    }
                    else moved.Add(st);
                }
                structures = moved;
                if (shift != 0f)
                {
                    var tankIds = new HashSet<int>();
                    foreach (var st in structures)
                        if (st.Definition != null && st.Definition.isPlayerSide && st.Definition.hasCannon)
                            tankIds.Add(st.Id);
                    var riding = new List<UnitEntity>(playerUnits.Count);
                    foreach (var u in playerUnits)
                    {
                        bool rides = u.StandingOnStructureId is int id && tankIds.Contains(id);
                        riding.Add(rides ? u with { X = u.X + shift } : u);
                    }
                    playerUnits = riding;
                }
                if (tankArriveTimer <= 0f)
                {
                    var snapped = new List<UnitEntity>(playerUnits.Count);
                    foreach (var u in playerUnits)
                        snapped.Add(u.MarchTargetX is float slot
                            ? u with { X = slot, MarchTargetX = null }
                            : u);
                    playerUnits = snapped;
                    turnPhase = TurnPhase.PlayerScout;
                }
            }
            else if (phase == GamePhase.Playing && turnPhase == TurnPhase.PlayerScout)
            {
                scoutTimer = Mathf.Max(0f, scoutTimer - dt);
                if (scoutTimer <= 0f) turnPhase = TurnPhase.Aiming;
            }

            // --- 7b. advancing squads and the melee they exist for ------------------------
            //
            // BEFORE the volley gate, because the gate WAITS on `skirmishes.Count` and must see
            // this tick's fights rather than last tick's — a pair that resolved this tick would
            // otherwise hold the turn open for one extra tick, every time.
            //
            // Order within the block is load-bearing: resolve the fights that exist, THEN let
            // newly-arrived fighters claim, THEN march whoever is still walking. Claiming before
            // resolving would let a fighter that died this tick take a victim with it.
            var skirmishes = s.Skirmishes;
            if (phase == GamePhase.Playing)
            {
                var melee = AdvanceSystems.StepSkirmishes(skirmishes, playerUnits, enemyUnits, dt);
                if (melee.KilledPlayers.Count > 0 || melee.KilledEnemies.Count > 0)
                {
                    playerUnits = melee.PlayerUnits;
                    enemyUnits = melee.EnemyUnits;
                    dyingUnits = dyingUnits
                        .Concat(melee.KilledPlayers.Select(RagdollFrom))
                        .Concat(melee.KilledEnemies.Select(RagdollFrom))
                        .ToList();
                    playerKilled += melee.KilledPlayers.Count;
                    enemyKilled += melee.KilledEnemies.Count;
                    // A mutual kill can end the battle, and it is the ONE kill path that is not a
                    // projectile — the burn block below recomputes for the same reason.
                    phase = TurnFlow.ResolvePhase(playerUnits.Count, enemyUnits.Count);
                }
                else
                {
                    playerUnits = melee.PlayerUnits;
                    enemyUnits = melee.EnemyUnits;
                }
                skirmishes = melee.Skirmishes;

                skirmishes = AdvanceSystems.Claim(skirmishes, enemyUnits, playerUnits);

                // THE MARCH RUNS IN THE WINDUP ONLY. That is the one phase whose camera is
                // pointed at the enemy side, so the player watches the gap close before the
                // shooters fire — and the windup countdown is FROZEN while anyone is still
                // walking (see BattleRunner), so the march owns its own beat instead of racing
                // the volley for the same slice of time.
                if (turnPhase == TurnPhase.EnemyWindup && AdvanceSystems.Marching(enemyUnits))
                {
                    enemyUnits = AdvanceSystems.March(
                        enemyUnits, playerUnits, s.Props, skirmishes, dt);
                }
            }

            if (phase == GamePhase.Playing && turnPhase == TurnPhase.Resolving)
            {
                var gate = TurnFlow.EvaluateVolley(
                    projectiles.Count, s.Projectiles.Count, handover, turnSide,
                    helicopter?.BurstsLeft ?? 0, skirmishes.Count);

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

                            // ADVANCING SQUADS BANK THEIR BUDGET HERE, exactly once, on the edge
                            // into the windup they spend it in — the same shape as the burn
                            // below, and for the same reason: one legible step per turn rather
                            // than a continuous drift the player cannot count.
                            //
                            // Overwatch Flare halves this when it is built; nothing arms it yet.
                            enemyUnits = AdvanceSystems.BankBudget(enemyUnits, overwatchFlare: false);

                            // INCENDIARY BURNS HERE, exactly once, on the edge into the windup.
                            // DYNAMISM_DESIGN asks for ONE legible damage event the player can
                            // read as "the fire did that", not a per-second DoT — a DoT would
                            // also need a new per-tick damage pipeline that nothing else wants.
                            // The set is CLEARED as it is spent, so a unit burns once per hit
                            // rather than forever after one incendiary round.
                            if (burning.Count > 0 && burnDamage > 0)
                            {
                                int burnCount = 0, burnKills = 0;
                                var afterBurn = new List<UnitEntity>(enemyUnits.Count);
                                foreach (var u in enemyUnits)
                                {
                                    if (!burning.Contains(u.Id)) { afterBurn.Add(u); continue; }
                                    burnCount++;
                                    int hp = u.Hp - burnDamage;
                                    if (hp > 0) { afterBurn.Add(u with { Hp = hp, LastHitAge = 0f }); continue; }
                                    dyingUnits = dyingUnits.Concat(new[] { RagdollFrom(u) }).ToList();
                                    enemyKilled++; burnKills++;
                                }
                                enemyUnits = afterBurn;
                                phase = TurnFlow.ResolvePhase(playerUnits.Count, enemyUnits.Count);
                                // Kept deliberately. The burn has NO VISUAL yet, so without this
                                // there is no way to confirm from a device that it fired at all —
                                // and this repo has twice declared a working feature broken by
                                // inferring from a detector instead of probing the path. Once per
                                // turn at most, in the same family as the EVENT lines.
                                Debug.Log($"[Burn] {burnCount} burning took {burnDamage} " +
                                          $"({burnKills} died)");
                            }
                            burning.Clear();

                            // Aims NOW, not at the fire. The windup is 1.5s of watching
                            // the enemy line — if the rifles stay at ready until the
                            // rounds leave, there is nothing to watch. Same random draw
                            // FireEnemyVolley will use, so the pose is the shot.
                            var prepared = PrepareEnemyVolley(
                                s with { EnemyUnits = enemyUnits, PlayerUnits = playerUnits },
                                random);
                            enemyAim = prepared.EnemyAimDegrees;
                            enemyLaunch = prepared.EnemyLaunch;
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
                            enemyLaunch = EmptyLaunch;
                        }
                        handover = 0f;
                        break;
                }
            }

            // --- 7c. MID-BATTLE EVENTS ----------------------------------------------------
            //
            // BEFORE the camera, so a boss that walks out of a fallen structure is the
            // thing the frame sizes against THIS tick. Sitting after the camera left the
            // reveal a tick late, still sized for the masonry that had just left.
            //
            // EventSystems has decided these correctly since the port and NOTHING EVER
            // ASKED IT — `bossPhases` and `reinforcementWaves` were read only by
            // BattleRunner, and only to size the pools.
            var triggeredBoss = new HashSet<int>(s.TriggeredBossPhases);
            var triggeredWaves = new HashSet<int>(s.TriggeredReinforcementWaves);
            string bossAnnouncement = s.BossAnnouncement;
            float bossTimer = Mathf.Max(0f, s.BossAnnouncementTimer - dt);
            if (bossTimer <= 0f) bossAnnouncement = null;
            string telegraph = s.TelegraphText;

            var idsBeforeEvents = new HashSet<int>(enemyUnits.Select(u => u.Id));
            int enemyStructBefore = s.Structures.Count(st => st.Definition != null
                                                             && !st.Definition.isPlayerSide);
            if (phase == GamePhase.Playing && level != null)
            {
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

                telegraph = null;
                int telegraphAway = int.MaxValue;
                for (int i = 0; i < level.reinforcementWaves.Count; i++)
                {
                    var w = level.reinforcementWaves[i];
                    if (triggeredWaves.Contains(i)) continue;
                    if (EventSystems.ReinforcementWaveBeat(w.arrivesOnTurn, turnNumber,
                                                          w.telegraphLeadTurns)
                        != EventSystems.WaveTriggerBeat.Telegraph) continue;

                    int away = w.arrivesOnTurn - turnNumber;
                    if (away >= telegraphAway) continue;
                    telegraphAway = away;
                    telegraph = EventSystems.TelegraphLine(w.telegraphLabel, away);
                }

                for (int i = 0; i < level.reinforcementWaves.Count; i++)
                {
                    var wave = level.reinforcementWaves[i];
                    if (triggeredWaves.Contains(i)) continue;
                    if (EventSystems.ReinforcementWaveBeat(wave.arrivesOnTurn, turnNumber,
                                                          wave.telegraphLeadTurns)
                        != EventSystems.WaveTriggerBeat.Arrive) continue;

                    enemyUnits = Spawn(enemyUnits, level, wave.spawnGroups,
                                       EventSystems.ReinforcementWaveIdBase + i * 100, random);
                    triggeredWaves.Add(i);
                    bossAnnouncement = wave.announcement;
                    bossTimer = EventSystems.BossAnnouncementSeconds;
                }
            }

            var arrived = enemyUnits.Where(u => !idsBeforeEvents.Contains(u.Id)).ToList();
            float enemyCamX = s.EnemyCamXAnchor;
            float enemyCamHalf = s.EnemyCamHalfWidth;
            float arrivalCamX = s.ArrivalCamXAnchor;
            float arrivalCamHalf = s.ArrivalCamHalfWidth;
            int enemyStructNow = structures.Count(st => st.Definition != null
                                                        && !st.Definition.isPlayerSide);
            if (arrived.Count > 0)
                LevelBuilder.ArrivalFraming(arrived, out arrivalCamX, out arrivalCamHalf);
            if (arrived.Count > 0 || enemyStructNow != enemyStructBefore)
                LevelBuilder.EnemyFraming(enemyUnits, structures, out enemyCamX, out enemyCamHalf);
            if (bossTimer <= 0f) arrivalCamHalf = 0f;

            // THE MARCH AND THE FIGHT ARE WHAT THE CAMERA IS FOR during a windup that has one.
            // These two arguments were hardcoded to `0f, false` from the port until 2026-08-12:
            // the whole marcher branch of PhaseHalfWidth existed, was never fed, and so the
            // windup always framed the SHOOTERS while the assault walked into the player's line
            // off the left edge. Rob, first device build: "that happens off camera and it's
            // weird."
            var engagedIds = new HashSet<int>(skirmishes.Select(sk => sk.AttackerId));
            var marchingXs = enemyUnits.Where(u => u.AdvanceRemaining > 0f && !engagedIds.Contains(u.Id))
                                       .Select(u => u.X).ToList();
            // BOTH ENDS OF EACH FIGHT. Framing the attacker alone would crop the soldier he is
            // killing, which is the half the player cares about.
            var skirmishXs = skirmishes
                .SelectMany(sk => new[]
                {
                    enemyUnits.FirstOrDefault(u => u.Id == sk.AttackerId)?.X,
                    playerUnits.FirstOrDefault(u => u.Id == sk.VictimId)?.X,
                })
                .Where(x => x.HasValue).Select(x => x.Value).ToList();
            bool marchersActive = marchingXs.Count > 0 || skirmishXs.Count > 0;

            // AssaultFrame: chargers while they are far, the signed-off union at contact.
            float assaultHalfWidth = CameraDirector.AssaultFrame(
                marchingXs, skirmishXs, playerUnits.Select(u => u.X).ToList(),
                out float assaultAnchorX);

            // THE HOLD. While a fight is running the beat is refreshed and the frame recorded;
            // once the list empties it runs down, and the camera stays put for the whole of it.
            // Carried, not recomputed — see GameState.MeleeHold: the fighters are gone from the
            // unit lists by then, so a recomputed frame would snap somewhere else on the tick the
            // hold begins, which is precisely the lurch this is here to prevent.
            float meleeHold = s.MeleeHold;
            float meleeHoldAnchorX = s.MeleeHoldAnchorX;
            float meleeHoldHalfWidth = s.MeleeHoldHalfWidth;
            if (skirmishXs.Count > 0)
            {
                meleeHold = AdvanceSystems.PostMeleeHoldSeconds;
                meleeHoldAnchorX = assaultAnchorX;
                meleeHoldHalfWidth = assaultHalfWidth;
            }
            else meleeHold = Mathf.Max(0f, meleeHold - dt);

            // --- 8. camera ----------------------------------------------------------------
            // CAMERA X IS ALWAYS A SPRING. It used to be nulled outside a volley, and the
            // renderer then fell back to a phase anchor — so every phase change TELEPORTED the
            // camera across the field instead of panning. Keeping one continuous spring and only
            // changing its TARGET is what makes the whole choreography read as camera work.
            var groundVolley = projectiles.Where(p => !p.IsHeliShot).ToList();

            // A FIGHT OWNS THE CAMERA UNTIL IT IS OVER, in whatever phase it is running.
            //
            // Holding it inside the windup branch alone was not enough (Rob, second device build:
            // "when the actual melee attack takes place, the camera should stay on that until
            // it's complete"). A skirmish SPANS phases — the handover gate deliberately waits for
            // it — so a fight still playing when the windup ends handed the frame straight to the
            // volley chase, which is the one target guaranteed to be somewhere else. The player
            // watched the charge arrive and then had the kill itself yanked away.
            //
            // It outranks the volley chase rather than sharing with it: two subjects at opposite
            // ends of the field average out to a frame containing neither.
            bool fighting = phase == GamePhase.Playing
                            && (skirmishXs.Count > 0 || meleeHold > 0f);

            // Collapse hold is the melee hold's sibling: it owns the camera
            // for both beats (ride the fall, then pan to the live line).
            // Melee still outranks it — two subjects at opposite ends
            // average to neither.
            bool watchingCollapse = phase == GamePhase.Playing && collapseHold > 0f;

            bool chasing = !fighting && !watchingCollapse
                        && phase == GamePhase.Playing
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
                float arriveAnchor = s.PlayerCamXAnchor;
                if (!fighting && turnPhase == TurnPhase.TankArrive)
                    CameraDirector.TankArriveFrame(structures, playerUnits,
                                                   out arriveAnchor, out _);
                float anchorTarget = fighting
                    ? (skirmishXs.Count > 0 ? assaultAnchorX : meleeHoldAnchorX)
                    : watchingCollapse ? collapseHoldAnchorX
                    : turnPhase switch
                {
                    TurnPhase.Aiming => s.PlayerCamXAnchor,
                    TurnPhase.TankArrive => arriveAnchor,
                    TurnPhase.PlayerScout => enemyCamX,
                    // THE WINDUP ANCHOR MOVES WITH THE ASSAULT when there is one. A fixed
                    // per-level enemy anchor is correct for a shooting line that stands still and
                    // wrong for a force that walks the width of the field — see
                    // CameraDirector.EnemyWindupAnchorX for the three beats.
                    // MARCH sits on the chargers (so a riot shield is readable) until the
                    // gap closes; CONTACT takes the signed-off union. See AssaultFrame.
                    // With nobody moving, EnemyWindupAnchorX falls through to the shooters.
                    TurnPhase.EnemyWindup => marchersActive
                        ? assaultAnchorX
                        : CameraDirector.EnemyWindupAnchorX(
                              marchingXs, skirmishXs,
                              enemyUnits.Where(u => u.Definition != null
                                                    && u.Definition.meleeDamage == 0)
                                        .Select(u => u.X).ToList(),
                              enemyUnits.Select(u => u.X).ToList(),
                              enemyCamX),
                    TurnPhase.Resolving => turnSide == TurnSide.Enemy ? s.PlayerCamXAnchor
                                                                     : enemyCamX,
                    // Ride the aircraft from the player line across the
                    // enemy, then (plane gone) sit back on the player
                    // line so they fire from their own frame.
                    TurnPhase.AirstrikeRun => AirstrikeCameraAnchorFor(s),
                    _ => s.PlayerCamXAnchor,
                };
                if (!fighting && !watchingCollapse && turnPhase != TurnPhase.AirstrikeRun
                    && arrivalCamHalf > 0f && bossTimer > 0f)
                    anchorTarget = arrivalCamX;
                followX = s.CameraFollowX ?? anchorTarget;
                float arriveSmooth = watchingCollapse
                    && CameraDirector.CollapseIsFollowing(collapseHold)
                    ? CameraDirector.CollapseFollowSmoothTime
                    : CameraDirector.MarchEscortSmoothTime;
                SpringFollow.Step(ref followX, ref followXVel, anchorTarget, dt,
                                  arriveSmooth);
            }

            // Enemy half-width is recaptured when a structure or spawn changes the SET.
            // Casualties do not — that is the membership twitch these captures exist to
            // prevent. An arrival on the clock outranks the leftover cluster so the
            // escort is readable for the banner's 2.5s.
            float halfWidth = CameraDirector.PhaseHalfWidth(
                turnPhase, turnSide,
                s.PlayerCamHalfWidth, enemyCamHalf, enemyCamHalf,
                assaultHalfWidth, marchersActive,
                s.PlayerCamHalfWidth, false);
            if (turnPhase == TurnPhase.TankArrive)
            {
                CameraDirector.TankArriveFrame(structures, playerUnits, out _, out halfWidth);
            }

            // The fight sets its own framing in any phase, for the same reason it sets the anchor
            // — PhaseHalfWidth only consults the march branch during the windup, and a fight that
            // outlives the windup would otherwise be framed for a volley happening elsewhere.
            if (fighting)
                halfWidth = skirmishXs.Count > 0 ? assaultHalfWidth : meleeHoldHalfWidth;
            else if (watchingCollapse)
                halfWidth = collapseHoldHalfWidth;
            else if (turnPhase != TurnPhase.AirstrikeRun
                     && arrivalCamHalf > 0f && bossTimer > 0f)
                halfWidth = arrivalCamHalf;

            // Room for the aircraft. The camera rides it, so this is a
            // floor, not a frame of the whole rake.
            if (turnPhase == TurnPhase.AirstrikeRun)
                halfWidth = Mathf.Max(halfWidth, AirstrikeRunHalfWidth(s));
            float targetZ = CameraDirector.TargetZ(halfWidth + CameraDirector.FramePad,
                                                   s.StaticCamera, s.StaticCamZ);
            float followZ = s.CameraFollowZ ?? targetZ;
            float followZVel = s.CameraFollowZVelocity;
            SpringFollow.Step(ref followZ, ref followZVel, targetZ, dt, 0.12f);

            var stepResult = s with
            {
                Projectiles = projectiles,
                Explosions = explosions,
                PlayerUnits = playerUnits,
                PlayerMarchInProgress = marching,
                EnemyUnits = enemyUnits,
                Structures = structures,
                DyingUnits = dyingUnits,
                Skirmishes = skirmishes,
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
                EnemyLaunch = enemyLaunch,
                TurnHandoverDelay = handover,
                ScoutTimer = scoutTimer,
                TankArriveTimer = tankArriveTimer,
                MeleeHold = meleeHold,
                MeleeHoldAnchorX = meleeHoldAnchorX,
                MeleeHoldHalfWidth = meleeHoldHalfWidth,
                CollapseHold = collapseHold,
                CollapseHoldAnchorX = collapseHoldAnchorX,
                CollapseHoldHalfWidth = collapseHoldHalfWidth,
                VictoryCamHold = phase == GamePhase.Victory && s.Phase != GamePhase.Victory
                    ? CameraDirector.VictoryCamHoldSeconds
                    : s.VictoryCamHold,
                TurnNumber = turnNumber,
                CameraFollowX = followX,
                CameraFollowXVelocity = followXVel,
                CameraFollowZ = followZ,
                CameraFollowZVelocity = followZVel,
                EnemyCamXAnchor = enemyCamX,
                EnemyCamHalfWidth = enemyCamHalf,
                ArrivalCamXAnchor = arrivalCamX,
                ArrivalCamHalfWidth = arrivalCamHalf,
                TotalPlayerKills = s.TotalPlayerKills + enemyKilled,
                TotalEnemyKills = s.TotalEnemyKills + playerKilled,
                TotalGroundImpacts = s.TotalGroundImpacts + groundImpactsThisTick,
                TotalStructureImpacts = s.TotalStructureImpacts + structureImpactsThisTick,
                TotalWoundedHits = s.TotalWoundedHits + woundedThisTick,
                TotalBlasts = s.TotalBlasts + blastsThisTick,
                BurningEnemyIds = burning,
                Scorches = scorches,
                NextScorchSlot = nextScorch,
                Debris = debris,
                NextDebrisSlot = nextDebris,
                Wrecks = wrecks,
                AirstrikePlane = planeNow,
                AirstrikeSpawnDelay = spawnDelay,
                PendingVolleyDelay = volleyDelay,
            };

            // --- 9. the airstrike run -----------------------------------------------------
            //
            // LAST, and on the assembled state on purpose: it releases a projectile and reads the
            // projectile list to decide whether the bomb has landed, so it has to see THIS tick's
            // physics, collisions and cull rather than last tick's. Running it earlier would test
            // a bomb against a list that had not been culled yet and hold the volley an extra tick
            // every time.
            if (stepResult.Phase == GamePhase.Playing
                && stepResult.TurnPhase == TurnPhase.AirstrikeRun)
            {
                return StepAirstrikeRun(stepResult, dt, random, ammoCatalog);
            }

            return stepResult;
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
        {
            bool tumble = CosmeticSystems.DiesInATumble(u.Y, u.StandingOnStructureId);
            var i = CosmeticSystems.ImpulseFor(u.Id, u.IsPlayerSide, tumble);
            return new DyingUnitEntity(u.Id, u.Definition, u.IsPlayerSide, u.X, u.Y, u.Z,
                                       Vx: i.Vx, Vy: i.Vy, RotationSpeed: i.RotationSpeed)
            {
                Vz = i.Vz,
                Rotation = i.Rotation,
                YawSpeed = i.YawSpeed,
                TiltSpeed = i.TiltSpeed,
                Tumble = tumble,
            };
        }

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
        /// Stops a thrown body at a structure's face, rests it on a structure's roof,
        /// or lets it drape off a lip.
        ///
        /// Corpses used to sail straight THROUGH buildings, which is the one place a purely
        /// cosmetic system stops looking cosmetic: a body passing through a bunker says the
        /// bunker is not there. Blocks on EVERY structure, not just the opposing side's —
        /// projectiles are allowed through friendly walls so a garrison can fire over its own
        /// fortress, but a body has no such excuse.
        ///
        /// THE ROOF AND THE FACE ARE MUTUALLY EXCLUSIVE. Gravity dips a landed body a hair
        /// below <c>topY</c> every tick. The face test then saw "spawned inside" and killed
        /// <c>vx</c>, while the roof test snapped them back up. A garrison on a deck could
        /// never walk off, and the renderer kept them flailing because they never settled
        /// against dirt. Rob, 2026-08-14: twitching, stuck on the lip.
        /// </summary>
        /// <param name="fromY">
        /// The body's PREVIOUS height. Needed for the same reason `fromX` is: whether a body
        /// belongs on the roof is a question about where it CAME FROM, not where it is now. A
        /// body descending onto the footprint was above the roof last tick; one flung into the
        /// wall was not, and must be stopped by the face instead of lifted up it.
        /// </param>
        static void BlockOnStructures(IReadOnlyList<StructureEntity> structures,
                                      float fromX, float fromY, float y, ref float x, ref float vx,
                                      ref float vz, ref float restY, ref float bend,
                                      int unitId, float supportY)
        {
            foreach (var st in structures)
            {
                CollisionSystem.RagdollBox(st, out float minX, out float maxX,
                                           out float baseY, out float topY);

                bool inX = x > minX && x < maxX;
                bool wasInside = fromX > minX && fromX < maxX;
                // At or above the roof last tick — standing on it, or falling onto it.
                // A body at chest height against the wall is not this.
                bool fromRoof = fromY >= topY - 1e-3f;
                // Seated on THIS roof, not merely flying above it. fromRoof is
                // true for anyone high in the air, so L8's watch garrison was
                // treated as on the post's lip the instant they crossed its
                // left face — pinned mid-air and dropped as a curtain.
                bool seatedOnRoof = supportY >= topY - 1e-3f
                                 && supportY <= topY + CosmeticSystems.RagdollAirborneSlack;

                // LIP: only a body already RESTING on this roof walks off.
                // A flyer entering from the side is not on the lip.
                if (inX && seatedOnRoof && CosmeticSystems.RagdollOnLip(x, minX, maxX))
                {
                    bool offLeft = (x - minX) <= (maxX - x);
                    if (offLeft)
                    {
                        x = minX;
                        if (vx > 0f) vx = 0f;
                        bend = -1f;
                    }
                    else
                    {
                        x = maxX;
                        if (vx < 0f) vx = 0f;
                        bend = 1f;
                    }
                    continue;
                }

                // ROOF: inside, came from above/on it, not at a lip. Rest here and skip
                // the face — that is the vx-kill described above.
                //
                // THE TEST IS THE ROOF, NOT THE BASE. This read `y >= baseY`, and a ground
                // structure's base IS the ground — so any body that got horizontally inside the
                // footprint at ANY height was rested on the roof. A corpse flung at a wall at
                // chest height was snapped four units up the face and left standing on top of
                // the building. That is Rob's "physically impossible interactions with
                // structures", reported 2026-08-07; the condition's own comment already said
                // "cleared the wall", which `y >= baseY` never tested.
                // Already over this footprint and above its roof — land here.
                // Entering from the SIDE while high is flying over (L8 watch
                // → post). Those must not catch the near face as a roof.
                if (inX && fromRoof && wasInside)
                {
                    restY = Mathf.Max(restY, topY);
                    continue;
                }

                // FACE: only while the body is inside the box's vertical span. Above the
                // roof it is flying over, which is a real trajectory and not a miss.
                if (y > topY || y < baseY) continue;
                if (!inX) continue;

                // Put it back against the face it came in through. Inbound
                // speed becomes depth so a rank does not stack on one pane —
                // they drape along the wall while they fall. Approaching from
                // the right means resting against the right-hand face.
                // Bend INTO the wall — the part that hit stops, the rest folds toward it.
                if (fromX >= maxX)
                {
                    x = maxX;
                    vz += CosmeticSystems.RagdollWallScatterVz(unitId, Mathf.Abs(vx));
                    vx = 0f;
                    bend = -1f;
                }
                else if (fromX <= minX)
                {
                    x = minX;
                    vz += CosmeticSystems.RagdollWallScatterVz(unitId, Mathf.Abs(vx));
                    vx = 0f;
                    bend = 1f;
                }
                else vx = 0f;                      // spawned inside: just stop, do not teleport
            }
        }

        /// <summary>
        /// A ruin is a low mound. Falling garrison used to pass through the
        /// wreck mesh and flail inside it because the standing box was gone.
        /// No Bend — slump-against-masonry on a collapsed pile is the twitch
        /// Rob saw on L1.
        /// </summary>
        static void BlockOnWrecks(IReadOnlyList<WreckEntity> wrecks,
                                  float fromY, float y, float x, ref float vx, ref float surface)
        {
            if (wrecks == null) return;
            foreach (var w in wrecks)
            {
                float half = w.Width * 0.5f;
                if (x < w.X - half || x > w.X + half) continue;
                float lid = CosmeticSystems.WreckLidY(w);
                if (fromY >= lid - 1e-3f && y <= lid)
                    surface = Mathf.Max(surface, lid);
                else if (y < lid && vx != 0f)
                    vx = 0f;
            }
        }

        static List<DyingUnitEntity> StepRagdolls(IReadOnlyList<DyingUnitEntity> dying, float dt,
                                                  IReadOnlyList<StructureEntity> structures,
                                                  IReadOnlyList<WreckEntity> wrecks)
        {
            var outp = new List<DyingUnitEntity>(dying.Count);
            foreach (var d in dying)
            {
                float age = d.Age + dt;
                if (CosmeticSystems.RagdollExpired(age)) continue;

                float vy = d.Vy - TrajectoryPhysics.Gravity * dt;
                float y = d.Y + vy * dt;
                float x = d.X + d.Vx * dt;
                float vx = d.Vx;
                // Depth is live now: ImpulseFor throws each body on its own Vz so a volley
                // does not share one plane. Grounded bodies drop it — sliding in Z after the
                // flop reads as the corpse skating, not settling.
                float vz = d.Vz;
                float z = d.Z + vz * dt;
                float bend = d.Tumble ? d.Bend : 0f;
                // Dirt is the lying box, always. RagdollRestY(live spin) at
                // ±90 is 0.5 — they "land" on a phantom floor, the second
                // pass treats that as a lip walk-off, and they hang at y=0.5
                // with SupportY=-1. Roofs and wreck lids raise surface.
                float surface = CosmeticSystems.RagdollRestY(0f);
                BlockOnStructures(structures, d.X, d.Y, y, ref x, ref vx, ref vz, ref surface, ref bend,
                                  d.Id, d.SupportY);
                if (!d.Tumble) bend = 0f;
                BlockOnWrecks(wrecks, d.Y, y, x, ref vx, ref surface);

                if (y <= surface)
                {
                    float rot = d.Rotation, rotSpeed = d.RotationSpeed;
                    if (CosmeticSystems.ShouldRoll(vx))
                    {
                        CosmeticSystems.StepRoll(vx, dt, out vx, out rotSpeed);
                        rot += rotSpeed * dt;
                    }
                    else
                    {
                        // ±90 is a body on its SIDE — horizontal at this camera.
                        // Flopping to 0/180 sat them back upright (the sit-up).
                        CosmeticSystems.StepFlopToSide(rot, rotSpeed, dt, out rot, out rotSpeed);
                        vx = 0f;
                    }
                    // Pose change moves the body's own rest; a roof still wins if they
                    // are on one. Re-run so a roll that just reached a lip this pose
                    // step is allowed to leave rather than snapped to dirt from y=4.
                    //
                    // fromY is the SURFACE they just landed on, not the dipped ballistic
                    // y. Passing the dip made fromRoof false and the face test killed vx
                    // as "spawned inside" — then they fell through. Seen red: falling
                    // onto a roof ended at y 0.05, and a centre-roof slide had vx 0.
                    //
                    // GROUNDED HEIGHT IS THE LYING BOX, not RagdollRestY(rot). At ±90
                    // that formula returns standing height and they hover.
                    float onY = surface;
                    surface = CosmeticSystems.RagdollRestY(0f);
                    BlockOnStructures(structures, x, onY, onY, ref x, ref vx, ref vz, ref surface, ref bend,
                                      d.Id, onY);
                    BlockOnWrecks(wrecks, onY, onY, x, ref vx, ref surface);
                    if (onY <= surface)
                    {
                        outp.Add(d with
                        {
                            X = x, Y = surface, Z = z,
                            Vx = vx, Vy = 0f, Vz = 0f,
                            Rotation = rot, RotationSpeed = rotSpeed,
                            Yaw = d.Yaw * CosmeticSystems.DecayPerTick60(0.88f, dt),
                            SettleTilt = d.SettleTilt * CosmeticSystems.DecayPerTick60(0.88f, dt),
                            Age = age, SupportY = surface, Bend = bend,
                        });
                    }
                    else
                    {
                        // Walked off a lip during the pose step — fall from the roof,
                        // not from dirt.
                        outp.Add(d with
                        {
                            X = x, Y = onY, Z = z,
                            Vx = vx, Vy = vy,
                            Rotation = rot, RotationSpeed = rotSpeed,
                            Age = age, SupportY = -1f, Bend = bend,
                        });
                    }
                }
                else
                {
                    // A body that hit a wall mid-flight keeps falling — leftover
                    // travel is now in Vz, so they slide along the face.
                    // Full 3-axis tumble. The die clip is not posing them, so
                    // this spin is the whole read — they flip, they do not sit.
                    outp.Add(d with
                    {
                        X = x, Y = y, Z = z,
                        Vy = vy, Vx = vx, Vz = vz,
                        Rotation = d.Rotation + d.RotationSpeed * dt,
                        Yaw = d.Yaw + d.YawSpeed * dt,
                        SettleTilt = d.SettleTilt + d.TiltSpeed * dt,
                        Age = age, SupportY = -1f, Bend = bend,
                    });
                }
            }
            return outp;
        }

        /// <summary>
        /// Fires the player's volley — one round per living player unit.
        /// </summary>
        /// <param name="ammoCatalog">
        /// Optional. Null means Standard, which is the IDENTITY modifier, so every existing
        /// caller and every test written before ammo existed keeps its exact behaviour. The
        /// whole volley takes the selected ammo INCLUDING the tank shell — `DYNAMISM_DESIGN.md`
        /// is explicit that there are no special cases, and an AP shell is the bunker-buster
        /// fantasy the type exists for.
        /// </param>
        public static GameState FireVolley(GameState s, Vector3 aimVelocity, System.Random random,
                                           AmmoCatalogSO ammoCatalog = null)
        {
            if (s.Phase != GamePhase.Playing || s.TurnPhase != TurnPhase.Aiming) return s;
            if (s.PlayerUnits.Count == 0) return s;

            // AN ARMED AIRSTRIKE FLIES FIRST. The plane enters over the
            // player line, the camera rides it across the enemy, it
            // exits, the camera comes home, THEN the volley fires.
            if (s.AirstrikeArmed)
                return BeginAirstrikeRun(s, aimVelocity);

            return LaunchVolley(s, aimVelocity, random, ammoCatalog);
        }

        /// <summary>
        /// Builds and launches the infantry volley and the tank shell. Split out of FireVolley so
        /// the airstrike run can call it A BEAT LATE with the aim the player originally released —
        /// the rounds, the spread, the ammo and the shell solve are all identical either way.
        /// </summary>
        static GameState LaunchVolley(GameState s, Vector3 aimVelocity, System.Random random,
                                      AmmoCatalogSO ammoCatalog)
        {

            var ammo = AmmoModifiers.From(ammoCatalog, s.SelectedAmmo);

            var rounds = new List<ProjectileEntity>(s.Projectiles);
            int slot = s.NextBulletSlot;
            foreach (var u in s.PlayerUnits)
            {
                // BURST FIRE. `projectilesPerVolley` was read by AutoFire and by nothing else, so
                // the machine gunner — sold in the store as "fires a burst instead of a round" —
                // fired ONE round in the player's hands and was, measurably, a rifleman at half
                // damage for twice the points. Same family as the three properties below, and
                // found the same way: by measuring the volley instead of reading the asset
                // (`RosterAudit.Report`, Tier 2.3).
                int shots = u.Definition != null ? Mathf.Max(u.Definition.projectilesPerVolley, 1) : 1;
                for (int shot = 0; shot < shots; shot++)
                {
                    // A little spread per ROUND, not per shooter, so a burst lands as a burst — three
                    // rounds drawn with one jitter would be one round drawn three times, which is the
                    // "more hits, each one lighter" promise delivered as "one hit, three times as
                    // heavy". CLUSTER widens exactly this: still convergent fire at real targets (a
                    // blind fan is forbidden by the lock), just a wider zone, so more distinct enemies
                    // fall inside it and each round lands lighter.
                    //
                    // TWO INDEPENDENT DRAWS, one per axis (2026-08-12). This used to draw ONE value
                    // and add it to both Vx and Vy, which is not a spread at all — it displaces every
                    // round along the same 45° line, so a three-round burst stayed collinear for the
                    // whole arc and rendered as one thicker streak. Measured on device: a squad of
                    // machine gunners put 1.83x the tracer of a rifle squad per shooter, so the rounds
                    // were there and were landing, and you still could not see three of them. The
                    // magnitude is unchanged — only the direction is now free.
                    float jitterX = ((float)random.NextDouble() - 0.5f) * 0.25f * ammo.SpreadScale;
                    float jitterY = ((float)random.NextDouble() - 0.5f) * 0.25f * ammo.SpreadScale;
                    rounds.Add(new ProjectileEntity(
                        Id: 10000 + slot++,
                        X: u.X, Y: u.Y + InfantryMuzzleY, Z: u.Z,
                        Vx: aimVelocity.x + jitterX, Vy: aimVelocity.y + jitterY, Vz: 0f,
                        Damage: ammo.UnitDamage(u.Definition != null ? u.Definition.damage : 8),
                        OwnerIsPlayer: true)
                    {
                        // The PLAYER's volley used to leave all three of these at their defaults, so
                        // every round the player fired was a plain bullet with no splash and a 1x
                        // structure multiplier — while AutoFire, three methods down, set them
                        // correctly. The rocket trooper's 6x against buildings and the grenadier's 2x
                        // existed only under the debug driver, and a rocket rendered as a tracer.
                        Type = u.Definition != null ? u.Definition.projectileType : ProjectileType.Bullet,
                        SplashRadius = u.Definition != null ? u.Definition.splashRadius : 0f,
                        StructureDamageMultiplier = ammo.StructureMultiplier(
                            u.Definition != null ? u.Definition.structureDamageMultiplier : 1f),
                        // What CollisionSystem reads to mark a survivor burning. It has read this
                        // since the port and nothing ever set it.
                        Ammo = ammo.Type,
                    });
                }
            }

            int shellSlot = s.NextShellSlot;

            // THE SHELL IS SOLVED TO THE VOLLEY'S LANDING POINT, not scaled by a constant.
            //
            // It used to be `aimVelocity * velocityBoost`. The tank sits BEHIND the line, so a
            // shell thrown at the line's own velocity falls short, and 1.12 was hand-tuned to
            // push it back out. But range goes as v^2, so a 1.12 boost buys 1.2544x the range and
            // overshoots badly: measured by PortSelfTest against the old code, the shell landed
            // 2.5 to 3.9 units PAST the volley depending on the aim (at aim 6,6 the volley lands
            // at 10.92 and the shell at 14.86). Found on device 2026-08-07 on L12, where aiming
            // the infantry at the near fortress tier put the shell onto the FAR one for its
            // full 96.
            //
            // That mattered more than any level's HP, because the shell is the only thing a stock
            // squad has that can break a structure (96 against a rifleman's 2, and only three of
            // them). The player aims ONE reticle and fired TWO weapons that landed in different
            // places, and could not place the one that counted.
            //
            // So: take where the infantry volley is actually going, and solve the gun onto it at
            // the SAME launch angle, which keeps the shell visually part of the same volley.
            // velocityBoost survives with a real meaning — the gun's speed HEADROOM over the drag
            // that ordered the shot, i.e. how much further back the tank may stand and still make
            // the shot. A muzzle ~2 units behind needs about 1.07x, so 1.12 covers it.
            var lineOrigin = MeanMuzzle(s.PlayerUnits);
            var volleyTarget = TrajectoryPhysics.LandingPoint(lineOrigin, aimVelocity);
            float aimAngle = Mathf.Atan2(aimVelocity.y, aimVelocity.x) * Mathf.Rad2Deg;
            float aimSpeed = Mathf.Sqrt(aimVelocity.x * aimVelocity.x
                                        + aimVelocity.y * aimVelocity.y);

            var shells = CannonShells(s, ref shellSlot,
                (muzzle, c) => TrajectoryPhysics.SolveVelocity(
                    muzzle, volleyTarget, aimAngle, aimSpeed * c.velocityBoost),
                ammo);
            rounds.AddRange(shells);

            // NO AIRSTRIKE ROUND IS INJECTED HERE ANY MORE. The bomb is released by the aircraft
            // during TurnPhase.AirstrikeRun, before this volley exists — which is the whole point
            // of the beat. Arriving here at all means either nothing was armed, or the run has
            // already finished and dropped.

            return s with
            {
                Projectiles = rounds,
                NextBulletSlot = slot,
                NextShellSlot = shellSlot,
                TankShellsRemaining = s.TankShellsRemaining - shells.Count,
                // The aim has been spent. Left set, a second run would rebuild this same volley.
                PendingVolleyAim = null,
                TurnPhase = TurnPhase.Resolving,
                TurnSide = TurnSide.Player,
            };
        }

        // ---- the airstrike run ---------------------------------------------------------------

        /// <summary>How fast the aircraft crosses, in units per second.</summary>
        /// <remarks>
        /// 14 units of travel at this speed is a 2.0s pass. Slower reads as a lumbering transport;
        /// faster and the eye cannot track a 4.5-unit aircraft across a frame only ~10 units wide.
        /// </remarks>
        public const float PlaneSpeed = 7f;

        /// <summary>
        /// Height the aircraft flies at, in game units.
        ///
        /// Raised from 6.5 on 2026-08-10 — Rob wanted it nearer the top of the frame, and height is
        /// also the lever that shrinks it without touching the model. Judged in
        /// `PlanePreview.Shots` at 6.5 / 8 / 9.5 / 11 against a rank of soldiers, AT THE RUN'S OWN
        /// camera distance.
        ///
        /// It does NOT move the release or the impact: the drop lead is `PlaneSpeed * BombFallTime`
        /// and neither is a function of height. What it does change is how fast the bomb falls,
        /// since it still covers the drop in the same fixed time.
        /// </summary>
        public const float PlaneY = 9.5f;

        /// <summary>
        /// How long the bomb falls once released.
        ///
        /// THIS NUMBER IS A FRAMING CONSTRAINT, not a feel one. The release happens
        /// `PlaneSpeed * BombFallTime` = ~5.95 units short of the target, and the frame is only
        /// ~4.94 units of half-width at camZ 11 — so a longer fall puts the RELEASE off-screen,
        /// which is the exact failure this whole beat exists to fix. The camera bias below is what
        /// buys the rest of the margin.
        ///
        /// It is also still a legible fall: seconds, not frames.
        /// </summary>
        public const float BombFallTime = 0.85f;

        /// <summary>
        /// How far the aircraft spawns before, and flies past, the drop point.
        ///
        /// Both ends are off-frame at resolve framing, so it ENTERS and LEAVES rather than popping
        /// into and out of existence — which was the original round's worst single property.
        ///
        /// **IT IS A FLOOR NOW, NOT THE SPAWN ITSELF.** The spawn is DERIVED in
        /// `BeginAirstrikeRun` — far enough back to exist `StrafeLead` before the rake's first
        /// firing point, and to still be short of the release when it drops — because the rake is
        /// sized by the enemy position and no fixed offset from the aim can guarantee both. This
        /// constant only keeps the aircraft from ever spawning NEARER than it used to, and sets how
        /// far past the far end it flies before it is retired.
        ///
        /// **The spare unit in that derivation is not slack.** The firing loop fires every round
        /// whose point the plane has ALREADY passed, so a spawn at or beyond the first firing point
        /// dumps several rounds from one position in a single tick — a literal burst, which is the
        /// thing this whole beat has been iterating away from.
        ///
        /// It is also most of what sets the beat's LENGTH: the run lasts roughly
        /// `(target - spawn) / PlaneSpeed`, so a wider enemy line now costs a longer pass. That is
        /// the right trade — the pass exists to show the enemy position being raked — but it does
        /// mean the beat is no longer one fixed number across the campaign.
        /// </summary>
        public const float PlaneRunHalfLength = 11f;

        /// <summary>
        /// How far past the RIGHT EDGE of the held enemy frame the
        /// aircraft keeps flying — just enough to leave the picture.
        /// The camera does not follow it there.
        /// </summary>
        public const float PlaneExitOvershoot = 0.8f;

        /// <summary>
        /// How far AHEAD of the aircraft the camera looks while riding it.
        /// Enough to see the nose and the rounds leaving it; not so much
        /// that the plane sits on the left edge.
        /// </summary>
        public const float PlaneCameraBias = 1.5f;

        /// <summary>After the plane leaves, how long the camera has to
        /// spring home before the infantry fire.</summary>
        public const float AirstrikeReturnSeconds = 0.75f;

        /// <summary>
        /// Starts the aircraft's pass and banks the aim for the volley that follows it.
        ///
        /// The consumable is SPENT here, on the same true->false edge of AirstrikeArmed the runner
        /// has always watched, so the permanent inventory spend in BattleRunner needs no change:
        /// the item is committed the moment the aircraft is in the air, which is also the moment
        /// the player can no longer change their mind.
        /// </summary>
        /// <summary>
        /// Where the camera sits during the run: rides the aircraft
        /// until the enemy, then HOLDS so the plane can leave the
        /// right edge. Once the plane is gone, the player line.
        /// </summary>
        public static float AirstrikeCameraAnchorFor(GameState s)
        {
            var p = s.AirstrikePlane;
            if (p == null) return s.PlayerCamXAnchor;
            float ride = p.X + PlaneCameraBias;
            float cap = AirstrikeCameraCap(p);
            return ride < cap ? ride : cap;
        }

        /// <summary>The enemy frame the camera will not pan past.</summary>
        public static float AirstrikeCameraCap(AirstrikePlaneEntity p)
            => p == null ? 0f : (p.StrafeFromX + p.StrafeToX) * 0.5f;

        /// <summary>
        /// Room for the aircraft itself. The camera rides it, so the
        /// frame does not have to hold the whole rake at once.
        /// </summary>
        public static float AirstrikeRunHalfWidth(GameState s)
            => CameraDirector.AirstrikeRunHalfWidth;

        /// <summary>
        /// Flies the aircraft, and retires it once it is past its own exit point. Null in, null out
        /// — the overwhelmingly common case, since there is no aircraft for all but a couple of
        /// seconds of a battle.
        /// </summary>
        static AirstrikePlaneEntity StepPlane(AirstrikePlaneEntity p, float dt)
        {
            if (p == null) return null;
            float x = p.X + p.Vx * dt;
            return x > p.ExitX ? null : p with { X = x };
        }

        /// <summary>
        /// The ground the burst rakes: THE WHOLE ENEMY POSITION, structures included, with a
        /// margin at each end so the walk starts before the first man and finishes past the last
        /// wall rather than on top of them.
        ///
        /// **Structure EDGES, not centres.** A structure's centre says nothing about how much
        /// ground it covers, and an outpost is 2 units wide — raking to its centre leaves half the
        /// building unhit, which is most of the way back to the bug this replaced.
        ///
        /// Falls back to the aim point if the enemy has no position at all (every unit dead and
        /// every structure gone), which cannot happen while a turn is being taken but costs one
        /// line to make impossible rather than merely unlikely.
        /// </summary>
        public static (float from, float to) StrafeSpan(GameState s, float targetX)
        {
            float min = float.MaxValue, max = float.MinValue;
            foreach (var u in s.EnemyUnits) { min = Mathf.Min(min, u.X); max = Mathf.Max(max, u.X); }
            foreach (var st in s.Structures)
            {
                if (st.Definition.isPlayerSide) continue;
                float halfW = (st.Definition.hasHitWidth ? st.Definition.hitWidth
                                                         : st.Definition.size) / 2f;
                min = Mathf.Min(min, st.X - halfW);
                max = Mathf.Max(max, st.X + halfW);
            }
            if (min > max) return (targetX - StrafeMargin, targetX + StrafeMargin);
            return (min - StrafeMargin, max + StrafeMargin);
        }

        /// <summary>How far outside the enemy position the rake starts and finishes.</summary>
        public const float StrafeMargin = 1.5f;

        /// <summary>
        /// How long the aircraft takes from its spawn to the moment its bomb lands: the run out to
        /// the release point, plus the fall.
        /// </summary>
        static float PlaneRunToImpact(float spawnX, float targetX)
            => (targetX - PlaneSpeed * BombFallTime - spawnX) / PlaneSpeed + BombFallTime;

        /// <summary>
        /// Where the aircraft must start: LEFT of the player line so it
        /// enters over our troops, and still far enough back for
        /// `StrafeLead` before the first rake point and the bomb release.
        /// </summary>
        static float PlaneSpawnX(GameState s, float rakeFromX, float targetX)
        {
            float playerLeft = float.MaxValue;
            foreach (var u in s.PlayerUnits)
                if (u.X < playerLeft) playerLeft = u.X;
            if (playerLeft > 1e8f) playerLeft = -8f;
            return Mathf.Min(playerLeft - 2.5f,
                             Mathf.Min(rakeFromX - StrafeLead - 1f,
                                       targetX - PlaneSpeed * BombFallTime - 1f));
        }

        public static GameState BeginAirstrikeRun(GameState s, Vector3 aimVelocity)
        {
            var target = TrajectoryPhysics.LandingPoint(MeanMuzzle(s.PlayerUnits), aimVelocity);
            var (from, to) = StrafeSpan(s, target.x);
            float spawn = PlaneSpawnX(s, from, target.x);

            float cap = (from + to) * 0.5f;
            float offScreen = cap + CameraDirector.AirstrikeRunHalfWidth
                                  + CameraDirector.FramePad
                                  + PlaneExitOvershoot;
            float lastJob = Mathf.Max(to - StrafeLead + 1f,
                                      target.x - PlaneSpeed * BombFallTime + 1f);
            var plane = new AirstrikePlaneEntity(
                X: spawn, Y: PlaneY, Vx: PlaneSpeed,
                ExitX: Mathf.Max(offScreen, lastJob))
            {
                StrafeFromX = from,
                StrafeToX = to,
                StrafeIdFirst = StrafeIdBase + s.NextGrenadeSlot * StrafeRounds,
                BombTargetX = target.x,
            };

            return s with
            {
                AirstrikeArmed = false,
                LoadedConsumables = Consumables.Decrement(s.LoadedConsumables,
                                                          ConsumableType.Airstrike),
                TurnSide = TurnSide.Player,
                AirstrikePlane = plane,
                AirstrikeSpawnDelay = 0f,
                PendingVolleyAim = aimVelocity,
                PendingVolleyDelay = 999f,
                TurnPhase = TurnPhase.AirstrikeRun,
            };
        }

        /// <summary>Shell ids sit in their own band, like the bullets' 10000 and the enemy's
        /// 20000. Raw ids have to stay globally unique — hit tracking keys off them.</summary>
        const int ShellIdBase = 30000;

        /// <summary>
        /// Smoke Screen doubles the enemy's aim jitter radius for exactly one volley
        /// (`EnemyAI.JitterRadius`). Two is the figure the Kotlin shipped and it is the whole
        /// effect — no new state, no new pipeline, one knob that already existed.
        /// </summary>
        public const float SmokeScreenJitterMultiplier = 2f;

        // ---- the Airstrike consumable ------------------------------------------------------
        //
        /// <summary>Airstrike ids get their own band, beside the shells' 30000.</summary>
        const int AirstrikeIdBase = 40000;
        /// <summary>
        /// How high the bomb is released from — the AIRCRAFT's altitude, since the aircraft is
        /// what drops it.
        ///
        /// It used to be 5.0 with the round appearing in clear sky at that height, and an earlier
        /// version of this comment claimed that read as "off the top of the frame". It did not: a
        /// soldier is ~1.30 world units, so 5.0 is under four soldier-heights, comfortably inside
        /// the picture. That gap is what `_plans/archive/AIRSTRIKE_PLANE.md` closes — the bomb no longer
        /// appears from nothing at any height, because something visibly drops it.
        /// </summary>
        public const float AirstrikeOriginY = PlaneY;
        /// <summary>
        /// The strafing burst: how many cannon rounds, what each does, and how far along the
        /// ground the line of hits walks before the bomb lands on the end of it.
        ///
        /// Rob asked for "bursts of rounds, like a strafing" after seeing the pass, and the earlier
        /// decision NOT to add gunfire is reversed with it. That decision was about refusing a cue
        /// that does NOTHING, which is still right — rounds visibly striking men who then shrug
        /// them off is worse feedback than no rounds at all, because it reads as the expensive
        /// thing having missed. These do something.
        ///
        /// **This makes the Airstrike stronger: 24 becomes 24 + 28 = 52.** It is the dearest item
        /// in the shop at 250 coins and was doing less than a Sniper's single shot, so that is a
        /// deliberate correction rather than an accident — but it IS a balance change, and
        /// `StrafeDamage` is the one constant to turn down if the levels disagree.
        ///
        /// **The COUNT is presentation and the TOTAL is balance, and they are kept apart.** The
        /// count has been raised twice, 7 -> 14 -> 28, each time to hold the SPACING at ~0.33 units
        /// as the walk grew from 4 to 9 — and `StrafeDamage` has come down 4 -> 2 -> 1 each time so
        /// the burst's contribution stays at 28 and the Airstrike's total stays at 52. Density is
        /// presentation; the total is what the campaign feels, and `BalanceAudit` does not know
        /// about consumables at all.
        ///
        /// **NOTE THE ONE THING THIS ARITHMETIC DOES NOT CAPTURE:** a wider rake spreads the same
        /// nominal damage over more empty ground, so its EFFECTIVE damage falls even though the
        /// total is unchanged. The burst is a presentation feature that happens to hurt; if it ever
        /// needs to hurt a fixed amount, that is a different design and wants a different mechanism
        /// than a walk of independent rounds.
        /// </summary>
        public const int StrafeRounds = 28;
        public const int StrafeDamage = 1;
        /// <summary>
        /// How long a cannon round is in the air, and how far AHEAD of the aircraft it is thrown.
        ///
        /// These two make the round rake FORWARD instead of dropping. A round that merely inherited
        /// the aircraft's 7 u/s and fell from 9.5 units arrived almost vertically and was gone in a
        /// handful of frames — mechanically correct and, on a device capture, invisible: one fading
        /// blob and no tracer anywhere. Solving it onto its landing point instead gives it
        /// `StrafeLead / StrafeFallTime` = 10 u/s of forward speed, so it outruns the aircraft and
        /// draws a streak, which is what gunfire looks like.
        ///
        /// The lead no longer bounds the burst: the aircraft's SPAWN is derived from the rake's own
        /// start (see BeginAirstrikeRun), so a longer lead moves the spawn back rather than firing
        /// rounds before the plane exists.
        /// </summary>
        public const float StrafeFallTime = 0.40f;
        public const float StrafeLead = 4f;

        /// <summary>
        /// Where round `k` of the burst lands: an even walk from one end of the ENEMY POSITION to
        /// the other, carried on the aircraft.
        ///
        /// **The burst has nothing to do with where the player aimed.** It used to walk to the
        /// volley's landing point and stop there, which made the rake a property of the shot rather
        /// than of the target — aim short and it raked open ground; aim past the line and it raked
        /// past the line. Rob: *"the strafe is independent of the player unit volley. it should
        /// start from the left, strafe should cover the whole enemy position and its structures."*
        /// The BOMB is the part of an airstrike that cares about the aim; the guns rake the enemy.
        /// </summary>
        public static float StrafeLandingX(AirstrikePlaneEntity p, int k)
            => p.StrafeFromX + k * ((p.StrafeToX - p.StrafeFromX) / (StrafeRounds - 1));

        public const int AirstrikeDamage = 24;
        public const float AirstrikeSplashRadius = 1.1f;
        public const float AirstrikeStructureMultiplier = 2f;

        /// <summary>
        /// The airstrike round: a Grenade-type splash shot RELEASED BY THE AIRCRAFT, arriving on
        /// `target` after exactly `BombFallTime`.
        ///
        /// It reuses the Grenade pool, visual and splash path as-is — the item is a new BUTTON,
        /// not a new combat pipeline, which is what `PROGRESSION_DESIGN.md` asks of all three.
        /// </summary>
        static ProjectileEntity Airstrike(Vector3 target, float fromX, float fromY, float forwardVx,
                                          ref int slot)
        {
            // RELEASED BY THE AIRCRAFT, so it inherits the aircraft's forward speed and ARCS onto
            // the target instead of dropping out of nowhere. That arc is the visible causal link
            // between the plane and the explosion — a bomb falling straight down from a plane that
            // has already gone past reads as a coincidence.
            //
            // The vertical is whatever covers the drop in the fixed time under this game's gravity;
            // the horizontal is the aircraft's own, and the drop POINT is chosen (in StepAirstrike-
            // Run) so that those two agree on the target rather than being solved against it here.
            float vy = (0f - fromY) / BombFallTime
                     + 0.5f * TrajectoryPhysics.Gravity * BombFallTime;
            return new ProjectileEntity(
                Id: AirstrikeIdBase + slot++,
                X: fromX, Y: fromY, Z: target.z,
                Vx: forwardVx, Vy: vy, Vz: 0f,
                Damage: AirstrikeDamage,
                OwnerIsPlayer: true)
            {
                // A BULLET, not the grenadier's grenade. The grenade prefab is olive-lime at 0.16
                // scale and was genuinely hard to follow against sky — Rob's words, and the same
                // complaint that started this whole beat. The bullet renders as a bright unlit
                // TRACER, which is the most visible thing this game draws.
                //
                // It is not mistaken for cannon fire because IsAirstrike scales it up in the
                // renderer — the flag has existed since Tier 1.3 and been read NOWHERE, which the
                // backlog noted as a free hook. This is it being used.
                Type = ProjectileType.Bullet,
                SplashRadius = AirstrikeSplashRadius,
                StructureDamageMultiplier = AirstrikeStructureMultiplier,
                IsAirstrike = true,
            };
        }

        /// <summary>
        /// One cannon round of the strafing burst — a fast, small, non-splash shot solved onto its
        /// own point of the walk.
        ///
        /// Its own ID BAND, deliberately: the run hands over when the BOMB has resolved, and that
        /// test asks the projectile list. Sharing a band would hold the volley back until the last
        /// tracer had landed, and a burst still in the air is not the beat ending.
        /// </summary>
        static ProjectileEntity StrafeRound(int idFirst, float fromX, float fromY,
                                            float landX, int k)
        {
            // SOLVED onto its own point of the walk, not dropped. Both components come from the
            // geometry: horizontal to cover the lead in the flight time, vertical to cover the
            // height in the same.
            float forwardVx = (landX - fromX) / StrafeFallTime;
            float vy = (0f - fromY) / StrafeFallTime
                     + 0.5f * TrajectoryPhysics.Gravity * StrafeFallTime;
            return new ProjectileEntity(
                // Unique WITHOUT consuming the bomb's slot counter: the bomb is found again by an
                // id-range test, and letting a use's worth of tracers march that counter along
                // would eventually walk a bomb id out of the range being tested. Ids must stay
                // globally unique — hit tracking keys off them — so each run carries the base of
                // its own block (see AirstrikePlaneEntity.StrafeIdFirst) and indexes within it.
                Id: idFirst + k,
                X: fromX, Y: fromY, Z: 0f,
                Vx: forwardVx, Vy: vy, Vz: 0f,
                Damage: StrafeDamage,
                OwnerIsPlayer: true)
            {
                // Deliberately NOT flagged IsAirstrike: that flag now means "the bomb" and is what
                // the renderer scales up. A cannon round is meant to look like a cannon round —
                // which is what IsStrafe is for, and it is a DIFFERENT shape, not a smaller
                // version of the same one. The bomb is a big round dot; these are streaks.
                Type = ProjectileType.Bullet,
                IsStrafe = true,
            };
        }

        /// <summary>Strafe ids sit clear of the bomb's 40000 band — see StrafeRound.</summary>
        const int StrafeIdBase = 45000;

        /// <summary>
        /// Fires whatever of the burst the aircraft has now flown far enough to fire.
        ///
        /// Lives on the ALWAYS-RUN physics path, not inside the run's phase step, because the rake
        /// is sized by the enemy position and routinely outlasts the bomb — see the call site.
        ///
        /// A `while`, not an `if`: one tick can cover more than one firing point at 28 rounds over
        /// a few units, and skipping the extras would thin the burst on exactly the frames where
        /// the game is busiest.
        /// </summary>
        static (AirstrikePlaneEntity, List<ProjectileEntity>) StepStrafe(
            AirstrikePlaneEntity p, List<ProjectileEntity> projectiles)
        {
            if (p == null || p.StrafeFired >= StrafeRounds) return (p, projectiles);

            List<ProjectileEntity> fired = null;
            while (p.StrafeFired < StrafeRounds)
            {
                float landX = StrafeLandingX(p, p.StrafeFired);
                if (p.X < landX - StrafeLead) break;   // not close enough to shoot at it yet
                fired ??= new List<ProjectileEntity>(projectiles);
                fired.Add(StrafeRound(p.StrafeIdFirst, p.X, p.Y, landX, p.StrafeFired));
                p = p with { StrafeFired = p.StrafeFired + 1 };
            }
            return (p, fired ?? projectiles);
        }

        /// <summary>
        /// Releases the bomb once the aircraft reaches its drop point.
        ///
        /// **The drop point is DERIVED from the bomb's own flight, not tuned.** The bomb inherits
        /// the aircraft's forward speed, so releasing it `PlaneSpeed * BombFallTime` short of the
        /// target is exactly what puts it ON the target — one arithmetic relationship rather than
        /// two constants free to drift apart. Wrong in either direction and the most expensive
        /// consumable in the game misses.
        ///
        /// On the ALWAYS-RUN path with the guns and the aircraft's motion, and for the same reason:
        /// once the volley and the pass are aligned on their impacts, the aircraft is routinely
        /// still short of its drop point while the phase has already moved on to Resolving.
        /// </summary>
        static (AirstrikePlaneEntity, List<ProjectileEntity>, int) StepBomb(
            AirstrikePlaneEntity p, List<ProjectileEntity> projectiles, int grenadeSlot)
        {
            if (p == null || p.HasDropped) return (p, projectiles, grenadeSlot);

            var target = new Vector3(p.BombTargetX, 0f, 0f);
            float dropX = target.x - p.Vx * BombFallTime;
            if (p.X < dropX) return (p, projectiles, grenadeSlot);

            var rounds = new List<ProjectileEntity>(projectiles);
            rounds.Add(Airstrike(target, p.X, p.Y, p.Vx, ref grenadeSlot));
            return (p with { HasDropped = true }, rounds, grenadeSlot);
        }

        /// <summary>
        /// Launches the held volley after the plane has left and the
        /// camera has had its return beat. The guns and bomb live on
        /// the always-run path; this only sequences the infantry.
        /// </summary>
        static GameState StepAirstrikeRun(GameState s, float dt, System.Random random,
                                          AmmoCatalogSO ammoCatalog)
        {
            if (s.PendingVolleyAim is Vector3 held)
            {
                // Still crossing, or the camera is still coming home.
                if (s.AirstrikePlane != null || s.PendingVolleyDelay > 0f) return s;
                var launched = LaunchVolley(s with { TurnPhase = TurnPhase.Aiming },
                                            held, random, ammoCatalog);
                LogVolley(launched, "after the airstrike");
                return launched with { PendingVolleyAim = null,
                                       TurnPhase = TurnPhase.Resolving };
            }

            return s.TurnPhase == TurnPhase.AirstrikeRun
                ? s with { TurnPhase = TurnPhase.Resolving }
                : s;
        }

        /// <summary>
        /// COUNT THE VOLLEY, NOT THE SKY. The burst outlives the run and the bomb may still be
        /// falling, so a raw `Projectiles.Count` reported 18 rounds for an 11-round volley on
        /// device. That is this line's SECOND false reading — it once said `volley: 0 rounds`
        /// because the volley had not been built yet — and a lying instrument is worse than a
        /// missing one when it is the only instrument a release build has.
        /// </summary>
        static void LogVolley(GameState s, string note)
        {
            int rounds = s.Projectiles.Count(p => !p.IsStrafe && !p.IsAirstrike);
            Debug.Log($"[Battle] volley: {rounds} rounds, {note}");
        }

        /// <summary>
        /// How fast a reinforcement jogs to its slot, in units per second.
        ///
        /// dt-PARAMETERISED, never a per-tick multiply: this runs on the same varying dt every
        /// other motion here does.
        /// </summary>
        public const float MarchSpeed = 2.4f;

        /// <summary>
        /// Walks any player unit still carrying a `MarchTargetX` toward it, clearing the target on
        /// arrival so the unit becomes an ordinary member of the line.
        ///
        /// **Without this the relief squad simply never arrives.** It spawns a formation's width
        /// BEHIND the player line, off the edge the camera frames, and would stand there for the
        /// rest of the battle: men bought, paid for and permanently out of the fight. The march is
        /// the item, not decoration on it.
        ///
        /// Clearing the target on arrival is the other half. `GameState.IsVisuallyIdle` is false
        /// while any player unit still carries one, and it is a LATCH — nothing else would ever
        /// clear it, so the state would report something moving for the rest of the battle. Nothing
        /// in this port reads that property yet (it is ported facility, asserted by PortSelfTest),
        /// which is exactly why it would have gone wrong quietly.
        /// </summary>
        static List<UnitEntity> StepMarch(IReadOnlyList<UnitEntity> units, float dt,
                                          out bool marching)
        {
            marching = false;
            var list = units as List<UnitEntity> ?? units.ToList();
            bool any = false;
            foreach (var u in list) if (u.MarchTargetX != null) { any = true; break; }
            if (!any) return list;

            var stepped = new List<UnitEntity>(list.Count);
            foreach (var u in list)
            {
                if (u.MarchTargetX is not float target) { stepped.Add(u); continue; }
                float x = u.X + MarchSpeed * dt;
                if (x >= target) stepped.Add(u with { X = target, MarchTargetX = null });
                else { stepped.Add(u with { X = x }); marching = true; }
            }
            return stepped;
        }

        /// <summary>
        /// The muzzle height FireVolley gives every infantry round, above the shooter's feet.
        /// Shared so the shell's aim point is solved from the same origin the volley is launched
        /// from — two copies of this number would put the shell on a subtly different target.
        /// </summary>
        public const float InfantryMuzzleY = 0.35f;

        /// <summary>
        /// The volley's representative launch point: the MEAN of the firing line, at muzzle
        /// height. The line is spread along x, so its rounds land spread too; the mean is the
        /// volley's centre and therefore what the heavy round should be put on.
        /// </summary>
        static Vector3 MeanMuzzle(System.Collections.Generic.IReadOnlyList<UnitEntity> units)
        {
            if (units == null || units.Count == 0) return new Vector3(0f, InfantryMuzzleY, 0f);
            float x = 0f, y = 0f;
            foreach (var u in units) { x += u.X; y += u.Y; }
            return new Vector3(x / units.Count, y / units.Count + InfantryMuzzleY, 0f);
        }

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
                                                  System.Func<Vector3, CannonSpec, Vector3> solve,
                                                  AmmoModifiers? ammo = null)
        {
            // Defaults to the identity, so AutoFire — which has no ammo selection — is unchanged.
            var mods = ammo ?? AmmoModifiers.Standard;
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
                    Damage: mods.UnitDamage(c.damage),
                    OwnerIsPlayer: true)
                {
                    Type = ProjectileType.Shell,
                    SplashRadius = c.splashRadius,
                    // The shell takes the selected ammo like everything else in the volley —
                    // DYNAMISM_DESIGN is explicit that there are NO special cases, and an AP
                    // shell is the bunker-buster the type exists for.
                    StructureDamageMultiplier = mods.StructureMultiplier(c.structureDamageMultiplier),
                    Ammo = mods.Type,
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

                float muzzleY = u.Y + InfantryMuzzleY;
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

        /// <summary>
        /// Rolls each enemy's launch at the START of the windup. FireEnemyVolley
        /// consumes these so the raised rifles and the rounds agree.
        /// </summary>
        public static GameState PrepareEnemyVolley(GameState s, System.Random random)
        {
            if (s.EnemyUnits.Count == 0 || s.PlayerUnits.Count == 0) return s;

            float jitterMultiplier = s.SmokeScreenArmed ? SmokeScreenJitterMultiplier : 1f;
            var aim = new Dictionary<int, float>(s.EnemyUnits.Count);
            var launch = new Dictionary<int, Vector3>(s.EnemyUnits.Count);
            foreach (var e in s.EnemyUnits)
            {
                var v = SolveEnemyLaunch(e, s.PlayerUnits, jitterMultiplier, random);
                aim[e.Id] = Mathf.Atan2(v.y, Mathf.Abs(v.x)) * Mathf.Rad2Deg;
                launch[e.Id] = v;
            }
            return s with { EnemyAimDegrees = aim, EnemyLaunch = launch };
        }

        static Vector3 SolveEnemyLaunch(UnitEntity e, IReadOnlyList<UnitEntity> playerUnits,
                                        float jitterMultiplier, System.Random random)
        {
            var target = playerUnits[random.Next(playerUnits.Count)];
            return EnemyAI.AimAt(new Vector3(e.X, e.Y + 0.35f, e.Z),
                                 new Vector3(target.X, target.Y, target.Z),
                                 jitterMultiplier);
        }

        /// <summary>The enemy's answering volley, aimed with jitter at random player units.</summary>
        public static GameState FireEnemyVolley(GameState s, System.Random random)
        {
            if (s.EnemyUnits.Count == 0 || s.PlayerUnits.Count == 0) return s;

            var rounds = new List<ProjectileEntity>(s.Projectiles);
            var aim = new Dictionary<int, float>(s.EnemyUnits.Count);
            var launch = s.EnemyLaunch;
            int slot = s.NextBulletSlot;

            // SMOKE SCREEN: this one volley is fired through smoke, so every shooter's aim wanders
            // twice as far. It is spent HERE, at the volley it affects — the same "consumed by the
            // thing it does, not by the tap that armed it" rule the Airstrike follows.
            //
            // If PrepareEnemyVolley already rolled through smoke, the stored launch IS the
            // smoked shot and we must not roll again.
            float jitterMultiplier = s.SmokeScreenArmed ? SmokeScreenJitterMultiplier : 1f;
            bool prepared = launch != null && launch.Count > 0;

            foreach (var e in s.EnemyUnits)
            {
                Vector3 v;
                if (prepared && launch.TryGetValue(e.Id, out var stored))
                    v = stored;
                else
                    v = SolveEnemyLaunch(e, s.PlayerUnits, jitterMultiplier, random);

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
                EnemyLaunch = launch ?? EmptyLaunch,
                SmokeScreenArmed = false,
                LoadedConsumables = s.SmokeScreenArmed
                    ? Consumables.Decrement(s.LoadedConsumables, ConsumableType.SmokeScreen)
                    : s.LoadedConsumables,
                TurnPhase = TurnPhase.Resolving,
                TurnSide = TurnSide.Enemy,
            };
        }
    }
}
