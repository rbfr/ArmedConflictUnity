using System.Collections.Generic;
using System.Linq;
using System.Text;
using TMPro;
using UnityEditor;
using UnityEngine;
using ArmedConflict.Game;
using ArmedConflict.Data;

/// <summary>
/// Behavioural checks on the ported game/ modules. "It compiles" is not evidence a port is
/// faithful — these assert the properties the Kotlin originals were written to guarantee.
/// Run: -batchmode -quit -executeMethod PortSelfTest.Run
/// </summary>
public static class PortSelfTest
{
    static int failed;
    static readonly StringBuilder Log = new();

    static void Check(bool ok, string what)
    {
        if (!ok) failed++;
        if (what == null) return;          // loop-body assertions log only their summary line
        Log.AppendLine($"  [{(ok ? "ok  " : "FAIL")}] {what}");
    }

    static void Near(float a, float b, float eps, string what)
        => Check(Mathf.Abs(a - b) <= eps, $"{what} ({a:F5} vs {b:F5})");

    public static void Run()
    {
        failed = 0;

        // --- SpringFollow: the REST DEADBAND is the whole point of not using Mathf.SmoothDamp.
        {
            float v = 0f, vel = 0f;
            for (int i = 0; i < 2000; i++) SpringFollow.Step(ref v, ref vel, 10f, 1f / 60f, 0.25f);
            Check(v == 10f, "spring lands EXACTLY on target (bit-identical, not merely close)");
            Check(vel == 0f, "spring velocity reaches exactly zero");

            float before = v;
            SpringFollow.Step(ref v, ref vel, 10f, 1f / 60f, 0.25f);
            Check(v == before && vel == 0f, "a settled spring does not change on further ticks");

            // Critically damped: must never overshoot a static target.
            float p = 0f, pv = 0f, maxSeen = 0f;
            for (int i = 0; i < 600; i++)
            {
                SpringFollow.Step(ref p, ref pv, 1f, 1f / 60f, 0.3f);
                maxSeen = Mathf.Max(maxSeen, p);
            }
            Check(maxSeen <= 1f + 1e-5f, "spring never overshoots a static target");

            // Stability across wildly different dt — the reason for this formula.
            float q = 0f, qv = 0f;
            SpringFollow.Step(ref q, ref qv, 5f, 2f, 0.25f);
            Check(!float.IsNaN(q) && Mathf.Abs(q) <= 5.001f, "spring stable at an absurd dt");
        }

        // --- Formation.Grid: centred, correct row/column split.
        {
            var g = Formation.Grid(8, anchorX: 0f, anchorZ: 0f);
            Check(g.Count == 8, "grid returns every unit");
            Near(g.Take(5).Average(p => p.x), 0f, 1e-4f, "full row is centred on the anchor");
            Check(g.Take(5).All(p => Mathf.Abs(p.y - g[0].y) < 1e-5f), "first five share a row");
            Check(Mathf.Abs(g[5].y - g[0].y) > 1e-3f, "sixth unit starts a new row");
            Near(Mathf.Abs(g[1].x - g[0].x), Formation.DefaultColumnSpacing, 1e-5f,
                 "column spacing is DefaultColumnSpacing");
        }

        // --- Formation.Mounted: nobody may stand off the deck.
        {
            const float width = 1.2f;
            foreach (int n in new[] { 3, 5, 9, 14 })
            {
                var m = Formation.Mounted(n, anchorX: 0f, width: width);
                Check(m.Count == n, $"mounted({n}) returns every unit");
                bool onDeck = m.All(p => Mathf.Abs(p.x) <= width / 2f + 1e-4f);
                Check(onDeck, $"mounted({n}) keeps every defender on the deck");
            }
            Check(Formation.Mounted(4, 0f, 1.2f).All(p => Mathf.Abs(p.y) < 1e-6f),
                  "fewer than 5 defenders stand in ONE rank");
            Check(Formation.Mounted(6, 0f, 1.2f).Select(p => p.y).Distinct().Count() == 2,
                  "5+ defenders pack into TWO ranks (reference: castle tiers)");

            // An anchor off the side of the deck must be pulled back onto it.
            var shoved = Formation.Mounted(5, anchorX: 4f, width: width, deckCenterX: 0f);
            Check(shoved.All(p => Mathf.Abs(p.x) <= width / 2f + 1e-4f),
                  "an off-deck anchor is clamped back onto the deck");
        }

        // --- Formation.Clustered: gaps between clumps must exceed spacing within one.
        {
            var c = Formation.Clustered(9, 0f, random: new System.Random(1234));
            Check(c.Count == 9, "clustered returns every unit");
            var xs = c.Select(p => p.x).OrderBy(x => x).ToList();
            var gaps = Enumerable.Range(1, xs.Count - 1).Select(i => xs[i] - xs[i - 1]).ToList();
            Check(gaps.Max() > gaps.Where(g => g > 1e-4f).Min() * 1.4f,
                  "clumps separate — largest gap clearly exceeds intra-clump spacing");
        }

        // --- CameraFraming
        {
            Near(CameraFraming.HalfWidth(0f, new List<float> { -3f, 2f }), 3f, 1e-5f,
                 "half-width covers the furthest point from the anchor");
            Near(CameraFraming.HalfWidth(10f, new List<float> { -3f, 3f }), 13f, 1e-5f,
                 "off-centre anchor still frames the whole set");
            Check(CameraFraming.HalfWidth(0f, new List<float>()) == 0f, "empty set gives zero");
        }

        // --- EnemyAI: jitter is the ONLY inaccuracy, and speed is capped.
        {
            Near(EnemyAI.JitterRadius(2f), 4f, 1e-5f, "smoke screen doubles the jitter radius");
            Near(EnemyAI.AdvanceBudget(2f, true), 1f, 1e-5f, "overwatch flare halves advance budget");
            Near(EnemyAI.AdvanceBudget(2f, false), 2f, 1e-5f, "no flare leaves advance budget alone");
            bool capped = true;
            for (int i = 0; i < 500; i++)
            {
                var v = EnemyAI.AimAt(Vector3.zero, new Vector3(30f, 0f, 0f));
                if (v.magnitude > 12.0001f) capped = false;
            }
            Check(capped, "launch speed never exceeds the cap, however far the target");
        }

        // --- TrajectoryPhysics / SweptCollision (ported in Step 4, re-checked here)
        {
            Near(SweptCollision.UnitHitRadius, 0.5f * (0.48f / 0.77f) * 1.22f, 1e-6f,
                 "hit radius stays 1.22x body-proportional");
            // The sweep must catch a target the endpoints both miss — the tunnelling case.
            float d2 = SweptCollision.SegmentDistanceSq(0f, 5f, 0f, -5f, 0f, 0f);
            Check(d2 < 1e-6f, "swept segment catches a target passed BETWEEN two ticks");
        }

        // --- GameState / entities
        {
            // Records must behave like Kotlin data classes: `with` = copy(), value equality.
            var u = new UnitEntity(1, null, 1f, 0f, 0f, 32, true);
            var moved = u with { X = 5f };
            Check(u.X == 1f, "`with` does not mutate the original (copy semantics)");
            Check(moved.X == 5f && moved.Hp == 32, "`with` carries unchanged fields through");
            Check(u == new UnitEntity(1, null, 1f, 0f, 0f, 32, true), "records compare by value");
            Check(u != moved, "records with different values are unequal");
            Check(u.KnockbackAge == -1f, "knockback defaults to inactive (-1), not 0");

            // MaxHp defaults to Hp by construction — the fix for hpScale placements whose
            // damage fraction was taken against the DEFINITION's maxHp and went negative.
            var s4 = new StructureEntity(1, null, 0f, 0f, 0f, 340);
            Near(s4.HpFraction, 1f, 1e-6f, "a fresh structure reads full health");
            var hurt = s4 with { Hp = 170 };
            Near(hurt.HpFraction, 0.5f, 1e-6f, "damage is a fraction of the PLACEMENT's max hp");
            var scaled = new StructureEntity(2, null, 0f, 0f, 0f, 1360) { MaxHp = 1360 };
            Near(scaled.HpFraction, 1f, 1e-6f, "a 4x hpScale placement still reads 1.0 at full health");
            Check(StructureDamage.ShedChunkCount(scaled.HpFraction, 3) == 0,
                  "a 4x placement sheds NOTHING at full health (the negative-fraction bug)");

            // Shed curve: first group after ~1/(n+1) of the damage, last just before death.
            Check(StructureDamage.ShedChunkCount(1.0f, 3) == 0, "full health sheds nothing");
            Check(StructureDamage.ShedChunkCount(0.0f, 3) == 3, "destroyed sheds every group");
            Check(StructureDamage.ShedChunkCount(0.5f, 3) == 2, "half health sheds two of three");
            Check(StructureDamage.ShedChunkCount(-0.5f, 3) == 3, "over-damage clamps, never exceeds");

            // IsVisuallyIdle — both traps that permanently disabled it in the Android build.
            var idle = new GameState
            {
                Phase = GamePhase.Playing,
                TurnPhase = TurnPhase.Aiming,
            };
            Check(idle.IsVisuallyIdle, "an empty playing/aiming state is visually idle");

            var withRubble = idle with
            {
                Debris = new List<DebrisPiece>
                {
                    new(1, "x", false, 0, 0, 0, 0, 0, 0, 0, 0.2f, float.MaxValue) { Asleep = true },
                },
            };
            Check(withRubble.IsVisuallyIdle,
                  "SLEEPING rubble does not block idle (it persists for the whole level)");

            var withLiveDebris = idle with
            {
                Debris = new List<DebrisPiece>
                {
                    new(1, "x", false, 0, 0, 0, 0, 0, 0, 0, 0.2f, 1f),
                },
            };
            Check(!withLiveDebris.IsVisuallyIdle, "moving debris DOES block idle");

            var settledWreck = idle with
            {
                Wrecks = new List<WreckEntity>
                {
                    new(1, "x", 0, 0, 0, 1, 1) { Age = GameState.WreckCollapseSeconds },
                },
            };
            Check(settledWreck.IsVisuallyIdle,
                  "a wreck past the COLLAPSE WINDOW does not block idle forever");

            var collapsing = idle with
            {
                Wrecks = new List<WreckEntity> { new(1, "x", 0, 0, 0, 1, 1) { Age = 0.1f } },
            };
            Check(!collapsing.IsVisuallyIdle, "a still-collapsing wreck DOES block idle");

            var gliding = idle with { CameraFollowXVelocity = 0.5f };
            Check(!gliding.IsVisuallyIdle, "a camera still gliding blocks idle");
        }

        // --- ProgressStore / EconomyStore (persistence)
        {
            var lvl = ScriptableObject.CreateInstance<LevelDefinitionSO>();
            lvl.id = "selftest_level"; lvl.levelBase = 100; lvl.isTestLevel = false;
            var testRig = ScriptableObject.CreateInstance<LevelDefinitionSO>();
            testRig.id = "selftest_rig"; testRig.levelBase = 100; testRig.isTestLevel = true;
            ProgressStore.AllLevels = new List<LevelDefinitionSO> { lvl, testRig };
            ProgressStore.ResetAll();

            Check(ProgressStore.BestStars(lvl.id) == 0, "a fresh level has no stars");
            Check(ProgressStore.RecordStars(lvl.id, 2), "recording a first result sets a best");
            Check(!ProgressStore.RecordStars(lvl.id, 1), "a WORSE run does not overwrite the best");
            Check(ProgressStore.BestStars(lvl.id) == 2, "the best survives a worse run");
            Check(ProgressStore.RecordStars(lvl.id, 3), "a better run does set a new best");

            ProgressStore.RecordStars(testRig.id, 3);
            Check(ProgressStore.TotalStars() == 3,
                  "TEST levels are excluded from the total (they must never unlock a stage)");

            // Coins
            ProgressStore.ResetAll();
            ProgressStore.AddCoins(100);
            Check(ProgressStore.Coins() == 100, "coins accumulate");
            Check(!ProgressStore.SpendCoins(101), "cannot overspend");
            Check(ProgressStore.Coins() == 100, "a failed spend does not deduct");
            Check(ProgressStore.SpendCoins(60) && ProgressStore.Coins() == 40, "a valid spend deducts");

            // Set storage — the delimited-string replacement for getStringSet.
            ProgressStore.ResetAll();
            Check(ProgressStore.IsUnitUnlocked("rifleman"), "Rifleman is unlocked without being stored");
            Check(!ProgressStore.IsUnitUnlocked("sniper"), "other units start locked");
            ProgressStore.UnlockUnit("sniper");
            ProgressStore.UnlockUnit("grenadier");
            ProgressStore.UnlockUnit("sniper");
            Check(ProgressStore.UnlockedUnitIds().Count == 2, "unlocking twice does not duplicate");
            Check(ProgressStore.IsUnitUnlocked("sniper") && ProgressStore.IsUnitUnlocked("grenadier"),
                  "multiple unlocks round-trip through one stored string");

            // Ammo: locked types must never be returned as selected.
            ProgressStore.ResetAll();
            Check(ProgressStore.SelectedAmmo() == AmmoType.Standard, "ammo defaults to Standard");
            ProgressStore.SetSelectedAmmo(AmmoType.Incendiary);
            Check(ProgressStore.SelectedAmmo() == AmmoType.Standard,
                  "a LOCKED ammo type never comes back as selected");
            ProgressStore.UnlockAmmo(AmmoType.Incendiary);
            Check(ProgressStore.SelectedAmmo() == AmmoType.Incendiary,
                  "once unlocked, the stored selection is honoured");

            // Victory payout — the ORDERING trap.
            ProgressStore.ResetAll();
            var first = EconomyStore.GrantVictoryPayout(lvl, starsEarned: 3, previousBestStars: 0);
            Check(first.FirstClear && first.First3Star, "a first 3-star clear pays both bonuses");
            Check(first.Coins == 200 + 100 + 150, "first 3-star = 2.0x base + base + 1.5x base");

            var repeat = EconomyStore.GrantVictoryPayout(lvl, starsEarned: 3, previousBestStars: 3);
            Check(!repeat.FirstClear && !repeat.First3Star, "a repeat clear pays no bonuses");
            Check(repeat.Coins == 200, "a repeat 3-star pays the multiplier only");

            var twoStar = EconomyStore.GrantVictoryPayout(lvl, starsEarned: 2, previousBestStars: 1);
            Check(twoStar.Coins == 150, "a 2-star repeat pays 1.5x base");
            Check(EconomyStore.GrantDefeatPayout(lvl) == 15, "defeat still pays 15% of base");

            // Milestones: one call may cross several thresholds, and must pay each exactly once.
            ProgressStore.ResetAll();
            var levels = new List<LevelDefinitionSO>();
            for (int i = 0; i < 20; i++)
            {
                var l = ScriptableObject.CreateInstance<LevelDefinitionSO>();
                l.id = $"ms_{i}"; l.levelBase = 10;
                levels.Add(l);
            }
            ProgressStore.AllLevels = levels;
            ProgressStore.ResetAll();
            foreach (var l in levels) ProgressStore.RecordStars(l.id, 3);   // 60 stars
            var crossed = EconomyStore.CheckMilestones();
            Check(crossed.Count == 2, "60 stars crosses BOTH the 25 and 50 milestones in one call");
            Check(EconomyStore.CheckMilestones().Count == 0,
                  "milestones are idempotent — a second call pays nothing");

            ProgressStore.ResetAll();
            ProgressStore.AllLevels = new List<LevelDefinitionSO>();
        }

        // --- LevelBuilder, against the REAL imported L1 asset
        {
            // Campaign level assets are named for their IDENTITY, not their number — the
            // ordering moves as the beat chart is authored against, and Level4.asset meaning
            // "level 7" was a trap waiting to happen.
            var l1 = AssetDatabase.LoadAssetAtPath<LevelDefinitionSO>(
                "Assets/GameData/Levels/PatrolEncounter.asset");
            if (l1 == null) { Check(false, "PatrolEncounter asset present"); }
            else
            {
                var st = LevelBuilder.BuildInitialState(l1, battleId: 1, totalLevels: 29,
                                                        random: new System.Random(7));
                Check(st.PlayerUnits.Count == 10, $"L1 builds 10 player units (2 on tank + 8 line)");
                Check(st.EnemyUnits.Count == 9, "L1 builds 9 enemy units (6 line + 3 garrison)");
                Check(st.Structures.Count == 2, "L1 builds 2 structures");
                Check(st.Phase == GamePhase.Preview, "a new battle starts in Preview");

                // Id bands must not collide — the tick relies on globally unique ids.
                var allIds = st.PlayerUnits.Select(u => u.Id)
                    .Concat(st.EnemyUnits.Select(u => u.Id))
                    .Concat(st.Structures.Select(s2 => s2.Id)).ToList();
                Check(allIds.Distinct().Count() == allIds.Count, "unit and structure ids never collide");

                // The garrison must stand on the outpost's measured deck, not on `size`.
                var outpost = st.Structures.First(s2 => s2.Definition.id == "outpost");
                var garrison = st.EnemyUnits.Where(u => u.StandingOnStructureId != null).ToList();
                // 5 since the Phase D authoring pass — composition rule 5 wants the
                // majority of the roster on the structure, even on the teaching level.
                Check(garrison.Count == 5, "5 enemies are garrisoned on the outpost");
                float deck = 0.560f * 2.5f;   // deckY already scaled at import
                foreach (var g in garrison)
                    Near(g.Y, deck, 1e-3f, "garrison stands on the measured deckY, not on size");

                // Ground units stand at y=0.
                Check(st.EnemyUnits.Where(u => u.StandingOnStructureId == null).All(u => u.Y == 0f),
                      "ground units stand at y = 0");

                // Garrison must sit ON the deck horizontally too.
                float halfDeck = outpost.Definition.standWidth / 2f;
                foreach (var g in garrison)
                    Check(Mathf.Abs(g.X - outpost.X) <= halfDeck + 1e-3f,
                          "garrison is clamped onto the deck horizontally");

                // Structure entity y centres the collision box on the placement.
                Near(outpost.Y, 0f * outpost.Definition.worldScale + outpost.Definition.size / 2f,
                     1e-4f, "structure y centres its box on the placement");

                // hpScale carries into MaxHp so damage fractions have the right denominator.
                Check(st.Structures.All(s2 => s2.MaxHp == s2.Hp), "structures start at full health");
                Check(st.Structures.All(s2 => Mathf.Approximately(s2.HpFraction, 1f)),
                      "every structure reads fraction 1.0 at build time");

                // Camera anchors are the INITIAL means, and the sides are on opposite sides.
                Check(st.PlayerCamXAnchor < 0f && st.EnemyCamXAnchor > 0f,
                      "camera anchors put the player left and the enemy right (game space)");
                Near(st.PlayerCamXAnchor, st.PlayerUnits.Average(u => u.X), 1e-4f,
                     "player anchor is the mean of the initial roster");

                // The tank's cannon ammo becomes the battle's shell count.
                Check(st.TankShellsRemaining > 0, "the player tank contributes its cannon shells");

                Check(st.Helicopter == null, "no helicopter while HeliEnabled is false");

                // Determinism: same seed, same layout. Formation jitter must not leak randomness.
                var again = LevelBuilder.BuildInitialState(l1, 1, 29, new System.Random(7));
                Check(again.PlayerUnits.Select(u => u.X).SequenceEqual(st.PlayerUnits.Select(u => u.X)),
                      "the same seed rebuilds an identical formation");
            }
        }

        // --- CollisionSystem: the RESOLUTION ORDER, which is where the behaviour lives
        {
            var unitDef = ScriptableObject.CreateInstance<UnitDefinitionSO>();
            unitDef.id = "t"; unitDef.maxHp = 32; unitDef.damage = 8;
            var wallDef = ScriptableObject.CreateInstance<StructureDefinitionSO>();
            wallDef.id = "wall"; wallDef.maxHp = 100; wallDef.size = 2f;
            wallDef.hasHitWidth = true; wallDef.hitWidth = 2.4f; wallDef.isPlayerSide = false;

            UnitEntity Enemy(int id, float x, float y, int hp = 32)
                => new(id, unitDef, x, y, 0f, hp, false);
            ProjectileEntity Shot(int id, float fromX, float fromY, float toX, float toY,
                                  float splash = 0f, int dmg = 8)
                => new(id, toX, toY, 0f, toX - fromX, toY - fromY, 0f, dmg, true)
                   { PrevX = fromX, PrevY = fromY, PrevZ = 0f, SplashRadius = splash };

            // A UNIT IN FRONT OF A WALL takes the round. Resolving structures first "shielded"
            // any ground unit inside a wide structure's AABB, so front-rank defenders read as
            // taking direct hits that dealt no damage.
            var wall = new StructureEntity(100, wallDef, 5f, 1f, 0f, 100);
            var inFront = Enemy(1, 5f, 0.3f);
            var r1 = CollisionSystem.ResolveHits(
                new[] { Shot(1, 5f, 3f, 5f, 0.3f) }, new[] { inFront },
                new List<UnitEntity>(), new[] { wall });
            Check(r1.UnitDamage.ContainsKey(1), "a unit standing inside a wall's box still takes the hit");
            Check(!r1.StructureDamage.ContainsKey(100), "the wall behind it takes NO damage");

            // A shot that strikes no unit is blocked by the wall.
            var r2 = CollisionSystem.ResolveHits(
                new[] { Shot(2, 5f, 3f, 5f, 1f) }, new List<UnitEntity>(),
                new List<UnitEntity>(), new[] { wall });
            Check(r2.StructureDamage.TryGetValue(100, out int wd) && wd == 8,
                  "a shot hitting nothing but the wall damages it");

            // OWN-SIDE structures never block — a garrison fires clean over its own fortress.
            var playerWallDef = ScriptableObject.CreateInstance<StructureDefinitionSO>();
            playerWallDef.id = "pw"; playerWallDef.size = 2f; playerWallDef.isPlayerSide = true;
            playerWallDef.hasHitWidth = true; playerWallDef.hitWidth = 2.4f;
            var ownWall = new StructureEntity(101, playerWallDef, 5f, 1f, 0f, 100);
            var r3 = CollisionSystem.ResolveHits(
                new[] { Shot(3, 5f, 3f, 5f, 1f) }, new List<UnitEntity>(),
                new List<UnitEntity>(), new[] { ownWall });
            Check(r3.StructureDamage.Count == 0, "a player shot passes through a PLAYER structure");

            // Damage accumulates against CURRENT hp, not maxHp: a half-health unit must not
            // soak a fresh unit's worth of hits.
            var half = Enemy(2, 0f, 0.3f, hp: 8);
            var r4 = CollisionSystem.ResolveHits(
                new[] { Shot(4, 0f, 3f, 0f, 0.3f), Shot(5, 0f, 3f, 0f, 0.3f) },
                new[] { half }, new List<UnitEntity>(), new List<StructureEntity>());
            Check(r4.UnitDamage[2] == 8, "a second round does not pile onto an already-dead unit");
            Check(r4.HitProjectileIds.Count == 1, "the second round is left free to fly on");

            // The detonation lands at the CONTACT POINT, not the overshot tick-end position.
            var far = Enemy(3, 0f, 0.5f);
            var overshoot = Shot(6, 0f, 4f, 0f, -4f);
            var r5 = CollisionSystem.ResolveHits(new[] { overshoot }, new[] { far },
                                                 new List<UnitEntity>(), new List<StructureEntity>());
            Check(r5.Detonations.Count == 1, "the swept hit produces one detonation");
            Near(r5.Detonations[0].Y, 0.5f, 0.05f,
                 "detonation sits at the contact point, not the tick-end position");

            // Splash damages everyone in radius including the trigger target, and marks them
            // for the knockback hop; a plain bullet marks nobody.
            var a = Enemy(4, 0f, 0.3f); var b = Enemy(5, 0.6f, 0.3f); var c = Enemy(6, 4f, 0.3f);
            var r6 = CollisionSystem.ResolveHits(
                new[] { Shot(7, 0f, 3f, 0f, 0.3f, splash: 1.5f) },
                new[] { a, b, c }, new List<UnitEntity>(), new List<StructureEntity>());
            Check(r6.UnitDamage.ContainsKey(4) && r6.UnitDamage.ContainsKey(5),
                  "splash catches the trigger target and its neighbour");
            Check(!r6.UnitDamage.ContainsKey(6), "splash does not reach outside its radius");
            Check(r6.ExplosiveHitUnitIds.Contains(5), "splash victims are marked for knockback");
            Check(r1.ExplosiveHitUnitIds.Count == 0, "a plain bullet marks nobody for knockback");

            // A splash weapon detonates on the ground rather than wasting into the dirt.
            var ground = new ProjectileEntity(8, 2f, -0.1f, 0f, 0f, -5f, 0f, 8, true)
                { PrevX = 2f, PrevY = 0.4f, SplashRadius = 1.5f };
            var near = Enemy(7, 2.5f, 0.3f);
            var r7 = CollisionSystem.ResolveHits(new[] { ground }, new[] { near },
                                                 new List<UnitEntity>(), new List<StructureEntity>());
            Check(r7.Detonations.Count == 1 && r7.Detonations[0].IsGroundBurst,
                  "a splash round that hits nothing detonates on the ground");
            Check(r7.UnitDamage.ContainsKey(7), "a ground burst still damages units in radius");

            // Collapse propagation, transitively up a stack.
            var tierDef = ScriptableObject.CreateInstance<StructureDefinitionSO>();
            tierDef.id = "tier"; tierDef.size = 1f;
            var t1 = new StructureEntity(1, tierDef, 0f, 0f, 0f, 10);
            var t2 = new StructureEntity(2, tierDef, 0f, 1f, 0f, 10) { RestsOnId = 1 };
            var t3 = new StructureEntity(3, tierDef, 0f, 2f, 0f, 10) { RestsOnId = 2 };
            var linked = new StructureEntity(4, tierDef, 5f, 0f, 0f, 10) { CollapseWith = 1 };
            var collapsed = CollisionSystem.PropagateCollapse(
                new[] { t2, t3, linked }, new[] { 1 });
            Check(collapsed.Contains(2) && collapsed.Contains(3),
                  "destroying the base collapses the whole stack transitively");
            Check(collapsed.Contains(4), "an explicit collapseWith partner comes down too");

            // A GARRISON ON A ROOF MUST BE SHOOTABLE. The collision box rises to the measured
            // deck, not to `size` — otherwise the gap between them is invisible masonry sitting
            // on top of the defenders, and a round descending on them is spent on the wall edge
            // instead. The only way to kill them then is to destroy the building.
            var outpost = ScriptableObject.CreateInstance<StructureDefinitionSO>();
            outpost.id = "outpost"; outpost.size = 2f;
            outpost.hasHitWidth = true; outpost.hitWidth = 3.25f;
            outpost.hasDeckY = true; outpost.deckY = 1.4f;      // roof well below `size`
            var post = new StructureEntity(200, outpost, 7f, 1f, 0f, 90);   // centre of a 2-tall box

            var defenderDef = ScriptableObject.CreateInstance<UnitDefinitionSO>();
            defenderDef.id = "d"; defenderDef.maxHp = 32;
            var defender = new UnitEntity(9, defenderDef, 7f, 1.4f, 0f, 32, false);

            // A round plunging onto the deck from above.
            var plunge = new ProjectileEntity(90, 7f, 1.45f, 0f, 0f, -9f, 0f, 8, true)
                { PrevX = 7f, PrevY = 2.6f };
            var roofHit = CollisionSystem.ResolveHits(new[] { plunge }, new[] { defender },
                                                      new List<UnitEntity>(), new[] { post });
            Check(roofHit.UnitDamage.ContainsKey(9), "a garrison on the roof CAN be shot directly");
            Check(!roofHit.StructureDamage.ContainsKey(200),
                  "and the round is not swallowed by phantom masonry above the roof");

            // The building itself must still block a round aimed at its BODY.
            var atWall = new ProjectileEntity(91, 6f, 0.7f, 0f, 3f, -3f, 0f, 8, true)
                { PrevX = 5f, PrevY = 1.2f };
            var wallHit = CollisionSystem.ResolveHits(new[] { atWall }, new List<UnitEntity>(),
                                                      new List<UnitEntity>(), new[] { post });
            Check(wallHit.StructureDamage.ContainsKey(200),
                  "the structure still blocks rounds that strike its actual body");

            // And a round passing well ABOVE the roof hits nothing at all.
            var over = new ProjectileEntity(92, 7f, 1.9f, 0f, 3f, 0f, 0f, 8, true)
                { PrevX = 6f, PrevY = 1.9f };
            var overHit = CollisionSystem.ResolveHits(new[] { over }, new List<UnitEntity>(),
                                                      new List<UnitEntity>(), new[] { post });
            Check(overHit.StructureDamage.Count == 0, "a round clearing the roof passes over it");

            var independent = new StructureEntity(5, tierDef, 9f, 0f, 0f, 10);
            var c2 = CollisionSystem.PropagateCollapse(new[] { independent }, new[] { 1 });
            Check(!c2.Contains(5), "an unrelated structure is left standing");
        }

        // --- ProjectileSystem: stepping, culling, and above all the ORDER
        {
            var unitDef = ScriptableObject.CreateInstance<UnitDefinitionSO>();
            unitDef.id = "t"; unitDef.maxHp = 32; unitDef.damage = 8;

            var flying = new ProjectileEntity(1, 0f, 5f, 0f, 4f, 0f, 0f, 8, true);
            var stepped = ProjectileSystem.StepAll(new[] { flying }, 1f / 60f, 0f);
            Check(stepped.Count == 1, "stepping preserves the round");
            Check(stepped[0].PrevX == 0f && stepped[0].PrevY == 5f,
                  "the pre-step position is recorded as Prev (the swept collision segment)");
            Check(stepped[0].X > 0f, "horizontal motion advances");
            Check(stepped[0].Vy < 0f, "gravity is applied to vertical velocity");
            Near(stepped[0].Age, 1f / 60f, 1e-6f, "age accumulates by dt");

            Check(ProjectileSystem.ClampDt(1f) == ProjectileSystem.MaxTickSeconds,
                  "a huge dt is clamped (never sub-stepped — hence swept collision)");

            // THE ORDERING GUARANTEE: a round that crosses y=0 THROUGH a target on the same tick
            // must register the hit. Cull runs last and honours the hit set, so the round is
            // removed as a hit rather than silently vanishing into the floor.
            var target = new UnitEntity(1, unitDef, 0f, 0.2f, 0f, 32, false);
            var plunging = new ProjectileEntity(2, 0f, -0.3f, 0f, 0f, -8f, 0f, 8, true)
                { PrevX = 0f, PrevY = 0.6f, PrevZ = 0f };
            var hits = CollisionSystem.ResolveHits(new[] { plunging }, new[] { target },
                                                   new List<UnitEntity>(), new List<StructureEntity>());
            Check(hits.UnitDamage.ContainsKey(1),
                  "a round crossing the floor THROUGH a target still registers the hit");
            var afterCull = ProjectileSystem.Cull(new[] { plunging }, hits.HitProjectileIds,
                                                  new List<UnitEntity>(), new[] { target },
                                                  new List<StructureEntity>());
            Check(afterCull.Count == 0, "and is then culled as a hit, not as a floor miss");

            // A round that reaches the floor having hit nothing is a ground impact, then culled.
            var missed = new ProjectileEntity(3, 1f, -0.1f, 0f, 0f, -8f, 0f, 8, true)
                { PrevX = 1f, PrevY = 0.5f };
            var noHits = new HashSet<int>();
            var ground = ProjectileSystem.GroundImpacts(new[] { missed }, noHits);
            Check(ground.Count == 1, "a round that reached the floor is a ground impact");
            Check(ProjectileSystem.Cull(new[] { missed }, noHits, new List<UnitEntity>(),
                                        new List<UnitEntity>(), new List<StructureEntity>()).Count == 0,
                  "and is culled");
            Check(ProjectileSystem.GroundImpacts(new[] { missed }, new HashSet<int> { 3 }).Count == 0,
                  "a round that HIT something is not also counted as a ground impact");

            // Side bounds: an overshoot can never hit anything, and must not hold the phase open.
            var player = new UnitEntity(2, unitDef, -8f, 0f, 0f, 32, true);
            var enemy = new UnitEntity(3, unitDef, 8f, 0f, 0f, 32, false);
            var overshot = new ProjectileEntity(4, 50f, 5f, 0f, 9f, 0f, 0f, 8, true);
            var inField = new ProjectileEntity(5, 3f, 5f, 0f, 9f, 0f, 0f, 8, true);
            var kept = ProjectileSystem.Cull(new[] { overshot, inField }, noHits,
                                             new[] { player }, new[] { enemy },
                                             new List<StructureEntity>());
            Check(kept.Count == 1 && kept[0].Id == 5, "a round far past the enemy is culled");

            var behind = new ProjectileEntity(6, -50f, 5f, 0f, -9f, 0f, 0f, 8, false);
            Check(ProjectileSystem.Cull(new[] { behind }, noHits, new[] { player }, new[] { enemy },
                                        new List<StructureEntity>()).Count == 0,
                  "a round far behind the player is culled too");

            // Explosions: advance, then survive exactly ONE extra tick at progress 1.
            var boom = new ExplosionEntity(1, 0f, 0f, 0f) { Progress = 0.9f };
            var t1 = ProjectileSystem.AdvanceExplosions(new[] { boom }, 0.2f);
            Check(t1.Count == 1 && Mathf.Approximately(t1[0].Progress, 1f),
                  "an explosion reaching the end is HELD for one extra tick at progress 1");
            var t2 = ProjectileSystem.AdvanceExplosions(t1, 0.2f);
            Check(t2.Count == 0, "and is removed on the tick after that");
            var mid = ProjectileSystem.AdvanceExplosions(
                new[] { new ExplosionEntity(2, 0f, 0f, 0f) { Progress = 0.1f } }, 0.2f);
            Check(mid.Count == 1 && mid[0].Progress > 0.1f, "an unfinished explosion keeps advancing");
        }

        // --- TurnFlow
        {
            // Win/loss: structures are a damage objective, never a win condition.
            Check(TurnFlow.ResolvePhase(0, 5) == GamePhase.Defeat, "no player units = defeat");
            Check(TurnFlow.ResolvePhase(5, 0) == GamePhase.Victory, "no enemy units = victory");
            Check(TurnFlow.ResolvePhase(5, 5) == GamePhase.Playing, "both alive = still playing");
            Check(TurnFlow.ResolvePhase(0, 0) == GamePhase.Defeat,
                  "a mutual wipe resolves as DEFEAT — the player check comes first");

            // Stars: readable thresholds, not a formula.
            Check(TurnFlow.StarsFor(10, 10) == 3, "no losses = 3 stars");
            Check(TurnFlow.StarsFor(8, 10) == 3, "losing under a quarter still = 3 stars");
            Check(TurnFlow.StarsFor(7, 10) == 2, "losing more than a quarter = 2 stars");
            Check(TurnFlow.StarsFor(4, 10) == 2, "at 40% survival = 2 stars");
            Check(TurnFlow.StarsFor(3, 10) == 1, "below 40% survival = 1 star");
            Check(TurnFlow.StarsFor(1, 0) == 1, "a zero initial count cannot divide by zero");

            // Volley gating — the door gunner and melee blocks come FIRST.
            Check(TurnFlow.EvaluateVolley(3, 3, 0f, TurnSide.Player, 0, 0) == TurnFlow.VolleyGate.Busy,
                  "rounds still in the air keep the volley busy");
            Check(TurnFlow.EvaluateVolley(0, 3, 0f, TurnSide.Player, 0, 0) == TurnFlow.VolleyGate.JustLanded,
                  "the volley emptying this tick starts the post-volley pause");
            Check(TurnFlow.EvaluateVolley(0, 0, 0.8f, TurnSide.Player, 0, 0) == TurnFlow.VolleyGate.Pausing,
                  "the pause runs down before handover");
            Check(TurnFlow.EvaluateVolley(0, 0, 0f, TurnSide.Player, 0, 0) == TurnFlow.VolleyGate.ReadyToHandOver,
                  "an empty sky and an expired pause hands the turn over");
            Check(TurnFlow.EvaluateVolley(0, 0, 0f, TurnSide.Enemy, 2, 0) == TurnFlow.VolleyGate.Busy,
                  "a door gunner mid-burst blocks handover even with an empty sky");
            Check(TurnFlow.EvaluateVolley(0, 0, 0f, TurnSide.Player, 2, 0) == TurnFlow.VolleyGate.ReadyToHandOver,
                  "the gunner block applies to the ENEMY turn only");
            Check(TurnFlow.EvaluateVolley(0, 0, 0f, TurnSide.Player, 0, 1) == TurnFlow.VolleyGate.Busy,
                  "an unresolved melee skirmish blocks handover");

            // Victory award — the ORDERING contract, end to end through the real stores.
            var lvl = ScriptableObject.CreateInstance<LevelDefinitionSO>();
            lvl.id = "turnflow_level"; lvl.levelBase = 100;
            ProgressStore.AllLevels = new List<LevelDefinitionSO> { lvl };
            ProgressStore.ResetAll();

            var first = TurnFlow.AwardVictory(lvl, survivors: 10, initialCount: 10);
            Check(first.Stars == 3, "a clean win awards 3 stars");
            Check(first.BonusTag == "Daily Bonus!",
                  "the daily bonus overwrites the tag — only one banner is shown");
            Check(first.Coins == 450 + 50, "first 3-star clear + daily = 450 + 50");
            Check(ProgressStore.BestStars(lvl.id) == 3, "the star result is recorded");

            var second = TurnFlow.AwardVictory(lvl, survivors: 10, initialCount: 10);
            Check(second.Coins == 200, "a repeat clear pays the multiplier only, daily spent");
            Check(second.BonusTag == null, "and carries no bonus banner");

            ProgressStore.ResetAll();
            Check(TurnFlow.AwardDefeat(lvl) == 15, "defeat still pays 15% of base");

            // Star REASONS — PRODUCT_DIRECTION 0.5 shows the player why, every time, and the
            // number it promises has to be the number the award code actually pays on.
            bool survivorsAgree = true;
            for (int n = 1; n <= 30; n++)
                for (int want = 2; want <= 3; want++)
                {
                    int need = TurnFlow.SurvivorsFor(want, n);
                    if (TurnFlow.StarsFor(need, n) < want) survivorsAgree = false;
                    if (need > 0 && TurnFlow.StarsFor(need - 1, n) >= want) survivorsAgree = false;
                }
            Check(survivorsAgree,
                  "SurvivorsFor is the FEWEST survivors StarsFor still rewards, for every roster " +
                  "size — the promise on the victory card and the payout cannot disagree");

            Check(TurnFlow.SurvivorsFor(3, 14) == 11, "3★ on a 14-unit roster needs 11 alive");
            Check(TurnFlow.StarReason(10, 14) == "Lost 4 of 14 — keep 11 alive for 3 stars",
                  "a 2-star result names the shortfall and the next threshold in whole units");
            Check(TurnFlow.StarReason(10, 14).IndexOf('★') < 0,
                  "and says it in ASCII — the default TMP font renders ★ as a missing-glyph box");
            Check(TurnFlow.StarReason(14, 14).Contains("14 of 14"),
                  "a clean sweep reports what was kept and promises nothing further");
            Check(!TurnFlow.StarReason(14, 14).Contains("for 4"),
                  "and never dangles a fourth star");
            Check(TurnFlow.StarReason(3, 0) == "", "a zero-unit roster has no reason to give");

            ProgressStore.ResetAll();
            ProgressStore.AllLevels = new List<LevelDefinitionSO>();
        }

        // --- CameraDirector
        {
            var ud = ScriptableObject.CreateInstance<UnitDefinitionSO>();
            ud.id = "u"; ud.maxHp = 32;

            // Per-phase framing: Aiming frames the PLAYER LINE ONLY, scout frames the enemy.
            float p = 3f, e = 9f, shooter = 5f, march = 6f, reinforce = 7f;
            Check(CameraDirector.PhaseHalfWidth(TurnPhase.Aiming, TurnSide.Player,
                      p, e, shooter, march, false, reinforce, false) == p,
                  "Aiming frames the player line only");
            Check(CameraDirector.PhaseHalfWidth(TurnPhase.PlayerScout, TurnSide.Player,
                      p, e, shooter, march, false, reinforce, false) == e,
                  "PlayerScout frames the enemy cluster");
            Check(CameraDirector.PhaseHalfWidth(TurnPhase.Aiming, TurnSide.Player,
                      p, e, shooter, march, false, reinforce, true) == reinforce,
                  "Aiming widens while reinforcements are still marching in");
            Check(CameraDirector.PhaseHalfWidth(TurnPhase.EnemyWindup, TurnSide.Enemy,
                      p, e, shooter, march, false, reinforce, false) == shooter,
                  "EnemyWindup frames the SHOOTERS when nobody is marching");
            Check(CameraDirector.PhaseHalfWidth(TurnPhase.EnemyWindup, TurnSide.Enemy,
                      p, e, shooter, march, true, reinforce, false) == march,
                  "EnemyWindup follows the marchers when any are moving");
            Check(CameraDirector.PhaseHalfWidth(TurnPhase.Resolving, TurnSide.Enemy,
                      p, e, shooter, march, false, reinforce, false) == p,
                  "Resolving an ENEMY volley frames the player being shot at");
            Check(CameraDirector.PhaseHalfWidth(TurnPhase.Resolving, TurnSide.Player,
                      p, e, shooter, march, false, reinforce, false) == e,
                  "Resolving a PLAYER volley frames the enemy being shot at");

            // A settled melee unit far up the field must not widen the shooter frame.
            var shooters = new List<float> { 6f, 6.5f, 7f };
            var withStructures = new List<float> { 6f, 6.5f, 7f, 8f };
            float reachNoMelee = CameraDirector.ShooterReach(shooters, withStructures);
            float reachWithMelee = CameraDirector.ShooterReach(shooters,
                withStructures.Concat(new[] { 8f }).ToList());
            Near(reachNoMelee, reachWithMelee, 1e-5f,
                 "a melee unit excluded from the shooter set cannot widen the frame");
            Check(CameraDirector.ShooterReach(new List<float>(), withStructures) == 0f,
                  "no shooters means no shooter reach");

            // March framing has a FLOOR so escorting one unit does not zoom to a keyhole.
            Check(CameraDirector.MarchHalfWidth(new List<float> { 5f }, new List<float>())
                      == CameraDirector.MarchHalfWidthMin,
                  "a single marcher still gets the minimum march frame");
            Check(CameraDirector.MarchHalfWidth(new List<float> { 0f, 20f }, new List<float>()) == 10f,
                  "a wide march spreads past the floor");

            // Half-width -> camera z, clamped into the usable band.
            Near(CameraDirector.TargetZ(4.5f, false, 19f), 10f, 1e-4f,
                 "half-width converts to distance through the half-FOV tangent");
            Check(CameraDirector.TargetZ(0.1f, false, 19f) == CameraDirector.ZMin,
                  "a tiny frame is clamped to the near limit");
            Check(CameraDirector.TargetZ(100f, false, 19f) == CameraDirector.GameplayZ,
                  "a huge frame is clamped to the far limit");
            Check(CameraDirector.TargetZ(100f, true, 12f) == 12f,
                  "staticCamera caps the zoom at the battlefield width");
            // The static zoom-in floor is applied AFTER the global ZMin clamp, so at ordinary
            // battlefield widths ZMin dominates and the static floor is inert — which is what
            // CLAUDE.md records about this flag on real levels.
            Check(CameraDirector.TargetZ(0.1f, true, 12f) == CameraDirector.ZMin,
                  "at a normal battlefield width the global near limit still wins");
            Check(CameraDirector.TargetZ(0.1f, true, 40f) == 40f * CameraDirector.StaticCameraZoomInFraction,
                  "on a very wide battlefield the static floor does bite (40 * 0.2 = 8 > ZMin)");

            // Volley follow: MONOTONIC pursuit — the camera only moves the way the volley flies.
            var pl = new List<UnitEntity> { new(1, ud, -8f, 0f, 0f, 32, true) };
            var en = new List<UnitEntity> { new(2, ud, 8f, 0f, 0f, 32, false) };
            var st = new List<StructureEntity>();
            var rounds = new List<ProjectileEntity>
            {
                new(1, 0f, 3f, 0f, 5f, 0f, 0f, 8, true),
            };
            float x0 = CameraDirector.FollowVolley(null, 0f, rounds, pl, en, st, false,
                                                   1f / 60f, out float v0);
            Near(x0, 0f, 1e-3f, "a fresh follow starts at the volley mean");

            // A player volley (flying right) must never be dragged BACKWARDS by a falling mean.
            var retreating = new List<ProjectileEntity>
            {
                new(1, -5f, 3f, 0f, 5f, 0f, 0f, 8, true),
            };
            float held = CameraDirector.FollowVolley(2f, 0f, retreating, pl, en, st, false,
                                                     1f / 60f, out _);
            Check(held >= 2f - 1e-4f,
                  "a rightward volley never drags the camera backwards (monotonic pursuit)");

            var enemyRounds = new List<ProjectileEntity>
            {
                new(1, 5f, 3f, 0f, -5f, 0f, 0f, 8, false),
            };
            float heldLeft = CameraDirector.FollowVolley(-2f, 0f, enemyRounds, pl, en, st, false,
                                                        1f / 60f, out _);
            Check(heldLeft <= -2f + 1e-4f, "a leftward enemy volley is monotonic the other way");

            // A melee reset drops the carried velocity, so the spring does not fling.
            CameraDirector.FollowVolley(5f, 99f, rounds, pl, en, st, true, 1f / 60f, out float vReset);
            Check(Mathf.Abs(vReset) < 50f, "a melee reset discards the carried velocity");
        }

        // --- CosmeticSystems
        {
            // RATE INDEPENDENCE: the same friction must decay the same amount per SECOND
            // regardless of tick rate, or a ragdoll that rolls on one device skids on another.
            float at60 = CosmeticSystems.DecayPerTick60(0.962f, 1f / 60f);
            Near(at60, 0.962f, 1e-5f, "at 60Hz the per-tick factor is used as authored");
            float oneSecAt60 = Mathf.Pow(CosmeticSystems.DecayPerTick60(0.962f, 1f / 60f), 60);
            float oneSecAt120 = Mathf.Pow(CosmeticSystems.DecayPerTick60(0.962f, 1f / 120f), 120);
            float oneSecAt30 = Mathf.Pow(CosmeticSystems.DecayPerTick60(0.962f, 1f / 30f), 30);
            Near(oneSecAt120, oneSecAt60, 1e-4f, "120Hz decays the same amount per second as 60");
            Near(oneSecAt30, oneSecAt60, 1e-4f, "30Hz decays the same amount per second as 60");

            // SHAKE must reach exactly zero, and must decay on EVERY tick path — a level ending
            // on a killing volley froze it forever and jittered the whole victory screen.
            float shake = CosmeticSystems.AddShakeForKills(0f, 4);
            Near(shake, 0.6f, 1e-5f, "four kills raise the shake");
            for (int i = 0; i < 200; i++) shake = CosmeticSystems.DecayShake(shake, 1f / 60f);
            Check(shake == 0f, "shake decays to EXACTLY zero, never a lingering epsilon");
            Check(CosmeticSystems.DecayShake(0f, 1f) == 0f, "decaying past zero cannot go negative");

            // HEALTH BAR CLOCK. Same failure shape as the shake: something the renderer tests for
            // and nothing clears leaves a bar over a soldier for the rest of the battle. -1 is the
            // only "off" value and it has to come back on its own.
            Check(CosmeticSystems.StepHitAge(-1f, 1f / 60f) == -1f,
                  "a unit that has not been hit never shows a bar");
            float age = 0f;
            int frames = 0;
            while (age >= 0f && frames < 100000) { age = CosmeticSystems.StepHitAge(age, 1f / 60f); frames++; }
            Check(age == -1f, "the bar retires, and at exactly -1 rather than an epsilon");
            Near(frames / 60f, CosmeticSystems.HealthBarSeconds, 2f / 60f,
                 $"the bar lasts its stated few seconds ({frames / 60f:F2}s)");
            // Rate independence — the panel is pinned to 60 today and must not be relied on.
            float at30 = 0f;
            int f30 = 0;
            while (at30 >= 0f && f30 < 100000) { at30 = CosmeticSystems.StepHitAge(at30, 1f / 30f); f30++; }
            Near(f30 / 30f, frames / 60f, 1f / 15f, "the bar lasts the same TIME at 30Hz");
            // And it FADES rather than blinking out: full early, zero at the end.
            Check(CosmeticSystems.HealthBarAlpha(0f) == 1f, "a fresh bar is fully opaque");
            Near(CosmeticSystems.HealthBarAlpha(CosmeticSystems.HealthBarSeconds), 0f, 1e-4f,
                 "and has faded to nothing by the time it retires");
            Check(CosmeticSystems.HealthBarAlpha(CosmeticSystems.HealthBarSeconds - 0.35f) < 1f,
                  "the last stretch is a fade, not a cut");
            // The TRACK must never outlive the FILL. Equal alpha is not equal legibility: a
            // near-black track holds contrast against every ground in this game long after a
            // saturated fill has washed out, so fading them together ends the bar as a black
            // rectangle — reported as "black means dead, right?".
            bool trackNeverLeads = true;
            for (float t = 0f; t <= CosmeticSystems.HealthBarSeconds; t += 0.05f)
                if (CosmeticSystems.HealthBarTrackAlpha(t) > CosmeticSystems.HealthBarAlpha(t) + 1e-5f)
                    trackNeverLeads = false;
            Check(trackNeverLeads, "the empty track is never more opaque than the fill on it");

            // RAGDOLL LEAN. A fraction of the tumble, capped — the two failure modes are the full
            // spin (a body that folds AND cartwheels) and zero (a statue flying backwards on
            // rails), and both have shipped.
            Check(CosmeticSystems.RagdollLeanDegrees(0f, true) == 0f,
                  "a body starts its fall upright");
            Check(Mathf.Abs(CosmeticSystems.RagdollLeanDegrees(220f, true)) > 5f,
                  "and leans measurably within the first second");
            for (float spun = 0f; spun < 4000f; spun += 37f)
                Check(Mathf.Abs(CosmeticSystems.RagdollLeanDegrees(spun, true))
                          <= CosmeticSystems.RagdollLeanMaxDegrees + 1e-3f,
                      spun == 0f ? "the lean is CAPPED, so it never winds up into a cartwheel" : null);
            Check(CosmeticSystems.RagdollLeanDegrees(500f, true)
                  * CosmeticSystems.RagdollLeanDegrees(500f, false) < 0f,
                  "the two sides tip opposite ways, each the way it is thrown");
            Check(CosmeticSystems.HealthBarTrackAlpha(CosmeticSystems.HealthBarSeconds - 0.2f)
                  < CosmeticSystems.HealthBarAlpha(CosmeticSystems.HealthBarSeconds - 0.2f),
                  "and it is visibly GONE first, so a bar dissolves to colour, not to black");


            // Ragdoll rest height: a body must never sink through the floor at any rotation.
            for (int deg = 0; deg < 360; deg += 7)
            {
                float restY = CosmeticSystems.RagdollRestY(deg);
                Check(restY >= -1e-6f, deg == 0 ? "rest height is never negative" : null);
                if (restY < -1e-6f) break;
            }
            Check(CosmeticSystems.RagdollRestY(90f) > CosmeticSystems.RagdollRestY(0f),
                  "a body propped upright rests HIGHER than one lying flat");
            Near(CosmeticSystems.RagdollRestY(0f), 0f, 1e-6f, "a flat body rests on the ground");

            // --- A CORPSE MUST NOT LEVITATE ONTO A ROOF IT NEVER REACHED.
            //
            // Reported by Rob 2026-08-07 as "dead units can have physically impossible
            // interactions with structures". BlockOnStructures rested a body on a structure's
            // ROOF whenever it was horizontally over the footprint and at or above the box's
            // BASE — and a ground structure's base is the ground, so ANY corpse whose x fell
            // inside a building's footprint was snapped to roof height. The condition's own
            // comment says "a body that CLEARED THE WALL should land on the roof"; `y >= baseY`
            // is not that test, `y >= topY` is.
            //
            // Driven through the real Step, because the bug lives in the interaction and not in
            // either piece alone.
            {
                var corpseDef = ScriptableObject.CreateInstance<UnitDefinitionSO>();
                corpseDef.id = "corpse"; corpseDef.maxHp = 32; corpseDef.damage = 8;
                var towerDef = ScriptableObject.CreateInstance<StructureDefinitionSO>();
                towerDef.id = "tower"; towerDef.maxHp = 100; towerDef.size = 4f;
                towerDef.hasHitWidth = true; towerDef.hitWidth = 3f; towerDef.isPlayerSide = false;

                // Box: baseY 0, topY 4, x from 3.5 to 6.5.
                var tower = new StructureEntity(900, towerDef, 5f, 2f, 0f, 100);
                CollisionSystem.StructureBox(tower, out _, out _, out float bY, out float tY);
                Check(Mathf.Approximately(bY, 0f) && Mathf.Approximately(tY, 4f),
                      $"test tower box is base {bY:F1} roof {tY:F1}");

                // A body IN THE AIR, thrown into the tower's face well BELOW its roof — the
                // real case. It is at y 1.5 against a roof of 4, arriving from the left. It must
                // be stopped by the face and fall; it must NOT be lifted up the wall.
                var thrown = new DyingUnitEntity(1, corpseDef, false, 3.4f, 1.5f, 0f, 4f, 0f, 0f);
                var st = new GameState
                {
                    Phase = GamePhase.Playing,
                    Structures = new List<StructureEntity> { tower },
                    DyingUnits = new List<DyingUnitEntity> { thrown },
                };
                // Stepped until it actually crosses into the box — one tick is not enough to
                // reach the face, and a check that never reaches the code it is testing is the
                // kind of green light this repo has been burned by before.
                float peakY = 0f;
                var walk = st;
                for (int i = 0; i < 40; i++)
                {
                    walk = BattleTick.Step(walk, 1f / 60f, null, new System.Random(1));
                    var b = walk.DyingUnits.FirstOrDefault(d => d.Id == 1);
                    if (b == null) break;
                    peakY = Mathf.Max(peakY, b.Y);
                }
                Check(peakY <= 1.5f + 1e-3f,
                      $"a corpse thrown into a wall BELOW the roof is never lifted up it " +
                      $"(peak y {peakY:F2} from 1.50, roof {tY:F1})");

                // And the behaviour that must SURVIVE the fix: a body genuinely above the roof
                // still lands on it rather than falling through.
                var overRoof = new DyingUnitEntity(2, corpseDef, false, 5f, 5f, 0f, 0f, -3f, 0f);
                var st2 = st with { DyingUnits = new List<DyingUnitEntity> { overRoof } };
                for (int i = 0; i < 120; i++) st2 = BattleTick.Step(st2, 1f / 60f, null, new System.Random(1));
                var landed = st2.DyingUnits.FirstOrDefault(d => d.Id == 2);
                Check(landed == null || landed.Y >= tY - 0.05f,
                      $"a body falling from ABOVE still rests on the roof (y {landed?.Y:F2} " +
                      $"vs roof {tY:F1})");
            }

            // Rolling: friction bleeds speed, and rotation is locked to travel (like a log).
            CosmeticSystems.StepRoll(2f, 1f / 60f, out float nvx, out float roll);
            Check(nvx < 2f && nvx > 0f, "roll friction bleeds speed without reversing it");
            Check(roll < 0f, "rightward travel rolls the body forward (negative degrees)");
            Check(CosmeticSystems.ShouldRoll(1f), "a fast body rolls");
            Check(!CosmeticSystems.ShouldRoll(0.1f), "a slow body stops rolling and flops");

            // Flop: near-critically damped toward the nearest LYING pose, so it settles.
            float rot = 60f, rotSpeed = 0f;
            for (int i = 0; i < 240; i++)
                CosmeticSystems.StepFlop(rot, rotSpeed, 1f / 60f, out rot, out rotSpeed);
            Near(rot, 0f, 1.0f, "a body at 60 degrees flops down to lying flat");
            Check(Mathf.Abs(rotSpeed) < 1f, "and comes to rest rather than oscillating");
            float rot2 = 130f, rs2 = 0f;
            for (int i = 0; i < 240; i++)
                CosmeticSystems.StepFlop(rot2, rs2, 1f / 60f, out rot2, out rs2);
            Near(rot2, 180f, 1.0f, "a body past vertical falls the OTHER way, to 180");

            Check(CosmeticSystems.RagdollExpired(5f), "a body is culled at the age limit");
            Check(!CosmeticSystems.RagdollExpired(4.9f), "and not before");

            // Debris sleep: ONLY rubble sleeps, and only when actually still.
            Check(CosmeticSystems.ShouldSleep(true, true, 0f, 0f, 0f), "still grounded rubble sleeps");
            Check(!CosmeticSystems.ShouldSleep(false, true, 0f, 0f, 0f),
                  "transient spatter never sleeps — it ages out on ttl instead");
            Check(!CosmeticSystems.ShouldSleep(true, false, 0f, 0f, 0f), "airborne rubble stays awake");
            Check(!CosmeticSystems.ShouldSleep(true, true, 1f, 0f, 0f), "moving rubble stays awake");
            Check(!CosmeticSystems.ShouldSleep(true, true, 0f, 0f, 50f), "spinning rubble stays awake");
            Check(CosmeticSystems.DebrisRubbleTtl > CosmeticSystems.DebrisTtlSeconds,
                  "rubble outlives transient debris by design");

            // Scorch merging: nearby marks combine instead of stacking identical decals.
            var marks = new List<ScorchMark> { new(1, 5f, 0f), new(2, 20f, 0f) };
            Check(CosmeticSystems.FindMergeTarget(marks, 5.05f, 0f) == 0,
                  "a hit next to an existing scorch merges into it");
            Check(CosmeticSystems.FindMergeTarget(marks, 12f, 0f) == -1,
                  "a distant hit makes its own mark");
            Check(CosmeticSystems.FindMergeTarget(new List<ScorchMark>(), 0f, 0f) == -1,
                  "an empty field has nothing to merge with");
            float grown = 1f;
            for (int i = 0; i < 100; i++) grown = CosmeticSystems.GrowScorch(grown);
            Check(grown <= CosmeticSystems.ScorchMaxScale + 1e-5f,
                  "repeated merges cannot grow one enormous blot");

            // Knockback: an AGE, not a displacement — collision is unaffected.
            Check(CosmeticSystems.StepKnockback(-1f, 1f / 60f) == -1f, "an inactive hop stays inactive");
            float k = CosmeticSystems.StepKnockback(0f, 1f / 60f);
            Check(k > 0f, "a triggered hop starts counting");
            Check(CosmeticSystems.StepKnockback(0.41f, 0.05f) == -1f,
                  "the hop returns to inactive when it expires, snapping back to formation");
        }

        // --- HelicopterSystem
        {
            var ud = ScriptableObject.CreateInstance<UnitDefinitionSO>();
            ud.id = "u"; ud.maxHp = 32;
            var pl = new List<UnitEntity> { new(1, ud, -8f, 0f, 0f, 32, true) };
            var en = new List<UnitEntity> { new(2, ud, 8f, 0f, 0f, 32, false) };
            var st = new List<StructureEntity>();
            var rng = new System.Random(3);

            HelicopterEntity Heli(HeliMode m, float x = 6f, int hp = HelicopterSystem.MaxHp,
                                  int bursts = HelicopterSystem.Bursts)
                => new(x, HelicopterSystem.Altitude, -HelicopterSystem.Speed, m, bursts)
                   { Hp = hp, MaxHp = HelicopterSystem.MaxHp, HoverX = 4f };

            // Shootability: the pre-battle flyby is scenery, a falling wreck is not a target.
            Check(!HelicopterSystem.IsShootable(HeliMode.Preview), "the preview flyby is not shootable");
            Check(HelicopterSystem.IsShootable(HeliMode.Hovering), "a hovering gunship is shootable");
            Check(!HelicopterSystem.IsShootable(HeliMode.Crashing), "a crashing heli is not shootable");

            // Entering settles into Hovering exactly at hoverX, not past it.
            var entering = Heli(HeliMode.Entering, x: 4.02f);   // 2.6 u/s * 1/60 = 0.043 travelled
            var r = HelicopterSystem.Step(entering, 1f / 60f, GamePhase.Playing, pl, en, st);
            Check(r.Heli.Mode == HeliMode.Hovering, "Entering becomes Hovering on arrival");
            Near(r.Heli.X, 4f, 1e-4f, "and stops exactly at the hover spot");
            Check(r.Heli.Vx == 0f, "with its approach speed cleared");

            // A battle that ends while it hovers: leave cosmetically, holding nothing open.
            var stranded = HelicopterSystem.Step(Heli(HeliMode.Hovering), 1f / 60f,
                                                 GamePhase.Victory, pl, en, st);
            Check(stranded.Heli.Mode == HeliMode.GunRun,
                  "a hovering heli leaves when the battle is already over");

            // WOUNDING changes behaviour — that is the counter-play, not just killing it.
            var hurt = HelicopterSystem.ApplyHit(Heli(HeliMode.Hovering), 10);
            Check(hurt.Mode == HeliMode.Retreating, "a wounded gunship breaks off and retreats");
            Check(hurt.Hp == HelicopterSystem.MaxHp - 10, "and carries the damage");
            Check(hurt.BurstsLeft == 0, "a retreating heli fires nothing further");
            Check(HelicopterSystem.IsWounded(hurt.Hp, hurt.MaxHp), "and reads as wounded");

            var killed = HelicopterSystem.ApplyHit(Heli(HeliMode.Hovering), 999);
            Check(killed.Mode == HeliMode.Crashing, "enough damage starts a crash");
            Check(killed.Vy > 0f, "with a brief upward lurch at the killing hit");
            Check(killed.BurstsLeft == 0,
                  "a falling heli must not hold the turn handover open");

            // The crash falls, spins, and produces its fireball at hull contact.
            var falling = killed with { Y = 0.23f, Vy = -3f };   // one tick drops ~0.051, past 0.22
            var crashed = HelicopterSystem.Step(falling, 1f / 60f, GamePhase.Playing, pl, en, st);
            Check(crashed.Heli == null && crashed.SpawnedCrashFireball,
                  "hitting the ground despawns the heli and spawns the fireball");
            var stillFalling = HelicopterSystem.Step(killed with { Y = 2f, Vy = -1f },
                                                     1f / 60f, GamePhase.Playing, pl, en, st);
            Check(stillFalling.Heli.Rotation > 0f, "a falling heli tumbles");
            Check(stillFalling.Heli.Vy < -1f, "and accelerates downward");

            // A crash still resolves after the battle ended, rather than despawning silently.
            var postBattle = HelicopterSystem.Step(killed with { Y = 0.23f, Vy = -3f },
                                                   1f / 60f, GamePhase.Victory, pl, en, st);
            Check(postBattle.SpawnedCrashFireball,
                  "a heli falling as the battle ends still explodes");

            // Hit detection is SWEPT and side-correct.
            var hover = Heli(HeliMode.Hovering, x: 6f);
            var through = new ProjectileEntity(1, 6f, 1f, 0f, 0f, -20f, 0f, 8, true)
                { PrevX = 6f, PrevY = 6f };
            Check(HelicopterSystem.IsHitBy(hover, through),
                  "a fast round through the hover disc registers (swept, not point-sampled)");
            var wide = new ProjectileEntity(2, 20f, 1f, 0f, 0f, -20f, 0f, 8, true)
                { PrevX = 20f, PrevY = 6f };
            Check(!HelicopterSystem.IsHitBy(hover, wide), "a round nowhere near it misses");
            var enemyRound = new ProjectileEntity(3, 6f, 1f, 0f, 0f, -20f, 0f, 8, false)
                { PrevX = 6f, PrevY = 6f };
            Check(!HelicopterSystem.IsHitBy(hover, enemyRound),
                  "the enemy never shoots down its own gunship");
            var ownGunner = new ProjectileEntity(4, 6f, 1f, 0f, 0f, -20f, 0f, 8, true)
                { PrevX = 6f, PrevY = 6f, IsHeliShot = true };
            Check(!HelicopterSystem.IsHitBy(hover, ownGunner),
                  "and its own door-gunner rounds cannot hit it");
            Check(!HelicopterSystem.IsHitBy(Heli(HeliMode.Preview), through),
                  "the cosmetic flyby cannot be shot down");

            // Door gunner: fires only on a gun run, and its rounds are excluded from the camera.
            var gunning = Heli(HeliMode.GunRun) with { FireCooldown = 0f };
            var shot = HelicopterSystem.TryFire(gunning, pl, 900, rng);
            Check(shot != null, "a gun-running heli with bursts left fires");
            Check(shot.IsHeliShot,
                  "gunner rounds are flagged so the volley camera ignores them");
            Check(!shot.OwnerIsPlayer, "gunner rounds belong to the enemy side");
            Check(HelicopterSystem.TryFire(Heli(HeliMode.Hovering) with { FireCooldown = 0f },
                                           pl, 901, rng) == null,
                  "a hovering heli does not fire");
            Check(HelicopterSystem.TryFire(gunning with { BurstsLeft = 0 }, pl, 902, rng) == null,
                  "an empty gun fires nothing");
            Check(HelicopterSystem.TryFire(gunning with { FireCooldown = 0.4f }, pl, 903, rng) == null,
                  "the fire interval is respected");
            var after = HelicopterSystem.ConsumeBurst(gunning);
            Check(after.BurstsLeft == HelicopterSystem.Bursts - 1, "firing consumes a burst");
            Near(after.FireCooldown, HelicopterSystem.FireInterval, 1e-5f, "and resets the cooldown");
        }

        // --- EventSystems
        {
            // Reinforcement waves: telegraph one turn EARLY, arrive on the turn, silent after.
            Check(EventSystems.ReinforcementWaveBeat(5, 5) == EventSystems.WaveTriggerBeat.Arrive,
                  "a wave arrives on its turn");
            Check(EventSystems.ReinforcementWaveBeat(5, 4) == EventSystems.WaveTriggerBeat.Telegraph,
                  "and telegraphs one turn early so the player can react");
            Check(EventSystems.ReinforcementWaveBeat(5, 3) == EventSystems.WaveTriggerBeat.None,
                  "with nothing shown two turns out");
            Check(EventSystems.ReinforcementWaveBeat(5, 6) == EventSystems.WaveTriggerBeat.None,
                  "and nothing after it has landed");

            // Wind: sign follows BASE, so a level's wind never reverses mid-battle.
            float baseWind = 2f;
            float w = baseWind;
            for (int i = 0; i < 40; i++) w = EventSystems.NextWindAccelZ(w, baseWind, false);
            Check(w > 0f, "weakening wind never flips direction");
            Near(w, baseWind * EventSystems.WindShiftMinFrac, 1e-4f, "it clamps at the band floor");
            for (int i = 0; i < 80; i++) w = EventSystems.NextWindAccelZ(w, baseWind, true);
            Near(w, baseWind * EventSystems.WindShiftMaxFrac, 1e-4f, "and at the band ceiling");

            float negBase = -2f, nw = negBase;
            for (int i = 0; i < 40; i++) nw = EventSystems.NextWindAccelZ(nw, negBase, true);
            Check(nw < 0f, "a leftward wind stays leftward however hard it gusts");
            Near(nw, negBase * EventSystems.WindShiftMaxFrac * -1f * -1f, 1e-4f,
                 "and clamps to its own band");

            Check(EventSystems.NextWindAccelZ(0f, 0f, true) == 0f,
                  "a level with no wind never gusts");

            // The banner is suppressed when the gust changed nothing.
            float atCeiling = baseWind * EventSystems.WindShiftMaxFrac;
            float unchanged = EventSystems.NextWindAccelZ(atCeiling, baseWind, true);
            Check(EventSystems.WindShiftAnnouncement(atCeiling, unchanged, true) == null,
                  "no banner when the wind was already clamped at the edge");
            Check(EventSystems.WindShiftAnnouncement(1f, 1.35f, true) != null,
                  "a real gust does announce itself");

            // Announcement timers clear their text.
            float timer = 0.1f;
            Check(!EventSystems.TickAnnouncement(ref timer, 0.2f), "an expired banner is hidden");
            Check(timer == 0f, "and its timer floors at zero rather than going negative");
            float live = 2f;
            Check(EventSystems.TickAnnouncement(ref live, 0.2f), "a live banner stays shown");

            // Boss phases.
            var lvl = ScriptableObject.CreateInstance<LevelDefinitionSO>();
            var ud2 = ScriptableObject.CreateInstance<UnitDefinitionSO>();
            ud2.id = "u"; ud2.maxHp = 32;
            lvl.enemyGroups = new List<EnemyGroup>
            {
                new() { definition = ud2, count = 2, anchorX = 6f, standingOnStructureId = "keep" },
            };
            var runtimeIds = new Dictionary<string, int> { { "keep", 100 }, { "gate", 101 } };

            // Destroyed outright counts.
            Check(EventSystems.IsTriggerDefeated("keep", runtimeIds, new HashSet<int> { 100 },
                                                 lvl, new List<UnitEntity>()),
                  "a destroyed trigger structure counts as defeated");

            // Still standing but its garrison is cleared ALSO counts — without this a player who
            // killed the defenders and left the masonry never fires the encounter.
            Check(EventSystems.IsTriggerDefeated("keep", runtimeIds, new HashSet<int>(),
                                                 lvl, new List<UnitEntity>()),
                  "a garrisoned structure with its defenders cleared also counts");
            var stillHeld = new List<UnitEntity>
            {
                new(1, ud2, 6f, 1f, 0f, 32, false) { StandingOnStructureId = 100 },
            };
            Check(!EventSystems.IsTriggerDefeated("keep", runtimeIds, new HashSet<int>(),
                                                  lvl, stillHeld),
                  "but not while any of its garrison still stands");

            // An UNgarrisoned structure only counts once actually destroyed.
            Check(!EventSystems.IsTriggerDefeated("gate", runtimeIds, new HashSet<int>(),
                                                  lvl, new List<UnitEntity>()),
                  "an ungarrisoned structure must actually be destroyed");
            Check(!EventSystems.IsTriggerDefeated("missing", runtimeIds, new HashSet<int>(),
                                                  lvl, new List<UnitEntity>()),
                  "an unknown structure id is never defeated");

            // Trigger gating.
            var trig = new BossPhaseTrigger { triggerStructureIds = new List<string> { "keep", "gate" } };
            Check(!EventSystems.ShouldTriggerBossPhase(0, trig, new HashSet<int>(), id => id == "keep"),
                  "a phase waits until EVERY trigger structure is defeated");
            Check(EventSystems.ShouldTriggerBossPhase(0, trig, new HashSet<int>(), id => true),
                  "and fires once they all are");
            Check(!EventSystems.ShouldTriggerBossPhase(0, trig, new HashSet<int> { 0 }, id => true),
                  "an already-triggered phase never fires twice");
            var empty = new BossPhaseTrigger { triggerStructureIds = new List<string>() };
            Check(!EventSystems.ShouldTriggerBossPhase(0, empty, new HashSet<int>(), id => true),
                  "an EMPTY trigger set is not vacuously true (it would fire on tick one)");
        }

        // --- Backdrop: the properties that separate a range from a row of pyramids. None of
        // these can be seen in a screenshot of the ONE biome a level happens to load, and the
        // three that are checked here are all mistakes this file's history actually shipped.
        {
            foreach (SilhouetteStyle style in System.Enum.GetValues(typeof(SilhouetteStyle)))
            {
                var plan = ArmedConflict.Render.Backdrop.Plan(style, ArmedConflict.Render.Backdrop.DesignAspect);
                Check(plan.Count > 0, null);
                float visible = ArmedConflict.Render.Backdrop.VisibleWidthAt(
                    ArmedConflict.Render.Backdrop.NearZ, ArmedConflict.Render.Backdrop.DesignAspect);
                foreach (var layer in plan)
                {
                    Check(layer.Width > ArmedConflict.Render.Backdrop.VisibleWidthAt(
                              layer.Z, ArmedConflict.Render.Backdrop.DesignAspect), null);
                    Check(layer.Profile.Length >= 8, null);
                    foreach (var p in layer.Profile) Check(p >= -0.001f && p <= 1.001f, null);
                    // Every layer must be a closed silhouette: the base is BELOW the ground
                    // plane, so a valley meets the ground instead of stopping on a ledge.
                    Check(layer.BaseY < 0f, null);
                    var mesh = ArmedConflict.Render.SilhouetteMesh.Build(layer);
                    Check(mesh.vertexCount == layer.Profile.Length * 2, null);
                }
                Check(true, $"{style}: {plan.Count} layers, all wider than frame, profiles in range");
            }

            // Depth ordering. A layer FURTHER away that is also SMALLER on screen contradicts
            // its own haze colour, which is what made the two mountain rows read as one.
            var mtn = ArmedConflict.Render.Backdrop.Plan(SilhouetteStyle.Mountains,
                                                         ArmedConflict.Render.Backdrop.DesignAspect);
            float farAng = ArmedConflict.Render.Backdrop.AngularHeight(mtn[0].Height, mtn[0].Z);
            float nearAng = ArmedConflict.Render.Backdrop.AngularHeight(mtn[1].Height, mtn[1].Z);
            Check(farAng > nearAng * 1.3f,
                  $"mountains: the far range OUT-REACHES the foothills on screen ({farAng:F3} vs {nearAng:F3})");

            // Snow is a cap on the crests that earn it, not a blanket over the range.
            var snowLayer = mtn[0];
            int snowy = 0;
            foreach (var p in snowLayer.Profile) if (p > snowLayer.SnowLine) snowy++;
            Check(snowy > 0 && snowy < snowLayer.Profile.Length / 4,
                  $"mountains: snow covers only the top crests ({snowy}/{snowLayer.Profile.Length} columns)");
            Check(ArmedConflict.Render.SilhouetteMesh.BuildSnow(mtn[1]) == null,
                  "no snow on the foothills (BuildSnow returns null when no line is set)");
        }

        // --- The enemy's RAISED RIFLE must describe the round it actually fired.
        //
        // EnemyAI picks a fresh random arc per unit inside AimAt, so the only safe way to pose
        // the arms is to read the elevation back off the velocity that was really used. Drawing
        // a second angle for display would pose every enemy at an arc no round takes, and it
        // would look entirely plausible on screen — which is why this is asserted rather than
        // eyeballed.
        {
            var lv = AssetDatabase.FindAssets("t:LevelDefinitionSO")
                .Select(g => AssetDatabase.LoadAssetAtPath<LevelDefinitionSO>(
                    AssetDatabase.GUIDToAssetPath(g)))
                .Where(l => l != null).OrderBy(l => l.levelNumber).First();

            var st = LevelBuilder.BuildInitialState(lv, 1, 24, new System.Random(11));
            st = st with { Phase = GamePhase.Playing, TurnPhase = TurnPhase.Aiming };
            int before = st.Projectiles.Count;
            var fired = BattleTick.FireEnemyVolley(st, new System.Random(11));

            Check(fired.EnemyAimDegrees.Count == fired.EnemyUnits.Count,
                  $"every enemy records a launch elevation ({fired.EnemyAimDegrees.Count}" +
                  $" of {fired.EnemyUnits.Count})");

            var shots = fired.Projectiles.Skip(before).ToList();
            int matched = 0;
            foreach (var u in fired.EnemyUnits)
            {
                if (!fired.EnemyAimDegrees.TryGetValue(u.Id, out float posed)) continue;
                // The round this unit fired left from its own position.
                var shot = shots.FirstOrDefault(r => Mathf.Abs(r.X - u.X) < 1e-3f
                                                  && Mathf.Abs(r.Z - u.Z) < 1e-3f);
                if (shot == null) continue;
                float real = Mathf.Atan2(shot.Vy, Mathf.Abs(shot.Vx)) * Mathf.Rad2Deg;
                if (Mathf.Abs(real - posed) < 0.01f) matched++;
            }
            Check(matched == fired.EnemyUnits.Count,
                  $"each posed elevation equals the round that unit fired ({matched}" +
                  $" of {fired.EnemyUnits.Count})");

            // And it must not outlive the volley: units are POOLED, so a pose left set would be
            // inherited by whoever recycles the slot.
            Check(st.EnemyAimDegrees.Count == 0, "no enemy pose is held outside a volley");
        }

        // --- EVERY level must build and must have geometry for everything it places.
        //
        // This is the check that makes level navigation safe to ship. While only L1 was
        // reachable, a level whose structure GLB was never imported, or that threw while
        // building, was indistinguishable from a level nobody had visited — and the models
        // folder genuinely held two of the set. A device sweep finds this too, at about a
        // minute a level; this finds it in the same second as a typo.
        {
            var levels = AssetDatabase.FindAssets("t:LevelDefinitionSO")
                .Select(g => AssetDatabase.LoadAssetAtPath<LevelDefinitionSO>(
                    AssetDatabase.GUIDToAssetPath(g)))
                .Where(l => l != null)
                .OrderBy(l => l.levelNumber)
                .ToList();

            var haveModel = new HashSet<string>(
                AssetDatabase.FindAssets("t:GameObject", new[] { "Assets/Models" })
                    .Select(AssetDatabase.GUIDToAssetPath)
                    .Where(p => p.EndsWith(".glb") && !p.Contains("/Kenney/"))
                    .Select(LevelScenery.ModelKey));

            Check(levels.Count == 29, $"all 29 levels present ({levels.Count})");

            Check(levels.Count(l => !l.isTestLevel) == 12,
                  "the campaign is 12 levels — PRODUCT_DIRECTION Tier 0.1's funnel, "
                  + $"one beat each ({levels.Count(l => !l.isTestLevel)})");

            // CONTIGUITY IS A CAMPAIGN RULE ONLY, as of the 2026-08-06 campaign/rig split.
            //
            // It used to be asserted across all 24 — which is what forced every test rig to be
            // renumbered whenever the campaign changed size, a standing chore and a standing bug.
            // SpikeSceneBattle now orders campaign-then-rigs, so the campaign block leads and is
            // indexed by position; a rig's number is free and indexes nothing.
            //
            // The campaign half still matters, and matters MORE than before: the orphan sweep is
            // gone (see LegacyKotlinImport), so a stale level asset can no longer be deleted for
            // us, and this check is the only thing that catches one rejoining the campaign.
            var campaign = levels.Where(l => !l.isTestLevel).OrderBy(l => l.levelNumber).ToList();
            var misnumbered = campaign.Where((l, i) => l.levelNumber != i + 1).ToList();
            Check(misnumbered.Count == 0,
                  $"campaign levelNumbers are contiguous from 1 ({campaign.Count} levels)" +
                  (misnumbered.Count == 0 ? "" : $" (first bad: {misnumbered[0].displayName} " +
                                                 $"is L{misnumbered[0].levelNumber})"));

            // Ids are the PlayerPrefs keys the star results are stored under, so a duplicate
            // silently makes two levels share a best-star record. The rigs are excluded from
            // TotalStars but still record stars, so this covers all 24.
            // --- LOADOUT ---------------------------------------------------------------------
            {
                var roster = AssetDatabase.LoadAssetAtPath<RosterDefinitionSO>(
                    "Assets/GameData/Roster.asset");
                Check(roster != null && roster.slots.Count > 0, "the roster asset exists");

                if (roster != null)
                {
                    ProgressStore.ResetAll();
                    System.Func<string, bool> unlocked = ProgressStore.IsUnitUnlocked;

                    Check(roster.slots.Any(s2 => s2.coinPrice == 0),
                          "at least one roster unit is free — a locked-out player has no squad");
                    Check(roster.slots.All(s2 => s2.unit != null && s2.pointCost >= 1),
                          "every roster slot has a unit and costs at least a point");
                    Check(roster.slots.OrderBy(s2 => s2.pointCost).First().coinPrice == 0,
                          "the CHEAPEST unit is the free one — Loadout.Default picks by point " +
                          "cost, so a dear free unit would make the default squad unaffordable");

                    foreach (var lv in levels.Where(l2 => !l2.isTestLevel))
                    {
                        var def = Loadout.Default(lv, roster, unlocked);
                        Check(Loadout.IsLegal(def, lv, roster, unlocked),
                              $"{lv.displayName}: the DEFAULT loadout is legal — pillar 8, a " +
                              "player who taps straight through must lose nothing");
                        Check(Loadout.UnitsUsed(def) == Loadout.Slots(lv),
                              $"{lv.displayName}: the default fills every slot " +
                              $"({Loadout.UnitsUsed(def)}/{Loadout.Slots(lv)}) — this is the " +
                              "squad the level was balanced against");

                        // THE FRAMING CONTRACT. Composition rule 1 measures the player line and
                        // the aiming camera is framed on it, so no legal loadout may make the
                        // line wider than the level was authored with. Checked against the real
                        // built state, the same number LevelComposition reads.
                        var asAuthored = LevelBuilder.BuildInitialState(lv, 1, 12, new System.Random(9));
                        var chosen = Loadout.ToPlayerGroups(lv, def);
                        var asPicked = LevelBuilder.BuildInitialState(
                            lv, 1, 12, new System.Random(9), playerGroupsOverride: chosen);
                        Check(asPicked.PlayerCamHalfWidth <= asAuthored.PlayerCamHalfWidth + 0.01f,
                              $"{lv.displayName}: the default squad is no wider than the authored " +
                              $"line ({asPicked.PlayerCamHalfWidth:F2} vs " +
                              $"{asAuthored.PlayerCamHalfWidth:F2})");

                        // A squad of the DEAREST affordable units must also fit the frame — that
                        // is the widest a legal loadout can get once everything is unlocked.
                        var dearest = roster.slots.OrderByDescending(s2 => s2.pointCost).First();
                        int many = Mathf.Min(Loadout.Slots(lv),
                                             Loadout.Budget(lv) / dearest.pointCost);
                        if (many > 0)
                        {
                            var heavy = new List<Pick> { new(dearest.unit, many) };
                            var st2 = LevelBuilder.BuildInitialState(
                                lv, 1, 12, new System.Random(9),
                                playerGroupsOverride: Loadout.ToPlayerGroups(lv, heavy));
                            Check(st2.PlayerCamHalfWidth <= asAuthored.PlayerCamHalfWidth + 0.01f,
                                  $"{lv.displayName}: an all-{dearest.unit.name} squad also fits " +
                                  "the authored frame");
                        }

                        // The budget must never be so tight that the default cannot fill the
                        // level, nor so loose that slots stop being the binding constraint.
                        Check(Loadout.Budget(lv) >= Loadout.Slots(lv),
                              $"{lv.displayName}: deployBudget ({Loadout.Budget(lv)}) covers at " +
                              $"least one cheap body per slot ({Loadout.Slots(lv)})");
                    }

                    // Garrisons are level geometry and must survive any loadout.
                    var withTank = levels.First(l2 => !l2.isTestLevel
                        && l2.playerGroups.Any(g => !string.IsNullOrEmpty(g.standingOnStructureId)));
                    var kept = Loadout.ToPlayerGroups(withTank,
                        Loadout.Default(withTank, roster, unlocked));
                    Check(kept.Count(g => !string.IsNullOrEmpty(g.standingOnStructureId))
                          == withTank.playerGroups.Count(g => !string.IsNullOrEmpty(g.standingOnStructureId)),
                          "a loadout never disturbs the garrisoned groups — the tank crew is " +
                          "level geometry, not a squad pick");

                    // Legality, at the edges.
                    var one = levels.First(l2 => !l2.isTestLevel);
                    var rifle = roster.slots.OrderBy(s2 => s2.pointCost).First();
                    Check(!Loadout.IsLegal(new List<Pick>(), one, roster, unlocked),
                          "an EMPTY loadout is illegal — that is not a decision, it is no battle");
                    Check(!Loadout.IsLegal(
                              new List<Pick> { new(rifle.unit, Loadout.Slots(one) + 1) },
                              one, roster, unlocked),
                          "overfilling the slots is illegal even when the points would allow it");
                    Check(Loadout.IsLegal(
                              new List<Pick> { new(rifle.unit, 1) }, one, roster, unlocked),
                          "UNDER-filling is legal — fewer, better troops is a real choice");

                    var locked = roster.slots.FirstOrDefault(s2 => s2.coinPrice > 0);
                    if (locked != null)
                        Check(!Loadout.IsLegal(new List<Pick> { new(locked.unit, 1) },
                                               one, roster, unlocked),
                              "a locked unit cannot be fielded before it is bought");

                    ProgressStore.ResetAll();
                }
            }

            var dupIds = levels.GroupBy(l => l.id).Where(g => g.Count() > 1).ToList();
            Check(dupIds.Count == 0,
                  "every level id is unique — ids key the saved star results" +
                  (dupIds.Count == 0 ? "" : $" (duplicated: {dupIds[0].Key})"));

            // MID-BATTLE EVENTS reference level data by NAME, and a name that matches nothing
            // fails silently — the phase simply never fires and the level plays as an ordinary
            // fight. Nothing about that looks broken from the outside, which is why it is
            // asserted rather than trusted.
            foreach (var l in levels)
            {
                var structureIds = new HashSet<string>(
                    l.structures.Where(s2 => !string.IsNullOrEmpty(s2.id)).Select(s2 => s2.id));

                foreach (var b in l.bossPhases)
                {
                    Check(b.triggerStructureIds != null && b.triggerStructureIds.Count > 0,
                          $"{l.displayName}: a boss phase has trigger structures " +
                          "(an empty set never fires — ShouldTriggerBossPhase refuses it)");
                    foreach (var tid in b.triggerStructureIds ?? new List<string>())
                        Check(structureIds.Contains(tid),
                              $"{l.displayName}: boss trigger '{tid}' names a structure the level has");
                    foreach (var g in b.spawnGroups)
                        Check(g.definition != null && g.count > 0,
                              $"{l.displayName}: every boss spawn group has a unit and a count");
                }

                foreach (var w in l.reinforcementWaves)
                {
                    // Turn 1 is the player's first move, so the WHOLE lead has to fit before
                    // arrival: a 2-turn warning on a wave landing on turn 2 gets one turn, and the
                    // level would be shipping half a telegraph while passing an `>= 2` check.
                    Check(w.telegraphLeadTurns >= 1,
                          $"{l.displayName}: a reinforcement wave warns for at least one turn " +
                          $"(lead is {w.telegraphLeadTurns})");
                    Check(w.arrivesOnTurn - w.telegraphLeadTurns >= 1,
                          $"{l.displayName}: a wave arriving on turn {w.arrivesOnTurn} with a " +
                          $"{w.telegraphLeadTurns}-turn lead starts warning before turn 1");
                    Check(!string.IsNullOrEmpty(w.telegraphLabel),
                          $"{l.displayName}: a reinforcement wave carries a telegraph label " +
                          "(pillar 7: telegraph, don't blindside)");
                    // The count belongs to EventSystems.TelegraphLine, which recomputes it every
                    // turn. One authored into the label is a number that stops counting down and
                    // can silently disagree with arrivesOnTurn.
                    Check(w.telegraphLabel == null ||
                          !System.Text.RegularExpressions.Regex.IsMatch(w.telegraphLabel,
                                                                       @"\d+\s*turns?"),
                          $"{l.displayName}: the telegraph label '{w.telegraphLabel}' hardcodes a " +
                          "turn count — the countdown is composed live, not authored");
                    foreach (var g in w.spawnGroups)
                        Check(g.definition != null && g.count > 0,
                              $"{l.displayName}: every wave spawn group has a unit and a count");
                }
            }

            // EVERY AUTHORED STRING THAT REACHES TextMeshPro MUST HAVE A GLYPH FOR EVERY
            // CHARACTER IN IT.
            //
            // The failure is silent: TMP substitutes a blank box and logs nothing, so it shows up
            // on a device, mid-battle, inside a banner that fires on one level on one turn.
            //
            // ASKED OF THE FONT, NOT ASSUMED. This check was first written as "ASCII only",
            // because that is what CLAUDE.md said, and it immediately flagged 23 strings —
            // every campaign levelGoal and every test-rig name, all of which use an em dash. A
            // DEVICE SCREENSHOT then showed the em dash rendering perfectly in the loadout panel:
            // LiberationSans SDF covers Latin-1 and General Punctuation, and it is only the
            // symbols the star and coin icons were replaced over (U+2605, U+25C6) that it lacks.
            // An ASCII rule would have been 23 false positives dressed as bugs. Ask
            // TMP_Settings.defaultFontAsset what it actually has.
            {
                var font = TMP_Settings.defaultFontAsset;
                Check(font != null, "the default TMP font asset loads, so glyph coverage is " +
                                    "checkable at all (a null one would pass every check below)");
                // Proves the check can FAIL, in the same run that uses it: a character the font is
                // known not to have must be reported missing. Without this the whole block is
                // vacuous the day HasCharacter starts returning true for everything.
                Check(font != null && !font.HasCharacter('\u2605'),
                      "and reports the star glyph MISSING, which is why the HUD draws it as a sprite");

                void Glyphs(string s, string what)
                {
                    if (string.IsNullOrEmpty(s) || font == null) return;
                    int bad = -1;
                    for (int i = 0; i < s.Length && bad < 0; i++)
                        if (!char.IsWhiteSpace(s[i]) && !font.HasCharacter(s[i])) bad = i;
                    // The offending codepoint is REPORTED, not just flagged. A blank box in a
                    // banner tells you nothing about which character it was, and the whole reason
                    // this class of bug survives is that the failure carries no information.
                    Check(bad < 0,
                          bad < 0 ? $"{what} renders in the default font"
                                  : $"{what} has NO GLYPH for U+{(int)s[bad]:X4} at {bad}: \'{s}\'");
                }

                foreach (var l in levels)
                {
                    Glyphs(l.displayName, $"L{l.levelNumber} displayName");
                    Glyphs(l.levelGoal, $"L{l.levelNumber} levelGoal");
                    foreach (var b in l.bossPhases)
                        Glyphs(b.announcement, $"L{l.levelNumber} boss announcement");
                    foreach (var w in l.reinforcementWaves)
                    {
                        Glyphs(w.announcement, $"L{l.levelNumber} wave announcement");
                        Glyphs(w.telegraphLabel, $"L{l.levelNumber} telegraph label");
                    }
                }

                foreach (var u in AssetDatabase.FindAssets("t:UnitDefinitionSO")
                             .Select(AssetDatabase.GUIDToAssetPath)
                             .Select(AssetDatabase.LoadAssetAtPath<UnitDefinitionSO>)
                             .Where(u => u != null))
                    Glyphs(u.displayName, $"unit {u.name} displayName");

                // And the strings the CODE composes. Wind's came over from the Kotlin carrying a
                // wind emoji and a directional arrow; wind is not wired yet, so those three boxes
                // were waiting for whoever wires it.
                Glyphs(EventSystems.WindShiftAnnouncement(1f, 1.35f, true), "wind rising banner");
                Glyphs(EventSystems.WindShiftAnnouncement(1.35f, 1f, false), "wind falling banner");
                Glyphs(EventSystems.TelegraphLine("Armor column inbound", 2), "a composed telegraph");
            }

            // END TO END: a boss phase must actually put units on the field. The decision
            // function has been tested since the port; what was never true is that anything
            // CALLED it — bossPhases were read only to size the pools, so the Sovereign would
            // have stayed off the board no matter how correct the arithmetic was.
            {
                var bossLevel = levels.First(l => !l.isTestLevel && l.bossPhases.Count > 0);
                var st0 = LevelBuilder.BuildInitialState(bossLevel, 1, 12, new System.Random(3));
                int before = st0.EnemyUnits.Count;

                // Remove the trigger structures and their garrison, which is what defeating them
                // looks like to the tick, then run one step.
                var triggerIds = new HashSet<string>(bossLevel.bossPhases[0].triggerStructureIds);
                var doomed = new HashSet<int>();
                for (int i = 0; i < bossLevel.structures.Count; i++)
                    if (triggerIds.Contains(bossLevel.structures[i].id))
                        doomed.Add(LevelBuilder.StructureIdBase + i);

                var razed = st0 with
                {
                    // BuildInitialState does not set Phase — BattleRunner.LoadLevel does, right
                    // after. Without it Step takes the cosmetic-only path and no event fires,
                    // which is exactly how this check failed the first time it was written.
                    Phase = GamePhase.Playing,
                    TurnPhase = TurnPhase.Aiming,
                    Structures = st0.Structures.Where(s2 => !doomed.Contains(s2.Id)).ToList(),
                    EnemyUnits = st0.EnemyUnits
                        .Where(u => u.StandingOnStructureId == null
                                 || !doomed.Contains(u.StandingOnStructureId.Value)).ToList(),
                };
                var after = BattleTick.Step(razed, 0.016f, bossLevel, new System.Random(3));

                Check(after.TriggeredBossPhases.Count == 1,
                      $"{bossLevel.displayName}: the boss phase fires once its structure is gone");
                Check(after.EnemyUnits.Count > razed.EnemyUnits.Count,
                      "and puts its spawn groups on the field");
                Check(!string.IsNullOrEmpty(after.BossAnnouncement),
                      "and raises its announcement");

                // TELEGRAPHS. A wave arriving on turn N must warn on turn N-1 and the warning must
                // still be standing for the whole of that turn — pillar 7. Driven off the real
                // level so a wave authored with no telegraph text cannot pass.
                var waveLevel = levels.First(l => !l.isTestLevel && l.reinforcementWaves.Count > 0);
                var wave0 = waveLevel.reinforcementWaves[0];
                var ws = LevelBuilder.BuildInitialState(waveLevel, 1, 12, new System.Random(4))
                         with { Phase = GamePhase.Playing, TurnPhase = TurnPhase.Aiming };

                int lead = wave0.telegraphLeadTurns;
                var early = BattleTick.Step(ws with { TurnNumber = wave0.arrivesOnTurn - lead - 1 },
                                            0.016f, waveLevel, new System.Random(4));
                Check(string.IsNullOrEmpty(early.TelegraphText),
                      "no telegraph before the wave's lead begins — a warning that early is noise");

                // ASSERT THE STRING THE PLAYER READS, not that some telegraph is set. The count
                // is the whole point of a multi-turn lead, and it is the part that goes stale.
                var opened = BattleTick.Step(ws with { TurnNumber = wave0.arrivesOnTurn - lead },
                                             0.016f, waveLevel, new System.Random(4));
                Check(opened.TelegraphText ==
                      EventSystems.TelegraphLine(wave0.telegraphLabel, lead),
                      $"the telegraph opens at the full lead reading '{opened.TelegraphText}'");
                Check(opened.TelegraphText != null &&
                      opened.TelegraphText.Contains(lead == 1 ? "1 turn" : $"{lead} turns"),
                      "and carries the live turn count");

                var warned = BattleTick.Step(ws with { TurnNumber = wave0.arrivesOnTurn - 1 },
                                             0.016f, waveLevel, new System.Random(4));
                Check(warned.TelegraphText ==
                      EventSystems.TelegraphLine(wave0.telegraphLabel, 1),
                      "the telegraph stands on the turn before the wave arrives");
                Check(warned.TelegraphText != null && warned.TelegraphText.EndsWith("1 turn"),
                      "and has COUNTED DOWN to one turn — a number that never moves says the " +
                      "clock has stopped");
                if (lead > 1)
                    Check(warned.TelegraphText != opened.TelegraphText,
                          "so the line the player reads is not the same on both turns");
                Check(warned.EnemyUnits.Count == ws.EnemyUnits.Count,
                      "and the telegraph does not spend the wave — it must still arrive");

                var landed = BattleTick.Step(warned with { TurnNumber = wave0.arrivesOnTurn },
                                             0.016f, waveLevel, new System.Random(4));
                Check(landed.EnemyUnits.Count > ws.EnemyUnits.Count, "the wave then arrives");
                Check(string.IsNullOrEmpty(landed.TelegraphText),
                      "and the warning clears itself once it has");

                var again = BattleTick.Step(after, 0.016f, bossLevel, new System.Random(3));
                Check(again.EnemyUnits.Count == after.EnemyUnits.Count,
                      "and does NOT fire a second time on the next tick");

                var allIds2 = after.EnemyUnits.Select(u => u.Id)
                    .Concat(after.PlayerUnits.Select(u => u.Id)).ToList();
                Check(allIds2.Distinct().Count() == allIds2.Count,
                      "spawned unit ids never collide with the units already fighting");
            }

            var missing = new SortedSet<string>();
            var unitClasses = new SortedSet<string>();
            int built = 0;
            foreach (var l in levels)
            {
                var st = LevelBuilder.BuildInitialState(l, 1, levels.Count, new System.Random(7));
                built++;
                Check(st.PlayerUnits.Count > 0, null);
                foreach (var u in st.PlayerUnits.Concat(st.EnemyUnits))
                    unitClasses.Add(LevelScenery.ModelKey(u.Definition.modelAsset));
                foreach (var s in st.Structures)
                    if (!haveModel.Contains(LevelScenery.ModelKey(s.Definition.modelAsset)))
                        missing.Add(s.Definition.modelAsset);
                foreach (var p in l.props)
                    if (!haveModel.Contains(LevelScenery.ModelKey(p.modelAsset)))
                        missing.Add(p.modelAsset);
            }
            Check(built == levels.Count, $"every level builds an initial state ({built})");
            Check(missing.Count == 0,
                  missing.Count == 0
                      ? "every structure and prop the campaign places has an imported model"
                      : $"MODELS NOT IMPORTED: {string.Join(", ", missing)}");

            // Every unit CLASS the levels actually field needs its own rigged silhouette and a
            // prefab per side, or BattleRunner has no pool to hand it a slot from and the soldier
            // renders as nothing at all. This is the guard for that: a class added to the Kotlin
            // roster without a matching build_units_rigged.py builder fails HERE, in a second,
            // rather than as a gap in a firing line on a device.
            var noArt = unitClasses.Where(k => !RiggedUnits.Models.Contains(k)).ToList();
            Check(noArt.Count == 0,
                  noArt.Count == 0
                      ? $"every unit class the campaign fields has a rigged model ({unitClasses.Count})"
                      : $"UNIT CLASSES WITH NO RIGGED MODEL: {string.Join(", ", noArt)}");

            var noPrefab = new SortedSet<string>();
            foreach (var k in unitClasses)
                foreach (var side in new[] { "Player", "Enemy" })
                    if (AssetDatabase.LoadAssetAtPath<GameObject>(
                            $"Assets/Prefabs/{side}Unit_{k}.prefab") == null)
                        noPrefab.Add($"{side}Unit_{k}");
            Check(noPrefab.Count == 0,
                  noPrefab.Count == 0
                      ? "every fielded class has a per-side prefab"
                      : $"PREFABS MISSING (rebuild the scene): {string.Join(", ", noPrefab)}");

            // --- WHY UnitAnim.Stand() RESTORES THE ROOT BY HAND.
            //
            // `die` drives the ROOT; nothing below it does. Legacy Animation leaves a transform
            // wherever the clip last sampled it, so stopping the death and restarting the idle
            // brings back every joint EXCEPT the root — and a recycled slot then plays a perfect
            // breathing loop while lying on its back. That shipped: restarting a level came back
            // with the whole enemy line dead on the ground.
            //
            // This asserts the SHAPE of the problem rather than the symptom, so a future clip set
            // that breaks the assumption says so here instead of on a device.
            var animPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/Prefabs/PlayerUnit_unit_rifleman.prefab");
            if (animPrefab != null)
            {
                var animation = animPrefab.GetComponentInChildren<Animation>();
                Check(animation != null, "the unit prefab carries an Animation component");
                if (animation != null)
                {
                    bool DrivesRoot(string clipName)
                    {
                        var st = animation[clipName];
                        if (st?.clip == null) return false;
                        foreach (var b in AnimationUtility.GetCurveBindings(st.clip))
                            if (string.IsNullOrEmpty(b.path)) return true;
                        return false;
                    }
                    Check(DrivesRoot(UnitAnim.Die),
                          "`die` drives the ROOT — which is why a corpse's root has to be restored");
                    Check(!DrivesRoot(UnitAnim.Idle),
                          "`idle` does NOT drive the root, so it cannot undo a death on its own");
                }
            }

            // --- AMMO TYPES. The whole feature was PRESENT AND DEAD before 2026-08-07: the
            // enum, ProjectileEntity.Ammo, GameState.SelectedAmmo, GameState.BurningEnemyIds,
            // the unlock/selection persistence and CollisionSystem.IncendiaryHitUnitIds all
            // existed, and FireVolley never set Ammo, so every round was Standard forever.
            // These checks assert the WIRING, which is the part that was missing.
            {
                var catalog = AssetDatabase.LoadAssetAtPath<AmmoCatalogSO>(
                    "Assets/GameData/AmmoCatalog.asset");
                Check(catalog != null && catalog.slots.Count == 4,
                      $"the ammo catalogue exists with four types ({catalog?.slots.Count ?? 0})");

                if (catalog != null)
                {
                    // STANDARD MUST BE THE IDENTITY. This is what makes PRODUCT_DIRECTION's "no
                    // ammo is ever REQUIRED to clear a level" checkable rather than a promise:
                    // a level cleared on Standard is a level cleared with every modifier at 1.
                    var std = AmmoModifiers.From(catalog, AmmoType.Standard);
                    Check(std.UnitDamageScale == 1f && std.StructureDamageScale == 1f
                          && std.SpreadScale == 1f && std.BurnDamage == 0,
                          "Standard ammo is the IDENTITY — it cannot change a volley");
                    Check(AmmoModifiers.From(null, AmmoType.Cluster).UnitDamageScale == 1f,
                          "a MISSING catalogue falls back to Standard rather than throwing");

                    // Only Standard is free, or a fresh player has a choice they never earned.
                    Check(catalog.slots.Count(a => a.coinPrice == 0) == 1
                          && catalog.Find(AmmoType.Standard).coinPrice == 0,
                          "Standard is the only free ammo");

                    // The burn must not one-shot the FRAILEST unit in the CURRENT roster. The
                    // old value of 6 was calibrated against an 8hp Sniper that no longer exists
                    // (HANDOVER records it); anchoring to the live roster is what stops this
                    // expiring silently the next time the roster is cut.
                    int frailest = AssetDatabase.FindAssets("t:UnitDefinitionSO")
                        .Select(AssetDatabase.GUIDToAssetPath)
                        .Select(AssetDatabase.LoadAssetAtPath<UnitDefinitionSO>)
                        .Where(u => u != null && u.maxHp > 0).Min(u => u.maxHp);
                    int burn = catalog.Find(AmmoType.Incendiary).burnDamage;
                    Check(burn > 0 && burn < frailest,
                          $"the incendiary burn ({burn}) chips rather than one-shots the " +
                          $"frailest unit in the roster ({frailest} hp)");

                    // AP is the masonry answer and pays for it against soft targets.
                    var ap = AmmoModifiers.From(catalog, AmmoType.AP);
                    Check(ap.StructureDamageScale > 1f && ap.UnitDamageScale < 1f,
                          $"AP trades unit damage ({ap.UnitDamageScale:F2}x) for structure " +
                          $"damage ({ap.StructureDamageScale:F1}x)");
                    // THE NET EFFECT PER ROUND, which is the contract and is what the first
                    // version of this got wrong. The engine does `Damage * StructureMultiplier`,
                    // so scaling Damage down for soft targets silently scaled MASONRY down too:
                    // AP's real effect was 0.6 * 2 = 1.2x, and the check that only looked at the
                    // multiplier passed anyway. Caught on device — 128 off a 165hp citadel where
                    // ~192 was intended. Assert the PRODUCT, never the factor.
                    var stdM = AmmoModifiers.From(catalog, AmmoType.Standard);
                    foreach (var (baseDmg, baseMult) in new[] { (32, 3f), (8, 0.25f), (8, 6f) })
                    {
                        float stdOut = stdM.UnitDamage(baseDmg) * stdM.StructureMultiplier(baseMult);
                        float apOut = ap.UnitDamage(baseDmg) * ap.StructureMultiplier(baseMult);
                        // Tolerance DERIVED from integer rounding, not guessed: Damage is an
                        // int, so an 8-damage round scaled by 0.6 lands on 5 rather than 4.8 and
                        // the ratio comes out 2.08x. A fixed epsilon would either fail this
                        // honestly-correct case or be too loose to catch a real 1.2x regression.
                        float tol = ap.StructureDamageScale * (0.5f / baseDmg) + 0.02f;
                        Check(Mathf.Abs(apOut / stdOut - ap.StructureDamageScale) < tol,
                              $"AP does {ap.StructureDamageScale:F1}x STRUCTURE damage per round " +
                              $"(dmg {baseDmg} x{baseMult}: {stdOut:F0} -> {apOut:F0}, " +
                              $"{apOut / stdOut:F2}x)");
                        Check(ap.UnitDamage(baseDmg) < stdM.UnitDamage(baseDmg),
                              $"...while doing LESS to men ({stdM.UnitDamage(baseDmg)} -> " +
                              $"{ap.UnitDamage(baseDmg)})");
                    }
                    // Damage is floored at 1, so a scale can never make an owned ammo do nothing.
                    Check(AmmoModifiers.From(catalog, AmmoType.AP).UnitDamage(1) >= 1,
                          "an ammo scale never rounds a round's damage down to zero");

                    var cluster = AmmoModifiers.From(catalog, AmmoType.Cluster);
                    Check(cluster.SpreadScale > 1f && cluster.UnitDamageScale < 1f,
                          $"Cluster spreads wider ({cluster.SpreadScale:F1}x) for lighter hits " +
                          $"({cluster.UnitDamageScale:F2}x)");

                    // --- THE WIRING ITSELF: a fired round must CARRY the selection.
                    // Its own lookup rather than the shell block's `withCannon`, which is
                    // declared below: a level with a cannon lets the same checks cover the
                    // SHELL, which must take the ammo like everything else.
                    var withCannonLevel = levels.FirstOrDefault(l => l.playerGroups.Count > 0 &&
                        l.structures.Any(p => p.definition != null && p.definition.isPlayerSide
                                              && p.definition.hasCannon));
                    if (withCannonLevel != null)
                    {
                        var baseState = LevelBuilder.BuildInitialState(
                            withCannonLevel, 1, levels.Count, new System.Random(7))
                            with { Phase = GamePhase.Playing, TurnPhase = TurnPhase.Aiming };

                        foreach (var type in new[] { AmmoType.Incendiary, AmmoType.AP, AmmoType.Cluster })
                        {
                            var picked = baseState with { SelectedAmmo = type };
                            var v = BattleTick.FireVolley(picked, new Vector3(6f, 6f, 0f),
                                                          new System.Random(3), catalog);
                            Check(v.Projectiles.Where(p => p.OwnerIsPlayer).All(p => p.Ammo == type),
                                  $"every round in the volley carries {type}");
                            // Including the SHELL — DYNAMISM_DESIGN is explicit that there are
                            // no special cases, and an AP shell is the bunker-buster fantasy.
                            var sh = v.Projectiles.FirstOrDefault(p => p.Type == ProjectileType.Shell);
                            Check(sh != null && sh.Ammo == type,
                                  $"the TANK SHELL also carries {type}");
                        }

                        // Standard leaves the volley exactly as it was before ammo existed.
                        var asStd = BattleTick.FireVolley(baseState, new Vector3(6f, 6f, 0f),
                                                          new System.Random(3), catalog);
                        var noCat = BattleTick.FireVolley(baseState, new Vector3(6f, 6f, 0f),
                                                          new System.Random(3), null);
                        Check(asStd.Projectiles.Where(p => p.OwnerIsPlayer).Select(p => p.Damage)
                                  .SequenceEqual(noCat.Projectiles.Where(p => p.OwnerIsPlayer)
                                  .Select(p => p.Damage)),
                              "Standard fires the identical volley to having no catalogue at all");

                        // --- THE BURN ACTUALLY LANDS. This is the half that was most likely to
                        // stay dead: CollisionSystem has populated IncendiaryHitUnitIds since
                        // the port and NOTHING consumed it, and GameState.BurningEnemyIds was
                        // declared and never written. Driven through the real Step so the
                        // handover edge is the thing under test, not a helper.
                        {
                            // ReadyToHandOver: player side, nothing in the air, no pause left.
                            var marked = baseState with
                            {
                                SelectedAmmo = AmmoType.Incendiary,
                                TurnSide = TurnSide.Player,
                                TurnPhase = TurnPhase.Resolving,
                                TurnHandoverDelay = 0f,
                                Projectiles = new List<ProjectileEntity>(),
                                BurningEnemyIds = new HashSet<int> { baseState.EnemyUnits[0].Id },
                            };
                            int hpBefore = marked.EnemyUnits[0].Hp;
                            var burned = BattleTick.Step(marked, 1f / 60f, withCannonLevel,
                                                         new System.Random(5), catalog);
                            var after = burned.EnemyUnits.FirstOrDefault(
                                u => u.Id == marked.EnemyUnits[0].Id);
                            Check(after != null && after.Hp == hpBefore - burn,
                                  $"a burning unit takes its burn on the handover " +
                                  $"({hpBefore} -> {after?.Hp})");
                            Check(burned.TurnPhase == TurnPhase.EnemyWindup,
                                  "the burn lands on the edge into the enemy windup");

                            // ONCE, not forever. The set is spent as it is applied, so one
                            // incendiary hit is one tick — a unit that kept burning every turn
                            // off a single round would make the type a win button.
                            Check(burned.BurningEnemyIds.Count == 0,
                                  "the burning set is CLEARED as it is spent, so a unit burns once");

                            // And Standard never burns, however the set got populated.
                            var noBurn = BattleTick.Step(marked with { SelectedAmmo = AmmoType.Standard },
                                                         1f / 60f, withCannonLevel,
                                                         new System.Random(5), catalog);
                            Check(noBurn.EnemyUnits.First(u => u.Id == marked.EnemyUnits[0].Id).Hp
                                  == hpBefore,
                                  "Standard ammo applies no burn");
                        }

                        // AP really does reach the round's structure multiplier.
                        var apVolley = BattleTick.FireVolley(
                            baseState with { SelectedAmmo = AmmoType.AP },
                            new Vector3(6f, 6f, 0f), new System.Random(3), catalog);
                        var apShell = apVolley.Projectiles.First(p => p.Type == ProjectileType.Shell);
                        var stdShell = asStd.Projectiles.First(p => p.Type == ProjectileType.Shell);
                        Check(apShell.StructureDamageMultiplier > stdShell.StructureDamageMultiplier,
                              $"an AP shell hits masonry harder than a standard one " +
                              $"({apShell.StructureDamageMultiplier:F1}x vs " +
                              $"{stdShell.StructureDamageMultiplier:F1}x)");
                    }
                }
            }

            // --- THE TANK SHELL. It is off-roster — built from a STRUCTURE, not a unit — so
            // nothing in the unit-facing checks above would notice it had stopped firing, which
            // is exactly how it went missing from the port in the first place.
            var withCannon = levels.FirstOrDefault(l => l.playerGroups.Count > 0 &&
                l.structures.Any(p => p.definition != null && p.definition.isPlayerSide
                                      && p.definition.hasCannon));
            Check(withCannon != null, "at least one level fields a player cannon");
            if (withCannon != null)
            {
                var st0 = LevelBuilder.BuildInitialState(withCannon, 1, levels.Count, new System.Random(7));
                st0 = st0 with { Phase = GamePhase.Playing, TurnPhase = TurnPhase.Aiming };
                Check(st0.TankShellsRemaining > 0,
                      $"a level with a cannon starts with ammo ({st0.TankShellsRemaining})");

                var fired = BattleTick.FireVolley(st0, new Vector3(6f, 6f, 0f), new System.Random(3));
                int shellsOut = fired.Projectiles.Count(p => p.Type == ProjectileType.Shell);
                Check(shellsOut > 0, $"the player volley includes a tank shell ({shellsOut})");
                Check(fired.TankShellsRemaining == st0.TankShellsRemaining - shellsOut,
                      "firing SPENDS the shells it fired");
                // The shell is the only round with a structure multiplier worth having; a shell
                // that arrives at 1x is a rifle bullet with a big model.
                var shell = fired.Projectiles.First(p => p.Type == ProjectileType.Shell);
                Check(shell.StructureDamageMultiplier > 1f,
                      $"the shell carries its structure multiplier ({shell.StructureDamageMultiplier:F1}x)");
                Check(shell.Id >= 30000, "shell ids sit in their own band, clear of bullets and enemy fire");

                // THE SHELL MUST LAND WHERE THE VOLLEY LANDS. This is the check that was missing
                // while the shell was `aimVelocity * velocityBoost`: range goes as v^2, so a 1.12
                // boost is 1.2544x the range from a muzzle only ~2 units behind the line, and the
                // shell overshot by ~1.3 units. Measured on device before it was found — aiming
                // the infantry at one structure put the shell onto the one behind it, and since
                // the shell is the only round that can meaningfully hurt a building, the player
                // could not place the only weapon that mattered.
                //
                // Compared as LANDING POINTS from their real origins, not as velocities: the two
                // launch from different places, so equal velocity is precisely the bug.
                foreach (var aim in new[] { new Vector3(5f, 5f, 0f), new Vector3(6f, 6f, 0f),
                                            new Vector3(7f, 5f, 0f) })
                {
                    var v = BattleTick.FireVolley(
                        st0 with { TurnPhase = TurnPhase.Aiming }, aim, new System.Random(3));
                    var sh = v.Projectiles.FirstOrDefault(p => p.Type == ProjectileType.Shell);
                    if (sh == null) continue;

                    float shellX = TrajectoryPhysics.LandingPoint(
                        new Vector3(sh.X, sh.Y, sh.Z), new Vector3(sh.Vx, sh.Vy, sh.Vz)).x;

                    // The volley's own centre, from the same mean muzzle FireVolley solves to.
                    float meanX = st0.PlayerUnits.Average(u => u.X);
                    float meanY = st0.PlayerUnits.Average(u => u.Y) + BattleTick.InfantryMuzzleY;
                    float volleyX = TrajectoryPhysics.LandingPoint(
                        new Vector3(meanX, meanY, 0f), aim).x;

                    Check(Mathf.Abs(shellX - volleyX) < 0.5f,
                          $"the shell lands with the volley at aim ({aim.x},{aim.y}) — " +
                          $"shell {shellX:F2} vs volley {volleyX:F2}");
                }

                // velocityBoost is now HEADROOM, not a blind multiplier: it caps the solved speed
                // so the gun may reach further back than the drag that ordered the shot, and a
                // shell can still never be faster than that cap.
                {
                    var aim = new Vector3(6f, 6f, 0f);
                    var v = BattleTick.FireVolley(
                        st0 with { TurnPhase = TurnPhase.Aiming }, aim, new System.Random(3));
                    var sh = v.Projectiles.First(p => p.Type == ProjectileType.Shell);
                    float shellSpeed = Mathf.Sqrt(sh.Vx * sh.Vx + sh.Vy * sh.Vy);
                    float aimSpeed = Mathf.Sqrt(aim.x * aim.x + aim.y * aim.y);
                    var cannon = withCannon.structures
                        .First(p => p.definition != null && p.definition.isPlayerSide
                                    && p.definition.hasCannon).definition.cannon;
                    Check(shellSpeed <= aimSpeed * cannon.velocityBoost + 0.01f,
                          $"the shell never exceeds its velocityBoost headroom " +
                          $"({shellSpeed:F2} <= {aimSpeed * cannon.velocityBoost:F2})");
                }

                // Run the ammo down and past zero. An unclamped decrement would go negative and
                // the gun would fire forever on a level that had spent its shells.
                var s2 = fired;
                for (int i = 0; i < 12; i++)
                {
                    s2 = s2 with { TurnPhase = TurnPhase.Aiming };
                    s2 = BattleTick.FireVolley(s2, new Vector3(6f, 6f, 0f), new System.Random(3));
                }
                Check(s2.TankShellsRemaining == 0, $"ammo stops at zero ({s2.TankShellsRemaining})");

                // And the gate is honoured, so a level can field a tank with a cold gun.
                var cold = st0 with { CannonArmed = false };
                var coldFired = BattleTick.FireVolley(cold, new Vector3(6f, 6f, 0f), new System.Random(3));
                Check(coldFired.Projectiles.All(p => p.Type != ProjectileType.Shell),
                      "CannonArmed=false fires no shell");

                // The PLAYER's volley must carry each unit's own projectile type — it did not,
                // and only AutoFire did, so a rocket trooper's round was a plain bullet with no
                // splash and a 1x structure multiplier whenever a human fired it.
                var types = fired.Projectiles.Where(p => p.OwnerIsPlayer).Select(p => p.Type).Distinct().ToList();
                Check(types.Count >= 1, $"player rounds carry a projectile type ({types.Count} distinct)");
            }
        }

        Debug.Log($"[PortSelfTest] {(failed == 0 ? "ALL PASS" : $"{failed} FAILURES")}\n{Log}");
        if (failed > 0 && Application.isBatchMode) EditorApplication.Exit(1);
    }
}
