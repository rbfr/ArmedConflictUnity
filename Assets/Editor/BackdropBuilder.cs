using UnityEngine;
using UnityEditor;
using ArmedConflict.Data;

/// <summary>
/// Builds the biome backdrop from a BackgroundDefinitionSO: sky gradient, two silhouette
/// layers, and the ground colour.
///
/// The Filament build paints this in 2D behind a 3D scene. Here it is real geometry parked
/// far back — which is why Step 2 could delete groundLift: with a real 3D ground plane there
/// is no painted horizon for units to disagree with.
/// </summary>
public static class BackdropBuilder
{
    // NEGATIVE z. The camera sits at +camZ looking toward -Z (Step 2's solve), so positive z is
    // BEHIND the viewer — the first attempt put the whole backdrop at the camera's back and
    // rendered nothing at all.
    public const float SkyZ = -120f;
    public const float FarZ = -45f;
    public const float NearZ = -30f;

    /// <summary>
    /// A ridge layer has to be sized against the FRUSTUM AT ITS OWN DEPTH, not authored in
    /// absolute units. At z = -90 the visible width is about 90 units, so a 460-wide range with
    /// 7 peaks put roughly ONE peak on screen and a mountain range read as a single giant wedge.
    /// Visible width at distance d is 2 * d * (screenW / screenH).
    /// </summary>
    static float VisibleWidthAt(float z)
    {
        float d = Mathf.Abs(z) + 11f;                       // + roughly the gameplay camera z
        float aspect = (float)Screen.width / Mathf.Max(Screen.height, 1);
        if (Screen.height == 0 || aspect <= 0f) aspect = 1080f / 2404f;   // batchmode fallback
        return 2f * d * aspect;
    }

    public static void Build(BackgroundDefinitionSO bg, Material groundMat, Transform parent)
    {
        if (bg == null) return;

        groundMat.color = bg.groundColor;

        MakeSky(bg, parent);
        MakeSilhouette(bg, parent, "SilhouetteFar", bg.silhouetteFar, FarZ, 22f, 0.55f, 11, 11);
        MakeSilhouette(bg, parent, "SilhouetteNear", bg.silhouetteNear, NearZ, 13f, 0.42f, 9, 23);

        // The thin bright line where sky meets ground — cheap, and it is what makes the
        // horizon read as a horizon rather than as two flat bands meeting.
        var accent = GameObject.CreatePrimitive(PrimitiveType.Quad);
        accent.name = "HorizonAccent";
        Object.DestroyImmediate(accent.GetComponent<Collider>());
        accent.transform.SetParent(parent, false);
        accent.transform.position = new Vector3(0f, 0.02f, NearZ + 1f);
        // Unity's Quad primitive faces -Z; the camera views from +Z, so an unrotated quad shows
        // the camera its BACK face and is culled away entirely.
        accent.transform.rotation = Quaternion.Euler(0f, 180f, 0f);
        accent.transform.localScale = new Vector3(400f, 0.35f, 1f);
        accent.GetComponent<MeshRenderer>().sharedMaterial = Unlit("HorizonAccent", bg.horizonAccent);
    }

    static void MakeSky(BackgroundDefinitionSO bg, Transform parent)
    {
        // A vertical gradient texture rather than a flat colour: the Filament build gradients
        // skyTop -> skyHorizon, and a flat fill loses the depth that gives.
        var tex = new Texture2D(1, 64) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
        for (int y = 0; y < 64; y++)
            tex.SetPixel(0, y, Color.Lerp(bg.skyHorizon, bg.skyTop, y / 63f));
        tex.Apply();
        AssetDatabase.CreateAsset(tex, "Assets/Materials/SkyGradient.asset");

        var mat = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
        mat.mainTexture = tex;
        AssetDatabase.CreateAsset(mat, "Assets/Materials/SkyGradient.mat");

        var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
        quad.name = "Sky";
        Object.DestroyImmediate(quad.GetComponent<Collider>());
        quad.transform.SetParent(parent, false);
        quad.transform.position = new Vector3(0f, 40f, SkyZ);
        quad.transform.rotation = Quaternion.Euler(0f, 180f, 0f);   // face the camera, see above
        quad.transform.localScale = new Vector3(700f, 200f, 1f);
        quad.GetComponent<MeshRenderer>().sharedMaterial = mat;
    }

    /// <summary>
    /// One ridge layer. Deterministic from `seed` so a level looks the same every launch —
    /// a backdrop that reshuffles between runs reads as a rendering bug.
    /// </summary>
    static void MakeSilhouette(BackgroundDefinitionSO bg, Transform parent, string name,
                               Color color, float z, float height, float jitter, int peaks, int seed)
    {
        var rng = new System.Random(seed);
        var verts = new System.Collections.Generic.List<Vector3>();
        var tris = new System.Collections.Generic.List<int>();

        // 1.4x the visible width so the range runs off both edges rather than ending mid-frame.
        float width = VisibleWidthAt(z) * 1.4f;
        float step = width / peaks;
        float baseY = -2f;

        for (int i = 0; i < peaks; i++)
        {
            float cx = -width / 2f + step * (i + 0.5f);
            float h = height * (1f - jitter * (float)rng.NextDouble());
            float halfBase = step * (0.62f + 0.5f * (float)rng.NextDouble());

            int v = verts.Count;
            verts.Add(new Vector3(cx - halfBase, baseY, 0f));
            verts.Add(new Vector3(cx + halfBase, baseY, 0f));
            verts.Add(new Vector3(cx, h, 0f));
            tris.Add(v); tris.Add(v + 1); tris.Add(v + 2);
        }

        var mesh = new Mesh { name = name };
        mesh.SetVertices(verts);
        mesh.SetTriangles(tris, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        AssetDatabase.CreateAsset(mesh, $"Assets/Materials/{name}.asset");

        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.position = new Vector3(0f, 0f, z);
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        go.AddComponent<MeshRenderer>().sharedMaterial = Unlit(name + "Mat", color);
    }

    static Material Unlit(string name, Color c)
    {
        var path = $"Assets/Materials/{name}.mat";
        var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (existing != null) { existing.color = c; return existing; }
        var m = new Material(Shader.Find("Universal Render Pipeline/Unlit")) { color = c };
        AssetDatabase.CreateAsset(m, path);
        return m;
    }
}
