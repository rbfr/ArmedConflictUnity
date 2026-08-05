using UnityEditor;
using UnityEngine;
using ArmedConflict.Data;

/// <summary>
/// Builds unit prefabs from Kenney's Blocky Characters 2.0 (CC0) — the free rigged, animated
/// stand-in used to prove the pipeline before any money is spent on a military pack.
///
/// What this is testing, and why each part is here:
///  - ANIMATION WITHOUT SKINNING. The rig is `root → leg-left, leg-right, torso → (arm-left,
///    arm-right, head)`: six boxes, 0 skins, 0 bones, 72 triangles, 27 clips of plain TRS curves.
///    Our own units cannot be animated at all as they stand — they are grouped by MATERIAL
///    (`accent_*`, `skin_upper_*`) rather than by limb, so there is no elbow to bend.
///  - TEAM COLOUR. These carry one texture per character, so the four-tone node-name convention
///    the Blender units use does not apply and the side has to come from somewhere else. Here it
///    is a tint multiplied over the shared texture, which is the cheapest thing that could work;
///    judge it on device, since a tint strong enough to read as a uniform also stains the face.
///  - SCALE. Normalised to `UnitGeometry.UnitScaleUnits` exactly like the Blender units, so the
///    comparison is at identical on-screen height and nothing else moves.
/// </summary>
public static class KenneyUnits
{
    // character-m: the closest thing in the pack to a soldier (satchel, webbing, olive shirt).
    // The pack is civilians, robots and an orc — the CONTENT is wrong for this game on purpose;
    // it is the rig and the clips being evaluated, not the wardrobe.
    const string Model = "Assets/Models/Kenney/character-m.glb";

    public static GameObject MakePrefab(string name, Color tint, bool facesScreenRight)
    {
        var src = AssetDatabase.LoadAssetAtPath<GameObject>(Model);
        if (src == null) { Debug.LogError($"[KenneyUnits] missing {Model}"); return null; }

        var root = new GameObject(name);
        var model = (GameObject)PrefabUtility.InstantiatePrefab(src);
        model.transform.SetParent(root.transform, false);

        // The model faces +Z (at the camera). Screen-right is -X — the camera sits at +Z looking
        // toward -Z, so its right vector is -X — hence -90° to face screen-right. Baked into the
        // prefab because SyncUnits assigns `rotation = identity` to every slot every frame.
        model.transform.localRotation = Quaternion.Euler(0f, facesScreenRight ? -90f : 90f, 0f);

        Normalize(root, UnitGeometry.UnitScaleUnits);
        Tint(root, name, tint);
        root.AddComponent<UnitAnim>();

        System.IO.Directory.CreateDirectory("Assets/Prefabs");
        var prefab = PrefabUtility.SaveAsPrefabAsset(root, $"Assets/Prefabs/{name}.prefab");
        Object.DestroyImmediate(root);
        return prefab;
    }

    /// <summary>
    /// Scales the whole character so its tallest axis matches the Blender units' height. Measured
    /// off the renderers rather than assumed: the pack is authored around 1.8 world units and the
    /// game's units are 0.48, so an unscaled character is nearly four times too big.
    /// </summary>
    static void Normalize(GameObject go, float units)
    {
        var rs = go.GetComponentsInChildren<MeshRenderer>();
        if (rs.Length == 0) return;
        var b = rs[0].bounds;
        foreach (var r in rs) b.Encapsulate(r.bounds);
        float longest = Mathf.Max(b.size.x, Mathf.Max(b.size.y, b.size.z));
        if (longest > 0.0001f) go.transform.localScale = Vector3.one * (units / longest);
    }

    /// <summary>
    /// One tinted material per side, shared by every renderer on the character. A
    /// MaterialPropertyBlock would avoid the asset, but these are PREFABS baked into the scene,
    /// not pooled instances varying at runtime, so a shared material is both simpler and one
    /// fewer thing for the batcher to break on.
    /// </summary>
    static void Tint(GameObject go, string name, Color tint)
    {
        var rs = go.GetComponentsInChildren<MeshRenderer>();
        if (rs.Length == 0) return;

        var srcMat = rs[0].sharedMaterial;
        var mat = new Material(srcMat != null ? srcMat.shader : Shader.Find("Universal Render Pipeline/Lit"));
        if (srcMat != null) mat.CopyPropertiesFromMaterial(srcMat);
        mat.color = tint;
        var path = $"Assets/Materials/{name}Tint.mat";
        AssetDatabase.CreateAsset(mat, path);
        foreach (var r in rs) r.sharedMaterial = mat;
    }
}
