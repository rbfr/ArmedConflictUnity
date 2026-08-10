using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using ArmedConflict.Game;

/// <summary>
/// Renders the airstrike's ATTACK PLANE at the real gameplay framing, headless:
///
///   DISPLAY=:1 $U -batchmode -quit -projectPath . -executeMethod PlanePreview.Shots -logFile -
///
/// Why it exists, and it is the same argument FlamePreview makes: no test can say whether a
/// 4-unit aircraft crossing at y=7 READS as an attack plane to somebody holding a phone. What a
/// test can do is confirm the mesh imported. Those are different questions, and this project has
/// a documented history of the second one passing while the first was wrong — the flame shipped
/// UPSIDE DOWN through a green test suite and its preview caught it on the first frame.
///
/// It renders the SHIPPED GLB with its authored materials, not a hand-built stand-in, for the
/// reason BackdropPreview learned the hard way: a second implementation produces plausible,
/// wrong pictures.
///
/// The thing being judged here is specifically the SILHOUETTE FROM BELOW. `BattleCamera` sits at
/// y=1.2 looking UP ~14 degrees, so a plane at y=7 is seen from roughly 28 degrees underneath —
/// wings at about half planform, underside dominant. Judging this model from a side elevation in
/// Blender would answer a question nobody asks.
///
/// AND IT IS STILL NOT THE DEVICE. Frames are written at several heights and screen positions
/// because "does it read" changes with both, and none of them can show it MOVING.
/// </summary>
public static class PlanePreview
{
    const int Width = 540;
    const int Height = 1202;      // half the device's 1080x2404, same aspect

    const string ModelPath = "Assets/Models/attack_plane.glb";

    /// <summary>
    /// Heights to judge, in GAME units. The airstrike currently spawns its bomb at y=5, and a
    /// plane has to fly ABOVE whatever it drops — these bracket the plausible band. Height is the
    /// one number that trades legibility (lower is bigger) against reading as an AIRCRAFT rather
    /// than as a low-flying prop (higher is more plainly in the sky).
    /// </summary>
    static readonly float[] Heights = { 5.5f, 7f, 9f };

    public static void Shots()
    {
        Directory.CreateDirectory("Builds/plane");

        AssetDatabase.Refresh();
        var model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
        if (model == null)
        {
            Debug.LogError($"[PlanePreview] {ModelPath} missing — run " +
                           "tools/blender/build_attack_plane.py in the OLD repo first");
            return;
        }

        foreach (var y in Heights) Shot(model, y, 0f, $"y{y:F1}_center");
        // Entering frame from the player's side, which is where the beat starts. A silhouette that
        // only reads dead-centre is no use: this is the frame the player first sees it in.
        // x=-7 is very near the edge at this framing — 90 VFOV at camZ 11 does not reach much
        // further, which is itself worth knowing before authoring a flight path.
        Shot(model, 7f, -7f, "y7.0_entering");
        Variants(model);
        Debug.Log($"[PlanePreview] wrote frames to Builds/plane/*.png");
    }

    /// <summary>
    /// The straight wing does NOT read at this camera unmodified: the span runs along DEPTH, and
    /// seen from 28 degrees below it projects vertically, so the aircraft comes out as a cross-
    /// shaped blob rather than a plane. Two levers can fix that and both are cheap — BANK (a
    /// runtime roll, free) and SPAN (an author-time change to the builder).
    ///
    /// This renders the grid rather than arguing about it, per the standing rule that a silhouette
    /// is judged across the SET and not one specimen at a time. Span is approximated here by
    /// scaling the instance along its span axis; if a narrower span wins, it gets rebuilt properly
    /// in Blender rather than shipped as a squashed import.
    /// </summary>
    static void Variants(GameObject model)
    {
        foreach (float bank in new[] { 0f, 25f, 45f })
        foreach (float span in new[] { 1.0f, 0.7f })
            Shot(model, 7f, 0f, $"bank{bank:F0}_span{span:F1}", bank, span);
    }

    static void Shot(GameObject model, float planeY, float planeX, string name,
                     float bankDegrees = 0f, float spanScale = 1f)
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
        ground.transform.position = new Vector3(0f, 0f, 14f);
        ground.transform.localScale = new Vector3(30f, 1f, 9f);
        ground.GetComponent<MeshRenderer>().sharedMaterial = Mat(new Color(0.62f, 0.53f, 0.40f));

        // URP reads per-light settings off this companion component; without it every LIT
        // material renders BLACK in batchmode. Order matters — the data component RequireComponents
        // a Light and adds one itself, and a GameObject may hold only one. See FlamePreview.
        var lightGo = new GameObject("Sun");
        var light = lightGo.AddComponent<Light>();
        if (lightGo.GetComponent<UnityEngine.Rendering.Universal.UniversalAdditionalLightData>() == null)
            lightGo.AddComponent<UnityEngine.Rendering.Universal.UniversalAdditionalLightData>();
        light.type = LightType.Directional;
        light.intensity = 1.1f;
        lightGo.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(0.55f, 0.56f, 0.6f);

        // A rank of soldiers on the ground, for SCALE. The plane's size is only meaningful next to
        // the thing the player is looking at the rest of the time; judged alone, any size looks
        // fine because nothing contradicts it.
        var unit = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/EnemyUnit.prefab");
        if (unit != null)
        {
            for (int i = 0; i < 6; i++)
            {
                var body = Object.Instantiate(unit);
                body.SetActive(true);
                body.transform.position = GameSpace.ToUnity(-1.4f + i * 0.56f, 0f, 0f);
            }
        }

        // The plane. Placement routed through GameSpace like every other placement — it negates X,
        // and a mirrored scene looks entirely plausible, which is exactly how it survives review.
        var plane = Object.Instantiate(model);
        plane.SetActive(true);
        plane.transform.position = GameSpace.ToUnity(planeX, planeY, 0f);
        // The GLB is authored nose toward +X = toward the enemy (build_tank.py's convention). In
        // Unity that is -X once GameSpace has negated it, so the model turns to face the way it is
        // actually travelling. Getting this wrong flies it backwards, which reads as a retreat.
        //
        // The BANK is applied about the travel axis AFTER that turn, so a positive angle always
        // rolls the same way relative to the camera whichever way the aircraft is pointed.
        plane.transform.rotation = Quaternion.Euler(0f, 180f, 0f) * Quaternion.Euler(bankDegrees, 0f, 0f);
        if (!Mathf.Approximately(spanScale, 1f))
            plane.transform.localScale = new Vector3(1f, 1f, spanScale);   // span runs along Z

        var camGo = new GameObject("Cam");
        var cam = camGo.AddComponent<Camera>();
        cam.fieldOfView = BattleCamera.VerticalFovDegrees;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.45f, 0.62f, 0.82f);   // sky, which is what it flies against
        cam.nearClipPlane = 0.1f;
        cam.farClipPlane = 300f;
        // Resolve framing — the distance the volley is judged at, and where the plane would fly.
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

        File.WriteAllBytes($"Builds/plane/{name}.png", shot.EncodeToPNG());
        Object.DestroyImmediate(rt);
    }

    static Material Mat(Color c)
        => new Material(Shader.Find("Universal Render Pipeline/Unlit")) { color = c };
}
