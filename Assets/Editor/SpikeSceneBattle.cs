using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using ArmedConflict.Data;
using ArmedConflict.Game;

/// <summary>
/// Builds the playable battle scene: real L1 data, real GLBs, driven by BattleRunner.
/// Run with: -batchmode -quit -executeMethod SpikeSceneBattle.Build
/// </summary>
public static class SpikeSceneBattle
{
    const string ScenePath = "Assets/Scenes/Battle.unity";

    public static void Build()
    {
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var level = AssetDatabase.LoadAssetAtPath<LevelDefinitionSO>("Assets/GameData/Levels/Level1.asset");
        if (level == null) { Debug.LogError("[Battle] Level1 asset missing"); return; }

        var mats = LoadMats();

        var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
        ground.name = "Ground";
        ground.transform.localScale = new Vector3(30f, 1f, 30f);
        ground.GetComponent<MeshRenderer>().sharedMaterial = mats.ground;

        // Structures from the level data, at their real runtime positions.
        var state = LevelBuilder.BuildInitialState(level, 1, 29, new System.Random(1));
        foreach (var st in state.Structures)
        {
            var src = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath(st.Definition.modelAsset));
            if (src == null) { Debug.LogWarning($"[Battle] missing {st.Definition.modelAsset}"); continue; }
            var go = (GameObject)PrefabUtility.InstantiatePrefab(src);
            go.name = $"struct_{st.Id}";
            go.transform.position = GameSpace.ToUnity(st.X, st.Y - st.Definition.size / 2f, st.Z);
            if (st.Definition.modelAbsoluteScale)
                go.transform.localScale = Vector3.one * st.Definition.worldScale;
            else
                Normalize(go, st.Definition.isPlayerSide ? 1.5f : st.Definition.size);
            Tone(go, st.Definition.isPlayerSide ? mats.structPlayer : mats.structEnemy,
                 st.Definition.isPlayerSide ? mats.structPlayerAccent : mats.structEnemyAccent, null);
        }

        // Biome backdrop from the level's own BackgroundDefinition — the shipping build paints
        // one, and comparing a bare scene against it was never a fair A/B.
        var backdrop = new GameObject("Backdrop");
        BackdropBuilder.Build(level.background, mats.ground, backdrop.transform);

        // Props (L1's sandbags). Authored at z=0 like every campaign prop.
        foreach (var prop in level.props)
        {
            var src = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath(prop.modelAsset));
            if (src == null) { Debug.LogWarning($"[Battle] missing prop {prop.modelAsset}"); continue; }
            var pg = (GameObject)PrefabUtility.InstantiatePrefab(src);
            pg.name = $"prop_{System.IO.Path.GetFileNameWithoutExtension(prop.modelAsset)}";
            pg.transform.position = GameSpace.ToUnity(prop.x, 0f, prop.z);
            Normalize(pg, prop.scale);
            Tone(pg, mats.structPlayer, mats.structPlayerAccent, null);
        }

        var poolRoot = new GameObject("Pool");

        var playerPrefab = MakeUnitPrefab("PlayerUnit", mats.playerUniform, mats.playerGear, mats.skin);
        var enemyPrefab = MakeUnitPrefab("EnemyUnit", mats.enemyUniform, mats.enemyGear, mats.skin);
        var shotPrefab = MakeShellPrefab(mats);
        var gunPrefab = MakeGunPrefab(mats);
        var blastPrefab = MakeBlastPrefab();

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

        // AN AUDIOLISTENER IS REQUIRED FOR ANY SOUND AT ALL. Unity's default Main Camera PREFAB
        // ships with one, but a camera built from new GameObject() + AddComponent<Camera>() does
        // not — so every AudioSource plays into nothing, silently and with no warning. That is
        // what made the audio inaudible even though the clips were loaded, the triggers fired
        // and the volumes were right.
        camGo.AddComponent<AudioListener>();

        var runner = camGo.AddComponent<BattleRunner>();
        var so = new SerializedObject(runner);
        so.FindProperty("cam").objectReferenceValue = cam;
        so.FindProperty("level").objectReferenceValue = level;
        so.FindProperty("playerUnitPrefab").objectReferenceValue = playerPrefab;
        so.FindProperty("enemyUnitPrefab").objectReferenceValue = enemyPrefab;
        so.FindProperty("projectilePrefab").objectReferenceValue = shotPrefab;
        so.FindProperty("bulletPrefab").objectReferenceValue = MakeProjectilePrefab(
            "Bullet", "projectile_bullet", 0.22f, mats.tracer, mats.tracerTail);
        so.FindProperty("rocketPrefab").objectReferenceValue = MakeProjectilePrefab(
            "Rocket", "projectile_rocket", 0.30f, mats.rocketBody, mats.rocketGlow);
        so.FindProperty("grenadePrefab").objectReferenceValue = MakeProjectilePrefab(
            "Grenade", "projectile_grenade", 0.16f, mats.grenade, mats.grenadeBand);
        so.FindProperty("gunPrefab").objectReferenceValue = gunPrefab;
        so.FindProperty("explosionPrefab").objectReferenceValue = blastPrefab;
        so.FindProperty("audioFx").objectReferenceValue = MakeAudio(camGo);
        so.FindProperty("poolRoot").objectReferenceValue = poolRoot.transform;
        so.ApplyModifiedProperties();

        EditorSceneManager.SaveScene(scene, ScenePath);
        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
        AssetDatabase.SaveAssets();
        Debug.Log($"[Battle] built {ScenePath}: {state.PlayerUnits.Count}v{state.EnemyUnits.Count}, " +
                  $"{state.Structures.Count} structures");
    }

    static string ModelPath(string asset)
        => "Assets/Models/" + System.IO.Path.GetFileName(asset);

    static GameObject MakeUnitPrefab(string name, Material body, Material gear, Material skin)
    {
        var src = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Models/unit_rifleman.glb");
        var go = (GameObject)PrefabUtility.InstantiatePrefab(src);
        go.name = name;
        Normalize(go, UnitGeometry.UnitScaleUnits);
        Tone(go, body, gear, skin);
        var path = $"Assets/Prefabs/{name}.prefab";
        System.IO.Directory.CreateDirectory("Assets/Prefabs");
        var prefab = PrefabUtility.SaveAsPrefabAsset(go, path);
        Object.DestroyImmediate(go);
        return prefab;
    }

    /// <summary>
    /// One prefab per projectile TYPE. Rendering every round as the tank shell made a rifle
    /// volley look like nine artillery rounds — the models were imported all along, the runner
    /// just picked one prefab for everything.
    ///
    /// Colours and scales are carried across from SceneHost. Note the BODIES are unlit on
    /// purpose: a tracer is a light source, not a lit object, and a PBR round goes near-black
    /// against a dim biome.
    /// </summary>
    static GameObject MakeProjectilePrefab(string name, string asset, float scale,
                                           Material body, Material accent)
    {
        var src = AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/Models/{asset}.glb");
        if (src == null) { Debug.LogWarning($"[Battle] missing {asset}"); return null; }
        var go = (GameObject)PrefabUtility.InstantiatePrefab(src);
        go.name = name;
        go.transform.localScale = Vector3.one * scale;
        foreach (var r in go.GetComponentsInChildren<MeshRenderer>())
            r.sharedMaterial = r.gameObject.name.StartsWith("accent") ? accent : body;
        System.IO.Directory.CreateDirectory("Assets/Prefabs");
        var prefab = PrefabUtility.SaveAsPrefabAsset(go, $"Assets/Prefabs/{name}.prefab");
        Object.DestroyImmediate(go);
        return prefab;
    }

    static GameObject MakeShellPrefab(Mats mats)
        => MakeProjectilePrefab("Shell", "projectile_shell", 0.34f, mats.shellBody, mats.shellNose);

    /// <summary>
    /// The unit's weapon. Every UnitDefinition carries a gunModelAsset and the importer brought
    /// them across; the first playable build simply never instantiated one, which is the single
    /// most visible gap against the shipping build.
    /// GUN_SCALE_UNITS derives from the unit scale, like everything else body-relative.
    /// </summary>
    static GameObject MakeGunPrefab(Mats mats)
    {
        var src = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Models/placeholder_gun.glb");
        var go = (GameObject)PrefabUtility.InstantiatePrefab(src);
        go.name = "Gun";
        Normalize(go, 0.40f * UnitGeometry.LegacyScaleRatio);
        foreach (var r in go.GetComponentsInChildren<MeshRenderer>())
            r.sharedMaterial = mats.gun;
        System.IO.Directory.CreateDirectory("Assets/Prefabs");
        var prefab = PrefabUtility.SaveAsPrefabAsset(go, "Assets/Prefabs/Gun.prefab");
        Object.DestroyImmediate(go);
        return prefab;
    }

    /// <summary>
    /// The blast. UNLIT fire-orange, shared by both sides regardless of who fired — a
    /// side-tinted explosion reads as a team colour rather than as fire.
    /// </summary>
    static GameObject MakeBlastPrefab()
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        go.name = "Blast";
        Object.DestroyImmediate(go.GetComponent<Collider>());
        var mat = new Material(Shader.Find("Universal Render Pipeline/Unlit"))
        { color = new Color(1f, 0.55f, 0.15f) };
        AssetDatabase.CreateAsset(mat, "Assets/Materials/Blast.mat");
        go.GetComponent<MeshRenderer>().sharedMaterial = mat;
        System.IO.Directory.CreateDirectory("Assets/Prefabs");
        var prefab = PrefabUtility.SaveAsPrefabAsset(go, "Assets/Prefabs/Blast.prefab");
        Object.DestroyImmediate(go);
        return prefab;
    }

    static BattleAudio MakeAudio(GameObject host)
    {
        var fx = host.AddComponent<BattleAudio>();
        var so = new SerializedObject(fx);
        void Clip(string field, string file)
            => so.FindProperty(field).objectReferenceValue =
                   AssetDatabase.LoadAssetAtPath<AudioClip>($"Assets/Audio/{file}.wav");
        Clip("volleyFire", "volley_fire");
        Clip("groundImpact", "ground_impact");
        Clip("unitDeath", "unit_death");
        Clip("unitHit", "unit_hit");
        Clip("explosion", "explosion_hit");
        Clip("victory", "victory_jingle");
        Clip("defeat", "defeat_jingle");
        Clip("helicopterLoop", "helicopter_loop");
        so.ApplyModifiedProperties();
        return fx;
    }

    static void Normalize(GameObject go, float units)
    {
        var rs = go.GetComponentsInChildren<MeshRenderer>();
        if (rs.Length == 0) return;
        var b = rs[0].bounds;
        foreach (var r in rs) b.Encapsulate(r.bounds);
        float longest = Mathf.Max(b.size.x, Mathf.Max(b.size.y, b.size.z));
        if (longest > 0.0001f) go.transform.localScale = Vector3.one * (units / longest);
    }

    static void Tone(GameObject go, Material body, Material accent, Material skin)
    {
        foreach (var r in go.GetComponentsInChildren<MeshRenderer>())
        {
            string n = r.gameObject.name;
            if (skin != null && n.StartsWith("skin")) r.sharedMaterial = skin;
            else if (n.StartsWith("accent")) r.sharedMaterial = accent;
            else r.sharedMaterial = body;
        }
    }

    class Mats
    {
        public Material gun, tracer, tracerTail, rocketBody, rocketGlow, grenade, grenadeBand;
        public Material skin, playerUniform, playerGear, enemyUniform, enemyGear,
                        structPlayer, structPlayerAccent, structEnemy, structEnemyAccent,
                        ground, shellBody, shellNose;
    }

    static Mats LoadMats() => new()
    {
        skin = L("UnitSkin"), playerUniform = L("PlayerUniform"), playerGear = L("PlayerGear"),
        enemyUniform = L("EnemyUniform"), enemyGear = L("EnemyGear"),
        structPlayer = L("StructPlayer"), structPlayerAccent = L("StructPlayerAccent"),
        structEnemy = L("StructEnemy"), structEnemyAccent = L("StructEnemyAccent"),
        ground = L("GroundMat"), shellBody = L("ShellBody"), shellNose = L("ShellNose"),
        gun = L("UnitGun"),
        // Carried from SceneHost. Tracers are UNLIT — a tracer is a light source.
        tracer      = Unlit("Tracer",      new Color(1f, 0.647f, 0f)),        // 0xFFA500
        tracerTail  = Unlit("TracerTail",  new Color(0.541f, 0.353f, 0.071f)),// 0x8A5A12
        rocketBody  = Lit("RocketBody",    new Color(0.36f, 0.38f, 0.32f), 0.2f, 0.6f),
        rocketGlow  = Unlit("RocketGlow",  new Color(1f, 0.914f, 0.627f)),    // white-hot exhaust
        grenade     = Unlit("Grenade",     new Color(0.608f, 0.757f, 0.235f)),// olive-lime
        grenadeBand = Lit("GrenadeBand",   new Color(0.58f, 0.45f, 0.18f), 0.4f, 0.5f),
    };

    static Material L(string n) => AssetDatabase.LoadAssetAtPath<Material>($"Assets/Materials/{n}.mat");

    static Material Unlit(string n, Color c) => Make(n, c, "Universal Render Pipeline/Unlit", 0f, 0f);
    static Material Lit(string n, Color c, float metallic, float smoothness)
        => Make(n, c, "Universal Render Pipeline/Lit", metallic, smoothness);

    static Material Make(string n, Color c, string shader, float metallic, float smoothness)
    {
        var path = $"Assets/Materials/{n}.mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null)
        {
            m = new Material(Shader.Find(shader));
            AssetDatabase.CreateAsset(m, path);
        }
        m.color = c;
        if (shader.EndsWith("Lit")) { m.SetFloat("_Metallic", metallic); m.SetFloat("_Smoothness", smoothness); }
        return m;
    }
}
