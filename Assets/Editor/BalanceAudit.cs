using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using ArmedConflict.Data;
using ArmedConflict.Game;

/// <summary>
/// The headless half of the Phase E BALANCE AUDIT (PRODUCT_DIRECTION.md: "a level that breaks
/// under a LEGAL loadout is a product bug").
///
///     -batchmode -quit -executeMethod BalanceAudit.Report
///
/// WHAT THIS CAN AND CANNOT DO. It cannot measure difficulty — that needs a human drag, because
/// `Auto` never misses and is structure-blind. What it CAN do is settle the half of the audit
/// that is arithmetic and therefore does not need a device at all:
///
///   1. REACH. Victory is "every enemy UNIT dead" (TurnFlow.ResolvePhase), so an enemy the
///      player's maximum shot cannot physically arrive at makes the level unwinnable, at any
///      skill level, forever. That is a pure ballistics question against AimSystem.MaxAimMagnitude
///      and it is checkable to the metre.
///   2. THE VOLLEY RACE, at EQUAL accuracy. Both sides do fixed damage per volley into a fixed HP
///      pool, so "how many clean volleys does each side need to wipe the other" is exact. The
///      only unknown is accuracy, and holding it EQUAL between the sides removes it: a level
///      where the player needs more clean volleys than the enemy does is one that demands the
///      player out-shoot an AI that solves the arc exactly. That is a flag, not a verdict.
///   3. THE MELEE CLOCK. An advancing group arrives on a schedule the level author fixed
///      (advancePerTurn), so the turns available before contact are known.
///
/// Everything it reports is a REACHABILITY or PACE fact. A level that passes here can still be
/// too hard, and that is what the device pass is for — but a level that FAILS here cannot be
/// rescued by any amount of skill, and finding those costs one headless run instead of twelve
/// device sessions.
///
/// Run against BOTH extremes of the legal loadout space, because the product bug is defined over
/// legal loadouts, not over the default one.
/// </summary>
public static class BalanceAudit
{
    /// <summary>
    /// Required power above this and a competent shooter has almost no margin: the reachable band
    /// has collapsed onto the maximum drag, so every miss is short and the level reads as "my
    /// shots pass through them". Under 1.0 it is not a break, so it is a warning.
    /// </summary>
    const float PowerHeadroomWarn = 0.92f;

    /// <summary>Muzzle offset FireVolley applies to every infantry round.</summary>
    const float MuzzleY = 0.35f;

    /// <summary>
    /// How many times the player's clean-volley count may exceed the enemy's before the level is
    /// worth a device drag.
    ///
    /// Not 1.0, which is the arithmetic break-even, because the two sides are NOT symmetric in the
    /// player's disfavour only: the player also has the tank shell, splash, and the fact that
    /// every kill permanently removes damage from the enemy's next volley. Below 2x those cover
    /// the gap; above it the level is asking the player to out-shoot an AI that solves the arc
    /// exactly, and that is a claim to test rather than to assume. At 1.0 this warned on 21 of 24
    /// squads, which is an instrument that discriminates nothing.
    /// </summary>
    const float RaceRatioWarn = 2f;

    public static void Report()
    {
        var roster = AssetDatabase.LoadAssetAtPath<RosterDefinitionSO>("Assets/GameData/Roster.asset");
        if (roster == null) { Debug.LogError("[Balance] no Roster.asset"); EditorApplication.Exit(1); return; }

        var levels = AssetDatabase.FindAssets("t:LevelDefinitionSO")
            .Select(AssetDatabase.GUIDToAssetPath)
            .Select(AssetDatabase.LoadAssetAtPath<LevelDefinitionSO>)
            .Where(l => l != null && !l.isTestLevel)
            .OrderBy(l => l.levelNumber)
            .ToList();

        ProgressStore.ResetAll();
        System.Func<string, bool> unlocked = ProgressStore.IsUnitUnlocked;

        Debug.Log($"[Balance] max shot: v={AimSystem.MaxAimMagnitude}, g={TrajectoryPhysics.Gravity}, " +
                  $"flat range {AimSystem.MaxRange45:F2} units. {levels.Count} campaign levels.");

        int errors = 0, warns = 0;
        foreach (var level in levels)
        {
            foreach (var squad in Squads(level, roster, unlocked))
            {
                var findings = Audit(level, squad.Picks, squad.Label);
                foreach (var f in findings)
                {
                    if (f.Level == LevelComposition.Severity.Error) { errors++; Debug.LogError(f.Text); }
                    else if (f.Level == LevelComposition.Severity.Warn) { warns++; Debug.LogWarning(f.Text); }
                    else Debug.Log(f.Text);
                }
            }
        }

        Debug.Log($"[Balance] done — {errors} errors, {warns} warnings across {levels.Count} levels. " +
                  "Reach errors are product bugs; the rest are candidates for a device drag.");
        if (errors > 0) EditorApplication.Exit(1);
    }

    struct Squad { public string Label; public List<Pick> Picks; }

    /// <summary>
    /// The two ends of the legal loadout space. The default is what a player who taps straight
    /// through fights with, and the all-dearest squad is the strongest legal one — the audit has
    /// to hold at both, since the product rule is written over LEGAL loadouts.
    /// </summary>
    static IEnumerable<Squad> Squads(LevelDefinitionSO level, RosterDefinitionSO roster,
                                     System.Func<string, bool> unlocked)
    {
        yield return new Squad { Label = "stock", Picks = Loadout.Default(level, roster, unlocked) };

        var dearest = roster.slots.OrderByDescending(s => s.pointCost).First();
        int many = Mathf.Min(Loadout.Slots(level), Loadout.Budget(level) / Mathf.Max(dearest.pointCost, 1));
        if (many > 0)
            yield return new Squad
            {
                Label = $"all-{dearest.unit.name}",
                Picks = new List<Pick> { new(dearest.unit, many) },
            };
    }

    static List<LevelComposition.Finding> Audit(LevelDefinitionSO level, List<Pick> picks, string label)
    {
        var outp = new List<LevelComposition.Finding>();
        // Fixed seed: formation jitter must not make two runs of the audit disagree.
        var state = LevelBuilder.BuildInitialState(level, 1, 12, new System.Random(9),
                                                   playerGroupsOverride: Loadout.ToPlayerGroups(level, picks));
        string tag = $"[Balance] L{level.levelNumber} {level.displayName} ({label})";

        if (state.PlayerUnits.Count == 0 || state.EnemyUnits.Count == 0)
        {
            outp.Add(new LevelComposition.Finding(LevelComposition.Severity.Error,
                $"{tag}: builds with {state.PlayerUnits.Count} player / {state.EnemyUnits.Count} " +
                "enemy units — nothing to audit"));
            return outp;
        }

        // --- 1. REACH -------------------------------------------------------------------------
        // The volley leaves EVERY player unit at one velocity, so the line's front rank sets what
        // the squad can reach at all and the back rank sets what the WHOLE squad can reach. The
        // enemy is at +x, so "front" is the largest player x.
        float frontX = state.PlayerUnits.Max(u => u.X);
        float backX = state.PlayerUnits.Min(u => u.X);
        float originY = state.PlayerUnits.Average(u => u.Y) + MuzzleY;

        var targets = state.EnemyUnits
            .Select(u => (Name: u.Definition != null ? u.Definition.id : "unit", u.X, u.Y, Unit: true))
            .Concat(state.Structures.Where(s => !s.Definition.isPlayerSide)
                .Select(s => (Name: s.Definition.id, s.X, s.Y, Unit: false)))
            .ToList();

        var reachRule = ReachRule(state);
        outp.Add(new LevelComposition.Finding(reachRule.Level, $"{tag}: {reachRule.Text}"));

        // Structures do not gate victory, so an unreachable one is a warning: it strands a boss
        // trigger and any garrison that can only be cleared by razing what it stands on.
        var structs = targets.Where(t => !t.Unit).ToList();
        if (structs.Count > 0)
        {
            var worstStruct = structs.OrderByDescending(t => RequiredPower(frontX, originY, t.X, t.Y)).First();
            float p = RequiredPower(frontX, originY, worstStruct.X, worstStruct.Y);
            if (p > 1f)
                outp.Add(new LevelComposition.Finding(LevelComposition.Severity.Warn,
                    $"{tag}: structure '{worstStruct.Name}' is out of reach ({p * 100f:F0}% power " +
                    "needed) — anything gated on razing it, including a boss phase, can never fire."));
        }

        // --- 2. THE VOLLEY RACE, at equal accuracy -------------------------------------------
        int playerVolley = state.PlayerUnits.Sum(u => u.Definition != null ? u.Definition.damage : 8);
        int enemyVolley = state.EnemyUnits.Sum(u => u.Definition != null ? u.Definition.damage : 8);
        int playerHp = state.PlayerUnits.Sum(u => u.Hp);
        int enemyHp = state.EnemyUnits.Sum(u => u.Hp);

        // THERE ARE TWO WAYS TO WIN AND THE CHEAPER ONE IS WHAT THE LEVEL COSTS. A garrisoned
        // unit dies the instant the structure under it is destroyed, so on a level that garrisons
        // most of its roster (which the composition rules REQUIRE) razing the buildings can clear
        // the field for a fraction of the bodies' HP. Counting only route A rated an
        // all-RocketTrooper squad at 20+ volleys and therefore hopeless — while the rocket
        // trooper's whole design is a 6x structure multiplier, i.e. it is built to take route B.
        float shootBodies = enemyHp / Mathf.Max(playerVolley, 1f);

        float structMult = picks.Sum(p => p.Count * (p.Unit != null ? p.Unit.structureDamageMultiplier : 1f))
                         / Mathf.Max(picks.Sum(p => p.Count), 1);
        var garrisonedOn = new HashSet<int>(state.EnemyUnits
            .Where(u => u.StandingOnStructureId != null).Select(u => u.StandingOnStructureId.Value));
        float razeBuildings =
            state.Structures.Where(s => garrisonedOn.Contains(s.Id)).Sum(s => s.Hp)
                / Mathf.Max(playerVolley * structMult, 1f)
          + state.EnemyUnits.Where(u => u.StandingOnStructureId == null).Sum(u => u.Hp)
                / Mathf.Max(playerVolley, 1f);

        float playerVolleys = Mathf.Min(shootBodies, razeBuildings);
        string route = razeBuildings < shootBodies ? "raze" : "shoot";
        float enemyVolleys = playerHp / Mathf.Max(enemyVolley, 1f);
        float ratio = playerVolleys / Mathf.Max(enemyVolleys, 0.01f);

        string race = $"player needs {playerVolleys:F1} clean volleys via {route} " +
                      $"(shoot {shootBodies:F1} / raze {razeBuildings:F1}), enemy needs " +
                      $"{enemyVolleys:F1} ({playerHp} hp / {enemyVolley} per volley) — " +
                      $"ratio {ratio:F1}x";

        if (ratio > RaceRatioWarn)
            outp.Add(new LevelComposition.Finding(LevelComposition.Severity.Warn,
                $"{tag}: the player is {ratio:F1}x BEHIND the race — {race}. Past {RaceRatioWarn:F0}x " +
                "the tank shell and per-turn attrition stop covering the gap; drag this one."));
        else
            outp.Add(new LevelComposition.Finding(LevelComposition.Severity.Ok, $"{tag}: race ok — {race}"));

        // --- 3. THE MELEE CLOCK ---------------------------------------------------------------
        var advancers = state.EnemyUnits.Where(u => u.AdvancePerTurn > 0f).ToList();
        if (advancers.Count > 0)
        {
            float soonest = advancers.Min(u => (u.X - frontX) / u.AdvancePerTurn);
            string clock = $"{advancers.Count} advancing, first contact in {soonest:F1} turns " +
                           $"against {playerVolleys:F1} volleys to clear";
            if (soonest < playerVolleys)
                outp.Add(new LevelComposition.Finding(LevelComposition.Severity.Warn,
                    $"{tag}: melee arrives before the field can be cleared — {clock}"));
            else
                outp.Add(new LevelComposition.Finding(LevelComposition.Severity.Ok, $"{tag}: melee clock ok — {clock}"));
        }

        return outp;
    }

    /// <summary>
    /// RULE 7 — REACH. The seventh composition rule, shared with LevelComposition so the level
    /// inspector and this audit cannot disagree about whether a level is physically playable.
    ///
    /// It exists because the other six missed a level that was unwinnable. They measure framing
    /// and HORIZONTAL separation, and the power budget is spent on HEIGHT: L7 Barracks Line
    /// passed all six with a garrison that needed 100% power from the front rank and 108% from
    /// the back, i.e. the tank crew's rounds could never arrive at all.
    ///
    /// Both ranks are reported because a volley leaves EVERY player unit at one velocity. The
    /// front rank sets what the squad can reach at all; the back rank sets what the WHOLE squad
    /// can reach, and a back rank over 100% is throwing part of every volley away for the length
    /// of the battle.
    /// </summary>
    public static LevelComposition.Finding ReachRule(GameState state)
    {
        if (state.PlayerUnits.Count == 0 || state.EnemyUnits.Count == 0)
            return new LevelComposition.Finding(LevelComposition.Severity.Ok,
                "rule 7: reach not measurable — needs units on both sides");

        float frontX = state.PlayerUnits.Max(u => u.X);
        float backX = state.PlayerUnits.Min(u => u.X);
        float originY = state.PlayerUnits.Average(u => u.Y) + MuzzleY;

        var worst = state.EnemyUnits
            .OrderByDescending(u => RequiredPower(frontX, originY, u.X, u.Y)).First();
        float front = RequiredPower(frontX, originY, worst.X, worst.Y);
        float back = RequiredPower(backX, originY, worst.X, worst.Y);
        string name = worst.Definition != null ? worst.Definition.id : "unit";

        string text = $"rule 7: deepest enemy UNIT '{name}' at dx {worst.X - frontX:F1} " +
                      $"dy {worst.Y - originY:F1} needs {front * 100f:F0}% power " +
                      $"({back * 100f:F0}% from the back rank)";

        if (front > 1f)
            return new LevelComposition.Finding(LevelComposition.Severity.Error,
                text + ". UNWINNABLE — victory is every enemy unit dead, and this one cannot be " +
                "reached at maximum power from any point on the player line.");
        if (back > 1f)
            return new LevelComposition.Finding(LevelComposition.Severity.Warn,
                text + ". The BACK RANK cannot reach it, so part of every volley is wasted for " +
                "the whole battle.");
        if (front > PowerHeadroomWarn)
            return new LevelComposition.Finding(LevelComposition.Severity.Warn,
                text + ". No aim headroom — every miss will be short, which is the shape that " +
                "reads as 'my shots pass through them'.");
        return new LevelComposition.Finding(LevelComposition.Severity.Ok, text);
    }

    /// <summary>
    /// Fraction of maximum power needed to put a round on (targetX, targetY) from
    /// (originX, originY), or >1 when no arc reaches it.
    ///
    /// The envelope, not the 45-degree range: the minimum launch speed that can reach a point at
    /// (dx, dy) is v^2 = g * (dy + sqrt(dx^2 + dy^2)), which is the standard result for the
    /// optimum angle with a height difference. AimSystem.MaxRange45 is that same formula at
    /// dy = 0, and using it alone would call a garrison on a fortress roof reachable when it is
    /// not — height is exactly what the campaign's hardest shots spend their power on.
    /// </summary>
    static float RequiredPower(float originX, float originY, float targetX, float targetY)
    {
        float dx = Mathf.Abs(targetX - originX);
        float dy = targetY - originY;
        float vSq = TrajectoryPhysics.Gravity * (dy + Mathf.Sqrt(dx * dx + dy * dy));
        if (vSq <= 0f) return 0f;
        return Mathf.Sqrt(vSq) / AimSystem.MaxAimMagnitude;
    }
}
