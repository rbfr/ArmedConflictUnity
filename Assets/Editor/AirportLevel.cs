using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using ArmedConflict.Data;

/// <summary>
/// Authors the L13 airfield set-piece: Hangar structure + Scorched Apron level +
/// Ashfield stage. Idempotent — re-running updates numbers, it does not mint a
/// second copy.
///
///     DISPLAY=:0 $U -batchmode -quit -projectPath . -executeMethod AirportLevel.Author -logFile -
/// </summary>
public static class AirportLevel
{
    const string StructPath = "Assets/GameData/Structures/Hangar.asset";
    const string LevelPath = "Assets/GameData/Levels/ScorchedApron.asset";
    const string StagePath = "Assets/GameData/Stages/Ashfield.asset";

    [MenuItem("ArmedConflict/Author Airport Level")]
    public static void Author()
    {
        var hangar = Hangar();
        var level = Level(hangar);
        Stage(level);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log($"[Airport] L{level.levelNumber} {level.displayName} / {hangar.so.displayName}");
    }

    static HangarDef Hangar()
    {
        var so = AssetDatabase.LoadAssetAtPath<StructureDefinitionSO>(StructPath);
        if (so == null)
        {
            so = ScriptableObject.CreateInstance<StructureDefinitionSO>();
            AssetDatabase.CreateAsset(so, StructPath);
        }
        so.id = "hangar";
        so.displayName = "Hangar";
        so.modelAsset = "models/hangar.glb";
        so.wreckModelAsset = "models/hangar_collapse.glb";
        so.maxHp = 200;
        // Open-mouth hangar. Walkway 0.82 * 2.5 = 2.05, parapet ~0.94 * 2.5 = 2.35.
        // Fascia is the 6° floor-read; the roof is thin. CORE, not chunks.
        so.size = 2.35f;
        so.isPlayerSide = false;
        so.modelAbsoluteScale = true;
        so.modelScaleUnits = 2.5f;
        so.standWidth = 2.80f;
        // Catwalk centre is blender Y -0.62 × 2.5 ≈ 1.55 toward camera.
        // 0.35 left the row behind the fascia — only helmets cleared the roof.
        so.deckStandZOffset = 1.45f;
        so.hitWidth = 3.40f;
        so.hasHitWidth = true;
        so.deckY = 2.05f;
        so.hasDeckY = true;
        so.worldScale = 2.5f;
        so.hasCannon = false;
        so.hasFlagMount = false;
        so.damageChunks = Chunks();
        EditorUtility.SetDirty(so);
        return new HangarDef { so = so };
    }

    struct HangarDef { public StructureDefinitionSO so; }

    /// <summary>
    /// Blender metres × 2.5. X = battlefield, Y = up, Z = depth (Blender Y).
    /// </summary>
    static List<DamageChunk> Chunks()
    {
        const float k = 2.5f;
        DamageChunk C(float x, float yBlender, float zUp, float sx, float syBlender, float szUp, int pieces = 1)
            => new DamageChunk
            {
                offsetX = x * k, offsetY = zUp * k, offsetZ = yBlender * k,
                sizeX = sx * k, sizeY = szUp * k, sizeZ = syBlender * k,
                pieces = pieces,
            };
        return new List<DamageChunk>
        {
            C(-0.84f, -0.12f, 0.46f, 0.10f, 0.48f, 0.20f),           // left skin
            C( 0.84f, -0.12f, 0.46f, 0.10f, 0.48f, 0.20f),           // right skin
            C( 0.28f, -0.08f, 0.81f, 0.36f, 0.20f, 0.05f),           // roof scar
        };
    }

    static T Load<T>(string path) where T : Object
        => AssetDatabase.LoadAssetAtPath<T>(path);

    static EnemyGroup Group(string unitPath, int count, float x, float z = 0f,
                            string on = "", float advance = 0f)
        => new EnemyGroup
        {
            definition = Load<UnitDefinitionSO>(unitPath),
            count = count,
            anchorX = x,
            anchorZ = z,
            standingOnStructureId = on,
            advancePerTurn = advance,
        };

    static StructurePlacement Place(string id, StructureDefinitionSO def, float x)
        => new StructurePlacement
        {
            id = id,
            definition = def,
            x = x, y = 0f, z = 0f,
            hpScale = 1f,
        };

    static PropPlacement Prop(string model, float x, float z, float scale,
                              bool keep = true, bool abs = false, float half = 1f,
                              string diesWith = "", Color tint = default,
                              bool onFire = false)
        => new PropPlacement
        {
            modelAsset = model,
            x = x, z = z, scale = scale,
            keepColors = keep,
            absoluteScale = abs,
            halfWidth = half,
            collapsesWith = diesWith,
            tint = tint,
            onFire = onFire,
        };

    static LevelDefinitionSO Level(HangarDef hangar)
    {
        var so = Load<LevelDefinitionSO>(LevelPath);
        if (so == null)
        {
            so = ScriptableObject.CreateInstance<LevelDefinitionSO>();
            AssetDatabase.CreateAsset(so, LevelPath);
        }

        var tank = Load<StructureDefinitionSO>("Assets/GameData/Structures/PlayerTank.asset");
        var desert = Load<BackgroundDefinitionSO>("Assets/GameData/Backgrounds/Desert.asset");
        const string U = "Assets/GameData/Units";

        so.id = "level_13";
        so.displayName = "Scorched Apron";
        so.levelNumber = 13;
        so.levelGoal = "Clear the hangar before they cross the tarmac";
        so.background = desert;
        so.heliChance = 0f;
        so.levelBase = 180;
        so.deployBudget = 12;
        so.windAccelZ = 0f;
        so.staticCamera = false;
        so.isTestLevel = false;
        so.bossPhases = new List<BossPhaseTrigger>();
        so.reinforcementWaves = new List<ReinforcementWave>();
        so.playerSpacingScale = 0.85f;
        so.designNotes =
            "Beat 13: second melee shape. L4 taught the charge behind wire; this is the " +
            "same panic on OPEN tarmac — five shield bearers sprint the apron with nothing in " +
            "front of them but air. They do not fire; they close and melee. The hangar is the dominant (and only) structure, " +
            "majority garrisoned, so dropping it is still the efficient kill. MG sit at " +
            "the bay mouth, not on the roof (class placement). Wrecked planes and the " +
            "runway are keepColors props — they do not collide. Desert reused (L5/L12); " +
            "PRODUCT_DIRECTION after-12 expansion, not a new verb. 2026-09-07: hangar " +
            "core (deck/walls) is not a chunk — the first pass named every plate " +
            "chunk_N, one shell hid the roof, garrison floated. Hangar pulled 8.0 -> " +
            "7.2 (separation 16.7) and made taller so the 6° camera can see it; " +
            "playerSpacingScale 0.85 so the aim frame is not a parade. Ground units " +
            "stay in front of hitWidth 3.40 (box edge 6.10). 2026-09-08: hanging " +
            "door chunk removed so the mouth is an open hole; a small wreck stays " +
            "parked in the bay and cooks off with the hangar.";

        so.enemyGroups = new List<EnemyGroup>
        {
            Group($"{U}/EnemyShieldBearer.asset", 5, 3.0f, 0f, "", 1.4f),
            Group($"{U}/EnemyMachineGunner.asset", 2, 4.4f, 0f),
            Group($"{U}/EnemyRiflemanCrowd.asset", 10, 7.8f, 0.12f, "hangar"),
            Group($"{U}/EnemyGrenadierCrowd.asset", 4, 7.8f, 0.12f, "hangar"),
        };
        so.structures = new List<StructurePlacement>
        {
            Place("player_tank", tank, -9.5f),
            Place("hangar", hangar.so, 7.8f),
        };
        so.playerGroups = new List<EnemyGroup>
        {
            Group($"{U}/Rifleman.asset", 2, -9.5f, 0.12f, "player_tank"),
            Group($"{U}/Rifleman.asset", 6, -7.2f),
            Group($"{U}/Grenadier.asset", 2, -5.6f),
        };
        so.props = new List<PropPlacement>
        {
            Prop("models/prop_runway.glb", -1.0f, -2.2f, 1f, keep: true, abs: true, half: 8f),
            // One airliner, snapped in half. The 6° camera has to see a plane
            // that crashed — not a graveyard of unreadable bits. onFire: the
            // snap is the burn face; tongues + smoke sit on the hull.
            Prop("models/prop_wreck_fighter.glb", 3.6f, -4.5f, 4.6f, half: 2.4f,
                onFire: true),
            // Smaller jet IN the bay, pulled toward the mouth (larger z = nearer
            // camera). Scale 1.7 so it reads as parked inside, not an apron wreck.
            Prop("models/prop_wreck_fighter_bay.glb", 7.8f, 0.90f, 1.7f, half: 0.9f,
                diesWith: "hangar", tint: new Color(0.36f, 0.44f, 0.28f, 1f)),
            // Further back and bigger so it reads as the field's tower, not a
            // hut next to the apron. Radar child spins at runtime.
            Prop("models/prop_control_tower.glb", -6.0f, -15.5f, 5.6f, half: 1.2f),
        };

        EditorUtility.SetDirty(so);
        return so;
    }

    static void Stage(LevelDefinitionSO level)
    {
        var stage = Load<StageDefinitionSO>(StagePath);
        if (stage == null)
        {
            stage = ScriptableObject.CreateInstance<StageDefinitionSO>();
            AssetDatabase.CreateAsset(stage, StagePath);
        }
        stage.id = "stage_ashfield";
        stage.displayName = "Ashfield";
        stage.tagline = "Their last strip is already burning.";
        stage.starsToUnlock = 16;
        stage.unlockRewardId = "";
        stage.completionCoinBonus = 150;
        stage.faction = Load<FactionDefinitionSO>("Assets/GameData/Factions/IroncladLegion.asset");
        if (stage.levels == null) stage.levels = new List<LevelDefinitionSO>();
        if (!stage.levels.Contains(level)) stage.levels.Add(level);
        EditorUtility.SetDirty(stage);
    }
}
