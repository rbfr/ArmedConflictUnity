using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// UNITY_SPIKE.md Step 3 — reproduce L1 as a static scene: 19 crowd units in formation,
/// one structure, one tank.
/// Passes if: steady 60 fps, all units render every frame, and no unit is missing its head,
/// arms or gun (that was three separate Filament bugs; if it recurs here, stop and investigate).
/// </summary>
public static class SpikeSceneL1
{
    const string ScenePath = "Assets/Scenes/Step3_L1.unity";
    const string Step4Path = "Assets/Scenes/Step4_Shot.unity";

    // Enemy unit transforms + their GAME-space (x,y), collected for the Step 4 collision set.
    static readonly List<Transform> EnemyUnits = new();
    static readonly List<Vector2> EnemyXY = new();

    // Carried from CLAUDE.md — derived values, not taste.
    const float UnitScaleUnits = 0.48f;                       // UnitGeometry.UNIT_SCALE_UNITS
    const float LegacyScaleRatio = UnitScaleUnits / 0.77f;
    const float GunScaleUnits = 0.40f * LegacyScaleRatio;
    const float StructureScale = 2.5f;                        // StructureDefinition.kt
    const float ColumnSpacing = 0.49f * LegacyScaleRatio;     // Formation.DEFAULT_COLUMN_SPACING
    const float RowSpacing = 0.38f;                           // deliberately does NOT derive
    const int FormationColumns = 5;

    /// <summary>Formation.grid, ported verbatim.</summary>
    static List<Vector2> Grid(int count, float anchorX, float anchorZ, int columns, float colSpacing)
    {
        var result = new List<Vector2>();
        if (count <= 0) return result;
        int rows = (count + columns - 1) / columns;
        for (int i = 0; i < count; i++)
        {
            int row = i / columns;
            int unitsInRow = Mathf.Min(columns, count - row * columns);
            int col = i % columns;
            float x = anchorX + (col - (unitsInRow - 1) / 2f) * colSpacing;
            float z = anchorZ + (row - (rows - 1) / 2f) * RowSpacing;
            result.Add(new Vector2(x, z));
        }
        return result;
    }

    public static void Build() => BuildScene(step4: false);
    public static void BuildStep4() => BuildScene(step4: true);

    static void BuildScene(bool step4)
    {
        EnemyUnits.Clear();
        EnemyXY.Clear();
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        var mats = new ToneMaterials();
        var report = new List<string>();

        // Ground: a real 3D plane, so no groundLift correction is needed (Step 2 confirmed).
        var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
        ground.name = "Ground";
        ground.transform.localScale = new Vector3(20f, 1f, 20f);
        ground.GetComponent<MeshRenderer>().sharedMaterial = mats.Ground;

        var unitPrefab = Load("unit_rifleman");
        var gunPrefab = Load("placeholder_gun");
        var tankPrefab = Load("placeholder_tank");
        var outpostPrefab = Load("outpost");
        var sandbagPrefab = Load("sandbags");

        var root = new GameObject("L1");

        // --- structures -------------------------------------------------------------------
        // The player tank is deliberately NOT multiplied by STRUCTURE_SCALE.
        var tank = Place(tankPrefab, GameSpace.ToUnity(-9.5f, 0f, 0f), root.transform, "PlayerTank");
        NormalizeLongestAxis(tank, 1.5f);
        Tone(tank, mats.StructurePlayerPrimary, mats.StructurePlayerAccent, null, report);

        var outpost = Place(outpostPrefab, GameSpace.ToUnity(7.0f, 0f, 0f), root.transform, "Outpost");
        // modelAbsoluteScale = true: bypasses normalisation, GLB scale x worldScale.
        outpost.transform.localScale = Vector3.one * StructureScale;
        Tone(outpost, mats.StructureEnemyPrimary, mats.StructureEnemyAccent, null, report);

        var sandbags = Place(sandbagPrefab, GameSpace.ToUnity(-6.4f, 0f, 0.3f), root.transform, "Sandbags");
        NormalizeLongestAxis(sandbags, 0.8f);
        Tone(sandbags, mats.StructurePlayerPrimary, mats.StructurePlayerAccent, null, report);

        // --- units ------------------------------------------------------------------------
        // deckY drives where a garrison stands; the outpost breaks the "deck at size" contract.
        const float outpostDeckY = 0.560f * StructureScale;
        const float tankDeckY = 0.60f;

        int built = 0;
        built += Squad(unitPrefab, gunPrefab, root.transform, mats, report,
            count: 2, anchorX: -9.5f, anchorZ: 0.12f, y: tankDeckY, player: true, tag: "P_tank");
        built += Squad(unitPrefab, gunPrefab, root.transform, mats, report,
            count: 8, anchorX: -7.0f, anchorZ: 0f, y: 0f, player: true, tag: "P_line");
        built += Squad(unitPrefab, gunPrefab, root.transform, mats, report,
            count: 6, anchorX: 4.5f, anchorZ: 0f, y: 0f, player: false, tag: "E_line");
        built += Squad(unitPrefab, gunPrefab, root.transform, mats, report,
            count: 3, anchorX: 7.0f, anchorZ: 0.12f, y: outpostDeckY, player: false, tag: "E_deck");

        // --- lighting + camera --------------------------------------------------------------
        var lightGo = new GameObject("Directional Light");
        var light = lightGo.AddComponent<Light>();
        light.type = LightType.Directional;
        light.shadows = LightShadows.None;
        light.intensity = 1.1f;
        lightGo.transform.rotation = Quaternion.Euler(50f, -30f, 0f);

        var camGo = new GameObject("Main Camera");
        camGo.tag = "MainCamera";
        var cam = camGo.AddComponent<Camera>();
        cam.fieldOfView = BattleCamera.VerticalFovDegrees;
        cam.usePhysicalProperties = false;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.16f, 0.18f, 0.22f);
        cam.nearClipPlane = 0.1f;
        cam.farClipPlane = 300f;

        string path;
        if (!step4)
        {
            var probe = camGo.AddComponent<Step3Probe>();
            var so = new SerializedObject(probe);
            so.FindProperty("cam").objectReferenceValue = cam;
            so.FindProperty("expectedUnits").intValue = built;
            so.FindProperty("gameCamX").floatValue = 6.0f;
            so.FindProperty("camZ").floatValue = 11f;
            so.ApplyModifiedProperties();
            path = ScenePath;
        }
        else
        {
            // The round: a small sphere, unlit-bright so it reads against both ground and sky.
            var shot = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            shot.name = "Projectile";
            shot.transform.localScale = Vector3.one * 0.22f;
            Object.DestroyImmediate(shot.GetComponent<Collider>());  // collision is ours, not Unity's
            var shotMat = new Material(Shader.Find("Universal Render Pipeline/Unlit"))
            { color = new Color(1f, 0.86f, 0.35f) };
            AssetDatabase.CreateAsset(shotMat, "Assets/Materials/Projectile.mat");
            shot.GetComponent<MeshRenderer>().sharedMaterial = shotMat;

            var battle = camGo.AddComponent<Step4Battle>();
            var so = new SerializedObject(battle);
            so.FindProperty("cam").objectReferenceValue = cam;
            so.FindProperty("projectile").objectReferenceValue = shot.transform;
            so.FindProperty("restCamX").floatValue = -7f;
            so.FindProperty("camZ").floatValue = 11f;
            var units = so.FindProperty("enemyUnits");
            var xy = so.FindProperty("enemyXY");
            units.arraySize = EnemyUnits.Count;
            xy.arraySize = EnemyXY.Count;
            for (int i = 0; i < EnemyUnits.Count; i++)
            {
                units.GetArrayElementAtIndex(i).objectReferenceValue = EnemyUnits[i];
                xy.GetArrayElementAtIndex(i).vector2Value = EnemyXY[i];
            }
            so.ApplyModifiedProperties();
            path = Step4Path;
        }

        EditorSceneManager.SaveScene(scene, path);
        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(path, true) };
        AssetDatabase.SaveAssets();

        Debug.Log($"[SpikeL1] built {path} units={built} enemies={EnemyXY.Count}");
    }

    static int Squad(GameObject unitPrefab, GameObject gunPrefab, Transform parent,
                     ToneMaterials mats, List<string> report,
                     int count, float anchorX, float anchorZ, float y, bool player, string tag)
    {
        var spots = Grid(count, anchorX, anchorZ, Mathf.Min(FormationColumns, count), ColumnSpacing);
        for (int i = 0; i < spots.Count; i++)
        {
            var go = Place(unitPrefab, GameSpace.ToUnity(spots[i].x, y, spots[i].y), parent, $"{tag}_{i}");
            NormalizeLongestAxis(go, UnitScaleUnits);
            Tone(go, player ? mats.PlayerUniform : mats.EnemyUniform,
                     player ? mats.PlayerGear : mats.EnemyGear, mats.Skin, report);
            if (!player) { EnemyUnits.Add(go.transform); EnemyXY.Add(new Vector2(spots[i].x, y)); }

            var gun = Place(gunPrefab, GameSpace.ToUnity(spots[i].x, y, spots[i].y), parent, $"{tag}_{i}_gun");
            NormalizeLongestAxis(gun, GunScaleUnits);
            Tone(gun, mats.Gun, mats.Gun, null, report);
        }
        return spots.Count;
    }

    static GameObject Load(string name)
    {
        var path = $"Assets/Models/{name}.glb";
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (prefab == null) Debug.LogError($"[SpikeL1] could not load {path}");
        return prefab;
    }

    static GameObject Place(GameObject prefab, Vector3 pos, Transform parent, string name)
    {
        var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
        go.name = name;
        go.transform.position = pos;
        return go;
    }

    /// <summary>Equivalent of SceneView's scaleToUnits: longest axis becomes `units`.</summary>
    static void NormalizeLongestAxis(GameObject go, float units)
    {
        var renderers = go.GetComponentsInChildren<MeshRenderer>();
        if (renderers.Length == 0) return;
        var b = renderers[0].bounds;
        foreach (var r in renderers) b.Encapsulate(r.bounds);
        float longest = Mathf.Max(b.size.x, Mathf.Max(b.size.y, b.size.z));
        if (longest > 0.0001f) go.transform.localScale = Vector3.one * (units / longest);
    }

    /// <summary>
    /// The node-name-prefix material convention, ported: skin* -> flesh, trim* -> class signature,
    /// accent* -> dark gear, everything else -> the side's uniform. In Unity this is a build-time
    /// assignment rather than SceneHost's per-frame override.
    /// </summary>
    static void Tone(GameObject go, Material body, Material accent, Material skin, List<string> report)
    {
        foreach (var r in go.GetComponentsInChildren<MeshRenderer>())
        {
            string n = r.gameObject.name;
            if (skin != null && n.StartsWith("skin")) r.sharedMaterial = skin;
            else if (n.StartsWith("accent")) r.sharedMaterial = accent;
            else r.sharedMaterial = body;
        }
        report.Add($"{go.name}: renderers={go.GetComponentsInChildren<MeshRenderer>().Length}");
    }

    class ToneMaterials
    {
        static Material Make(string name, Color c)
        {
            var path = $"Assets/Materials/{name}.mat";
            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing != null) { existing.color = c; return existing; }
            var m = new Material(Shader.Find("Universal Render Pipeline/Lit")) { color = c };
            m.SetFloat("_Smoothness", 0.15f);
            m.enableInstancing = true;
            AssetDatabase.CreateAsset(m, path);
            return m;
        }

        // Colours carried across from SceneHost.kt where they are plain constants.
        public readonly Material Skin = Make("UnitSkin", new Color(0.76f, 0.56f, 0.42f));
        public readonly Material Gun = Make("UnitGun", new Color(0.21f, 0.22f, 0.24f));
        public readonly Material PlayerUniform = Make("PlayerUniform", new Color(0.30f, 0.40f, 0.24f));
        public readonly Material PlayerGear = Make("PlayerGear", new Color(0.17f, 0.19f, 0.16f));
        // L1 is stage_valley -> RedguardPalette.
        public readonly Material EnemyUniform = Make("EnemyUniform", new Color(0.52f, 0.20f, 0.18f));
        public readonly Material EnemyGear = Make("EnemyGear", new Color(0.20f, 0.14f, 0.13f));
        public readonly Material StructurePlayerPrimary = Make("StructPlayer", new Color(0.40f, 0.44f, 0.34f));
        public readonly Material StructurePlayerAccent = Make("StructPlayerAccent", new Color(0.20f, 0.24f, 0.16f));
        public readonly Material StructureEnemyPrimary = Make("StructEnemy", new Color(0.52f, 0.44f, 0.34f));
        public readonly Material StructureEnemyAccent = Make("StructEnemyAccent", new Color(0.30f, 0.24f, 0.18f));
        public readonly Material Ground = Make("GroundMat", new Color(0.62f, 0.60f, 0.52f));
    }
}
