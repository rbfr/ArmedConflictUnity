using UnityEditor;
using UnityEngine;
using ArmedConflict.Data;

/// <summary>
/// Authors the campaign's faction assets and attaches one to each stage — Tier 2.1.
///
/// A one-shot command, in the same family as `SandboxLevels.Generate`: the assets it writes are
/// DATA and are edited directly afterwards (`Assets/GameData` is the source of truth). It is
/// idempotent and only fills in a field it finds empty, so re-running it after Rob has retuned a
/// colour does not undo the retune — the one thing an authoring script must never do here.
///
///     DISPLAY=:1 $U -batchmode -quit -projectPath . -executeMethod FactionAuthoring.Author -logFile -
/// </summary>
public static class FactionAuthoring
{
    const string Dir = "Assets/GameData/Factions";

    [MenuItem("ArmedConflict/Author Factions")]
    public static void Author()
    {
        System.IO.Directory.CreateDirectory(Dir);

        // Redguard is the enemy red this build has always used, unchanged and deliberately so:
        // stage 1 is where the player learns what "the enemy" looks like, and a faction system
        // whose first act is to repaint the tutorial army teaches nothing. The identity is what
        // is new here, not the colour.
        var redguard = Make("Redguard", "stage_valley",
                            new Color(0.52f, 0.20f, 0.18f),
                            new Color(0.20f, 0.14f, 0.13f),
                            new Color(0.92f, 0.38f, 0.33f));

        // Steel blue-grey — the Kotlin build's Ironclad Legion, carried over. It reads as a
        // DIFFERENT ARMY rather than as a different team: the player's own olive green is the
        // colour that must stay unambiguous, and the enemy is also the side of the field the
        // camera never starts on, so position carries the "who is who" reading even before colour.
        var ironclad = Make("IroncladLegion", "stage_stronghold",
                            new Color(0.27f, 0.33f, 0.42f),
                            new Color(0.14f, 0.17f, 0.22f),
                            new Color(0.56f, 0.72f, 0.92f));

        Attach("Assets/GameData/Stages/ValleyFront.asset", redguard);
        Attach("Assets/GameData/Stages/EnemyStronghold.asset", ironclad);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
    }

    static FactionDefinitionSO Make(string file, string id, Color uniform, Color gear, Color banner)
    {
        string path = $"{Dir}/{file}.asset";
        var f = AssetDatabase.LoadAssetAtPath<FactionDefinitionSO>(path);
        if (f != null)
        {
            Debug.Log($"[Factions] {file} exists, left alone");
            return f;
        }
        f = ScriptableObject.CreateInstance<FactionDefinitionSO>();
        f.id = id;
        f.displayName = ObjectNames.NicifyVariableName(file);
        f.uniformColor = uniform;
        f.gearColor = gear;
        f.bannerColor = banner;
        AssetDatabase.CreateAsset(f, path);
        Debug.Log($"[Factions] wrote {path}: {f.displayName}");
        return f;
    }

    static void Attach(string stagePath, FactionDefinitionSO faction)
    {
        var stage = AssetDatabase.LoadAssetAtPath<StageDefinitionSO>(stagePath);
        if (stage == null) { Debug.LogError($"[Factions] missing stage {stagePath}"); return; }
        if (stage.faction != null)
        {
            Debug.Log($"[Factions] {stage.displayName} already fields {stage.faction.displayName}");
            return;
        }
        stage.faction = faction;
        EditorUtility.SetDirty(stage);
        Debug.Log($"[Factions] {stage.displayName} -> {faction.displayName}");
    }
}
