using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using ArmedConflict.Data;

/// <summary>
/// Regenerates the eight roster/grouping SANDBOX levels — the rigs that vary squad count and
/// roster size against each other, with no structures, to measure formation and framing.
///
/// Run:  -batchmode -quit -executeMethod SandboxLevels.Generate
///       or Tools > ArmedConflict > Regenerate Sandbox Levels
///
/// These are the only levels the project has ever GENERATED rather than authored: their Kotlin
/// original was a helper function (`rosterSandbox()`) the exporter could not parse, so the
/// importer rebuilt them itself as a side effect of every import. That side effect is why the
/// level list came from two places at once — the exact hazard `PortSelfTest`'s contiguity check
/// exists to catch — and it is now a command you run on purpose instead.
///
/// It reads the ScriptableObjects, which are the source of truth as of 2026-08-06. In particular
/// it PRESERVES each rig's existing levelNumber rather than deriving one from `levelOrder`: the
/// numbering belongs to the level list now, not to a JSON file exported from a retired repo.
/// </summary>
public static class SandboxLevels
{
    const string LevelDir = "Assets/GameData/Levels";

    /// <summary>(asset, label, players, enemies, player squads, enemy squads)</summary>
    static readonly (string Name, string Label, int Pc, int Ec, int Ps, int Es)[] Specs =
    {
        ("LevelRosterSmall",     "Roster S v S",       6,  6, 2, 2),
        ("LevelRosterMedium",    "Roster M v M",      14, 14, 3, 3),
        ("LevelRosterLarge",     "Roster L v L",      26, 26, 5, 5),
        ("LevelRosterSmallVsLg", "Roster S v L",       6, 26, 2, 5),
        ("LevelRosterLargeVsSm", "Roster L v S",      26,  6, 5, 2),
        ("LevelGroupingOne",     "Grouping 1 squad",  14, 14, 1, 1),
        ("LevelGroupingTwo",     "Grouping 2 squads", 14, 14, 2, 2),
        ("LevelGroupingSeven",   "Grouping 7 squads", 14, 14, 7, 7),
    };

    static readonly string[] PlayerCycle =
        { "Rifleman", "Rifleman", "MachineGunner", "Rifleman", "Grenadier" };
    static readonly string[] EnemyCycle =
        { "EnemyRifleman", "EnemyRifleman", "EnemyMachineGunner", "EnemyRifleman", "EnemyGrenadier" };

    [MenuItem("Tools/ArmedConflict/Regenerate Sandbox Levels")]
    public static void Generate()
    {
        var units = LoadAll<UnitDefinitionSO>("Assets/GameData/Units");
        var structures = LoadAll<StructureDefinitionSO>("Assets/GameData/Structures");
        var backgrounds = LoadAll<BackgroundDefinitionSO>("Assets/GameData/Backgrounds");

        int built = 0;
        foreach (var s in Specs)
        {
            string path = $"{LevelDir}/{s.Name}.asset";
            var so = AssetDatabase.LoadAssetAtPath<LevelDefinitionSO>(path);
            if (so == null)
            {
                // Deliberately NOT created here. A sandbox that does not exist has no place in
                // the level order, and inventing a levelNumber for it is exactly the second
                // source of truth this command was split out to remove.
                Debug.LogWarning($"[SandboxLevels] {s.Name} does not exist — skipped. Create the " +
                                 "asset and give it a levelNumber first.");
                continue;
            }

            so.displayName = $"TEST — {s.Label}";
            so.levelGoal = $"Sandbox: {s.Pc} v {s.Ec}, {s.Ps} v {s.Es} squads";
            so.isTestLevel = true;
            so.levelBase = 0;
            // Winter: flat bright ground reads massed units best.
            so.background = backgrounds.GetValueOrDefault("Winter");
            // No enemy structures ON PURPOSE — a dominant structure would drive the
            // scout/resolve framing and mask the thing being measured.
            so.structures = new List<StructurePlacement>
            {
                new()
                {
                    id = "player_tank",
                    definition = structures.GetValueOrDefault("PlayerTank"),
                    x = -10.5f, y = 0f, z = 0f, hpScale = 1f, standWidth = -1f,
                },
            };
            so.playerGroups = Groups(s.Pc, s.Ps, -7.5f, PlayerCycle, units);
            so.enemyGroups = Groups(s.Ec, s.Es, 6.5f, EnemyCycle, units);
            so.props = new List<PropPlacement>();
            EditorUtility.SetDirty(so);
            built++;
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log($"[SandboxLevels] regenerated {built} of {Specs.Length} sandbox levels " +
                  "(levelNumber and id left untouched)");
    }

    /// <summary>
    /// Splits count into squads whose sizes differ by at most one, anchored around centerX at
    /// 1.7 spacing. Port of the Kotlin `sandboxGroups()`.
    /// </summary>
    static List<EnemyGroup> Groups(int count, int squads, float centerX, string[] cycle,
                                   Dictionary<string, UnitDefinitionSO> units)
    {
        const float squadSpacing = 1.7f;
        int n = Mathf.Clamp(squads, 1, count);
        var result = new List<EnemyGroup>();
        for (int i = 0; i < n; i++)
        {
            int size = count / n + (i < count % n ? 1 : 0);
            result.Add(new EnemyGroup
            {
                definition = units.GetValueOrDefault(cycle[i % cycle.Length]),
                count = size,
                anchorX = centerX + (i - (n - 1) / 2f) * squadSpacing,
            });
        }
        return result;
    }

    static Dictionary<string, T> LoadAll<T>(string folder) where T : ScriptableObject
        => AssetDatabase.FindAssets($"t:{typeof(T).Name}", new[] { folder })
            .Select(AssetDatabase.GUIDToAssetPath)
            .Select(AssetDatabase.LoadAssetAtPath<T>)
            .Where(a => a != null)
            .GroupBy(a => a.name)
            .ToDictionary(g => g.Key, g => g.First());
}
