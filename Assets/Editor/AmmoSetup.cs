using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using ArmedConflict.Data;

/// <summary>
/// Authors `Assets/GameData/AmmoCatalog.asset` from `DYNAMISM_DESIGN.md` Phase A.
///
///     -batchmode -quit -executeMethod AmmoSetup.Build
///
/// Deliberately IDEMPOTENT and re-runnable, unlike the one-shot `CampaignAuthor` that was
/// deleted after use: this asset is four rows of numbers with no cross-asset references, so
/// regenerating it cannot destroy hand-authored work the way a level rewriter could. Edit the
/// asset directly OR edit these numbers and re-run; both are safe.
/// </summary>
public static class AmmoSetup
{
    const string Path = "Assets/GameData/AmmoCatalog.asset";

    public static void Build()
    {
        var catalog = AssetDatabase.LoadAssetAtPath<AmmoCatalogSO>(Path);
        bool fresh = catalog == null;
        if (fresh) catalog = ScriptableObject.CreateInstance<AmmoCatalogSO>();

        catalog.slots = new List<AmmoSlot>
        {
            new()
            {
                type = AmmoType.Standard,
                displayName = "Standard",
                oneLiner = "Your volley, unchanged.",
                coinPrice = 0,
                // THE IDENTITY. Every field a no-op, which is what makes "no ammo is ever
                // REQUIRED to clear a level" a checkable property rather than a promise.
                unitDamageScale = 1f, structureDamageScale = 1f, spreadScale = 1f, burnDamage = 0,
            },
            new()
            {
                type = AmmoType.Incendiary,
                displayName = "Incendiary",
                oneLiner = "Hits set men alight. They burn when the enemy turn starts.",
                coinPrice = 300,
                // Rounds land slightly lighter; the burn is where the damage went.
                unitDamageScale = 0.85f, structureDamageScale = 1f, spreadScale = 1f,
                // 8 = a quarter of a 32hp rifleman, and it does NOT one-shot the frailest unit
                // in the current roster (the 16hp Sniper). HANDOVER records the old 6 as having
                // been calibrated against an 8hp Sniper that no longer exists — this number is
                // re-derived against the roster as it stands, not inherited.
                burnDamage = 8,
            },
            new()
            {
                type = AmmoType.AP,
                displayName = "AP",
                oneLiner = "Punches masonry. Wasted on men in the open.",
                coinPrice = 400,
                // The trade the type exists for. 2x on structures STACKS with the unit's own
                // multiplier, so a rocket trooper firing AP is the best masonry answer in the
                // game — that combination is the reward for owning both.
                unitDamageScale = 0.6f, structureDamageScale = 2f, spreadScale = 1f, burnDamage = 0,
            },
            new()
            {
                type = AmmoType.Cluster,
                displayName = "Cluster",
                oneLiner = "Spreads wide. More men hit, each one lighter.",
                coinPrice = 500,
                // Wider convergent fire, NOT a blind fan (that is forbidden by the lock): it
                // scales the per-shooter jitter the volley already has, so more distinct enemies
                // fall inside the zone. The counter-pick to a wide formation.
                unitDamageScale = 0.65f, structureDamageScale = 1f, spreadScale = 3.2f, burnDamage = 0,
            },
        };

        if (fresh) AssetDatabase.CreateAsset(catalog, Path);
        else EditorUtility.SetDirty(catalog);
        AssetDatabase.SaveAssets();

        Debug.Log($"[Ammo] {(fresh ? "created" : "updated")} {Path} with {catalog.slots.Count} types: " +
                  string.Join(", ", catalog.slots.ConvertAll(s => $"{s.displayName} {s.coinPrice}c")));
    }
}
