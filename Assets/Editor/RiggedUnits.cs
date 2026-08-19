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
    /// The rigged roster, keyed by the `modelAsset` the Kotlin data names — minus the `_rigged`
    /// suffix, because the DATA still points at the old unriggable models and will keep doing so
    /// while the Filament build ships. The lookup is by key so a class the Kotlin adds later
    /// falls back to the rifleman rather than rendering nothing.
    ///
    /// Six crowd silhouettes and one hero. Every one is the SAME skeleton with the same joint
    /// names, so one set of retargeted clips drives all of them — that is the whole reason the
    /// per-class art could be done at all without seven sets of animation.
    /// </summary>
    public static readonly string[] Models =
    {
        "unit_rifleman", "unit_sniper", "unit_mg", "unit_rocket",
        "unit_grenadier", "unit_shield", "unit_hero",
    };

    public static string ModelPath(string key) => $"Assets/Models/{key}_rigged.glb";

    /// <summary>
    /// The per-class signature colour, carried over verbatim from SceneHost.unitTrimColor in the
    /// Android build — where every one of these values is the output of an on-device judgement
    /// that is recorded next to it. Two are worth knowing without opening that file: the machine
    /// gunner's brass was DARKENED because at full brightness the ammo pack read as a separate
    /// object stuck to the soldier, and the heavy's steel was darkened once the weapons stopped
    /// being near-white and it became the brightest thing in the frame.
    ///
    /// This is the FOURTH tone, and the port did not have it. Without it every prop below —
    /// ghillie, ammo drum, rocket tips, shells, riot shield, hero sash — falls through to the
    /// side's uniform colour and the class reads as a slightly lumpy rifleman.
    /// </summary>
    public static Color TrimColor(string modelKey) => modelKey switch
    {
        "unit_hero" => new Color(0.34f, 0.36f, 0.40f),      // armour plate steel
        "unit_sniper" => new Color(0.30f, 0.36f, 0.20f),    // ghillie moss
        "unit_mg" => new Color(0.44f, 0.35f, 0.16f),        // aged brass belt and drum
        "unit_rocket" => new Color(0.78f, 0.32f, 0.10f),    // warning-orange rocket tips
        "unit_grenadier" => new Color(0.56f, 0.62f, 0.26f), // lime shells, matching the grenade
        "unit_shield" => new Color(0.35f, 0.42f, 0.52f),    // gunmetal riot shield
        _ => new Color(0.13f, 0.13f, 0.11f),                // fallback = gear dark
    };

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
        // EVERY prefab, not just one. There are fourteen now — seven silhouettes on each side —
        // and they are built from seven separate GLBs, so "the rifleman animates" stops being
        // evidence about the sniper the moment a second model exists. A joint misnamed in one
        // builder is exactly the failure this catches, and it is silent everywhere else.
        int failures = 0, checkedPrefabs = 0;
        foreach (var side in new[] { "Player", "Enemy" })
            foreach (var key in Models)
                failures += VerifyPrefab($"Assets/Prefabs/{side}Unit_{key}.prefab", ref checkedPrefabs);

        if (checkedPrefabs == 0)
        {
            Debug.LogError("[Verify] no unit prefabs found — build the scene first");
            if (Application.isBatchMode) EditorApplication.Exit(1);
            return;
        }
        Debug.Log(failures == 0
            ? $"[Verify] RETARGET OK across {checkedPrefabs} prefabs"
            : $"[Verify] {failures} FAILURES across {checkedPrefabs} prefabs");
        if (failures > 0 && Application.isBatchMode) EditorApplication.Exit(1);
    }

    static int VerifyPrefab(string path, ref int checkedPrefabs)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (prefab == null) { Debug.LogError($"[Verify] missing {path}"); return 1; }
        var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        var anim = go.GetComponentInChildren<Animation>();
        if (anim == null)
        {
            Debug.LogError($"[Verify] {path}: no Animation component");
            Object.DestroyImmediate(go);
            return 1;
        }
        checkedPrefabs++;
        string label = System.IO.Path.GetFileNameWithoutExtension(path);

        int failures = 0;
        foreach (var clipName in Wanted)
        {
            var state = anim[clipName];
            if (state == null) { Debug.LogError($"[Verify] {label}: clip '{clipName}' missing"); failures++; continue; }

            // EVERY curve in the clip must address a transform that actually exists. This is the
            // check that catches a broken retarget, and the per-joint sampling below is NOT: if
            // the prefix strip is wrong every curve lands on a path like `root/torso`, the joint
            // loop then finds no bindings for `torso` at all, and a clip driving nothing reported
            // RETARGET OK. Verified by deliberately breaking ClipPrefix — before this, that
            // passed clean.
            foreach (var b in AnimationUtility.GetCurveBindings(state.clip))
            {
                if (b.path.Length == 0 || anim.transform.Find(b.path) != null) continue;
                Debug.LogError($"[Verify] {label} {clipName}: curve path '{b.path}' matches no transform " +
                               "— the retarget did not land");
                failures++;
                break;                              // one report per clip is enough
            }

            foreach (var joint in new[] { "torso", "torso/arm-left", "torso/arm-right", "torso/head" })
            {
                var t = anim.transform.Find(joint);
                if (t == null) { Debug.LogError($"[Verify] {label}: no joint '{joint}'"); failures++; continue; }

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
                    Debug.LogError($"[Verify] {label} {clipName}: '{joint}' has a VARYING rotation curve " +
                                   "but never moves — the retarget did not land");
                    failures++;
                }
                else if (rot.Length > 0 && !varies)
                    Debug.Log($"[Verify] {label} {clipName}: '{joint}' holds a constant pose (by design)");
                else if (rot.Length > 0)
                    Debug.Log($"[Verify] {label} {clipName}: '{joint}' rotates up to {moved:F1}° across the clip");
            }
        }
        Object.DestroyImmediate(go);
        if (failures > 0) Debug.LogError($"[Verify] {label}: {failures} failures");
        return failures;
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
    static readonly string[] Wanted =
        { UnitAnim.Idle, UnitAnim.Hold, UnitAnim.Shoot, UnitAnim.Die, UnitAnim.Walk,
          UnitAnim.Melee };

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
        // placeholder_gun is authored barrel-along-+X, magazine-along-−Z, grip at the origin
        // (build_rifleman.py). Kenney's holding-both pitches both arms −90° about X so the
        // arm's −Y (down the bone) becomes model +Z (downfield after the facing yaw). Identity
        // parenting left +X along the arm's +X — toward the camera — which is the "handing
        // the rifle over" read. Align barrel with the bone and magazine with arm +Z; after
        // the hold that is downfield and hanging down.
        float armLen = SHOULDER_Z - HIP_Z;
        gun.transform.localPosition = new Vector3(0.10f, -armLen * 0.88f, 0.04f);
        // LookRotation(forward, right) put local +X (the barrel) down the bone
        // toward the shoulder — 180° off the hands. The phone showed every
        // muzzle at the tank. This is the opposite roll so the barrel continues
        // past the hands, the way the soldier faces.
        gun.transform.localRotation = Quaternion.LookRotation(Vector3.forward, Vector3.left);
        gun.transform.localScale = Vector3.one * 1.35f;
        foreach (var r in gun.GetComponentsInChildren<MeshRenderer>()) r.sharedMaterial = gunMat;
    }

    // Mirrors build_unit_rigged.py. Only used to place the weapon down the arm.
    const float RigHeight = 2.70f;
    const float HIP_Z = 0.50f * RigHeight;
    const float SHOULDER_Z = 0.80f * RigHeight;

    public static GameObject MakePrefab(string name, Material body, Material accent, Material skin,
                                        Material gun, bool facesScreenRight,
                                        string modelKey = null, Material trim = null)
    {
        string path = modelKey == null ? Model : ModelPath(modelKey);
        var src = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (src == null) { Debug.LogError($"[RiggedUnits] missing {path}"); return null; }

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

        Tone(root, body, accent, trim, skin);
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
        string path = $"Assets/Animations/{src.name}.anim";
        System.IO.Directory.CreateDirectory("Assets/Animations");

        var dst = new AnimationClip { frameRate = src.frameRate };
        int bound = 0, dropped = 0;
        foreach (var b in AnimationUtility.GetCurveBindings(src))
        {
            var curve = AnimationUtility.GetEditorCurve(src, b);
            string p = b.path;
            if (p == ClipPrefix) p = "";                                   // the root itself
            else if (p.StartsWith(ClipPrefix + "/")) p = p.Substring(ClipPrefix.Length + 1);
            else { dropped++; continue; }
            // Idle damp is per joint. The 2026-08-05 rocking was the TORSO; damping
            // the whole clip to 0.4 also killed the head, which is the motion that
            // reads as "alive" once the hold has frozen the arms. Head keeps more of
            // Kenney's look-around; torso stays quieter than the old blanket 0.4.
            float amp = 1f;
            if (src.name == UnitAnim.Idle)
                amp = p == "torso/head" ? 0.75f
                    : p == "torso" ? 0.28f
                    : IdleAmplitude;
            if (amp < 1f) curve = Damp(curve, amp);
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

    /// <summary>The FOUR-tone convention: colour binds to MESH names by prefix while animation
    /// binds to JOINT names by path, and neither knows about the other.
    ///
    /// Order matters and is the Filament build's, kept identical on purpose — skin, then trim,
    /// then accent, then the side's uniform. `trim` was the tone the port was missing; a null
    /// falls back to accent so a caller that has no per-class colour still gets dark gear rather
    /// than a prop in the uniform colour.</summary>
    static void Tone(GameObject go, Material body, Material accent, Material trim, Material skin)
    {
        foreach (var r in go.GetComponentsInChildren<MeshRenderer>())
        {
            string n = r.gameObject.name;
            if (skin != null && n.StartsWith("skin")) r.sharedMaterial = skin;
            else if (n.StartsWith("trim")) r.sharedMaterial = trim != null ? trim : accent;
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
