using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using ArmedConflict.Data;

/// <summary>
/// Splits every GARRISONED enemy group into more, weaker bodies, so a deck reads as a crowd
/// instead of a clump on a wide roof. See UNIT_VARIETY_DESIGN.md, "Tier 2.2, part four".
///
/// THE INVARIANT IS CONSTANT OUTPUT: a group's total HP and its total damage per volley are
/// unchanged by the split, so no level Rob has signed off gets harder or easier. That is why the
/// factors below are the ones they are — each divides its class's HP and damage EXACTLY, and a
/// factor that left a remainder would silently retune a level.
///
/// The variants are separate definitions rather than a per-entity stat override because
/// UnitEntity reads its stats off Definition in eight places, and "grep for EVERY READER" is a
/// trap this project has already paid for twice. They share the parent's modelAsset, which is
/// what BattleRunner.UnitClassKey keys on — so they reuse the same prefab and the same auto-sized
/// slot pool, and NO SCENE REBUILD IS NEEDED.
///
/// Idempotent and safe to re-run: a group already pointing at a crowd variant is left alone.
///
///     -batchmode -quit -executeMethod CrowdSplit.Apply
/// </summary>
public static class CrowdSplit
{
    /// <summary>
    /// parent definition name -> how many bodies it becomes. A class not listed here is NOT
    /// split, and the omission is as deliberate as the numbers.
    ///
    /// TWO CONSTRAINTS PICK THESE, not taste:
    ///  - the factor must divide BOTH hp and damage exactly, or the split silently retunes the
    ///    level it was supposed to leave alone;
    ///  - no crowd body may drop to or below the incendiary burn's 8 damage, or the burn stops
    ///    CHIPPING and starts one-shotting. That property is deliberately maintained (see
    ///    HANDOVER's open item on burnDamage) and PortSelfTest anchors it to the roster's
    ///    frailest unit, which is how the first version of this table was caught: Sniper at x2
    ///    and Grenadier at x3 both land on exactly 8 hp.
    ///
    /// So the SNIPER is not split at all — 16 hp only halves to 8 — and it loses nothing, because
    /// its two garrisons sit on 1.50 decks where the split bought a second rank rather than any
    /// width. The GRENADIER takes x2 (12 hp) instead of the x3 its deck would have preferred.
    /// </summary>
    static readonly Dictionary<string, int> Factors = new()
    {
        { "EnemyRifleman",      2 },   // 32/8 -> 2 x 16/4
        { "EnemyMachineGunner", 2 },   // 40/4 -> 2 x 20/2
        { "EnemyGrenadier",     2 },   // 24/6 -> 2 x 12/3
    };

    /// <summary>
    /// The incendiary burn, which a crowd body must survive. Named here rather than compared
    /// against a literal so the two cannot drift apart.
    /// </summary>
    const int BurnDamage = 8;

    const string CrowdSuffix = "Crowd";

    /// <summary>GAME_DESIGN_LOCKS.md, garrisoned units included. Checked by LevelComposition.</summary>
    const int RosterMax = 30;
    const string UnitDir = "Assets/GameData/Units";

    public static void Apply()
    {
        int made = 0, repointed = 0, skipped = 0;

        // --- the variants -------------------------------------------------------------
        var crowdOf = new Dictionary<UnitDefinitionSO, UnitDefinitionSO>();
        foreach (var (parentName, factor) in Factors.Select(kv => (kv.Key, kv.Value)))
        {
            var parent = AssetDatabase.LoadAssetAtPath<UnitDefinitionSO>(
                $"{UnitDir}/{parentName}.asset");
            if (parent == null) { Debug.LogError($"[CrowdSplit] no {parentName}"); continue; }

            if (parent.maxHp % factor != 0 || parent.damage % factor != 0)
            {
                Debug.LogError($"[CrowdSplit] {parentName} hp {parent.maxHp} / dmg " +
                               $"{parent.damage} do not both divide by {factor} — that would " +
                               "retune the level. Pick a factor that divides exactly.");
                continue;
            }
            if (parent.maxHp / factor <= BurnDamage)
            {
                Debug.LogError($"[CrowdSplit] {parentName} at x{factor} is " +
                               $"{parent.maxHp / factor} hp, at or under the incendiary burn's " +
                               $"{BurnDamage} — the burn would one-shot it instead of chipping. " +
                               "Use a smaller factor, or do not split this class.");
                continue;
            }

            string path = $"{UnitDir}/{parentName}{CrowdSuffix}.asset";
            var crowd = AssetDatabase.LoadAssetAtPath<UnitDefinitionSO>(path);
            bool fresh = crowd == null;
            if (fresh) crowd = ScriptableObject.CreateInstance<UnitDefinitionSO>();

            // Everything except the two stats the split divides — same model, same gun, same
            // projectile type and count, same multipliers, so the only difference is granularity.
            crowd.id = parent.id + "_crowd";
            crowd.displayName = parent.displayName;
            crowd.modelAsset = parent.modelAsset;
            crowd.gunModelAsset = parent.gunModelAsset;
            crowd.maxHp = parent.maxHp / factor;
            crowd.damage = parent.damage / factor;
            crowd.projectileType = parent.projectileType;
            crowd.bulletVariant = parent.bulletVariant;
            crowd.projectilesPerVolley = parent.projectilesPerVolley;
            crowd.splashRadius = parent.splashRadius;
            crowd.structureDamageMultiplier = parent.structureDamageMultiplier;
            crowd.meleeDamage = parent.meleeDamage;
            crowd.renderScale = parent.renderScale;

            if (fresh) { AssetDatabase.CreateAsset(crowd, path); made++; }
            else EditorUtility.SetDirty(crowd);
            crowdOf[parent] = crowd;
        }

        // --- the garrisons ------------------------------------------------------------
        var levels = AssetDatabase.FindAssets("t:LevelDefinitionSO")
            .Select(AssetDatabase.GUIDToAssetPath)
            .Select(AssetDatabase.LoadAssetAtPath<LevelDefinitionSO>)
            .Where(l => l != null && !l.isTestLevel)
            .OrderBy(l => l.levelNumber);

        foreach (var level in levels)
        {
            bool dirty = false;
            int total = level.enemyGroups.Sum(g => g.count);

            // THE 7-30 ROSTER SCALE IS A LOCK, not a rule this may bend — GAME_DESIGN_LOCKS.md,
            // garrisoned units included. L12 is the level that proved it: splitting both its
            // garrisons takes it 18 -> 31, one over. So the groups are taken WORST-FILLED FIRST
            // (largest group on its deck = the biggest clump, and the one a player actually
            // notices), and a split that would breach the lock is skipped rather than applied.
            foreach (var g in level.enemyGroups
                         .Where(g => !string.IsNullOrEmpty(g.standingOnStructureId)
                                     && g.definition != null)
                         .OrderByDescending(g => g.count)
                         .ToList())
            {
                if (!crowdOf.TryGetValue(g.definition, out var crowd)) { skipped++; continue; }

                int factor = Factors[g.definition.name];
                int added = g.count * (factor - 1);
                if (total + added > RosterMax)
                {
                    Debug.LogWarning(
                        $"[CrowdSplit] L{level.levelNumber} {g.standingOnStructureId}: " +
                        $"{g.definition.name} x{g.count} NOT split — x{factor} would take the " +
                        $"roster to {total + added}, past the locked {RosterMax}");
                    skipped++;
                    continue;
                }

                Debug.Log($"[CrowdSplit] L{level.levelNumber} {g.standingOnStructureId}: " +
                          $"{g.definition.name} x{g.count} -> {crowd.name} x{g.count * factor}");
                g.definition = crowd;
                g.count *= factor;
                total += added;
                repointed++;
                dirty = true;
            }
            if (dirty) EditorUtility.SetDirty(level);
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log($"[CrowdSplit] {made} variant(s) created, {repointed} garrison group(s) split, " +
                  $"{skipped} left alone (already crowd, or a class with no factor)");
    }
}
