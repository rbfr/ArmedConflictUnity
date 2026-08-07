using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using ArmedConflict.Data;
using ArmedConflict.Game;

/// <summary>
/// Checks levels against the seven composition rules in LEVEL_AUTHORING.md.
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
                Debug.Log($"[Composition] L{level.levelNumber} {level.displayName}: all seven rules ok");
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
        // running the check at all. Errors are the locked roster scale, which is not negotiable.
        if (errors > 0 && Application.isBatchMode) EditorApplication.Exit(1);
    }

    /// <summary>
    /// The seven rules. A half-authored level legitimately fails to build (no background, a null
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

        // --- the locked roster scale: not a composition rule, but it bounds every level ---
        int playerTotal = level.playerGroups.Sum(g => g.count);
        foreach (var (side, n) in new[] { ("player", playerTotal), ("enemy", enemyTotal) })
            if (n < 7 || n > 30)
                findings.Add(new Finding(Severity.Error,
                    $"{side} roster is {n} — GAME_DESIGN_LOCKS.md locks 7-30 per side, " +
                    "garrisoned units included"));

        return findings;
    }

    static float Width(StructureDefinitionSO d)
        => d.hasHitWidth ? d.hitWidth : d.size;
}
