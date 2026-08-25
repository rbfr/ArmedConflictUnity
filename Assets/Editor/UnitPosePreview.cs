using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Renders the soldier's WEAPON HOLD, headless:
///
///   DISPLAY=:1 $U -batchmode -quit -projectPath . -executeMethod UnitPosePreview.Shots -logFile -
///
/// Why it exists. Rob, 2026-08-25, looking at a zoomed-in device frame: *"it's not a natural
/// pose. both arms are sticking out/raised, and the gun is in the right hand."* He is right, and
/// the cause is structural rather than a bad number: the arms are posed by Kenney's `holding-both`
/// clip, which pitches BOTH arms -90 degrees about X so they point straight downfield, while
/// `RiggedUnits.AttachGun` hangs the rifle off `arm-right` alone. The left hand therefore holds
/// nothing and the weapon sits out at one shoulder instead of on the centreline.
///
/// Tuning that against the device costs a scene build, an APK build, an install and a navigation
/// per attempt — call it eight minutes to look at one pair of angles. This renders the SAME
/// prefab, sampling the SAME clip, in about two.
///
/// IT SAMPLES THE SHIPPED CLIP RATHER THAN POSING A STAND-IN. A preview that builds its own idea
/// of the hold would produce plausible, wrong pictures — the mistake `BackdropPreview` and
/// `PlanePreview` both record. `Animation` does not run in batchmode, so the clip is applied with
/// `SampleAnimation`, which is what the runtime layer would have written.
///
/// AND IT IS STILL NOT THE DEVICE. It cannot show the hold moving, crossfading out of a march, or
/// sitting in a rank at real size. Judge the candidate here; confirm the winner on the phone.
/// </summary>
public static class UnitPosePreview
{
    const int Width = 720, Height = 720;
    const string OutDir = "Builds/pose";

    /// <summary>The clip the arms are actually held by — `UnitAnim.Hold`.</summary>
    const string HoldClip = "holding-both";

    /// <summary>
    /// Candidate poses. Each is a pair of shoulder-frame corrections applied ON TOP of the hold
    /// clip, exactly as <see cref="UnitAnim"/> applies its aim lift — pre-multiplied, so the
    /// rotation happens in the shoulder's frame rather than rolling the arm about its own length.
    ///
    /// `inward` yaws an arm toward the centreline (bringing the hand across the body onto the
    /// weapon); `drop` pitches it down from the horizontal so the arms stop reading as a zombie's.
    /// The right arm carries the grip and stays tucked; the left reaches further across to meet
    /// the forestock.
    /// </summary>
    static readonly (string Name, float LeftInward, float LeftDrop, float RightInward, float RightDrop,
                     float GunShiftX)[] Candidates =
        {
            // WHAT SHIPS, read off UnitAnim rather than retyped — the gun offset is baked into
            // the prefab now, so this candidate needs no shift of its own and IS the device.
            ("z_shipped",  UnitAnim.HoldLeftInward, UnitAnim.HoldLeftDrop,
                           UnitAnim.HoldRightInward, UnitAnim.HoldRightDrop, 0f),
            ("a_noCorrection", 0f, 0f, 0f, 0f, 0f),   // the clip alone, for contrast
            ("b_45_-0.25", 45f,  4f,   6f,  4f, -0.25f),
            ("c_50_-0.35", 50f,  4f,   8f,  4f, -0.35f),
            ("d_55_-0.45", 55f,  6f,  10f,  6f, -0.45f),
        };

    public static void Shots()
    {
        Directory.CreateDirectory(OutDir);
        AssetDatabase.Refresh();

        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
            "Assets/Prefabs/PlayerUnit_unit_rifleman.prefab")
            ?? AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/PlayerUnit.prefab");
        if (prefab == null) { Debug.LogError("[PosePreview] no rifleman prefab"); return; }

        // WARM-UP FRAME, DISCARDED. The FIRST render of a batchmode session comes out unlit —
        // black body, no key light — reproducibly, whatever it contains. That silently poisoned
        // the CONTROL, which is always rendered first: `a_current` came out black next to a green
        // `c_natural`, which reads as the pose change having fixed the colour. It had not. Render
        // one frame nobody looks at, and every frame that is judged is lit.
        Shot(prefab, Candidates[0], 0f, "_warmup");

        foreach (var c in Candidates)
            foreach (var yaw in new[] { 0f, 35f })          // straight-on, and the 3/4 the camera sees
                Shot(prefab, c, yaw, $"{c.Name}_yaw{yaw:F0}");

        Debug.Log($"[PosePreview] wrote {Candidates.Length * 2} frames to {OutDir}/*.png");
    }

    static void Shot(GameObject prefab,
                     (string Name, float LeftInward, float LeftDrop, float RightInward, float RightDrop,
                      float GunShiftX) c,
                     float yawDegrees, string name)
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        // URP reads per-light settings off this companion component; without it every LIT material
        // renders BLACK in batchmode (see FlamePreview). The DIRECTION is the game's own, post-fix:
        // judging a pose under the light that made the army look black is judging the wrong thing.
        var lightGo = new GameObject("Sun");
        var light = lightGo.AddComponent<Light>();
        if (lightGo.GetComponent<UnityEngine.Rendering.Universal.UniversalAdditionalLightData>() == null)
            lightGo.AddComponent<UnityEngine.Rendering.Universal.UniversalAdditionalLightData>();
        light.type = LightType.Directional;
        light.intensity = 1.1f;
        lightGo.transform.rotation = Quaternion.Euler(50f, 210f, 0f);
        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(0.32f, 0.34f, 0.38f);

        var unit = Object.Instantiate(prefab);
        unit.SetActive(true);
        unit.transform.position = Vector3.zero;
        unit.transform.rotation = Quaternion.Euler(0f, yawDegrees, 0f);

        // The clip first, then the correction on top — the same order the runtime uses, where
        // LateUpdate runs after legacy Animation has written the arms.
        // THE CLIP COMES OFF THE INSTANCE, not off the GLB. RiggedUnits RETARGETS every clip
        // before binding it (the Kenney paths do not match our hierarchy), so the raw asset
        // samples onto nothing — which is precisely what the probe below caught: every joint sat
        // at identity while the picture looked like a plausible rest pose.
        //
        // And it is sampled on the ANIMATION'S OWN GAMEOBJECT: curve paths are relative to the
        // component's object, which is the GLB root, one level under the prefab root. RiggedUnits
        // has the same warning written over the AddComponent call.
        var probeAnim = unit.GetComponentInChildren<Animation>(true);
        var rigRoot = probeAnim != null ? probeAnim.gameObject : unit;
        var clip = probeAnim != null && probeAnim[HoldClip] != null ? probeAnim[HoldClip].clip : null;
        if (clip != null) clip.SampleAnimation(rigRoot, 0f);
        else Debug.LogWarning($"[PosePreview] '{HoldClip}' not bound on the prefab — rest pose only");
        // PROVE THE CLIP LANDED. SampleAnimation is silent when the paths do not match the
        // hierarchy, and a preview of the REST pose that everyone reads as the hold is exactly
        // the "plausible, wrong picture" this file was written to avoid.
        var probeArm = rigRoot.transform.Find("torso/arm-left");
        string armLog = probeArm != null
            ? $", arm-left after clip {probeArm.localRotation.eulerAngles}"
            : ", arm-left NOT FOUND";
        Apply(unit, c.LeftInward, c.LeftDrop, c.RightInward, c.RightDrop);
        // THE WEAPON HAS TO COME INBOARD TOO. With one bone per arm the left hand can travel at
        // most an arm's length across, and the rifle sat OUTBOARD of the right shoulder — no
        // angle reaches it. Swept here on the instance so a value can be chosen before it is
        // baked into RiggedUnits.AttachGun, which only runs on a prefab rebuild.
        if (c.GunShiftX != 0f)
        {
            var g = (probeAnim != null ? probeAnim.transform : unit.transform)
                .Find("torso/arm-right/gun");
            if (g != null) g.localPosition += new Vector3(c.GunShiftX, 0f, 0f);
        }
        if (probeArm != null) armLog += $" -> {probeArm.localRotation.eulerAngles}";

        // WHERE THE HANDS AND THE WEAPON ACTUALLY ARE, in the rig's own space. Rob, after the
        // first pass: "still see the same - one arm out, other arm holds weapon." Eyeballing a
        // 35-degree yaw is how that happened — the hand moved half the distance it needed to and
        // the picture still looked like progress. These numbers say how far short it falls, so
        // the fix can be solved instead of nudged.
        Measure(unit, c.Name, yawDegrees);

        // AND THE READY DROP ON TOP, because the runtime always has one. UnitAnim settles the
        // idle at -ReadyDrop (low ready) and applies it as the arm lift in LateUpdate, so a
        // preview without it shows a pose 16 degrees higher than any player will ever see — and
        // I would have tuned the hold's own drop against a picture that does not exist.
        ApplyReady(unit);

        var camGo = new GameObject("Cam");
        var cam = camGo.AddComponent<Camera>();
        cam.fieldOfView = 32f;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.58f, 0.50f, 0.38f);   // the ground it is normally seen against
        cam.nearClipPlane = 0.05f;
        cam.farClipPlane = 50f;
        // FRAMED FROM THE MODEL'S ACTUAL BOUNDS, not from a typed distance. The prefab is
        // normalised to UnitScaleUnits at build time, so it is nowhere near the 2.70 the BUILDER
        // works in — the first version of this preview assumed 2.7, put the camera at 5.2m and
        // rendered a soldier 20px tall in the corner. Bounds cannot be wrong about that.
        var bounds = new Bounds(unit.transform.position, Vector3.zero);
        bool any = false;
        foreach (var r in unit.GetComponentsInChildren<Renderer>(true))
        {
            if (any) bounds.Encapsulate(r.bounds); else { bounds = r.bounds; any = true; }
        }
        float h = Mathf.Max(bounds.size.y, 0.01f);
        // Fill ~80% of the frame vertically.
        float dist = (h / 0.8f) * 0.5f / Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad);
        cam.transform.position = bounds.center + new Vector3(0f, h * 0.12f, dist);
        cam.transform.rotation = Quaternion.Euler(4f, 180f, 0f);
        Debug.Log($"[PosePreview] {name}: model height {h:F2}, cam z {dist:F2}" +
                  (armLog ?? ""));

        var rt = new RenderTexture(Width, Height, 24) { antiAliasing = 1 };
        cam.targetTexture = rt;
        cam.Render();
        RenderTexture.active = rt;
        var tex = new Texture2D(Width, Height, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
        tex.Apply();
        RenderTexture.active = null;
        cam.targetTexture = null;
        File.WriteAllBytes($"{OutDir}/{name}.png", tex.EncodeToPNG());
        Object.DestroyImmediate(rt);
    }

    /// <summary>
    /// Prints hand and weapon positions in the RIG's local space, where lateral offset is just x.
    /// The hand meshes are `skin_arm-*`; the weapon is the `gun` child AttachGun parents to
    /// `arm-right`. Measured after the hold correction and before the ready drop, because the
    /// ready drop is a shared pitch and does not change the lateral gap being closed.
    /// </summary>
    static void Measure(GameObject unit, string label, float yaw)
    {
        if (yaw != 0f) return;                       // one report per candidate is enough
        var anim = unit.GetComponentInChildren<Animation>(true);
        var rig = anim != null ? anim.transform : unit.transform;

        // RENDERER BOUNDS, not transform.position. A mesh node's ORIGIN sits on its joint — the
        // first version of this printed both hands at +/-0.421, 2.160 for every candidate, which
        // is the SHOULDER pair, unchanged because the correction rotates about exactly that point.
        // The geometry is what moves, so the geometry is what has to be measured.
        Vector3 Local(Transform t)
        {
            if (t == null) return Vector3.zero;
            var r = t.GetComponent<Renderer>() ?? t.GetComponentInChildren<Renderer>();
            return rig.InverseTransformPoint(r != null ? r.bounds.center : t.position);
        }

        var lh = rig.Find("torso/arm-left/skin_arm-left");
        var rh = rig.Find("torso/arm-right/skin_arm-right");
        var gun = rig.Find("torso/arm-right/gun");
        if (lh == null || rh == null)
        { Debug.LogWarning($"[PosePreview] {label}: hand meshes not found"); return; }

        var l = Local(lh); var r = Local(rh);
        string g = gun != null ? Local(gun).ToString("F3") : "NO GUN";
        Debug.Log($"[PoseMeasure] {label}: left hand {l:F3}, right hand {r:F3}, gun {g}, " +
                  $"lateral gap left->right {Mathf.Abs(l.x - r.x):F3}");
    }

    /// <summary>
    /// The idle arm lift the runtime is always applying — `UnitAnim` settles `shownAim` at
    /// -ReadyDrop and pitches the arms by it. Reproduced from the same constants rather than a
    /// typed 16, so it cannot drift from the game.
    /// </summary>
    static void ApplyReady(GameObject unit)
    {
        var anim = unit.GetComponentInChildren<Animation>(true);
        var rig = anim != null ? anim.transform : unit.transform;
        UnitAnim.SplitAim(-UnitAnim.ReadyDrop, wholeBody: true, out _, out float armAim);
        var lift = UnitAnim.ArmLift(armAim);
        foreach (var path in new[] { "torso/arm-left", "torso/arm-right" })
        {
            var t = rig.Find(path);
            if (t != null) t.localRotation = lift * t.localRotation;
        }
    }

    /// <summary>
    /// The correction under test, in ONE place so the preview and the runtime cannot drift.
    /// <see cref="UnitAnim"/> calls this too — a preview that applies its own version of the pose
    /// is a preview of something the player never sees.
    /// </summary>
    public static void Apply(GameObject unit, float leftInward, float leftDrop,
                             float rightInward, float rightDrop)
    {
        // The rig sits under whatever the prefab's root is, and UnitAnim resolves it from the
        // Animation component's transform rather than from the prefab root. Search rather than
        // assume a depth: a preview that silently finds nothing renders the unposed model and
        // looks like the correction did nothing.
        var anim = unit.GetComponentInChildren<Animation>(true);
        var rig = anim != null ? anim.transform : unit.transform;
        var armL = rig.Find("torso/arm-left");
        var armR = rig.Find("torso/arm-right");
        if (armL == null || armR == null)
            Debug.LogWarning("[PosePreview] arm joints not found — rendering the model unposed");
        if (armL != null) armL.localRotation = UnitAnim.HoldCorrection(leftInward, leftDrop, true)
                                             * armL.localRotation;
        if (armR != null) armR.localRotation = UnitAnim.HoldCorrection(rightInward, rightDrop, false)
                                             * armR.localRotation;
    }
}
