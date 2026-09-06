using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using ArmedConflict.Data;
using ArmedConflict.Game;

/// <summary>
/// Checks levels against the ten composition rules in LEVEL_AUTHORING.md.
///
/// Run over the whole campaign, headless — which is how it will actually be used, because the
/// editor GUI here runs over VNC on llvmpipe and nobody opens it:
///
///     -batchmode -quit -executeMethod LevelComposition.Report
///
/// The same checks render live in the inspector via LevelDefinitionInspector, for whoever does
/// open it.
///
/// The rules were prose in a Kotlin comment for months and were broken repeatedly anyway — an
/// entire 25-level campaign was scrapped in one go for breaking rules 1-3. Prose cannot be
/// checked; this can, and moving authoring into Unity is what put the checks where the authoring
/// happens.
///
/// It measures by BUILDING THE LEVEL and reading the same numbers the camera uses
/// (GameState.PlayerCamHalfWidth / EnemyCamHalfWidth, filled by LevelBuilder), rather than
/// re-deriving spans from anchors. Re-deriving would make this a second source of truth about
/// framing — the exact failure the sandbox generator was just split out to remove — and it would
/// be wrong anyway, because a group's real width comes from Formation, not from its anchor.
/// </summary>
public static class LevelComposition
{
    // --- rule thresholds, from LEVEL_AUTHORING.md -------------------------------------
    const float PlayerLineIdealWidth = 6f;
    const float PlayerLineMaxWidth = 7f;      // "~6 wide" — flag once it is clearly past it
    const float EnemyClusterMaxWidth = 11f;
    const int   MaxEnemyStructures = 3;       // one dominant + at most two small supports
    const float SeparationMin = 14f;
    // 20 while L1 trials 18.5 (2026-08-20). Min stays 14 until the rest of the campaign moves.
    const float SeparationMax = 20f;

    public enum Severity { Ok, Warn, Error }

    public readonly struct Finding
    {
        public readonly Severity Level;
        public readonly string Text;
        public Finding(Severity level, string text) { Level = level; Text = text; }
    }

    /// <summary>Batch entry point: every campaign level, worst first. Exits 1 on an Error.</summary>
    public static void Report()
    {
        var levels = AssetDatabase.FindAssets("t:LevelDefinitionSO")
            .Select(AssetDatabase.GUIDToAssetPath)
            .Select(AssetDatabase.LoadAssetAtPath<LevelDefinitionSO>)
            .Where(l => l != null && !l.isTestLevel)
            .OrderBy(l => l.levelNumber)
            .ToList();

        int warns = 0, errors = 0;
        foreach (var level in levels)
        {
            var findings = Check(level, out string buildError);
            if (buildError != null)
            {
                Debug.LogError($"[Composition] L{level.levelNumber} {level.displayName}: does " +
                               $"not build — {buildError}");
                errors++;
                continue;
            }

            var bad = findings.Where(f => f.Level != Severity.Ok).ToList();
            warns += bad.Count(f => f.Level == Severity.Warn);
            errors += bad.Count(f => f.Level == Severity.Error);

            if (bad.Count == 0)
            {
                Debug.Log($"[Composition] L{level.levelNumber} {level.displayName}: all ten rules ok");
                continue;
            }
            foreach (var f in bad)
                Debug.Log($"[Composition] L{level.levelNumber} {level.displayName}: " +
                          $"{(f.Level == Severity.Error ? "ERROR" : "warn")} — {f.Text}");
        }

        Debug.Log($"[Composition] {levels.Count} campaign levels checked, " +
                  $"{warns} warning(s), {errors} error(s)");

        // Warnings do NOT fail the run. A level may bend a rule for a reason, and that reason
        // belongs in its designNotes; an author who cannot ship a deliberate exception will stop
        // running the check at all. Errors are the locked roster scale and rule 8 — a unit that
        // cannot be hit at all — neither of which is negotiable.
        if (errors > 0 && Application.isBatchMode) EditorApplication.Exit(1);
    }

    /// <summary>
    /// PROBE, not a rule: prints where the simulation actually PLACES every unit of every
    /// arrival set, for whichever campaign levels are named in -probeLevels (default: all).
    ///
    ///     -executeMethod LevelComposition.Arrivals -probeLevels 12
    ///
    /// It exists because the checkers and the DEVICE disagreed about L12's Sovereign on
    /// 2026-09-04 — every rule passed against an anchor of x 9.0 and nothing stood there on the
    /// phone. A rule reports a verdict; this reports the positions the verdict was reached from,
    /// which is the only way to tell a bad rule from a bad renderer. It goes through the same
    /// ArrivalSets rules 8 and 9 use, so it cannot describe a placement they did not judge.
    /// </summary>
    public static void Arrivals()
    {
        var wanted = new HashSet<int>();
        var argv = System.Environment.GetCommandLineArgs();
        for (int i = 0; i < argv.Length - 1; i++)
            if (argv[i] == "-probeLevels")
                foreach (var part in argv[i + 1].Split(','))
                    if (int.TryParse(part, out int n)) wanted.Add(n);

        var levels = AssetDatabase.FindAssets("t:LevelDefinitionSO")
            .Select(AssetDatabase.GUIDToAssetPath)
            .Select(AssetDatabase.LoadAssetAtPath<LevelDefinitionSO>)
            .Where(l => l != null && !l.isTestLevel)
            .Where(l => wanted.Count == 0 || wanted.Contains(l.levelNumber))
            .OrderBy(l => l.levelNumber);

        foreach (var level in levels)
        {
            GameState state;
            try { state = LevelBuilder.BuildInitialState(level, 0, 12, new System.Random(12345)); }
            catch (System.Exception e)
            {
                Debug.LogError($"[Arrivals] L{level.levelNumber}: does not build — {e.Message}");
                continue;
            }

            foreach (var (label, units, dead) in ArrivalSets(level, state))
            {
                Debug.Log($"[Arrivals] L{level.levelNumber} {level.displayName} — {label}: " +
                          $"{units.Count} unit(s), {dead.Count} structure(s) dead by trigger");
                foreach (var u in units)
                    Debug.Log($"[Arrivals]   {(u.Definition != null ? u.Definition.name : "null")} " +
                              $"x {u.X:F2}  y {u.Y:F2}  z {u.Z:F2}  hp {u.Hp}  " +
                              $"advance {u.AdvancePerTurn:F2}  scale " +
                              $"{(u.Definition != null ? u.Definition.renderScale : 0f):F2}");
            }
        }
    }

    /// <summary>
    /// The ten rules. A half-authored level legitimately fails to build (no background, a null
    /// unit reference) — that comes back as buildError, and must not read as a rule violation.
    /// </summary>
    public static List<Finding> Check(LevelDefinitionSO level, out string buildError)
    {
        buildError = null;
        var findings = new List<Finding>();

        GameState state;
        try
        {
            // Deterministic seed: a check that reports a different number every run is noise.
            // Formation uses the random for jitter only, so any fixed seed measures the level.
            state = LevelBuilder.BuildInitialState(level, 1, 1, new System.Random(12345));
        }
        catch (System.Exception e)
        {
            buildError = e.Message;
            return findings;
        }

        // --- rule 1: the player line sets the aiming zoom ---
        float playerWidth = state.PlayerCamHalfWidth * 2f;
        findings.Add(new Finding(
            playerWidth > PlayerLineMaxWidth ? Severity.Warn : Severity.Ok,
            $"rule 1: player line {playerWidth:F1} wide (~{PlayerLineIdealWidth} ideal)" +
            (playerWidth > PlayerLineMaxWidth
                ? ". A wide player line IS a zoomed-out level — nothing else in the layout can "
                + "compensate."
                : "")));

        // --- rule 2: the enemy cluster sets the scout/resolve zoom ---
        float enemyWidth = state.EnemyCamHalfWidth * 2f;
        findings.Add(new Finding(
            enemyWidth > EnemyClusterMaxWidth ? Severity.Warn : Severity.Ok,
            $"rule 2: enemy cluster {enemyWidth:F1} wide incl. structure edges " +
            $"(<={EnemyClusterMaxWidth})" +
            (enemyWidth > EnemyClusterMaxWidth
                ? ". Past this the scout camera hits its clamp and everything shrinks."
                : "")));

        // --- rule 3: one dominant structure, at most two small supports ---
        var enemyStructures = level.structures
            .Where(s => s.definition != null && !s.definition.isPlayerSide).ToList();
        findings.Add(new Finding(
            enemyStructures.Count > MaxEnemyStructures ? Severity.Warn : Severity.Ok,
            $"rule 3: {enemyStructures.Count} enemy structure(s) (<={MaxEnemyStructures})" +
            (enemyStructures.Count > MaxEnemyStructures
                ? ". One commanding keep, not a village — this cannot be framed."
                : "")));

        // --- rules 4 and 6: separation, TANK -> DOMINANT STRUCTURE, or front rank
        // when a level fields no player-side vehicle (L5, 2026-08-21). ---
        var tank = level.structures.FirstOrDefault(s => s.definition != null
                                                     && s.definition.isPlayerSide);
        var dominant = enemyStructures
            .OrderByDescending(s => Width(s.definition))
            .FirstOrDefault();
        if (dominant == null)
        {
            findings.Add(new Finding(Severity.Ok,
                "rules 4/6: separation not measurable — needs an enemy structure"));
        }
        else if (tank == null && (state.PlayerUnits == null || state.PlayerUnits.Count == 0))
        {
            findings.Add(new Finding(Severity.Ok,
                "rules 4/6: separation not measurable — no player line and no tank"));
        }
        else
        {
            float fromX = tank != null ? tank.x : state.PlayerUnits.Max(u => u.X);
            string from = tank != null ? "tank" : "front rank";
            float separation = Mathf.Abs(dominant.x - fromX);
            bool ok = separation >= SeparationMin && separation <= SeparationMax;
            findings.Add(new Finding(ok ? Severity.Ok : Severity.Warn,
                $"rules 4/6: separation {separation:F1}, {from} -> {dominant.definition.name} " +
                $"({SeparationMin}-{SeparationMax})" +
                (ok ? "" : separation > SeparationMax
                    ? ". Further out reads as 'shots pass through' — range is not the "
                    + "constraint, legibility is."
                    : ". Too close: the arc that makes the drag feel right needs the distance.")));
        }

        // --- rule 5: garrison the majority of the enemy roster ---
        int enemyTotal = level.enemyGroups.Sum(g => g.count);
        int garrisoned = level.enemyGroups
            .Where(g => !string.IsNullOrEmpty(g.standingOnStructureId)).Sum(g => g.count);
        if (enemyTotal > 0)
        {
            bool majority = garrisoned * 2 > enemyTotal;
            findings.Add(new Finding(majority ? Severity.Ok : Severity.Warn,
                $"rule 5: {garrisoned}/{enemyTotal} enemies garrisoned " +
                $"({100f * garrisoned / enemyTotal:F0}%)" +
                (majority ? ""
                    : ". Structures only read as objectives when killing them is the efficient "
                    + "way to kill units. Below half, the roster dies first and the structure HP "
                    + "never mattered.")));
        }

        // --- rule 7: every enemy must be physically reachable ---
        // Implemented in BalanceAudit so the audit and the inspector cannot disagree about
        // whether a level can be played at all. The first six rules are all about FRAMING and
        // horizontal separation, and they passed a level whose garrison sat outside the game's
        // ballistic envelope — height is what spends the power budget, and nothing measured it.
        findings.Add(BalanceAudit.ReachRule(state));

        // --- rule 7, FOR THE UNITS THAT ARE NOT ON THE FIELD YET ---
        //
        // Victory is every enemy unit dead, INCLUDING the ones that walk on during turn 4 and the
        // boss that bursts out of a razed keep. Reading turn 0 alone let an arrival be authored
        // past maximum range with every rule-7 check in the project still green — which is the
        // same bug rule 7 was written for, one turn later.
        //
        // It is not hypothetical: on 2026-08-12 the first fix for L11's rule 8 violation moved
        // its wave to anchorX 9 and put a heavy at dx 20.40 against a 20.25 envelope. This report
        // stayed green and it was caught by hand. Hence this.
        //
        // Each set is measured ALONE — the arrivals replace the roster rather than joining it —
        // so the finding names the wave that is out of reach instead of re-reporting whichever
        // turn-0 body happens to be deepest. `BalanceAudit.ReachRule` is reused rather than
        // reimplemented, for the same reason rule 8 delegates: one rule, one implementation.
        foreach (var (label, units, _) in ArrivalSets(level, state))
        {
            if (label == Turn0 || units.Count == 0) continue;
            var f = BalanceAudit.ReachRule(state with { EnemyUnits = units.ToList() });
            if (f.Level == Severity.Ok) continue;
            findings.Add(new Finding(f.Level, $"{label} — {f.Text}"));
        }

        // --- rule 8: no ground unit stands inside a structure's collision box ---
        findings.Add(CollisionBoxRule(level, state));
        findings.Add(BallisticShadowRule(level, state));
        findings.Add(WreckOcclusionRule(level, state));

        // --- the locked roster scale: not a composition rule, but it bounds every level ---
        int playerTotal = level.playerGroups.Sum(g => g.count);
        foreach (var (side, n) in new[] { ("player", playerTotal), ("enemy", enemyTotal) })
            if (n < 7 || n > 30)
                findings.Add(new Finding(Severity.Error,
                    $"{side} roster is {n} — GAME_DESIGN_LOCKS.md locks 7-30 per side, " +
                    "garrisoned units included"));

        return findings;
    }


    /// <summary>
    /// RULE 10: no unit ARRIVES INSIDE THE WRECK of the structure its own phase just destroyed.
    ///
    /// Rules 8 and 9 both ask whether a unit can be HIT. Neither asks whether it can be SEEN,
    /// and a boss that cannot be seen is not a fight — it is nine drags into an empty-looking
    /// patch of rubble.
    ///
    /// THIS IS THE HOLE THE `DeadByTrigger` EXEMPTION LEAVES, and the exemption is right: a boss
    /// bursts out of the structure that spawned it, so counting that structure's COLLISION BOX
    /// would condemn every boss in the game. Its box is gone. What is NOT gone is the WRECK the
    /// renderer swaps in — `LevelScenery` spawns it with the live building, at the building's own
    /// position and scale, and shows it the moment the building dies. It is pure geometry: no
    /// collider, nothing in `CollisionSystem`, invisible to every rule this file had. So a body
    /// standing in it is hittable, reachable, inside no box — and not on screen.
    ///
    /// FOUND ON DEVICE, 2026-09-04, and it had shipped in BOTH campaign boss phases. L12's
    /// Sovereign stands at x 8.92 inside a wreck spanning x 4.9-11.1; all four of L6's arrivals
    /// stand inside the keep's. Eight of the campaign's nine boss-phase bodies, none of which
    /// ever walks clear, because a boss is authored with `advancePerTurn: 0`. The L12 phase was
    /// played to the arrival and the free camera parked on the spot: wreck geometry, no body,
    /// while the same model at the same scale rendered correctly 6 units away.
    ///
    /// It reads the footprint the RENDERER uses rather than re-deriving one — the wreck's own
    /// bounds, scaled the way `LevelScenery` scales it, which is by the LIVE BUILDING's scale and
    /// not the wreck's. Get that wrong and the check measures a building nobody draws.
    ///
    /// ERROR when the wreck covers the body outright, Warn at half. Deliberately NOT judged: a
    /// unit standing next to a structure the PLAYER may destroy later. That wreck is not
    /// guaranteed to exist, the body is visible until it does, and casting that net would indict
    /// most of the campaign on a maybe. A trigger's wreck is certain the moment the phase fires.
    /// </summary>
    public static Finding WreckOcclusionRule(LevelDefinitionSO level, GameState state)
    {
        int judged = 0, buried = 0, halfBuried = 0;
        float worstCover = 0f;
        var named = new List<string>();

        // The sightline's slope, from the level's OWN framing rather than a constant: camZ is
        // `halfWidth / ZHalfFovTan`, and the camera flies at `BattleCamera.CameraY`. A wider
        // level is framed from further back and its ground angle is shallower, which is exactly
        // the direction that decides how much rubble hides.
        float camZ = Mathf.Max(1f, state.EnemyCamHalfWidth / CameraDirector.ZHalfFovTan);
        float groundTan = BattleCamera.CameraY / camZ;

        foreach (var (label, units, deadByTrigger) in ArrivalSets(level, state))
        {
            if (deadByTrigger.Count == 0) continue;

            var wrecks = new List<(string Name, float MinX, float MaxX,
                                   float MinZ, float MaxZ, float TopY)>();
            foreach (var p in level.structures)
                if (p.definition != null && deadByTrigger.Contains(p.definition)
                    && TryWreckFootprint(p, out var w))
                    wrecks.Add(w);
            if (wrecks.Count == 0) continue;

            foreach (var u in units)
            {
                // A garrison rides its own deck and dies with it; only bodies on the ground can
                // end up standing in rubble.
                if (u.StandingOnStructureId != null) continue;
                judged++;

                float height = UnitGeometry.UnitScaleUnits *
                               (u.Definition != null ? u.Definition.renderScale : 1f);
                if (height <= 0.0001f) continue;

                float cover = 0f;
                string under = null;
                foreach (var w in wrecks)
                {
                    if (u.X < w.MinX || u.X > w.MaxX) continue;

                    // IN FRONT OF the rubble's near face is SEEN, and it is the only place that
                    // is. Behind it, the camera's own elevation makes the wreck hide MORE than
                    // its height: it sits ~1.2 above the ground and looks nearly along it, so an
                    // occluder that far forward blocks the sightline for another `depth * tan` of
                    // the body. This is the same 6-degree geometry that makes a second rank
                    // invisible, working on rubble instead of on shoulders.
                    if (u.Z > w.MaxZ) continue;
                    float lift = (w.MaxZ - u.Z) * groundTan;
                    float c = (w.TopY + lift - u.Y) / height;
                    // The SPAN is the number an author needs — a wreck's edge, like a
                    // structure's, is nowhere near its anchor.
                    if (c > cover)
                    {
                        cover = c;
                        under = $"{w.Name}'s wreck (x {w.MinX:F2} to {w.MaxX:F2}, " +
                                $"top y {w.TopY:F2})";
                    }
                }
                if (cover <= 0f) continue;

                if (cover > worstCover) worstCover = cover;
                if (cover >= 1f) buried++;
                else if (cover >= 0.5f) halfBuried++;
                else continue;

                if (named.Count < 4)
                    named.Add($"{label} {(u.Definition != null ? u.Definition.name : "unit")} " +
                              $"at x {u.X:F2} stands {cover * 100f:F0}% inside {under}");
            }
        }

        if (judged == 0)
            return new Finding(Severity.Ok, "rule 10: no arrival lands on a razed structure");

        if (buried > 0)
            return new Finding(Severity.Error,
                $"rule 10: {buried} arrival(s) are HIDDEN INSIDE the wreck of the structure that " +
                $"spawned them, {halfBuried} half — {string.Join("; ", named)}. Hittable and " +
                "invisible: the player is asked to aim at rubble.");

        if (halfBuried > 0)
            return new Finding(Severity.Warn,
                $"rule 10: {halfBuried} arrival(s) stand up to {worstCover * 100f:F0}% inside a " +
                $"wreck — {string.Join("; ", named)}");

        return new Finding(Severity.Ok,
            $"rule 10: all {judged} arrival(s) stand clear of the rubble they emerge from");
    }

    /// <summary>
    /// The wreck's footprint in GAME space, exactly as `LevelScenery` places it: the wreck model
    /// carries the LIVE BUILDING's position and scale, so a wreck authored to a different size
    /// than its building is still drawn at the building's.
    /// </summary>
    static bool TryWreckFootprint(StructurePlacement p,
        out (string Name, float MinX, float MaxX, float MinZ, float MaxZ, float TopY) w)
    {
        w = default;
        var def = p.definition;
        if (def == null || string.IsNullOrEmpty(def.wreckModelAsset)) return false;
        if (!TryModelBounds(def.wreckModelAsset, out var wb)) return false;

        float scale;
        if (def.modelAbsoluteScale) scale = def.worldScale;
        else
        {
            if (!TryModelBounds(def.modelAsset, out var mb)) return false;
            float longest = Mathf.Max(mb.size.x, Mathf.Max(mb.size.y, mb.size.z));
            if (longest <= 0.0001f) return false;
            scale = (def.isPlayerSide ? 1.5f : def.size) / longest;
        }

        // `GameSpace.ToUnity` negates X, so the model's +x edge is the game-space -x one, and the
        // building sits `size / 2` low — the same offset the live model is given.
        float baseY = p.y - def.size / 2f;
        w = (string.IsNullOrEmpty(def.displayName) ? def.name : def.displayName,
             p.x - wb.max.x * scale, p.x - wb.min.x * scale,
             p.z + wb.min.z * scale, p.z + wb.max.z * scale,
             baseY + wb.max.y * scale);
        return true;
    }

    static readonly Dictionary<string, Bounds> ModelBoundsCache = new();

    /// <summary>
    /// A model's own bounds, read the way `LevelScenery.Normalize` reads them — off a LIVE
    /// instance, which is the only time a renderer reports them. Cached: the inspector calls this
    /// on every repaint.
    /// </summary>
    static bool TryModelBounds(string modelAsset, out Bounds bounds)
    {
        bounds = default;
        if (string.IsNullOrEmpty(modelAsset)) return false;

        string key = LevelScenery.ModelKey(modelAsset);
        if (ModelBoundsCache.TryGetValue(key, out bounds)) return true;

        var src = AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/Models/{key}.glb");
        if (src == null) return false;

        var inst = Object.Instantiate(src);
        var rs = inst.GetComponentsInChildren<MeshRenderer>();
        if (rs.Length == 0) { Object.DestroyImmediate(inst); return false; }
        var acc = rs[0].bounds;
        foreach (var r in rs) acc.Encapsulate(r.bounds);
        Object.DestroyImmediate(inst);

        ModelBoundsCache[key] = acc;
        bounds = acc;
        return true;
    }

    /// <summary>
    /// RULE 8: no ground unit stands inside a structure's collision box. Rob, on L6 after the
    /// hero pass: "heroes are behind the structure which makes them really tough to hit without
    /// firing at a steep angle." He was right, and the cause is geometric.
    ///
    /// A structure blocks as a box `hitWidth` wide — which is NOT the width of the building you
    /// see, and is what makes this invisible to the eye. L6's keep is drawn around x 6 and blocks
    /// from x 3.88; the heroes were placed at 4.3 "in front of the keep" and landed INSIDE it. To
    /// reach a unit standing in a box you must clear the box top and then fall to the ground
    /// within the same fraction of a unit, which is the near-vertical plunge Rob describes.
    ///
    /// **A structure's ANCHOR is not its EDGE.** That was the whole mistake, and the same one is
    /// available to anyone authoring a ground group near a building.
    ///
    /// Rule 7 cannot see this: it measures the distance and height to a unit and asks whether the
    /// roster has the power, with nothing in the model about what is IN THE WAY. All four levels
    /// passed all seven rules while three of them had heroes inside a wall.
    ///
    /// This is NOT only the hero pass's bug. Written to defend the hero fix, the check
    /// immediately indicted FOUR static riflemen the campaign already shipped — two on L9 inside
    /// the mountain bunker (by 0.46 and 0.74) and two on L10 inside the outpost (by 0.13 and
    /// 0.45), every one of them a unit the player could not hit without the same plunge. They
    /// were moved out with the heroes. The usual warning is to be suspicious when a new check
    /// indicts long-standing content; the answer there is to go and measure whether the content
    /// or the check is wrong, and here it was the content.
    ///
    /// ERROR, not Warn, and it is the only non-roster Error here. Rules 1-6 are framing
    /// judgements a level may bend for a reason it records in `designNotes`; this one says a unit
    /// the player is asked to kill cannot be hit, which is not a thing to bend. It failed the
    /// self-test suite hard before it moved here (2026-08-12) and it keeps failing hard.
    /// </summary>
    public static Finding CollisionBoxRule(LevelDefinitionSO level, GameState state)
    {
        int inside = 0, ground = 0, slowToClear = 0;
        float tightest = float.MaxValue;
        string worstWho = "", firstOffender = null, firstSlow = null;

        // The units on the field at turn 0, and then EVERY MID-BATTLE ARRIVAL. The first version
        // of this read `state.EnemyUnits` alone and so measured only turn 0 — boss phases and
        // reinforcement waves were invisible to it, and four levels shipped a static arrival
        // embedded in masonry because of it (L6 x2, L10, L11), plus L12's Sovereign in the gate's
        // shadow, which is how Rob found this on 2026-08-12. Arrivals are placed through the same
        // `LevelBuilder.BuildUnits` call `BattleTick.Spawn` uses, so the positions are the game's.
        foreach (var (label, units, deadByTrigger) in ArrivalSets(level, state))
        foreach (var u in units)
        {
            // A garrison stands ON its deck, above every box, and is meant to.
            if (u.StandingOnStructureId != null) continue;
            ground++;
            foreach (var st in state.Structures)
            {
                if (st.Definition == null || st.Definition.isPlayerSide) continue;
                // A boss phase triggers ON a structure's destruction, so that structure is
                // provably rubble by the time the phase spawns and cannot shadow anything. L12's
                // Sovereign spawns dead centre of the citadel it bursts out of; flagging that
                // would be asserting a state the game can never be in.
                if (deadByTrigger.Contains(st.Definition)) continue;
                // EXACTLY CollisionSystem's box: hitWidth when set, else size. No worldScale —
                // reading that in cost a wrong diagnosis before the numbers came out right.
                float halfW = (st.Definition.hasHitWidth ? st.Definition.hitWidth
                                                         : st.Definition.size) / 2f;
                float baseY = st.Y - st.Definition.size / 2f;
                float top = st.Definition.hasDeckY ? st.Definition.deckY : st.Definition.size;
                if (u.Y < baseY || u.Y > baseY + top) continue;   // above the box is fine
                float clear = Mathf.Abs(u.X - st.X) - halfW;
                if (clear < tightest)
                {
                    tightest = clear;
                    worstWho = $"{u.Definition.name} at x {u.X:F2} vs {st.Definition.name} " +
                               $"edge {st.X - halfW:F2}";
                }
                if (clear <= 0f)
                {
                    // AN ADVANCING UNIT IS EXEMPT ONLY IF IT ACTUALLY LEAVES, and "leaves" means
                    // ON ITS FIRST MARCH — that is the claim the exemption makes, so that is the
                    // claim it now has to meet. It used to wave through ANY unit with
                    // advancePerTurn > 0 on the reasoning that it walks out of the box; L11's
                    // wave, had it been given an advance instead of being moved, would have
                    // started 0.71 deep and needed THREE turns at 1.2 a turn to clear. Three turns
                    // of being unhittable is not "hittable soon", and it was invisible here.
                    //
                    // Advancing runs toward -X (AdvanceSystems.March), so the distance owed is to
                    // the box's PLAYER-SIDE edge. A unit that clears it in one turn costs the
                    // player nothing; anything slower is a static embed wearing a march.
                    //
                    // Opened as a known hole on 2026-08-12 when rule 8 gained arrivals, and closed
                    // the same day advancing squads went live — the day the exemption started
                    // carrying real weight.
                    float owed = u.X - (st.X - halfW);

                    // NO UNIT STARTS INSIDE A BUILDING — Rob, 2026-09-04: "i dont think we should
                    // have enemy units within the buildings... that doesn't make sense."
                    //
                    // This REPLACES the advancing exemption, which used to wave a unit through if
                    // it marched clear on its first turn and downgrade it to a Warning if it took
                    // longer. That split was about HITTABILITY — how many turns the player is
                    // asked to shoot at something they cannot reach — and on that axis it was
                    // right. But it answers the wrong question. A man standing inside masonry is
                    // not a pacing judgement, it is a man standing inside masonry, and no march
                    // he makes later changes what the player sees on the turn he arrives.
                    //
                    // Rule 9 still carries the hittability half, and carries it better: it FIRES
                    // THE SHOT and follows an advancer march by march. So nothing is lost by
                    // making this rule mean the simple thing its name says.
                    if (u.AdvancePerTurn > 0f)
                    {
                        slowToClear++;
                        if (firstSlow == null)
                            firstSlow = $"{label} {u.Definition.name} at x {u.X:F2} starts " +
                                        $"{owed:F2} inside {st.Definition.name} and advances " +
                                        $"{u.AdvancePerTurn:F2}/turn — " +
                                        $"{Mathf.CeilToInt(owed / u.AdvancePerTurn)} turns to clear";
                        continue;   // counted once, in the advancing bucket
                    }

                    inside++;
                    if (firstOffender == null)
                        firstOffender = $"{label} {u.Definition.name} at x {u.X:F2} inside " +
                                        $"{st.Definition.name} x[{st.X - halfW:F2}," +
                                        $"{st.X + halfW:F2}]";
                }
            }
        }

        // ground > 0 is part of the condition: over a fully garrisoned level this is vacuously
        // true, which is the empty-purse trap. A level with no ground units reports Ok and says
        // it measured none, rather than claiming a clean result it never looked for.
        if (ground == 0)
            return new Finding(Severity.Ok, "rule 8: no ground units to place — nothing measured");

        // An advancer inside a box is an ERROR now, not a Warning — see the note at the
        // exemption it replaced. It is reported on its own line so the message can say WHY it
        // was counted, which the generic line below cannot.
        if (inside == 0 && slowToClear > 0)
            return new Finding(Severity.Error,
                $"rule 8: {slowToClear} advancing unit(s) START INSIDE a collision box — " +
                $"{firstSlow}. Marching clear later does not help: they are standing in masonry " +
                "on the turn the player first sees them.");

        return new Finding(inside == 0 ? Severity.Ok : Severity.Error,
            $"rule 8: {inside} of {ground} ground unit(s) inside a structure's collision box " +
            $"(turn 0 + every boss/wave arrival), tightest clearance {tightest:F2} ({worstWho})" +
            (firstOffender == null ? "" : $" — first offender: {firstOffender}") +
            (inside == 0 ? ""
                : ". A structure's ANCHOR is not its EDGE — the box is hitWidth wide, not the "
                + "width of the building you see, and a unit inside it can only be hit by a "
                + "near-vertical plunge."));
    }

    /// <summary>
    /// Every set of enemy units rule 8 must judge: the turn-0 roster, then each boss phase's and
    /// each reinforcement wave's arrivals.
    ///
    /// Arrivals are built with the SAME call `BattleTick.Spawn` makes, so their positions are the
    /// ones the game will produce, not a re-derivation from anchors — a group's real spread comes
    /// from Formation, and re-deriving it here would make this a second source of truth about
    /// placement. The seed is the fixed one the rest of this file uses; formation jitter is small
    /// but nonzero, so a unit sitting a hair outside a box under this seed could be a hair inside
    /// under another. That is a property of the whole file, not of this rule.
    ///
    /// The third element is the set of structure DEFINITIONS a boss phase's trigger guarantees
    /// are already destroyed when it fires. A wave has no trigger, so it gets an empty set: the
    /// worst case for the player is every structure still standing, which is exactly the state a
    /// wave can land into.
    /// </summary>
    static List<(string Label, IReadOnlyList<UnitEntity> Units,
                 HashSet<StructureDefinitionSO> DeadByTrigger)>
        ArrivalSets(LevelDefinitionSO level, GameState state)
    {
        var sets = new List<(string, IReadOnlyList<UnitEntity>, HashSet<StructureDefinitionSO>)>
        {
            (Turn0, state.EnemyUnits, new HashSet<StructureDefinitionSO>())
        };

        for (int i = 0; i < level.bossPhases.Count; i++)
        {
            var phase = level.bossPhases[i];
            if (phase.spawnGroups == null || phase.spawnGroups.Count == 0) continue;

            var dead = new HashSet<StructureDefinitionSO>();
            if (phase.triggerStructureIds != null)
                foreach (var id in phase.triggerStructureIds)
                    foreach (var ls in level.structures)
                        if (ls.id == id && ls.definition != null) dead.Add(ls.definition);

            sets.Add(($"boss phase {i + 1}",
                      LevelBuilder.BuildUnits(level, phase.spawnGroups, false,
                                              BossProbeIdBase + i * 100, new System.Random(12345)),
                      dead));
        }

        for (int i = 0; i < level.reinforcementWaves.Count; i++)
        {
            var wave = level.reinforcementWaves[i];
            if (wave.spawnGroups == null || wave.spawnGroups.Count == 0) continue;
            sets.Add(($"wave turn {wave.arrivesOnTurn}",
                      LevelBuilder.BuildUnits(level, wave.spawnGroups, false,
                                              WaveProbeIdBase + i * 100, new System.Random(12345)),
                      new HashSet<StructureDefinitionSO>()));
        }

        return sets;
    }

    /// <summary>
    /// RULE 9: every enemy unit can actually be HIT by a real shot.
    ///
    /// Rule 7 asks whether the roster has the POWER to reach a unit — flat range at 45 degrees,
    /// turn 0 only, with nothing in the model about what is in the way. Rule 8 asks whether a
    /// unit is standing INSIDE a box. Neither asks the question a player asks, which is whether
    /// any throw they can make arrives. A unit behind a tall face passes both while being a
    /// two-point needle, or unreachable outright.
    ///
    /// THIS FIRES THE SHOT. A sweep of real trajectories through `TrajectoryPhysics.Step` at the
    /// tick's own dt, against `CollisionSystem`'s own boxes and `SweptCollision.UnitHitRadius`,
    /// and it counts how many of them land on the man. Zero is an ERROR: no drag the player can
    /// perform reaches him, which is not a rule a level may bend. A handful is a WARNING — the
    /// needle case, technically hittable and not a fight anyone can win on purpose.
    ///
    /// It judges TURN 0 AND EVERY ARRIVAL, through the same `ArrivalSets` rule 8 uses — and the
    /// `DeadByTrigger` half matters more here than anywhere: a boss bursts out of the structure
    /// whose destruction spawned it, so that structure is rubble and cannot shadow it. Counting
    /// it would condemn every boss in the game.
    ///
    /// WHY IT EXISTS. Offered twice and declined twice as a theoretical gap, then earned on
    /// 2026-09-04: L6's boss phase was played three times and NOTHING in it could be killed, at
    /// nine distinct powers spanning the whole envelope (45 to 88%). Difficulty does not look
    /// like that. `CLAUDE.md` had already written down the hole — "rule 7 still reads turn 0
    /// only; an arrival placed out of the ballistic envelope is caught by nothing" — so this is
    /// the instrument that says whether L6's Sovereign is hard or unhittable.
    /// </summary>
    public static Finding BallisticShadowRule(LevelDefinitionSO level, GameState state)
    {
        // The volley leaves the player line, not the origin. Same muzzle height the tick uses.
        var line = state.PlayerUnits;
        if (line == null || line.Count == 0)
            return new Finding(Severity.Ok, "rule 9: no player line to fire from");
        var origin = new Vector3(line.Average(u => u.X),
                                 line.Average(u => u.Y) + BattleTick.InfantryMuzzleY, 0f);

        const float Dt = 1f / 60f;
        float radiusSq = SweptCollision.UnitHitRadiusSq;

        var unreachable = new List<string>();
        var needles = new List<string>();
        int measured = 0, worstWindow = int.MaxValue;
        string worstWho = "none";

        foreach (var (label, units, deadByTrigger) in ArrivalSets(level, state))
        {
            // Boxes that are standing when THIS set is on the field.
            var boxes = new List<(float MinX, float MaxX, float MinY, float MaxY)>();
            foreach (var st in state.Structures)
            {
                var d = st.Definition;
                if (d == null || d.isPlayerSide || deadByTrigger.Contains(d)) continue;
                float halfW = (d.hasHitWidth ? d.hitWidth : d.size) / 2f;
                float baseY = st.Y - d.size / 2f;
                float top = d.hasDeckY ? d.deckY : d.size;
                boxes.Add((st.X - halfW, st.X + halfW, baseY, baseY + top));
            }

            // An ADVANCING unit gets a second chance from where its first march puts it, the
            // same exemption rule 8 makes and for the same reason: shadowed on arrival and
            // walking clear is a WARNING, permanently unhittable is an ERROR. Enemies close on
            // the player line, so a march is -x. Without this, L12's shield bearers — which
            // rule 8 already reports as clearing in two marches — read as four unkillable men.
            var probes = new List<(UnitEntity Unit, float X, int March)>();
            foreach (var u in units)
            {
                probes.Add((u, u.X, 0));
                for (int m = 1; m <= MaxMarchesProbed && u.AdvancePerTurn > 0f; m++)
                    probes.Add((u, u.X - u.AdvancePerTurn * m, m));
            }

            var hits = new Dictionary<int, int>();
            var clearsOnMarch = new Dictionary<int, int>();
            foreach (var u in units) { hits[u.Id] = 0; clearsOnMarch[u.Id] = -1; }

            // A coarse-but-real sweep of every drag the player can make. Angle at 2 degrees and
            // power at 1% is finer than a thumb can resolve on glass, so a window this cannot
            // find is not a window a player can hit.
            for (float deg = 15f; deg <= 85f; deg += 2f)
            {
                float rad = deg * Mathf.Deg2Rad;
                for (float power = 0.20f; power <= 1.0001f; power += 0.01f)
                {
                    float v = AimSystem.MaxAimMagnitude * power;
                    var pos = origin;
                    var vel = new Vector3(v * Mathf.Cos(rad), v * Mathf.Sin(rad), 0f);

                    for (int step = 0; step < 2000; step++)
                    {
                        var prev = pos;
                        TrajectoryPhysics.Step(ref pos, ref vel, Dt);
                        if (pos.y < 0f) break;
                        if (pos.x > 40f) break;

                        bool blocked = false;
                        foreach (var b in boxes)
                            if (pos.x >= b.MinX && pos.x <= b.MaxX
                                && pos.y >= b.MinY && pos.y <= b.MaxY) { blocked = true; break; }
                        if (blocked) break;

                        foreach (var pr in probes)
                        {
                            float dx = pos.x - pr.X;
                            float dy = pos.y - (pr.Unit.Y + BattleTick.InfantryMuzzleY);
                            if (dx * dx + dy * dy >= radiusSq) continue;
                            if (pr.March == 0) { hits[pr.Unit.Id]++; continue; }
                            int seen = clearsOnMarch[pr.Unit.Id];
                            if (seen < 0 || pr.March < seen) clearsOnMarch[pr.Unit.Id] = pr.March;
                        }
                    }
                }
            }

            foreach (var u in units)
            {
                measured++;
                int window = hits[u.Id];
                if (window < worstWindow)
                {
                    worstWindow = window;
                    worstWho = $"{u.Definition.name} at x {u.X:F1} y {u.Y:F1} ({label})";
                }
                string who = $"{u.Definition.name} x {u.X:F1} ({label})";
                if (window == 0)
                {
                    int clears = clearsOnMarch[u.Id];
                    if (clears > 0)
                        needles.Add($"{who} - shadowed on arrival, hittable after " +
                                    $"{clears} march{(clears == 1 ? "" : "es")}");
                    else
                        unreachable.Add(who);
                }
                else if (window <= NeedleWindow) needles.Add(who);
            }
        }

        if (unreachable.Count > 0)
            return new Finding(Severity.Error,
                $"rule 9: {unreachable.Count} of {measured} enemy unit(s) cannot be hit by ANY " +
                $"drag - {string.Join("; ", unreachable.Take(4))}" +
                (unreachable.Count > 4 ? " ..." : "") +
                ". No angle and no power reaches them, so the level cannot be finished.");

        if (needles.Count > 0)
            return new Finding(Severity.Warn,
                $"rule 9: {needles.Count} of {measured} enemy unit(s) are NEEDLES (<= " +
                $"{NeedleWindow} of the swept drags land) - {string.Join("; ", needles.Take(3))}" +
                (needles.Count > 3 ? " ..." : "") +
                ". Hittable, but not on purpose.");

        return new Finding(Severity.Ok,
            $"rule 9: every one of {measured} enemy unit(s) is reachable by a real drag " +
            $"(tightest window {worstWindow} on {worstWho})");
    }

    /// <summary>
    /// At or below this many landing drags out of the sweep, a unit is a NEEDLE rather than a
    /// target. The sweep sits far finer than a thumb, so a handful of hits across the whole
    /// angle-power space is not something a player can aim for on purpose.
    /// </summary>
    const int NeedleWindow = 12;

    /// <summary>
    /// How many marches an advancing unit is followed for before it is called unreachable.
    /// Shadowed-on-arrival-then-clear is rule 8's WARNING, taken deliberately; rule 9 does not
    /// get to overrule it by measuring the same unit one turn earlier and calling it an error.
    /// A unit that never clears in this many marches is not "walking out" by any reading.
    /// </summary>
    const int MaxMarchesProbed = 6;

    /// <summary>The label ArrivalSets gives the roster already on the field.</summary>
    const string Turn0 = "turn 0";

    // Ids are irrelevant to geometry; these only keep the probe's units from colliding with the
    // turn-0 roster's ids while a set is being measured.
    const int BossProbeIdBase = 900000;
    const int WaveProbeIdBase = 950000;

    static float Width(StructureDefinitionSO d)
        => d.hasHitWidth ? d.hitWidth : d.size;
}
