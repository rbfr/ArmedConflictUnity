using System.Linq;
using UnityEditor;
using UnityEngine;
using ArmedConflict.Data;

/// <summary>
/// Dumps what each campaign level actually IS — biome, structures, roster, and which of the
/// dynamism mechanics it exercises — so authoring decisions are made against the data rather than
/// against the level names.
///
/// Run: -batchmode -quit -executeMethod CampaignAudit.Dump
/// </summary>
public static class CampaignAudit
{
    public static void Dump()
    {
        var levels = AssetDatabase.FindAssets("t:LevelDefinitionSO")
            .Select(AssetDatabase.GUIDToAssetPath)
            .Select(AssetDatabase.LoadAssetAtPath<LevelDefinitionSO>)
            .Where(l => l != null && !l.isTestLevel)
            .OrderBy(l => l.levelNumber)
            .ToList();

        foreach (var l in levels)
        {
            int enemies = l.enemyGroups.Sum(g => g.count);
            int players = l.playerGroups.Sum(g => g.count);
            int garrisoned = l.enemyGroups
                .Where(g => !string.IsNullOrEmpty(g.standingOnStructureId)).Sum(g => g.count);
            var enemyKinds = l.enemyGroups.Where(g => g.definition != null)
                .Select(g => g.definition.name).Distinct().OrderBy(s => s);
            var structs = l.structures.Where(s => s.definition != null && !s.definition.isPlayerSide)
                .Select(s => s.definition.name);
            var advancing = l.enemyGroups.Where(g => g.advancePerTurn > 0f)
                .Select(g => $"{g.definition?.name}@{g.advancePerTurn}");

            var mechanics = new System.Collections.Generic.List<string>();
            if (l.windAccelZ != 0f) mechanics.Add($"wind {l.windAccelZ}");
            if (l.bossPhases.Count > 0) mechanics.Add($"boss x{l.bossPhases.Count}");
            if (l.reinforcementWaves.Count > 0) mechanics.Add($"waves x{l.reinforcementWaves.Count}");
            if (l.heliChance > 0f) mechanics.Add($"heli {l.heliChance}");
            if (advancing.Any()) mechanics.Add($"advance {string.Join("/", advancing)}");
            if (l.props.Count > 0) mechanics.Add($"props x{l.props.Count}");
            if (l.staticCamera) mechanics.Add("staticCam");

            Debug.Log($"[Audit] L{l.levelNumber} {l.displayName} | {l.background?.name} | " +
                      $"player {players} enemy {enemies} (garrison {garrisoned}) | " +
                      $"struct: {string.Join(",", structs)} | " +
                      $"enemy kinds: {string.Join(",", enemyKinds)} | " +
                      $"base {l.levelBase} budget {l.deployBudget} | " +
                      $"{(mechanics.Count == 0 ? "NO MECHANIC" : string.Join("; ", mechanics))}");
        }

        var backgrounds = AssetDatabase.FindAssets("t:BackgroundDefinitionSO")
            .Select(AssetDatabase.GUIDToAssetPath)
            .Select(AssetDatabase.LoadAssetAtPath<BackgroundDefinitionSO>)
            .Where(b => b != null).Select(b => b.name).OrderBy(s => s);
        Debug.Log($"[Audit] biomes available: {string.Join(", ", backgrounds)}");

        var units = AssetDatabase.FindAssets("t:UnitDefinitionSO")
            .Select(AssetDatabase.GUIDToAssetPath)
            .Select(AssetDatabase.LoadAssetAtPath<UnitDefinitionSO>)
            .Where(u => u != null).OrderBy(u => u.name);
        Debug.Log("[Audit] units: " + string.Join(", ",
            units.Select(u => $"{u.name}(hp{u.maxHp} dmg{u.damage}" +
                              $"{(u.meleeDamage > 0 ? $" melee{u.meleeDamage}" : "")})")));

        var strucs = AssetDatabase.FindAssets("t:StructureDefinitionSO")
            .Select(AssetDatabase.GUIDToAssetPath)
            .Select(AssetDatabase.LoadAssetAtPath<StructureDefinitionSO>)
            .Where(s => s != null && !s.isPlayerSide).OrderBy(s => s.name);
        Debug.Log("[Audit] enemy structures: " + string.Join(", ",
            strucs.Select(s => $"{s.name}(hp{s.maxHp} w{(s.hasHitWidth ? s.hitWidth : s.size):F1} " +
                               $"stand{s.standWidth:F2})")));
    }
}
