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
    readonly Dictionary<int, GameObject> wrecks = new();
    readonly List<BoundProp> boundProps = new();

    struct BoundProp
    {
        public GameObject Go;
        public int StructureId;
        public float X, Y, Z;
    }

    public struct CollapseBlast
    {
        public float X, Y, Z, Scale;
    }

    /// <summary>
    /// Damage-chunk groups per structure id, in ascending group number — the renderers that get
    /// hidden as the building sheds. Collected ONCE at build time: the grouping is a string parse
    /// over every child node, and doing it per frame for every structure is exactly the kind of
    /// per-slot rescan the Filament build's profile warns about.
    /// </summary>
    readonly Dictionary<int, List<Renderer>[]> chunkGroups = new();

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

    /// <summary>
    /// Wall scars, two layers: a wide soot halo and a crater. NOT ground-tinted and NOT a
    /// whole-mesh multiply — Rob, 2026-09-06, the building just turned dark.
    /// </summary>
    public Material ScarHaloMaterial { get; private set; }
    public Material ScarCraterMaterial { get; private set; }

    /// <summary>Additive muzzle flash. White core, not biome-tinted — a gunshot is
    /// the same colour on snow and dirt.</summary>
    public Material FlashMaterial { get; private set; }

    public GameObject Structure(int id) => structures.TryGetValue(id, out var go) ? go : null;
    public GameObject Wreck(int id) => wrecks.TryGetValue(id, out var go) ? go : null;

    /// <summary>
    /// Hide every prop bound to this structure and return the blasts that
    /// should cook it off. No-op if the prop is already hidden (restart
    /// must not re-fire the bang).
    /// </summary>
    public List<CollapseBlast> CollapseBoundProps(int structureId)
    {
        var blasts = new List<CollapseBlast>();
        for (int i = 0; i < boundProps.Count; i++)
        {
            var p = boundProps[i];
            if (p.StructureId != structureId || p.Go == null || !p.Go.activeSelf) continue;
            p.Go.SetActive(false);
            blasts.Add(new CollapseBlast { X = p.X, Y = p.Y, Z = p.Z, Scale = 1.85f });
            blasts.Add(new CollapseBlast { X = p.X + 0.55f, Y = p.Y * 0.7f, Z = p.Z, Scale = 1.25f });
        }
        return blasts;
    }

    public void RestoreBoundProps(int structureId)
    {
        for (int i = 0; i < boundProps.Count; i++)
        {
            var p = boundProps[i];
            if (p.StructureId == structureId && p.Go != null) p.Go.SetActive(true);
        }
    }

    public static string WreckKey(string wreckModelAsset)
        => string.IsNullOrEmpty(wreckModelAsset) ? null : ModelKey(wreckModelAsset);

    public int ModelCount => modelPrefabs == null ? 0 : modelPrefabs.Length;

    /// <summary>
    /// Game-space Z added to every wreck. Collapse throws rubble TOWARD the camera;
    /// parking the pile this far back leaves the ground line in front of it, so a
    /// boss can stand at the keep without walking into the lens. L6 2026-09-06:
    /// z-forward on the bodies made the Sovereign look closer to the player.
    /// </summary>
    public const float WreckBackZ = -1.6f;

    public List<Renderer>[] ChunkGroups(int id)
        => chunkGroups.TryGetValue(id, out var g) ? g : System.Array.Empty<List<Renderer>>();

    /// <summary>
    /// Groups the model's `chunk_N` nodes by their TRAILING NUMBER, ascending. The prefix varies
    /// with the tone the piece wears — `chunk_3`, `accent_chunk_3` and `trim_chunk_3` are all one
    /// group — so the number is the only thing that identifies it, and matching on the prefix
    /// would shed a wall's stone and leave its trim hanging in the air.
    /// </summary>
    static List<Renderer>[] CollectChunkGroups(GameObject go)
    {
        var byNumber = new SortedDictionary<int, List<Renderer>>();
        foreach (var r in go.GetComponentsInChildren<Renderer>(true))
        {
            string n = r.gameObject.name;
            if (!n.Contains("chunk")) continue;
            int end = n.Length;
            while (end > 0 && char.IsDigit(n[end - 1])) end--;
            if (end == n.Length) continue;                       // "chunk" with no number
            if (!int.TryParse(n.Substring(end), out int num)) continue;
            if (!byNumber.TryGetValue(num, out var list))
                byNumber[num] = list = new List<Renderer>();
            list.Add(r);
        }
        var groups = new List<Renderer>[byNumber.Count];
        byNumber.Values.CopyTo(groups, 0);
        return groups;
    }

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
                              unlitSource, unlitFadeSource, owned, models);

        RuinFx.Kit wreckKit = default;

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
            chunkGroups[st.Id] = CollectChunkGroups(go);

            // Authored collapse, spawned with the live building so we never mint a
            // slot mid-volley. Hidden until the building dies.
            if (!string.IsNullOrEmpty(st.Definition.wreckModelAsset))
            {
                var wreck = Spawn(st.Definition.wreckModelAsset, root);
                if (wreck != null)
                {
                    wreck.name = $"wreck_{st.Id}";
                    var wreckPos = go.transform.localPosition;
                    wreckPos.z += WreckBackZ;
                    wreck.transform.localPosition = wreckPos;
                    wreck.transform.localScale = go.transform.localScale;
                    Tone(wreck,
                         st.Definition.isPlayerSide ? structPlayer : structEnemy,
                         st.Definition.isPlayerSide ? structPlayerAccent : structEnemyAccent,
                         null);
                    if (wreck.GetComponent<WreckAnim>() == null)
                        wreck.AddComponent<WreckAnim>();
                    if (wreckKit.Fire == null)
                        wreckKit = RuinFx.MakeKit(unlitFadeSource, owned);
                    RuinFx.AttachWreck(wreck.transform, wreckKit, st.Id);
                    wreck.SetActive(false);
                    wrecks[st.Id] = wreck;
                }
            }
        }

        // Placement id → runtime structure id. Same order LevelBuilder assigns.
        var idByPlacement = new Dictionary<string, int>();
        for (int i = 0; i < level.structures.Count && i < placed.Count; i++)
            if (!string.IsNullOrEmpty(level.structures[i].id))
                idByPlacement[level.structures[i].id] = placed[i].Id;

        // Props are authored at z=0 like every campaign prop, and are cosmetic — nothing collides
        // with them. collapsesWith hides the mesh when that structure dies (L13 bay jet).
        foreach (var prop in level.props)
        {
            var pg = Spawn(prop.modelAsset, root);
            if (pg == null) continue;
            pg.name = $"prop_{ModelKey(prop.modelAsset)}";
            pg.transform.localPosition = GameSpace.ToUnity(prop.x, 0f, prop.z);
            if (prop.absoluteScale)
                pg.transform.localScale = Vector3.one * (prop.scale <= 0f ? 1f : prop.scale);
            else
                Normalize(pg, prop.scale);
            if (!prop.keepColors)
                Tone(pg, structPlayer, structPlayerAccent, null);
            if (!string.IsNullOrEmpty(prop.collapsesWith)
                && idByPlacement.TryGetValue(prop.collapsesWith, out int sid))
            {
                boundProps.Add(new BoundProp
                {
                    Go = pg, StructureId = sid,
                    X = prop.x, Y = 0.55f, Z = prop.z,
                });
            }
        }

        var g = bg != null ? bg.groundColor : Color.gray;
        ScorchMaterial = new Material(scorchSource)
        { color = new Color(g.r * 0.45f, g.g * 0.42f, g.b * 0.40f, 0.85f) };
        owned.Add(ScorchMaterial);

        // Clone the TRANSPARENT fade source. An opaque Unlit ignores alpha and the scar
        // is a hard square on the wall, then vanishes in one frame. Colour lives in the
        // texture (pit / rim / soot) so a white tint cannot flatten the crater.
        if (unlitFadeSource != null)
        {
            ScarHaloMaterial = new Material(unlitFadeSource) { color = Color.white };
            ScarHaloMaterial.mainTexture = HaloTex();
            owned.Add(ScarHaloMaterial);
            ScarCraterMaterial = new Material(unlitFadeSource) { color = Color.white };
            ScarCraterMaterial.mainTexture = CraterTex();
            owned.Add(ScarCraterMaterial);

            FlashMaterial = new Material(unlitFadeSource) { color = Color.white };
            FlashMaterial.mainTexture = FlashTex();
            FlashMaterial.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.One);
            FlashMaterial.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.One);
            owned.Add(FlashMaterial);
        }
    }

    public void Clear()
    {
        if (root != null) Destroy(root.gameObject);
        root = null;
        structures.Clear();
        wrecks.Clear();
        boundProps.Clear();
        chunkGroups.Clear();
        foreach (var o in owned) if (o != null) Destroy(o);
        owned.Clear();
        ScorchMaterial = null;
        ScarHaloMaterial = null;
        ScarCraterMaterial = null;
        FlashMaterial = null;
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
    static Texture2D haloTex, craterTex, flashTex;

    static Texture2D HaloTex()
    {
        if (haloTex != null) return haloTex;
        haloTex = MakeRadial(64, d =>
        {
            // Local soot only. Keep it thin so masonry still reads through; a heavy
            // halo on a keep became a dirty wall in two shells.
            float a = Mathf.Clamp01(1f - Mathf.SmoothStep(0.20f, 1f, d)) * 0.42f;
            return new Color(0.16f, 0.10f, 0.06f, a);
        });
        return haloTex;
    }

    static Texture2D FlashTex()
    {
        if (flashTex != null) return flashTex;
        flashTex = MakeRadial(64, d =>
        {
            float a = Mathf.Clamp01(1f - Mathf.SmoothStep(0.08f, 1f, d));
            // Hot core, yellow shoulder. Additive, so RGB is the light.
            var core = new Color(1f, 0.95f, 0.75f, a);
            var rim = new Color(1f, 0.55f, 0.12f, a * 0.7f);
            return Color.Lerp(core, rim, Mathf.Clamp01(d));
        });
        return flashTex;
    }

    static Texture2D CraterTex()
    {
        if (craterTex != null) return craterTex;
        craterTex = MakeRadial(96, d =>
        {
            if (d < 0.22f)
                return new Color(0.02f, 0.018f, 0.015f, 0.97f);
            if (d < 0.38f)
            {
                // Sharp lip: a hole in the wall, not a soft stain.
                float t = Mathf.InverseLerp(0.22f, 0.38f, d);
                var pit = new Color(0.02f, 0.018f, 0.015f, 0.97f);
                var rim = new Color(0.38f, 0.28f, 0.18f, 0.80f);
                return Color.Lerp(pit, rim, t);
            }
            float a = Mathf.Clamp01(1f - Mathf.SmoothStep(0.38f, 0.85f, d)) * 0.35f;
            return new Color(0.12f, 0.08f, 0.05f, a);
        });
        return craterTex;
    }

    static Texture2D MakeRadial(int size, System.Func<float, Color> at)
    {
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            { wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.HideAndDontSave };
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            float dx = (x + 0.5f) / size - 0.5f, dy = (y + 0.5f) / size - 0.5f;
            float d = Mathf.Sqrt(dx * dx + dy * dy) * 2f;
            tex.SetPixel(x, y, at(d));
        }
        tex.Apply();
        return tex;
    }

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
