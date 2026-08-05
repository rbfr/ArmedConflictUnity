using System.Linq;
using UnityEditor;
using UnityEngine;
using ArmedConflict.Data;

/// <summary>
/// Builds unit prefabs from OUR soldier authored on Kenney's joint hierarchy, driven by Kenney's
/// CC0 clips. This is the recommendation the stand-in prototype pointed at: the animation is
/// bought for free, and everything the Blender pipeline already knows — silhouettes, the
/// four-tone colour convention, style agreement with the hand-built structures — is kept.
///
/// The retarget works because Kenney's clips are ROTATION-ONLY (only `die` also translates
/// `root`), so joint POSITIONS are free and only the joint NAMES and PATHS have to agree. See
/// tools/blender/build_unit_rigged.py in the Android repo for the three binding constraints.
/// </summary>
public static class RiggedUnits
{
    /// <summary>
    /// Idle amplitude, as a fraction of the source clip's. Reported 2026-08-05 as the units
    /// "rocking back and forth" — some idle movement wanted, this much not. Tune here, not by
    /// clip speed: see the note in Retarget.
    /// </summary>
    const float IdleAmplitude = 0.4f;

    const string Model = "Assets/Models/unit_rifleman_rigged.glb";
    const string Clips = "Assets/Models/Kenney/character-m.glb";

    /// <summary>
    /// Prints every imported clip's curve paths. Run before trusting a retarget: a Legacy clip
    /// binds by PATH, and a path that matches nothing fails SILENTLY — the limb just never moves,
    /// which looks exactly like a model that was authored wrong.
    ///   -executeMethod RiggedUnits.Probe
    /// </summary>
    public static void Probe()
    {
        foreach (var obj in AssetDatabase.LoadAllAssetsAtPath(Clips))
        {
            if (obj is not AnimationClip clip) continue;
            var paths = AnimationUtility.GetCurveBindings(clip)
                .Select(b => b.path).Distinct().OrderBy(p => p);
            Debug.Log($"[Probe] clip '{clip.name}' legacy={clip.legacy} len={clip.length:F2}s " +
                      $"paths=[{string.Join(", ", paths)}]");
        }

        var src = AssetDatabase.LoadAssetAtPath<GameObject>(Model);
        if (src == null) { Debug.LogError($"[Probe] missing {Model}"); return; }
        var go = (GameObject)PrefabUtility.InstantiatePrefab(src);
        foreach (var t in go.GetComponentsInChildren<Transform>())
            Debug.Log($"[Probe] ours: {Path(t, go.transform)}");
        Object.DestroyImmediate(go);
    }

    /// <summary>
    /// Samples the built prefab and asserts the joints MOVE. An unbound curve is not an error in
    /// Unity — it is silence — so "3/3 clips bound" only says the clips were added, not that any
    /// of them drives anything. This reads the joint transforms at two times and fails if they
    /// are identical, which is the only evidence that the path retarget actually landed.
    ///   -executeMethod RiggedUnits.Verify
    /// </summary>
    public static void Verify()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/PlayerUnit.prefab");
        if (prefab == null) { Debug.LogError("[Verify] build the scene first"); return; }
        var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        var anim = go.GetComponentInChildren<Animation>();
        if (anim == null) { Debug.LogError("[Verify] no Animation component"); Object.DestroyImmediate(go); return; }

        int failures = 0;
        foreach (var clipName in Wanted)
        {
            var state = anim[clipName];
            if (state == null) { Debug.LogError($"[Verify] clip '{clipName}' missing"); failures++; continue; }

            // EVERY curve in the clip must address a transform that actually exists. This is the
            // check that catches a broken retarget, and the per-joint sampling below is NOT: if
            // the prefix strip is wrong every curve lands on a path like `root/torso`, the joint
            // loop then finds no bindings for `torso` at all, and a clip driving nothing reported
            // RETARGET OK. Verified by deliberately breaking ClipPrefix — before this, that
            // passed clean.
            foreach (var b in AnimationUtility.GetCurveBindings(state.clip))
            {
                if (b.path.Length == 0 || anim.transform.Find(b.path) != null) continue;
                Debug.LogError($"[Verify] {clipName}: curve path '{b.path}' matches no transform " +
                               "— the retarget did not land");
                failures++;
                break;                              // one report per clip is enough
            }

            foreach (var joint in new[] { "torso", "torso/arm-left", "torso/arm-right", "torso/head" })
            {
                var t = anim.transform.Find(joint);
                if (t == null) { Debug.LogError($"[Verify] no joint '{joint}'"); failures++; continue; }

                // Sample ACROSS the clip, not just at its midpoint. A looping idle is a breathing
                // cycle whose extremes fall at the quarters, so t=0 and t=length/2 are the same
                // neutral pose — which reported four working joints as frozen on the first run.
                state.clip.SampleAnimation(anim.gameObject, 0f);
                var a = t.localRotation;
                float moved = 0f;
                for (int s = 1; s <= 8; s++)
                {
                    state.clip.SampleAnimation(anim.gameObject, state.length * s / 8f);
                    moved = Mathf.Max(moved, Quaternion.Angle(a, t.localRotation));
                }
                var rot = AnimationUtility.GetCurveBindings(state.clip)
                    .Where(x => x.path == joint && x.propertyName.StartsWith("m_LocalRotation"))
                    .ToArray();

                // A curve can be PRESENT AND CONSTANT, and that is not a failure — `holding-both`
                // is a static pose whose whole job is to hold the arms still. Asking only "does
                // this joint move?" flagged both of its arms on every run, which is two standing
                // errors in the one guard that exists to catch a joint that has stopped moving.
                // The question is whether the RETARGET lost the motion, so a curve that never had
                // any cannot answer it.
                bool varies = rot.Any(b =>
                {
                    var c = AnimationUtility.GetEditorCurve(state.clip, b);
                    if (c == null || c.length < 2) return false;
                    float min = float.MaxValue, max = float.MinValue;
                    foreach (var k in c.keys) { min = Mathf.Min(min, k.value); max = Mathf.Max(max, k.value); }
                    return max - min > 1e-5f;
                });

                if (varies && moved < 0.01f)
                {
                    Debug.LogError($"[Verify] {clipName}: '{joint}' has a VARYING rotation curve " +
                                   "but never moves — the retarget did not land");
                    failures++;
                }
                else if (rot.Length > 0 && !varies)
                    Debug.Log($"[Verify] {clipName}: '{joint}' holds a constant pose (by design)");
                else if (rot.Length > 0)
                    Debug.Log($"[Verify] {clipName}: '{joint}' rotates up to {moved:F1}° across the clip");
            }
        }
        Object.DestroyImmediate(go);
        Debug.Log(failures == 0 ? "[Verify] RETARGET OK" : $"[Verify] {failures} FAILURES");
        if (failures > 0 && Application.isBatchMode) EditorApplication.Exit(1);
    }

    static string Path(Transform t, Transform root)
    {
        var parts = new System.Collections.Generic.List<string>();
        for (var c = t; c != null && c != root; c = c.parent) parts.Add(c.name);
        parts.Reverse();
        return string.Join("/", parts);
    }

    /// <summary>
    /// The prefix Kenney's curves are addressed from. glTFast wraps an imported glTF in a root
    /// GameObject and hangs the scene's nodes under it, so their paths carry both the file's root
    /// node (`character-m`) and its `root` child. Ours has no such wrapper node, so both segments
    /// come off and what is left — `torso/arm-left` — matches our hierarchy exactly.
    /// </summary>
    const string ClipPrefix = "character-m/root";

    // Only the clips the game actually uses. The pack ships 27, including a wheelchair set.
    static readonly string[] Wanted = { UnitAnim.Idle, UnitAnim.Hold, UnitAnim.Shoot, UnitAnim.Die };

    const string GunModel = "Assets/Models/placeholder_gun.glb";

    /// <summary>
    /// Hangs the weapon off `arm-right`, so it travels with the arm the clips are moving. The
    /// pooled gun objects the static units use are positioned from the unit's ROOT at a fixed
    /// chest offset — fine for a body that never moves, and visibly wrong the moment the arm
    /// does: the rifle stays put while the hands swing away from it. BattleRunner suppresses the
    /// pooled gun for any unit that carries its own.
    /// </summary>
    static void AttachGun(GameObject model, Material gunMat)
    {
        var hand = model.transform.Find("torso/arm-right");
        var src = AssetDatabase.LoadAssetAtPath<GameObject>(GunModel);
        if (hand == null || src == null) { Debug.LogWarning("[RiggedUnits] no gun attached"); return; }

        var gun = (GameObject)PrefabUtility.InstantiatePrefab(src);
        gun.name = "gun";
        gun.transform.SetParent(hand, false);
        // Down the arm to the hand, then forward. The arm hangs along -Y from the shoulder in
        // model space and the soldier faces +Z, so "at the hands, pointing where he looks" is
        // -Y for the length of the arm and +Z for the muzzle.
        float armLen = SHOULDER_Z - HIP_Z;
        gun.transform.localPosition = new Vector3(0f, -armLen * 0.92f, 0.34f);
        gun.transform.localRotation = Quaternion.identity;
        gun.transform.localScale = Vector3.one * 1.35f;
        foreach (var r in gun.GetComponentsInChildren<MeshRenderer>()) r.sharedMaterial = gunMat;
    }

    // Mirrors build_unit_rigged.py. Only used to place the weapon down the arm.
    const float RigHeight = 2.70f;
    const float HIP_Z = 0.50f * RigHeight;
    const float SHOULDER_Z = 0.80f * RigHeight;

    public static GameObject MakePrefab(string name, Material body, Material accent, Material skin,
                                        Material gun, bool facesScreenRight)
    {
        var src = AssetDatabase.LoadAssetAtPath<GameObject>(Model);
        if (src == null) { Debug.LogError($"[RiggedUnits] missing {Model}"); return null; }

        var root = new GameObject(name);

        // THREE levels, and the middle one is load-bearing. `die` animates the root's own
        // rotation and translation; if the facing rotation lived on the same transform the clip
        // drives, the first frame of a death would wipe it and the corpse would snap to face the
        // camera. Facing sits on its own pivot ABOVE the animated node, so a clip works inside it.
        var facing = new GameObject("facing");
        facing.transform.SetParent(root.transform, false);
        facing.transform.localRotation = Quaternion.Euler(0f, facesScreenRight ? -90f : 90f, 0f);

        var model = (GameObject)PrefabUtility.InstantiatePrefab(src);
        model.transform.SetParent(facing.transform, false);

        Tone(root, body, accent, skin);
        Normalize(root, UnitGeometry.UnitScaleUnits);

        // The Animation component goes on the GLB's own root, NOT the prefab root: clip paths are
        // relative to the component's GameObject. One level out and every path misses by exactly
        // one segment — silently, because an unbound curve is not an error.
        var anim = model.AddComponent<Animation>();
        int added = 0;
        foreach (var obj in AssetDatabase.LoadAllAssetsAtPath(Clips))
        {
            if (obj is not AnimationClip clip || !Wanted.Contains(clip.name)) continue;
            anim.AddClip(Retarget(clip), clip.name);
            added++;
        }
        anim.playAutomatically = false;
        root.AddComponent<UnitAnim>();
        AttachGun(model, gun);
        Debug.Log($"[RiggedUnits] {name}: {added}/{Wanted.Length} clips bound");

        System.IO.Directory.CreateDirectory("Assets/Prefabs");
        var prefab = PrefabUtility.SaveAsPrefabAsset(root, $"Assets/Prefabs/{name}.prefab");
        Object.DestroyImmediate(root);
        return prefab;
    }

    /// <summary>
    /// Rewrites a clip's curve paths onto our hierarchy and saves it as an asset — a prefab can
    /// only reference clips that live on disk, so a clip built in memory would serialise as null
    /// and the unit would come back unanimated with nothing logged.
    ///
    /// `legacy` is set AFTER the curves go in: SetCurve refuses to touch a clip already marked
    /// legacy, and the failure is a silent no-op rather than an exception.
    /// </summary>
    static AnimationClip Retarget(AnimationClip src)
    {
        // The idle is DAMPED. Kenney's breathing loop is authored for one character standing
        // alone and filling the frame; at gameplay framing a rank of them reads as the whole line
        // ROCKING back and forth, which is what it was reported as. Scaling every keyframe toward
        // its own curve's mean shrinks the swing without moving the pose it swings around.
        //
        // The two obvious alternatives are both worse. Slowing the clip plays the same motion at
        // the same amplitude, just lazier — the swing is what reads, not the rate. Dropping the
        // idle entirely leaves a line of statues, which is the tell that they are instanced
        // copies; the Desync exists for the same reason. Only `idle` is touched: the recoil and
        // the death are one-shots the player is meant to notice.
        float amp = src.name == UnitAnim.Idle ? IdleAmplitude : 1f;

        string path = $"Assets/Animations/{src.name}.anim";
        System.IO.Directory.CreateDirectory("Assets/Animations");

        var dst = new AnimationClip { frameRate = src.frameRate };
        int bound = 0, dropped = 0;
        foreach (var b in AnimationUtility.GetCurveBindings(src))
        {
            var curve = AnimationUtility.GetEditorCurve(src, b);
            if (amp < 1f) curve = Damp(curve, amp);
            string p = b.path;
            if (p == ClipPrefix) p = "";                                   // the root itself
            else if (p.StartsWith(ClipPrefix + "/")) p = p.Substring(ClipPrefix.Length + 1);
            else { dropped++; continue; }
            dst.SetCurve(p, b.type, b.propertyName, curve);
            bound++;
        }
        dst.legacy = true;
        dst.wrapMode = src.wrapMode;
        if (dropped > 0) Debug.LogWarning($"[RiggedUnits] {src.name}: {dropped} curves off-prefix");
        Debug.Log($"[RiggedUnits] retargeted '{src.name}': {bound} curves");

        AssetDatabase.CreateAsset(dst, path);
        return dst;
    }

    /// <summary>
    /// Shrinks a curve's excursion toward its own mean, keeping the average pose. Tangents scale
    /// with the values or the eased motion overshoots the flattened keys.
    ///
    /// These are QUATERNION component curves, so this is a per-component nlerp toward the mean
    /// rotation rather than a true slerp. That is exact enough here and only here: a breathing
    /// idle moves a few degrees, so there is no antipodal (q vs -q) case to get wrong, and Unity
    /// normalises on apply. Do not reuse it to damp something with real angular travel.
    /// </summary>
    static AnimationCurve Damp(AnimationCurve c, float amp)
    {
        if (c == null || c.length == 0) return c;
        var keys = c.keys;
        float mean = 0f;
        foreach (var k in keys) mean += k.value;
        mean /= keys.Length;
        for (int i = 0; i < keys.Length; i++)
        {
            keys[i].value = mean + (keys[i].value - mean) * amp;
            keys[i].inTangent *= amp;
            keys[i].outTangent *= amp;
        }
        return new AnimationCurve(keys);
    }

    /// <summary>The four-tone convention, unchanged: colour binds to MESH names by prefix while
    /// animation binds to JOINT names by path, and neither knows about the other.</summary>
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

    static void Normalize(GameObject go, float units)
    {
        var rs = go.GetComponentsInChildren<MeshRenderer>();
        if (rs.Length == 0) return;
        var b = rs[0].bounds;
        foreach (var r in rs) b.Encapsulate(r.bounds);
        float longest = Mathf.Max(b.size.x, Mathf.Max(b.size.y, b.size.z));
        if (longest > 0.0001f) go.transform.localScale = Vector3.one * (units / longest);
    }
}
