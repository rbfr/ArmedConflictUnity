using System.Collections.Generic;
using UnityEngine;
using ArmedConflict.Data;
using ArmedConflict.Game;
using ArmedConflict.Render;

/// <summary>
/// Everything a level puts in the world that is NOT pooled: the ground plane, the enemy
/// structures, the props and the biome backdrop.
///
/// All of it used to be BAKED into Battle.unity by the editor builder, from L1's data only — and
/// that is precisely why there was no way to reach a second level. Here it is built at RUNTIME
/// from the level asset, so LoadLevel is just <see cref="Clear"/> then <see cref="Build"/>.
///
/// The GLB prefabs arrive as a name→prefab table rather than by path: AssetDatabase does not
/// exist in a player, so every model a level can ask for has to be a serialized reference.
/// SpikeSceneBattle fills the table from Assets/Models, and a name it cannot resolve is logged
/// rather than silently skipped — a level quietly missing its dominant structure looks like a
/// layout bug, which is a long way from where the fault is.
/// </summary>
public class LevelScenery : MonoBehaviour
{
    [SerializeField] string[] modelNames;
    [SerializeField] GameObject[] modelPrefabs;
    [SerializeField] Material unlitSource;
    [SerializeField] Material unlitFadeSource;
    [SerializeField] Material groundSource;
    [SerializeField] Material structPlayer, structPlayerAccent, structEnemy, structEnemyAccent;
    [SerializeField] Material scorchSource;

    readonly Dictionary<string, GameObject> models = new();
    readonly Dictionary<int, GameObject> structures = new();

    /// <summary>
    /// Runtime Materials, Textures and Meshes are not reclaimed when the GameObject holding them
    /// is destroyed — Unity collects ASSETS, not instances. Everything created per level is
    /// recorded here and destroyed on Clear, or walking the campaign leaks a backdrop and a
    /// structure's tinting per level, which is exactly the shape of the Android build's
    /// "a session gets progressively more expensive" defect.
    /// </summary>
    readonly List<Object> owned = new();

    Transform root;
    bool indexed;

    /// <summary>The scorch mark's tint follows the LEVEL's ground colour, so it has to be rebuilt
    /// per level. A fixed dark blob is invisible on a dark biome and a black sticker on a bright
    /// one; the mark has to be a SHADE of the ground it lies on.</summary>
    public Material ScorchMaterial { get; private set; }

    public GameObject Structure(int id) => structures.TryGetValue(id, out var go) ? go : null;

    public int ModelCount => modelPrefabs == null ? 0 : modelPrefabs.Length;

    public void Build(LevelDefinitionSO level, IReadOnlyList<StructureEntity> placed)
    {
        Clear();
        Index();
        root = new GameObject("Scenery").transform;

        var bg = level.background;

        // The ground STOPS JUST IN FRONT OF the nearest backdrop layer (far edge at z = -28,
        // against a silhouette at -30). It used to run to z = -150, far BEHIND the whole
        // backdrop, so wherever a silhouette dipped the distant ground showed through above the
        // horizon as a floating tan wedge. The horizon has to be made by the backdrop; a ground
        // plane that outruns it is a second, contradictory one.
        //   A quad turned 90° about X lies flat facing up: 300 wide, 90 deep centred at z = 17,
        //   so it spans -28 .. +62. (The old baked version used PrimitiveType.Plane at scale
        //   30 x 9; the same extent, without a collider the build no longer contains.)
        var groundMat = new Material(groundSource)
        { color = bg != null ? bg.groundColor : Color.gray };
        owned.Add(groundMat);
        var ground = QuadMesh.Create("Ground", root, groundMat);
        ground.transform.localPosition = new Vector3(0f, 0f, 17f);
        ground.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
        ground.transform.localScale = new Vector3(300f, 90f, 1f);

        var backdrop = new GameObject("Backdrop");
        backdrop.transform.SetParent(root, false);
        // Backdrop.DesignAspect, never Screen — see the note on that constant.
        BackdropRuntime.Build(bg, Backdrop.DesignAspect, backdrop.transform,
                              unlitSource, unlitFadeSource, owned);

        foreach (var st in placed)
        {
            var go = Spawn(st.Definition.modelAsset, root);
            if (go == null) continue;
            go.name = $"struct_{st.Id}";
            go.transform.localPosition =
                GameSpace.ToUnity(st.X, st.Y - st.Definition.size / 2f, st.Z);
            if (st.Definition.modelAbsoluteScale)
                go.transform.localScale = Vector3.one * st.Definition.worldScale;
            else
                Normalize(go, st.Definition.isPlayerSide ? 1.5f : st.Definition.size);
            Tone(go, st.Definition.isPlayerSide ? structPlayer : structEnemy,
                 st.Definition.isPlayerSide ? structPlayerAccent : structEnemyAccent, null);
            structures[st.Id] = go;
        }

        // Props are authored at z=0 like every campaign prop, and are cosmetic — nothing collides
        // with them.
        foreach (var prop in level.props)
        {
            var pg = Spawn(prop.modelAsset, root);
            if (pg == null) continue;
            pg.name = $"prop_{ModelKey(prop.modelAsset)}";
            pg.transform.localPosition = GameSpace.ToUnity(prop.x, 0f, prop.z);
            Normalize(pg, prop.scale);
            Tone(pg, structPlayer, structPlayerAccent, null);
        }

        var g = bg != null ? bg.groundColor : Color.gray;
        ScorchMaterial = new Material(scorchSource)
        { color = new Color(g.r * 0.45f, g.g * 0.42f, g.b * 0.40f, 0.85f) };
        owned.Add(ScorchMaterial);
    }

    public void Clear()
    {
        if (root != null) Destroy(root.gameObject);
        root = null;
        structures.Clear();
        foreach (var o in owned) if (o != null) Destroy(o);
        owned.Clear();
        ScorchMaterial = null;
    }

    void OnDestroy() => Clear();

    void Index()
    {
        if (indexed) return;
        indexed = true;
        int n = Mathf.Min(modelNames.Length, modelPrefabs.Length);
        for (int i = 0; i < n; i++)
        {
            // The table is keyed on the BARE name, so two GLBs with the same filename in
            // different folders would silently overwrite each other and one structure would
            // quietly render as another. Say so rather than let it pass.
            if (models.ContainsKey(modelNames[i]))
                Debug.LogWarning($"[Scenery] duplicate model name {modelNames[i]}");
            models[modelNames[i]] = modelPrefabs[i];
        }
    }

    GameObject Spawn(string modelAsset, Transform parent)
    {
        if (!models.TryGetValue(ModelKey(modelAsset), out var src) || src == null)
        {
            Debug.LogWarning($"[Scenery] no prefab for {modelAsset}");
            return null;
        }
        return Instantiate(src, parent);
    }

    /// <summary>The Kotlin data says "models/outpost.glb"; the table is keyed on "outpost".</summary>
    public static string ModelKey(string modelAsset)
        => System.IO.Path.GetFileNameWithoutExtension(modelAsset);

    /// <summary>Scales a model so its longest axis measures `units`. Bounds are read AFTER the
    /// instance is live, which is the only time a renderer reports them.</summary>
    static void Normalize(GameObject go, float units)
    {
        var rs = go.GetComponentsInChildren<MeshRenderer>();
        if (rs.Length == 0) return;
        var b = rs[0].bounds;
        foreach (var r in rs) b.Encapsulate(r.bounds);
        float longest = Mathf.Max(b.size.x, Mathf.Max(b.size.y, b.size.z));
        if (longest > 0.0001f) go.transform.localScale = Vector3.one * (units / longest);
    }

    /// <summary>GLB-embedded materials do not survive the pipeline, so colour is resolved by NODE
    /// NAME PREFIX — the same contract the Filament build uses, kept identical on purpose.</summary>
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
}
