using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Builds the UNITY_SPIKE.md Step 1 scene headlessly: untextured cube + ground plane.
/// Run with: -batchmode -quit -executeMethod SpikeScene.BuildStep1
/// </summary>
public static class SpikeScene
{
    const string ScenePath = "Assets/Scenes/Step1.unity";
    const string Step2Path = "Assets/Scenes/Step2.unity";

    /// <summary>
    /// Step 2 scene: a real 3D ground plane plus 1.0-unit reference poles, including poles
    /// OFF-CENTRE IN Z. Those exist to check the spike doc's claim that groundLift is not
    /// needed here — the Filament correction existed only because the ground was a painted
    /// 2D band with no perspective.
    /// </summary>
    public static void BuildStep2()
    {
        System.IO.Directory.CreateDirectory("Assets/Scenes");
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        var mat = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/SpikeUntextured.mat");
        var poleMat = new Material(Shader.Find("Universal Render Pipeline/Lit"))
        {
            color = new Color(0.85f, 0.35f, 0.25f),
        };
        AssetDatabase.CreateAsset(poleMat, "Assets/Materials/SpikePole.mat");

        var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
        ground.name = "Ground";
        ground.transform.localScale = new Vector3(12f, 1f, 12f);
        ground.GetComponent<MeshRenderer>().sharedMaterial = mat;

        Transform centrePole = null;
        // z != 0 poles are the groundLift check; x spread keeps them individually visible.
        var spots = new (float x, float z)[]
        {
            (0f, 0f), (-2.5f, -4f), (2.5f, 4f), (-5f, 2f), (5f, -2f),
        };
        foreach (var (x, z) in spots)
        {
            var pole = GameObject.CreatePrimitive(PrimitiveType.Cube);
            pole.name = $"Pole_x{x}_z{z}";
            pole.transform.localScale = new Vector3(0.1f, 1f, 0.1f);
            pole.transform.position = new Vector3(x, 0.5f, z); // base exactly on y=0
            pole.GetComponent<MeshRenderer>().sharedMaterial = poleMat;
            if (x == 0f && z == 0f) centrePole = pole.transform;
        }

        var lightGo = new GameObject("Directional Light");
        var light = lightGo.AddComponent<Light>();
        light.type = LightType.Directional;
        light.shadows = LightShadows.None;
        lightGo.transform.rotation = Quaternion.Euler(50f, 210f, 0f);   // see SpikeSceneBattle: -30 lit the army from BEHIND

        var camGo = new GameObject("Main Camera");
        camGo.tag = "MainCamera";
        var cam = camGo.AddComponent<Camera>();
        cam.fieldOfView = BattleCamera.VerticalFovDegrees;
        cam.usePhysicalProperties = false;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.16f, 0.18f, 0.22f);
        cam.nearClipPlane = 0.1f;
        cam.farClipPlane = 200f;

        var verify = camGo.AddComponent<Step2Verify>();
        var so = new SerializedObject(verify);
        so.FindProperty("cam").objectReferenceValue = cam;
        so.FindProperty("referencePole").objectReferenceValue = centrePole;
        so.ApplyModifiedProperties();

        EditorSceneManager.SaveScene(scene, Step2Path);
        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(Step2Path, true) };

        AssetDatabase.SaveAssets();
        Debug.Log($"[SpikeScene] built {Step2Path}");
    }

    public static void BuildStep1()
    {
        System.IO.Directory.CreateDirectory("Assets/Scenes");
        System.IO.Directory.CreateDirectory("Assets/Materials");

        var scene = EditorSceneManager.NewScene(
            NewSceneSetup.EmptyScene, NewSceneMode.Single);

        var mat = new Material(Shader.Find("Universal Render Pipeline/Lit"))
        {
            color = new Color(0.72f, 0.72f, 0.74f),
        };
        AssetDatabase.CreateAsset(mat, "Assets/Materials/SpikeUntextured.mat");

        var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
        ground.name = "Ground";
        ground.transform.localScale = new Vector3(4f, 1f, 4f);
        ground.GetComponent<MeshRenderer>().sharedMaterial = mat;

        var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
        cube.name = "Cube";
        cube.transform.position = new Vector3(0f, 0.5f, 0f);
        cube.GetComponent<MeshRenderer>().sharedMaterial = mat;

        var lightGo = new GameObject("Directional Light");
        var light = lightGo.AddComponent<Light>();
        light.type = LightType.Directional;
        light.shadows = LightShadows.None; // the game draws its own blob contact shadows
        lightGo.transform.rotation = Quaternion.Euler(50f, 210f, 0f);   // see SpikeSceneBattle: -30 lit the army from BEHIND

        var camGo = new GameObject("Main Camera");
        camGo.tag = "MainCamera";
        var cam = camGo.AddComponent<Camera>();
        cam.fieldOfView = 90f;               // vertical FOV; the Step 2 solve depends on this
        cam.usePhysicalProperties = false;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.16f, 0.18f, 0.22f);
        camGo.transform.position = new Vector3(0f, 1.2f, -6f);
        camGo.transform.LookAt(new Vector3(0f, 0.5f, 0f), Vector3.up);

        var probe = camGo.AddComponent<Step1Probe>();
        var so = new SerializedObject(probe);
        so.FindProperty("spinner").objectReferenceValue = cube.transform;
        so.ApplyModifiedProperties();

        EditorSceneManager.SaveScene(scene, ScenePath);
        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };

        AssetDatabase.SaveAssets();
        Debug.Log($"[SpikeScene] built {ScenePath}");
    }
}
