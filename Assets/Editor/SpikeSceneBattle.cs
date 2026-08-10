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

    /// <summary>
    /// Unit art. Rigged = OUR soldier authored on Kenney's joint hierarchy and driven by their
    /// CC0 clips (RiggedUnits); Kenney = their whole character, the free stand-in that proved the
    /// pipeline; neither = the original scripted Blender units, unriggable and static.
    /// </summary>
    enum UnitArt { Blender, KenneyStandIn, Rigged }
    const UnitArt Art = UnitArt.Rigged;

    public static void Build()
    {
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        // CAMPAIGN FIRST, then the test rigs; each block ordered by its own level number.
        //
        // The campaign block being contiguous and leading is what lets BattleRunner treat
        // "index < campaignCount" as the player-facing path without a second array, and it is why
        // a test rig's levelNumber no longer has to be renumbered every time the campaign changes
        // size. That renumbering was a standing chore and a standing bug: the switcher indexes by
        // position, so a rig left on its old number silently moved.
        //
        // Sorting by filename would interleave Level10 with Level1 and scatter the named rigs.
        var levels = AssetDatabase.FindAssets("t:LevelDefinitionSO")
            .Select(g => AssetDatabase.LoadAssetAtPath<LevelDefinitionSO>(AssetDatabase.GUIDToAssetPath(g)))
            .Where(l => l != null)
            .OrderBy(l => l.isTestLevel)
            .ThenBy(l => l.levelNumber)
            .ToArray();
        if (levels.Length == 0) { Debug.LogError("[Battle] no level assets"); return; }
        var level = levels[0];

        var mats = LoadMats();

        // NOTHING about the level is baked into the scene any more — ground, structures, props
        // and backdrop are all built at runtime by LevelScenery. Baking them was what made a
        // second level unreachable, and it also meant the one biome L1 happens to use was the
        // only one anybody ever saw.
        var poolRoot = new GameObject("Pool");

        // A/B switch for the unit-art evaluation. false = the scripted Blender units (four-tone,
        // no rig, no animation); true = Kenney's CC0 rigged stand-in. Flip and rebuild the scene;
        // nothing else in the scene changes, so the two builds are comparable frame for frame.
        var playerPrefab = Art switch
        {
            UnitArt.Rigged => RiggedUnits.MakePrefab("PlayerUnit", mats.playerUniform,
                                                     mats.playerGear, mats.skin, mats.gun, facesScreenRight: true),
            UnitArt.KenneyStandIn => KenneyUnits.MakePrefab("PlayerUnit",
                                                     new Color(0.62f, 0.78f, 0.62f), facesScreenRight: true),
            _ => MakeUnitPrefab("PlayerUnit", mats.playerUniform, mats.playerGear, mats.skin),
        };
        var enemyPrefab = Art switch
        {
            UnitArt.Rigged => RiggedUnits.MakePrefab("EnemyUnit", mats.enemyUniform,
                                                     mats.enemyGear, mats.skin, mats.gun, facesScreenRight: false),
            UnitArt.KenneyStandIn => KenneyUnits.MakePrefab("EnemyUnit",
                                                     new Color(0.92f, 0.55f, 0.5f), facesScreenRight: false),
            _ => MakeUnitPrefab("EnemyUnit", mats.enemyUniform, mats.enemyGear, mats.skin),
        };

        // PER-CLASS prefabs, one per silhouette per side. The pair above stays as the FALLBACK a
        // class with no rigged model of its own falls through to; these are what the runner
        // actually pools. Only the Rigged art path has them — the other two exist to A/B the
        // rig itself, and giving a stand-in six silhouettes it does not have would defeat that.
        string[] classKeys = Art == UnitArt.Rigged ? RiggedUnits.Models : new string[0];
        var playerClassPrefabs = classKeys
            .Select(k => RiggedUnits.MakePrefab($"PlayerUnit_{k}", mats.playerUniform, mats.playerGear,
                                                mats.skin, mats.gun, facesScreenRight: true,
                                                modelKey: k, trim: TrimMat(k)))
            .Cast<Object>().ToArray();
        var enemyClassPrefabs = classKeys
            .Select(k => RiggedUnits.MakePrefab($"EnemyUnit_{k}", mats.enemyUniform, mats.enemyGear,
                                                mats.skin, mats.gun, facesScreenRight: false,
                                                modelKey: k, trim: TrimMat(k)))
            .Cast<Object>().ToArray();
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

        // The scorch prefab MUST be built before the scenery is wired. MakeScorchPrefab calls
        // AssetDatabase.CreateAsset on Scorch.mat, which REPLACES the asset and mints a new guid
        // — so a reference taken beforehand dangles to null. On device that surfaced as
        // ArgumentNullException inside Material's copy constructor, from LevelScenery.Build,
        // with a scene file that looked entirely correct except for one `{fileID: 0}`.
        var scorchPrefab = MakeScorchPrefab();

        var scenery = camGo.AddComponent<LevelScenery>();
        WireScenery(scenery, mats);

        var runner = camGo.AddComponent<BattleRunner>();
        var so = new SerializedObject(runner);
        so.FindProperty("cam").objectReferenceValue = cam;
        Fill(so.FindProperty("levels"), levels);
        // The loadout picker's menu. Optional: with no roster asset the picker never opens and
        // every level fields the squad it was authored with.
        so.FindProperty("roster").objectReferenceValue =
            AssetDatabase.LoadAssetAtPath<RosterDefinitionSO>("Assets/GameData/Roster.asset");
        // Ammo stats. Optional in the same way: no catalogue means every type is Standard.
        so.FindProperty("ammoCatalog").objectReferenceValue =
            AssetDatabase.LoadAssetAtPath<AmmoCatalogSO>("Assets/GameData/AmmoCatalog.asset");
        so.FindProperty("scenery").objectReferenceValue = scenery;
        so.FindProperty("playerUnitPrefab").objectReferenceValue = playerPrefab;
        so.FindProperty("enemyUnitPrefab").objectReferenceValue = enemyPrefab;
        // Three parallel arrays rather than a serialized dictionary — Unity does not serialise
        // one, and the runner keys off the same model name LevelScenery already uses.
        FillStrings(so.FindProperty("unitClassKeys"), classKeys);
        Fill(so.FindProperty("playerUnitClassPrefabs"), playerClassPrefabs);
        Fill(so.FindProperty("enemyUnitClassPrefabs"), enemyClassPrefabs);
        so.FindProperty("projectilePrefab").objectReferenceValue = shotPrefab;
        so.FindProperty("bulletPrefab").objectReferenceValue = MakeProjectilePrefab(
            "Bullet", "projectile_bullet", 0.22f, mats.tracer, mats.tracerTail);
        so.FindProperty("rocketPrefab").objectReferenceValue = MakeProjectilePrefab(
            "Rocket", "projectile_rocket", 0.30f, mats.rocketBody, mats.rocketGlow);
        so.FindProperty("grenadePrefab").objectReferenceValue = MakeProjectilePrefab(
            "Grenade", "projectile_grenade", 0.16f, mats.grenade, mats.grenadeBand);
        so.FindProperty("gunPrefab").objectReferenceValue = gunPrefab;
        so.FindProperty("explosionPrefab").objectReferenceValue = blastPrefab;
        so.FindProperty("scorchPrefab").objectReferenceValue = scorchPrefab;
        so.FindProperty("shadowPrefab").objectReferenceValue = MakeShadowPrefab();
        so.FindProperty("flamePrefab").objectReferenceValue = MakeFlamePrefab();
        so.FindProperty("planePrefab").objectReferenceValue = MakePlanePrefab(mats);
        so.FindProperty("debrisPrefab").objectReferenceValue = MakeDebrisPrefab(mats);
        // UNLIT and TRANSPARENT. Unlit because a health bar is UI that happens to live in the
        // world, and a lit one takes the biome's light and reads as a different colour per level
        // — the one thing a green/amber/red cue must never do. TRANSPARENT because the bar FADES
        // out, and an opaque URP/Unlit ignores alpha entirely: the bar would hold full strength
        // and then vanish on one frame. This repo has paid for that once already, on the ocean
        // sun (see BackdropFadeSource).
        Mat2("healthBarSource", FadeSource("HealthBar"));
        so.FindProperty("audioFx").objectReferenceValue = MakeAudio(camGo);
        so.FindProperty("poolRoot").objectReferenceValue = poolRoot.transform;

        void Mat2(string field, Material m)
        {
            if (m == null) Debug.LogError($"[Battle] BattleRunner.{field} is NULL");
            so.FindProperty(field).objectReferenceValue = m;
        }
        so.ApplyModifiedProperties();

        EditorSceneManager.SaveScene(scene, ScenePath);
        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
        AssetDatabase.SaveAssets();
        Debug.Log($"[Battle] built {ScenePath}: {levels.Length} levels, " +
                  $"{scenery.ModelCount} models, starting on {level.displayName}");
    }

    /// <summary>
    /// Hands LevelScenery everything it needs to build a level without an AssetDatabase: every
    /// GLB in Assets/Models keyed by bare name, and the material assets it clones per level.
    ///
    /// The whole models folder goes in rather than only what the 29 levels reference today —
    /// the table costs a reference each, and a level authored in the Kotlin later should not
    /// need a scene rebuild to find its geometry.
    /// </summary>
    static void WireScenery(LevelScenery scenery, Mats mats)
    {
        var models = AssetDatabase.FindAssets("t:GameObject", new[] { "Assets/Models" })
            .Select(AssetDatabase.GUIDToAssetPath)
            .Where(p => p.EndsWith(".glb") && !p.Contains("/Kenney/"))
            .Distinct()
            .OrderBy(p => p)
            .ToArray();

        var so = new SerializedObject(scenery);
        var names = so.FindProperty("modelNames");
        var prefabs = so.FindProperty("modelPrefabs");
        names.arraySize = models.Length;
        prefabs.arraySize = models.Length;
        for (int i = 0; i < models.Length; i++)
        {
            names.GetArrayElementAtIndex(i).stringValue = LevelScenery.ModelKey(models[i]);
            prefabs.GetArrayElementAtIndex(i).objectReferenceValue =
                AssetDatabase.LoadAssetAtPath<GameObject>(models[i]);
        }

        // A null here is invisible in the scene file (one `{fileID: 0}` among dozens of correct
        // references) and does not fail until the device tries to clone it. Say so at BUILD time.
        void Mat(string field, Material m)
        {
            if (m == null) Debug.LogError($"[Battle] LevelScenery.{field} is NULL — level build will throw");
            so.FindProperty(field).objectReferenceValue = m;
        }
        Mat("unlitSource", Unlit("BackdropSource", Color.white));
        Mat("unlitFadeSource", FadeSource());
        Mat("groundSource", mats.ground);
        Mat("structPlayer", mats.structPlayer);
        Mat("structPlayerAccent", mats.structPlayerAccent);
        Mat("structEnemy", mats.structEnemy);
        Mat("structEnemyAccent", mats.structEnemyAccent);
        Mat("scorchSource", AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/Scorch.mat"));
        so.ApplyModifiedProperties();
    }

    /// <summary>
    /// A TRANSPARENT URP/Unlit material asset, cloned at runtime by anything whose shape comes
    /// from its alpha — the ocean's sun glow and sea glitter. It has to be an authored ASSET:
    /// flipping _Surface and the blend modes on a copy of an opaque material at runtime does not
    /// reliably switch the shader variant, and the sun kept a visible rectangular quad edge.
    /// </summary>
    static Material FadeSource(string name = "Backdrop")
    {
        string Path = $"Assets/Materials/{name}FadeSource.mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(Path);
        if (m == null)
        {
            m = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            AssetDatabase.CreateAsset(m, Path);
        }
        m.color = Color.white;
        m.SetFloat("_Surface", 1f);
        m.SetFloat("_Blend", 0f);
        m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
        m.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        m.SetFloat("_ZWrite", 0f);
        m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
        m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        m.DisableKeyword("_ALPHATEST_ON");
        EditorUtility.SetDirty(m);
        return m;
    }

    static void Fill(SerializedProperty array, Object[] values)
    {
        array.arraySize = values.Length;
        for (int i = 0; i < values.Length; i++)
            array.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
    }

    static void FillStrings(SerializedProperty array, string[] values)
    {
        array.arraySize = values.Length;
        for (int i = 0; i < values.Length; i++)
            array.GetArrayElementAtIndex(i).stringValue = values[i];
    }

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
    /// The airstrike's aircraft. Authored at world scale by `build_attack_plane.py` in the OLD
    /// repo's tools/blender, so it takes NO scaling here — 4.47 units long, judged against a
    /// ~1.30-unit soldier by `PlanePreview.Shots`.
    ///
    /// It wears the PLAYER's uniform, deliberately. The whole point of the beat is that the
    /// player's own side sent it; in an enemy palette it would read as being bombed.
    /// </summary>
    static GameObject MakePlanePrefab(Mats mats)
    {
        var src = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Models/attack_plane.glb");
        if (src == null) { Debug.LogWarning("[Battle] missing attack_plane"); return null; }
        var go = (GameObject)PrefabUtility.InstantiatePrefab(src);
        go.name = "AttackPlane";
        foreach (var r in go.GetComponentsInChildren<MeshRenderer>())
        {
            // Four-tone by MESH NAME prefix, the same contract every unit and structure uses.
            var n = r.gameObject.name;
            r.sharedMaterial = n.StartsWith("accent") ? mats.playerGear
                             : n.StartsWith("trim") ? mats.shellNose
                             : mats.playerUniform;
        }
        System.IO.Directory.CreateDirectory("Assets/Prefabs");
        var prefab = PrefabUtility.SaveAsPrefabAsset(go, "Assets/Prefabs/AttackPlane.prefab");
        Object.DestroyImmediate(go);
        return prefab;
    }

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
        // TRANSPARENT, so the blast can fade instead of popping out of existence. An opaque
        // sphere reads as a solid orange ball rather than as fire, however well it is animated.
        var mat = new Material(Shader.Find("Universal Render Pipeline/Unlit"))
        { color = new Color(1f, 0.55f, 0.15f, 1f) };
        mat.SetFloat("_Surface", 1f);                       // transparent
        mat.SetFloat("_Blend", 0f);                         // alpha
        mat.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
        mat.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        mat.SetFloat("_ZWrite", 0f);
        mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
        mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        mat.DisableKeyword("_ALPHATEST_ON");
        AssetDatabase.CreateAsset(mat, "Assets/Materials/Blast.mat");
        go.GetComponent<MeshRenderer>().sharedMaterial = mat;
        System.IO.Directory.CreateDirectory("Assets/Prefabs");
        var prefab = PrefabUtility.SaveAsPrefabAsset(go, "Assets/Prefabs/Blast.prefab");
        Object.DestroyImmediate(go);
        return prefab;
    }

    /// <summary>
    /// A scorch mark: a flat quad on the ground with a radial burn texture.
    ///
    /// The TINT is not set here. It comes from the level's own ground colour scaled toward black,
    /// so LevelScenery re-materials the pool on every level switch — a fixed dark blob is
    /// invisible on a dark biome and a black sticker on a bright one, and the prefab has no idea
    /// which level it is about to be used on.
    /// </summary>
    static GameObject MakeScorchPrefab()
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
        go.name = "Scorch";
        Object.DestroyImmediate(go.GetComponent<Collider>());

        // A RADIAL falloff, not a bare quad. A quad is square, so an untextured scorch renders
        // as a hard-edged dark rectangle lying on the ground — it reads as a slab, not a burn.
        // The alpha ramp also lets marks overlap without compounding into a solid black patch.
        const int Size = 64;
        var tex = new Texture2D(Size, Size, TextureFormat.RGBA32, false)
        { wrapMode = TextureWrapMode.Clamp };
        for (int y = 0; y < Size; y++)
        for (int x = 0; x < Size; x++)
        {
            float dx = (x + 0.5f) / Size - 0.5f, dy = (y + 0.5f) / Size - 0.5f;
            float d = Mathf.Sqrt(dx * dx + dy * dy) * 2f;          // 0 centre -> 1 edge
            // Solid core, then a soft shoulder: a linear ramp reads as a blurry dot.
            float a = Mathf.Clamp01(1f - Mathf.SmoothStep(0.45f, 1f, d));
            tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
        }
        tex.Apply();
        AssetDatabase.CreateAsset(tex, "Assets/Materials/ScorchTex.asset");

        var mat = new Material(Shader.Find("Universal Render Pipeline/Unlit"))
        { color = new Color(1f, 1f, 1f, 0.85f) };
        mat.mainTexture = tex;
        mat.SetFloat("_Surface", 1f);
        mat.SetFloat("_Blend", 0f);
        mat.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
        mat.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        mat.SetFloat("_ZWrite", 0f);
        mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent - 1;
        mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        AssetDatabase.CreateAsset(mat, "Assets/Materials/Scorch.mat");
        go.GetComponent<MeshRenderer>().sharedMaterial = mat;
        System.IO.Directory.CreateDirectory("Assets/Prefabs");
        var prefab = PrefabUtility.SaveAsPrefabAsset(go, "Assets/Prefabs/Scorch.prefab");
        Object.DestroyImmediate(go);
        return prefab;
    }

    /// <summary>
    /// The unit CONTACT SHADOW: a soft dark ellipse on the ground under every living soldier.
    ///
    /// This is the cue that says a unit is STANDING ON something rather than floating in front of
    /// it, and the port shipped without it — which is invisible on the tan biomes, where the
    /// ground is much darker than the sky and the horizon carries the read on its own, and
    /// obvious on WINTER, where a near-white ground against a pale sky leaves the soldiers
    /// hanging in white space. Reported exactly that way.
    ///
    /// Softer shoulder than the scorch: a burn has an edge, a shadow does not.
    /// </summary>
    static GameObject MakeShadowPrefab()
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
        go.name = "UnitShadow";
        Object.DestroyImmediate(go.GetComponent<Collider>());

        const int Size = 64;
        var tex = new Texture2D(Size, Size, TextureFormat.RGBA32, false)
        { wrapMode = TextureWrapMode.Clamp };
        for (int y = 0; y < Size; y++)
        for (int x = 0; x < Size; x++)
        {
            float dx = (x + 0.5f) / Size - 0.5f, dy = (y + 0.5f) / Size - 0.5f;
            float d = Mathf.Sqrt(dx * dx + dy * dy) * 2f;
            // A real SOLID CORE, then a soft shoulder. The first pass shouldered from 0.12 — 
            // nearly all penumbra — which on snow left a smudge too faint to read as anything.
            // Note Mathf.SmoothStep is a smoothed LERP BETWEEN its arguments, not GLSL's
            // smoothstep (this repo has paid for that once, on the ocean sun), so the useful
            // knob here is where the ramp STARTS, not a threshold.
            float a = d < 0.42f ? 1f : Mathf.Clamp01(1f - (d - 0.42f) / 0.58f);
            a *= a;                                     // ease the shoulder out
            tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
        }
        tex.Apply();
        AssetDatabase.CreateAsset(tex, "Assets/Materials/ShadowTex.asset");

        var mat = new Material(Shader.Find("Universal Render Pipeline/Unlit"))
        { color = Color.white };      // tinted per level at runtime — see BattleRunner.TintShadows
        mat.mainTexture = tex;
        mat.SetFloat("_Surface", 1f);
        mat.SetFloat("_Blend", 0f);
        mat.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
        mat.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        mat.SetFloat("_ZWrite", 0f);
        // UNDER the scorch marks, which sit at Transparent-1: a burn is on top of the snow and a
        // shadow is cast onto it, so a shadow drawn over a scorch reads as the wrong order.
        mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent - 2;
        mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        AssetDatabase.CreateAsset(mat, "Assets/Materials/UnitShadow.mat");
        go.GetComponent<MeshRenderer>().sharedMaterial = mat;
        System.IO.Directory.CreateDirectory("Assets/Prefabs");
        var prefab = PrefabUtility.SaveAsPrefabAsset(go, "Assets/Prefabs/UnitShadow.prefab");
        Object.DestroyImmediate(go);
        return prefab;
    }

    /// <summary>
    /// The INCENDIARY FLAME: what a burning soldier looks like.
    ///
    /// The burn has dealt damage since Tier 1.1 with nothing to see — the only way to confirm it
    /// had fired at all was the `[Burn]` log. This is that cue. It is up from the moment the round
    /// lands until the burn resolves at the turn handover, so it is also a TELEGRAPH: the fire
    /// says these men are about to take damage, and the health bars drop as it goes out.
    ///
    /// TWO TONGUES, one quad each, flickering out of phase (CosmeticSystems.FlameScale). One
    /// tongue is a shape that changes size; two are a fire.
    ///
    /// The colour is in the TEXTURE, not in a tint. A flame is hot-yellow at its core and deep
    /// orange at its tips, and that gradient is the single thing that separates "fire" from "an
    /// orange triangle" at this scale — a per-instance tint can only scale the whole thing at
    /// once. The property block is left for the fade-out ALPHA, which is per-slot.
    /// </summary>
    static GameObject MakeFlamePrefab()
    {
        var tex = FlameTexture();
        AssetDatabase.CreateAsset(tex, "Assets/Materials/FlameTex.asset");

        var mat = new Material(Shader.Find("Universal Render Pipeline/Unlit"))
        { color = Color.white };
        mat.mainTexture = tex;
        // TRANSPARENT and UNLIT, and both matter. Transparent because the flame is shaped
        // entirely by its alpha and because it fades out — an opaque URP/Unlit ignores alpha
        // completely, which this repo has already paid for twice (the ocean sun, the health bar).
        // Unlit because fire emits: a lit flame takes Winter's pale light and CityRuins' dim one
        // and reads as a different substance per biome.
        mat.SetFloat("_Surface", 1f);
        mat.SetFloat("_Blend", 0f);
        mat.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
        mat.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        mat.SetFloat("_ZWrite", 0f);
        mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
        mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        mat.DisableKeyword("_ALPHATEST_ON");
        AssetDatabase.CreateAsset(mat, "Assets/Materials/Flame.mat");

        var root = new GameObject("Flame");
        // The shared quad's normal faces -Z, so it needs a 180-degree turn to face the camera at
        // +Z. About Y, NOT about X — and that is not interchangeable here, which the first render
        // showed in one frame: an X flip mirrors the VERTICAL, so the flame came out standing on
        // its point with the fat hot base licking down at the soldier's boots. A Y flip mirrors
        // the horizontal instead, which costs only the direction of the tip's lean.
        //
        // The health bar takes the opposite choice for the mirror-image reason — its fill anchors
        // to one END, so it cannot afford a horizontal mirror and can afford a vertical one.
        root.transform.rotation = Quaternion.Euler(0f, 180f, 0f);
        Tongue("outer", 1f);
        Tongue("inner", 0.54f);

        void Tongue(string name, float scale)
        {
            var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
            q.name = name;
            Object.DestroyImmediate(q.GetComponent<Collider>());
            q.transform.SetParent(root.transform, false);
            q.GetComponent<MeshRenderer>().sharedMaterial = mat;
            // Both tongues are anchored at the FOOT, not at their centres, so the inner one grows
            // and shrinks out of the base rather than hovering in the middle of the outer.
            // BattleRunner sets the world size; this is the relative one.
            q.transform.localScale = new Vector3(scale, scale, 1f);
            q.transform.localPosition = new Vector3(0f, 0f, name == "inner" ? -0.004f : 0f);
        }

        System.IO.Directory.CreateDirectory("Assets/Prefabs");
        var prefab = PrefabUtility.SaveAsPrefabAsset(root, "Assets/Prefabs/Flame.prefab");
        Object.DestroyImmediate(root);
        return prefab;
    }

    /// <summary>
    /// The flame's alpha SHAPE and colour ramp, generated rather than authored so the profile is
    /// a formula anything can check — see PortSelfTest, which asserts the tongue is wide at the
    /// base and empty at the tip corners.
    ///
    /// Public because that check must ask THE TEXTURE what shape it is. A check written against
    /// the description above would only assert the description.
    ///
    /// Note the row index runs BOTTOM-UP (t = 0 is the flame's foot at v = 0), which the prefab's
    /// 180-degree X flip then turns the right way up.
    /// </summary>
    public static Texture2D FlameTexture()
    {
        const int Size = 64;
        var tex = new Texture2D(Size, Size, TextureFormat.RGBA32, false)
        { wrapMode = TextureWrapMode.Clamp };

        // Deep orange at the tip, hot near-yellow at the core. Both are well clear of the team
        // reds and greens: fire that reads as a faction colour tells the player the wrong thing.
        var tip = new Color(0.96f, 0.28f, 0.06f);
        var core = new Color(1f, 0.90f, 0.42f);

        for (int y = 0; y < Size; y++)
        {
            float t = (y + 0.5f) / Size;                       // 0 at the foot, 1 at the tip
            // The centreline LEANS as it rises. A flame drawn about a straight axis is a
            // symmetric lozenge, which reads as a leaf or a gem however it is coloured.
            float lean = 0.11f * t * t;
            // Half-width: necked in at the very bottom, widest around a quarter of the way up,
            // tapering to a point. The two factors are separate on purpose — the first is the
            // neck, the second the taper — because tuning one profile to do both ends up
            // flattening whichever end was last adjusted.
            float neck = 0.34f + 0.66f * Mathf.Min(1f, t / 0.20f);
            // 0.62 first, which held the tongue narrow-but-present all the way to the top and drew
            // a NEEDLE — six soldiers with rocket exhaust. What reads as fire is a broad body that
            // gives out, so the taper is steeper and the tip fade below does most of the ending.
            float taper = Mathf.Pow(Mathf.Max(0f, 1f - t), 0.85f);
            float w = 0.50f * neck * taper;

            for (int x = 0; x < Size; x++)
            {
                float dx = (x + 0.5f) / Size - 0.5f - lean;
                float a;
                if (w <= 1e-4f) a = 0f;
                else
                {
                    float e = Mathf.Abs(dx) / w;               // 0 centreline -> 1 edge
                    // Solid core with a soft shoulder over the outer third. Deliberately NOT
                    // Mathf.SmoothStep, which is a smoothed LERP between its first two arguments
                    // and would return a near-constant here — the trap that once drew the ocean's
                    // sun as a cream rectangle.
                    a = e >= 1f ? 0f : e < 0.62f ? 1f : 1f - (e - 0.62f) / 0.38f;
                }
                // The tip thins out rather than ending on a hard edge, and the foot is slightly
                // translucent so the soldier's boots read through the base of the fire.
                a *= Mathf.Clamp01((1f - t) / 0.34f);
                a *= Mathf.Clamp01(0.55f + t / 0.12f);

                float hot = Mathf.Clamp01((1f - t) * (1f - t));
                var c = Color.Lerp(tip, core, hot);
                tex.SetPixel(x, y, new Color(c.r, c.g, c.b, a));
            }
        }
        tex.Apply();
        return tex;
    }

    /// <summary>A rubble chunk — a lit cube in the structures' own stone tone.</summary>
    static GameObject MakeDebrisPrefab(Mats mats)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = "Debris";
        Object.DestroyImmediate(go.GetComponent<Collider>());
        // The BODY tone, not the accent. Rubble is the building's masonry, so it should read as
        // that building — and the accent is 0.30/0.24/0.18, which at debris size on open ground
        // reads as near-black scorch rather than stone.
        go.GetComponent<MeshRenderer>().sharedMaterial = mats.structEnemy;
        System.IO.Directory.CreateDirectory("Assets/Prefabs");
        var prefab = PrefabUtility.SaveAsPrefabAsset(go, "Assets/Prefabs/Debris.prefab");
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

    /// <summary>
    /// One shared trim material per CLASS, not per side. The Filament build holds trim constant
    /// across both armies on purpose: the uniform says which side a soldier is on and the trim
    /// says which class he is, and letting the faction palette touch the trim too would collapse
    /// the two readings into one. Lit, like the uniforms — an unlit prop reads as an emissive
    /// sticker at gameplay distance.
    /// </summary>
    static Material TrimMat(string classKey)
        => Lit($"UnitTrim_{classKey}", RiggedUnits.TrimColor(classKey), 0.1f, 0.25f);

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
