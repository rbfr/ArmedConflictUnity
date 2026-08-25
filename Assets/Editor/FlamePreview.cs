using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using ArmedConflict.Data;
using ArmedConflict.Game;
using ArmedConflict.Render;

/// <summary>
/// Renders the incendiary FLAME on a line of soldiers, headless, at the real gameplay framing:
///
///   DISPLAY=:1 $U -batchmode -quit -projectPath . -executeMethod FlamePreview.Shots -logFile -
///
/// Why it exists: the flame is the one part of the burn a test cannot judge. PortSelfTest can
/// prove the texture tapers and the tongues swing out of phase; it cannot say whether the result
/// reads as FIRE on a soldier at the distance the player sits from him. A device build is minutes
/// and needs a phone; this is seconds.
///
/// It renders through <see cref="FlameRig"/> and the shipped Flame.prefab — the same placement
/// and the same art the game uses — so the preview and the game cannot drift. BackdropPreview was
/// once a hand-copied second implementation and spent a session producing plausible, wrong
/// pictures; that is the mistake this comment exists to stop repeating.
///
/// AND IT IS STILL NOT THE DEVICE. `CLAUDE.md`: never judge a visual from the preview alone.
/// Several frames are written at different times precisely because a flicker cannot be judged
/// from one, and even so a still cannot show the guttering.
/// </summary>
public static class FlamePreview
{
    const int Width = 540;
    const int Height = 1202;      // half the device's 1080x2404, same aspect

    /// <summary>Where the frames are sampled from. Not evenly spaced: the flicker is two
    /// non-harmonic sines, so evenly spaced samples of it can alias into looking periodic.</summary>
    static readonly float[] Times = { 0f, 0.031f, 0.074f, 0.119f };

    public static void Shots()
    {
        Directory.CreateDirectory("Builds/flame");

        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Flame.prefab");
        if (prefab == null)
        {
            Debug.LogError("[FlamePreview] Assets/Prefabs/Flame.prefab missing — " +
                           "run SpikeSceneBattle.Build first");
            return;
        }

        foreach (var t in Times) Shot(prefab, t);
        Debug.Log($"[FlamePreview] wrote {Times.Length} frames to Builds/flame/*.png");
    }

    static void Shot(GameObject flamePrefab, float time)
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        // A biome to judge it against. CityRuins is the darkest ground in the game and Winter the
        // brightest; fire has to read on both, so the preview uses the DARK one — a glow always
        // survives a bright background and can vanish into a dark one.
        var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
        ground.transform.position = new Vector3(0f, 0f, 17f);
        ground.transform.localScale = new Vector3(30f, 1f, 9f);
        ground.GetComponent<MeshRenderer>().sharedMaterial = Mat(new Color(0.24f, 0.22f, 0.21f));

        var lightGo = new GameObject("Sun");
        // URP reads its per-light settings off this companion component. AddComponent<Light>()
        // alone leaves it absent in batchmode, and every LIT material then renders BLACK while
        // the unlit ground renders correctly — which showed up here as six soldier-shaped holes.
        // ORDER MATTERS: the data component RequireComponents a Light and adds one itself, so
        // adding it first and then a Light throws (a GameObject may hold only one Light).
        var light = lightGo.AddComponent<Light>();
        if (lightGo.GetComponent<UnityEngine.Rendering.Universal.UniversalAdditionalLightData>() == null)
            lightGo.AddComponent<UnityEngine.Rendering.Universal.UniversalAdditionalLightData>();
        light.type = LightType.Directional;
        light.intensity = 1.1f;
        lightGo.transform.rotation = Quaternion.Euler(50f, 210f, 0f);   // see SpikeSceneBattle: -30 lit the army from BEHIND
        // An empty scene has no ambient probe, so the lit unit materials came out as black
        // silhouettes on the first run — which is a fine way to see the flame and a useless way to
        // judge it against a soldier. The game gets its ambient from the real scene's lighting.
        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(0.55f, 0.56f, 0.6f);

        // A rank of soldiers, half of them alight — the read that matters is whether a BURNING man
        // is instantly distinguishable from the one standing next to him.
        var unit = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/EnemyUnit.prefab");
        const int Count = 6;
        for (int i = 0; i < Count; i++)
        {
            float x = -1.4f + i * 0.56f;
            if (unit != null)
            {
                var body = Object.Instantiate(unit);
                body.SetActive(true);
                body.transform.position = GameSpace.ToUnity(x, 0f, 0f);
            }
            // Every other man burns. Ids are consecutive on purpose: that is what a spawned group
            // looks like, and it is the case FlamePhase has to scatter.
            if (i % 2 != 0) continue;
            var f = Object.Instantiate(flamePrefab);
            f.SetActive(true);
            FlameRig.Place(f.transform, f.transform.Find("outer"), f.transform.Find("inner"),
                           x, 0f, 0f, 1f, time, i);
        }

        var camGo = new GameObject("Cam");
        var cam = camGo.AddComponent<Camera>();
        cam.fieldOfView = BattleCamera.VerticalFovDegrees;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.42f, 0.45f, 0.5f);
        cam.nearClipPlane = 0.1f;
        cam.farClipPlane = 300f;
        // The framing the player judges a volley at — see CAMERA_ARCHITECTURE.md.
        BattleCamera.Apply(cam, 0f, BattleCamera.CameraY, 6f);

        var rt = new RenderTexture(Width, Height, 24) { antiAliasing = 1 };
        cam.targetTexture = rt;
        cam.Render();
        RenderTexture.active = rt;
        var shot = new Texture2D(Width, Height, TextureFormat.RGB24, false);
        shot.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
        shot.Apply();
        RenderTexture.active = null;
        cam.targetTexture = null;

        File.WriteAllBytes($"Builds/flame/t{time:F3}.png", shot.EncodeToPNG());
        Object.DestroyImmediate(rt);
    }

    static Material Mat(Color c)
        => new Material(Shader.Find("Universal Render Pipeline/Unlit")) { color = c };
}
