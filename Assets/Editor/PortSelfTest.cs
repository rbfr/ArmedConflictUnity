using System.Collections.Generic;
using System.Linq;
using System.Text;
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
            var l1 = AssetDatabase.LoadAssetAtPath<LevelDefinitionSO>("Assets/GameData/Levels/Level1.asset");
            if (l1 == null) { Check(false, "Level1 asset present"); }
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
                Check(garrison.Count == 3, "3 enemies are garrisoned on the outpost");
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

            var independent = new StructureEntity(5, tierDef, 9f, 0f, 0f, 10);
            var c2 = CollisionSystem.PropagateCollapse(new[] { independent }, new[] { 1 });
            Check(!c2.Contains(5), "an unrelated structure is left standing");
        }

        Debug.Log($"[PortSelfTest] {(failed == 0 ? "ALL PASS" : $"{failed} FAILURES")}\n{Log}");
        if (failed > 0 && Application.isBatchMode) EditorApplication.Exit(1);
    }
}
