using System.Linq;
using UnityEditor;
using UnityEngine;
using ArmedConflict.Data;

/// <summary>
/// Creates Assets/GameData/Roster.asset if it is missing.
///
/// Run: -batchmode -quit -executeMethod RosterSetup.Create
///      or Tools > ArmedConflict > Create Roster (if missing)
///
/// NON-DESTRUCTIVE by design — it refuses to touch an existing roster. The roster is authored
/// data now, like every level, and a setup script that overwrote it would be the same hazard
/// LegacyKotlinImport was guarded against. Tune prices in the inspector, not here.
/// </summary>
public static class RosterSetup
{
    const string Path = "Assets/GameData/Roster.asset";

    /// <summary>(unit asset, points to field, coins to unlock, one line the player can say back)</summary>
    static readonly (string Unit, int Points, int Coins, string Line)[] Seed =
    {
        ("Rifleman", 1, 0,
            "Cheap and steady. Eight of them is the squad every level is balanced against."),
        ("MachineGunner", 2, 250,
            "Fires a burst instead of a round. More hits, each one lighter."),
        ("Grenadier", 2, 350,
            "Lobs an explosive. Splash damage kills a packed garrison outright."),
        ("Sniper", 2, 400,
            "20 damage a shot — the only unit that one-shots a rifleman. Dies to anything."),
        ("ShieldBearer", 2, 500,
            "Walks forward and fights hand to hand. Soaks the charge so your line does not."),
        ("RocketTrooper", 3, 700,
            "Built for masonry. Brings a structure down in a fraction of the volleys."),
    };

    [MenuItem("Tools/ArmedConflict/Create Roster (if missing)")]
    public static void Create()
    {
        if (AssetDatabase.LoadAssetAtPath<RosterDefinitionSO>(Path) != null)
        {
            Debug.Log("[RosterSetup] roster already exists — left alone");
            return;
        }

        var units = AssetDatabase.FindAssets("t:UnitDefinitionSO", new[] { "Assets/GameData/Units" })
            .Select(AssetDatabase.GUIDToAssetPath)
            .Select(AssetDatabase.LoadAssetAtPath<UnitDefinitionSO>)
            .Where(u => u != null)
            .GroupBy(u => u.name)
            .ToDictionary(g => g.Key, g => g.First());

        var so = ScriptableObject.CreateInstance<RosterDefinitionSO>();
        foreach (var (name, points, coins, line) in Seed)
        {
            if (!units.TryGetValue(name, out var unit))
            {
                Debug.LogError($"[RosterSetup] no unit asset '{name}'");
                continue;
            }
            so.slots.Add(new RosterSlot
            {
                unit = unit, pointCost = points, coinPrice = coins, oneLiner = line,
            });
        }

        AssetDatabase.CreateAsset(so, Path);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log($"[RosterSetup] created {Path} with {so.slots.Count} pickable units");
    }
}
