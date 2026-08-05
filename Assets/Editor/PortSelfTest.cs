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

            Check(levels.Count == 24, $"all 24 levels present ({levels.Count})");

            // levelNumber MUST equal index + 1: the switcher indexes the array by position and
            // every HUD readout names the level by number. The Kotlin carries the same rule, and
            // it is what forced the test rigs to be renumbered when the campaign grew.
            var misnumbered = levels.Where((l, i) => l.levelNumber != i + 1).ToList();
            Check(misnumbered.Count == 0,
                  "levelNumber == index + 1 for every level" +
                  (misnumbered.Count == 0 ? "" : $" (first bad: {misnumbered[0].displayName})"));

            var missing = new SortedSet<string>();
            int built = 0;
            foreach (var l in levels)
            {
                var st = LevelBuilder.BuildInitialState(l, 1, levels.Count, new System.Random(7));
                built++;
                Check(st.PlayerUnits.Count > 0, null);
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
        }

        Debug.Log($"[PortSelfTest] {(failed == 0 ? "ALL PASS" : $"{failed} FAILURES")}\n{Log}");
        if (failed > 0 && Application.isBatchMode) EditorApplication.Exit(1);
    }
}
