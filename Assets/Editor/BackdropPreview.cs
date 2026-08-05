using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using ArmedConflict.Data;
using ArmedConflict.Render;

/// <summary>
/// Renders every biome's backdrop to a PNG, headless, at the real gameplay framing:
///
///   DISPLAY=:1 $U -batchmode -quit -projectPath . -executeMethod BackdropPreview.Shots -logFile -
///
/// Eleven of the 29 levels use Winter and three use CityRuins, so judging the backdrop from the
/// one level that happens to be built into the scene sees a fifth of the game. A device build is
/// ~3 minutes; this is seconds, and it is the only way to look at a biome no level has loaded yet.
///
/// It renders through BackdropRuntime — the same code the game builds a level's backdrop with —
/// so what the preview shows and what the player sees cannot drift apart. It used to be a
/// hand-copied second implementation, which was tolerable only while the game's own backdrop was
/// baked into the scene by a third.
/// </summary>
public static class BackdropPreview
{
    const int Width = 540;
    const int Height = 1202;   // half the device's 1080x2404, same aspect

    public static void Shots()
    {
        Directory.CreateDirectory("Builds/backdrops");
        foreach (var guid in AssetDatabase.FindAssets("t:BackgroundDefinitionSO"))
            Shot(AssetDatabase.GUIDToAssetPath(guid));
        Debug.Log("[BackdropPreview] wrote Builds/backdrops/*.png");
    }

    /// <summary>
    /// The background is loaded AFTER the new scene, and that order is load-bearing.
    ///
    /// `NewScene` triggers an unused-asset unload, and a freshly emptied scene references nothing
    /// — so a BackgroundDefinitionSO loaded beforehand has its NATIVE object freed and becomes
    /// Unity's "fake null": `bg == null` is true, while `bg.style` and `bg.groundColor` still read
    /// correctly off the managed wrapper. The old preview never noticed, because it only ever read
    /// fields. `BackdropRuntime` opens with a null guard — right for the game, silently true here —
    /// so every biome rendered as bare sky and ground with no error anywhere.
    /// </summary>
    static void Shot(string assetPath)
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        var bg = AssetDatabase.LoadAssetAtPath<BackgroundDefinitionSO>(assetPath);
        if (bg == null) { Debug.LogWarning($"[BackdropPreview] cannot load {assetPath}"); return; }
        string outPath = $"Builds/backdrops/{Path.GetFileNameWithoutExtension(assetPath)}.png";

        var owned = new List<Object>();
        var unlitSource = Mat(Color.white);
        // The SAME transparent asset the game clones — a preview that builds its own would be
        // testing a different material than the one that ships.
        var fadeSource = AssetDatabase.LoadAssetAtPath<Material>(
            "Assets/Materials/BackdropFadeSource.mat");
        if (fadeSource == null)
            Debug.LogError("[BackdropPreview] BackdropFadeSource.mat missing — run SpikeSceneBattle.Build");
        else if (bg.style == ArmedConflict.Data.SilhouetteStyle.Ocean)
            Debug.Log($"[Probe] fade mat: shader={fadeSource.shader.name} queue={fadeSource.renderQueue} " +
                      $"surface={fadeSource.GetFloat("_Surface")} zwrite={fadeSource.GetFloat("_ZWrite")} " +
                      $"src={fadeSource.GetFloat("_SrcBlend")} dst={fadeSource.GetFloat("_DstBlend")} " +
                      $"kw=[{string.Join(",", fadeSource.shaderKeywords)}]");

        var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
        ground.transform.position = new Vector3(0f, 0f, 17f);
        ground.transform.localScale = new Vector3(30f, 1f, 9f);
        ground.GetComponent<MeshRenderer>().sharedMaterial = Mat(bg.groundColor);

        var root = new GameObject("Backdrop").transform;
        // The preview's aspect, not the design one: the shot is 540x1202 and a backdrop sized for
        // a different frustum would be the wrong picture to judge.
        BackdropRuntime.Build(bg, (float)Width / Height, root, unlitSource, fadeSource, owned);
        if (root.childCount == 0)
            Debug.LogError($"[BackdropPreview] {bg.style} built NO layers — the shot will be blank");

        var camGo = new GameObject("Cam");
        var cam = camGo.AddComponent<Camera>();
        cam.fieldOfView = BattleCamera.VerticalFovDegrees;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = bg.skyHorizon;
        cam.nearClipPlane = 0.1f;
        cam.farClipPlane = 300f;
        // The framing the player judges a shot at — see CAMERA_ARCHITECTURE.md.
        BattleCamera.Apply(cam, 0f, BattleCamera.CameraY, 11f);

        var rt = new RenderTexture(Width, Height, 24) { antiAliasing = 1 };
        cam.targetTexture = rt;
        cam.Render();
        RenderTexture.active = rt;
        var shot = new Texture2D(Width, Height, TextureFormat.RGB24, false);
        shot.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
        shot.Apply();
        RenderTexture.active = null;
        cam.targetTexture = null;

        File.WriteAllBytes(outPath, shot.EncodeToPNG());
        Object.DestroyImmediate(rt);
    }

    static Material Mat(Color c)
        => new Material(Shader.Find("Universal Render Pipeline/Unlit")) { color = c };
}
