using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using ArmedConflict.Data;
using ArmedConflict.Game;

/// <summary>
/// Checks levels against the eight composition rules in LEVEL_AUTHORING.md.
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
    const float SeparationMax = 18f;

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
                Debug.Log($"[Composition] L{level.levelNumber} {level.displayName}: all eight rules ok");
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
    /// The eight rules. A half-authored level legitimately fails to build (no background, a null
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

        // --- rules 4 and 6: separation, measured TANK -> DOMINANT STRUCTURE ---
        var tank = level.structures.FirstOrDefault(s => s.definition != null
                                                     && s.definition.isPlayerSide);
        var dominant = enemyStructures
            .OrderByDescending(s => Width(s.definition))
            .FirstOrDefault();
        if (tank == null || dominant == null)
        {
            findings.Add(new Finding(Severity.Ok,
                "rules 4/6: separation not measurable — needs a player-side structure and at " +
                "least one enemy structure"));
        }
        else
        {
            float separation = Mathf.Abs(dominant.x - tank.x);
            bool ok = separation >= SeparationMin && separation <= SeparationMax;
            findings.Add(new Finding(ok ? Severity.Ok : Severity.Warn,
                $"rules 4/6: separation {separation:F1}, tank -> {dominant.definition.name} " +
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
        int inside = 0, ground = 0;
        float tightest = float.MaxValue;
        string worstWho = "", firstOffender = null;

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
            // An ADVANCING unit walks out of the box on its first move, so starting inside one
            // costs it nothing. L9's shield bearers start 0.01 inside the bunker purely on
            // formation jitter and are hittable from turn one. This is a semantic exemption, not
            // a tolerance — a static unit gets no such reprieve.
            if (u.AdvancePerTurn > 0f) continue;
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

    /// <summary>The label ArrivalSets gives the roster already on the field.</summary>
    const string Turn0 = "turn 0";

    // Ids are irrelevant to geometry; these only keep the probe's units from colliding with the
    // turn-0 roster's ids while a set is being measured.
    const int BossProbeIdBase = 900000;
    const int WaveProbeIdBase = 950000;

    static float Width(StructureDefinitionSO d)
        => d.hasHitWidth ? d.hitWidth : d.size;
}
