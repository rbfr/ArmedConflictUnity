using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using ArmedConflict.Data;
using ArmedConflict.Game;
using ArmedConflict.Render;
using ArmedConflict.UI;

/// <summary>
/// Drives a real battle: owns the GameState, ticks it, takes the drag, renders the result.
///
/// This is the assembly the eight ported slices were built for. Everything gameplay lives in
/// ArmedConflict.Game and is engine-independent; this file is the only part that knows about
/// MonoBehaviours, and it deliberately does no game logic of its own.
/// </summary>
public class BattleRunner : MonoBehaviour
{
    [SerializeField] Camera cam;
    /// <summary>
    /// Every level: the CAMPAIGN BLOCK FIRST, then the test rigs, each ordered by its own level
    /// number (SpikeSceneBattle sorts them that way). A player has no AssetDatabase, so every
    /// level the session can reach has to be a serialized reference.
    ///
    /// The campaign leading and being contiguous is the whole split: the player-facing path is
    /// "index &lt; campaignCount" and needs no second array.
    /// </summary>
    [SerializeField] LevelDefinitionSO[] levels;
    [SerializeField] LevelScenery scenery;
    [SerializeField] GameObject playerUnitPrefab;
    [SerializeField] GameObject enemyUnitPrefab;
    /// <summary>
    /// The per-class unit art: three parallel arrays, keyed by the same bare model name
    /// LevelScenery uses. Empty means "one silhouette for the whole roster" — the state this
    /// build shipped in until 2026-08-06, where every class rendered as the rifleman.
    /// </summary>
    [SerializeField] string[] unitClassKeys = new string[0];
    [SerializeField] GameObject[] playerUnitClassPrefabs = new GameObject[0];
    [SerializeField] GameObject[] enemyUnitClassPrefabs = new GameObject[0];
    [SerializeField] GameObject projectilePrefab;
    [SerializeField] GameObject gunPrefab;
    [SerializeField] GameObject bulletPrefab;
    [SerializeField] GameObject rocketPrefab;
    [SerializeField] GameObject grenadePrefab;
    [SerializeField] GameObject explosionPrefab;
    [SerializeField] GameObject scorchPrefab;
    [SerializeField] GameObject debrisPrefab;
    /// <summary>The soft ellipse under a living soldier — see SyncShadows.</summary>
    [SerializeField] GameObject shadowPrefab;
    /// <summary>The two-tongue flame carried by a unit the incendiary round set alight — see
    /// SyncFlames. Optional: with no prefab the burn still fires and is still logged, which is
    /// exactly the pre-2026-08-09 behaviour.</summary>
    [SerializeField] GameObject flamePrefab;
    [SerializeField] BattleAudio audioFx;
    [SerializeField] Transform poolRoot;
    /// <summary>Unlit white, tinted per bar with a property block. Unlit on purpose — a health
    /// bar is UI that happens to live in the world, and a lit one changes colour with the biome's
    /// light, which is the one thing this cue must never do.</summary>
    [SerializeField] Material healthBarSource;
    /// <summary>The loadout picker's menu. Optional — with no roster the levels field their
    /// authored squads and the picker never opens, which is exactly the pre-loadout behaviour.</summary>
    [SerializeField] RosterDefinitionSO roster;
    /// <summary>What each ammo type does. Optional — with no catalogue every type resolves to
    /// Standard's identity modifier, which is exactly the pre-ammo behaviour.</summary>
    [SerializeField] AmmoCatalogSO ammoCatalog;

    const int UnitPoolSize = 48;
    const int ProjectilePoolSize = 64;

    GameState state;
    System.Random random;
    UnitSlots playerUnits, enemyUnits;
    readonly List<GameObject> shotSlots = new();
    readonly List<GameObject> playerGuns = new();
    readonly List<GameObject> enemyGuns = new();
    readonly List<GameObject> blastSlots = new();
    // One pool PER TYPE. A shared pool would need the prefab swapped per frame, which
    // means destroying and recreating renderers mid-flight.
    readonly Dictionary<ProjectileType, List<GameObject>> shotPools = new();
    readonly List<GameObject> scorchSlots = new();
    readonly List<GameObject> debrisSlots = new();
    readonly List<(int Id, GameObject Go)> structureObjects = new();

    LevelDefinitionSO level;
    int levelIndex;
    int battleId;

    /// <summary>
    /// How many leading entries of `levels` are campaign levels. Everything at or beyond this is
    /// a dev rig and is NOT part of the player-facing path — PRODUCT_DIRECTION 0.1: "test rigs are
    /// not the campaign".
    /// </summary>
    int campaignCount;

    /// <summary>
    /// Unlocks the test rigs for the ◀ ▶ stepper. OFF by default, so nothing a player can press
    /// walks off the end of the campaign into the unit parade.
    ///
    /// Deliberately a runtime toggle rather than `Debug.isDebugBuild`: the rigs have to stay
    /// reachable in a RELEASE build, because that is the only build performance may be measured
    /// on and sweeping them from adb is how missing geometry gets found.
    /// </summary>
    bool showRigs;

    BattleUI ui;
    /// <summary>The turn-handover line, held for the enemy turn. Outranked by a real event.</summary>
    string turnBanner;
    /// <summary>The squad chosen for THIS battle. Null means "as authored".</summary>
    List<EnemyGroup> loadoutGroups;
    /// <summary>
    /// The battle whose end has already been paid for. The award must run EXACTLY ONCE per
    /// battle: the Playing->over edge is a single frame, but a level with no enemies resolves on
    /// its first tick and the free camera keeps the finished battle ticking indefinitely
    /// afterwards, so an edge test alone is not a guarantee. battleId already advances per
    /// LoadLevel, which makes it the right key — a REPLAY is a new battle and does pay again,
    /// deliberately (the one-time parts are handled inside GrantVictoryPayout by previousBest).
    /// </summary>
    int awardedBattleId = -1;

    // input
    bool dragging;
    Vector2 dragStart;
    Vector3 aimVel;
    readonly List<Vector3> arc = new();

    // The elevation the player's line is HOLDING. Live while dragging, then held through the
    // volley — the rounds are still in the air, so dropping the arms at release would have the
    // line stand down while its own shots are mid-flight. Cleared when the turn comes back.
    float aimPoseDegrees;

    // enemy turn pacing
    float enemyWindup;

    float smoothedDt;
    float worstDragDt;
    int dragFrames;
    GUIStyle style;
    Texture2D dot;
    MaterialPropertyBlock blastProps;

    // ---- health bars ---------------------------------------------------------------------
    //
    // Shown when a unit is HIT and faded out a few seconds later, driven by UnitEntity.LastHitAge
    // rather than by "is currently wounded". The player has read the hit by then; a bar that
    // persists for as long as the damage does turns a 26-strong line into a second HUD laid over
    // the army. A unit that has not been hit recently carries nothing.
    //
    // Sized against UnitGeometry.UnitScaleUnits, like every other body-relative thing in this
    // project. The WIDTH is bounded by Formation.MountedColumnSpacing (0.187) rather than by the
    // body: a garrison packs tighter than a ground line, so a bar sized to look right on an open
    // field overlaps its neighbour's on a parapet, which is where damaged units are most often
    // being counted.
    const float BarWidth = 0.34f * UnitGeometry.UnitScaleUnits;    // 0.163 world, ~30px
    const float BarHeight = 0.10f * UnitGeometry.UnitScaleUnits;   // 0.048 world, ~9px
    const float BarGap = 0.13f * UnitGeometry.UnitScaleUnits;      // clearance over the helmet
    const float BarBorder = 0.16f;                                 // inset of the fill, fraction

    /// <summary>
    /// The fill never shrinks below this fraction of the track, however close to death the unit
    /// is. At 30px wide, a linear fill at 25% health is SIX PIXELS of colour in a dark bar, which
    /// reads as a black bar — i.e. as a broken cue, and reported as one. The bar failed exactly
    /// when it mattered most.
    ///
    /// This deliberately breaks the linear mapping at the bottom end, and that is the right trade:
    /// down there the COLOUR is the message ("this one is nearly gone"), not the exact fraction,
    /// and a message that cannot be seen carries no information at all.
    /// </summary>
    const float BarMinFill = 0.22f;

    // The empty track is DARK, not black. At (0.08) it was indistinguishable from an unlit gap in
    // the scene, so a mostly-empty bar read as an artefact rather than as an empty bar.
    static readonly Color BarBackColor = new(0.20f, 0.20f, 0.23f, 1f);
    static readonly Color BarHigh = new(0.35f, 0.78f, 0.28f);
    static readonly Color BarMid = new(0.90f, 0.76f, 0.18f);
    static readonly Color BarLow = new(0.82f, 0.20f, 0.16f);
    static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

    readonly List<(GameObject Root, Transform Fill, MeshRenderer FillRenderer,
                   MeshRenderer BackRenderer)> healthBars = new();
    MaterialPropertyBlock barProps;

    // ---- contact shadows -------------------------------------------------------------------
    //
    // A soft dark ellipse under every LIVING unit. It is the cue that a soldier is standing ON
    // the ground rather than floating in front of it, and the port shipped without one: harmless
    // on the tan biomes, where the ground is far darker than the sky and the horizon carries the
    // read by itself, and badly wrong on WINTER, where a near-white ground under a pale sky left
    // the line hanging in white space.
    //
    // Diameter is body-relative, like everything else here.
    const float ShadowDiameter = 0.48f * UnitGeometry.UnitScaleUnits;

    /// <summary>
    /// How much longer the ellipse is in DEPTH than across. This is not a style choice — it is
    /// forced by the camera.
    ///
    /// The battle camera sits about 1.2 up at 10 back, i.e. roughly SIX DEGREES above the ground
    /// plane. A decal lying flat is therefore seen almost edge-on, and its on-screen HEIGHT is its
    /// world depth times the sine of that angle — about a tenth. A round shadow 28px wide projects
    /// to a 3px smear, which is what the first pass drew and why it read as nothing at all.
    ///
    /// Width cannot fix it: widening the shadow only makes a wider smear, and it starts colliding
    /// with the neighbouring soldier's. DEPTH is free — the camera looks along it — and it is the
    /// only axis that buys screen height. Same projection argument the unit silhouettes are
    /// governed by, pointing the other way.
    /// </summary>
    const float ShadowDepthStretch = 3.2f;
    /// <summary>Just off the ground, and BELOW the scorch plane so the two never z-fight.</summary>
    const float ShadowY = 0.006f;

    readonly List<GameObject> shadowSlots = new();
    Material shadowMat;

    // ---- the incendiary flame ----------------------------------------------------------------
    //
    // What a unit set alight by an incendiary round looks like. Before this the burn dealt its
    // damage with NOTHING to see, and the only way to confirm from a device that it had fired at
    // all was the [Burn] log — which is why that log was kept.
    //
    // Driven straight off GameState.BurningEnemyIds, so it needs no new tick state. That set is
    // filled when the round lands and cleared when the burn resolves at the turn handover, which
    // makes the fire a TELEGRAPH as well as a cue: it is up for the whole post-volley pause,
    // saying these men are about to take damage, and the health bars drop as it goes out.
    //
    // The sizes, the offsets and the guttering clock live in Render/FlameRig, which the headless
    // FlamePreview renders through as well — a preview that re-implements the placement is a
    // second implementation, and BackdropPreview already cost this project a session that way.
    readonly List<(GameObject Root, Transform Outer, Transform Inner,
                   MeshRenderer OuterRenderer, MeshRenderer InnerRenderer)> flameSlots = new();
    /// <summary>
    /// Unit id -> seconds of guttering left. RENDER-ONLY: nothing in the tick reads it, and it is
    /// dropped wholesale on a level switch. Bounded by the enemy roster — an entry is only ever
    /// made for a unit that was burning, and it is removed when it expires.
    /// </summary>
    readonly Dictionary<int, float> flameOut = new();
    /// <summary>
    /// This frame's burning set, as something with an O(1) Contains.
    ///
    /// GameState.BurningEnemyIds is an IReadOnlyCollection, so `Contains` on it resolves to LINQ's
    /// — a linear scan AND an enumerator allocation, per unit, per frame. Refilled here rather
    /// than rebuilt: Clear keeps the buckets, so the whole thing is allocation-free.
    /// </summary>
    readonly HashSet<int> burningNow = new();
    /// <summary>Scratch for iterating flameOut while writing to it. Reused, never re-allocated.</summary>
    readonly List<int> expiredFlames = new();
    MaterialPropertyBlock flameProps;

    void Start()
    {
        Application.targetFrameRate = 60;
        QualitySettings.vSyncCount = 0;

        random = new System.Random(12345);
        // ALL levels, including rigs — ProgressStore excludes test levels from TotalStars itself,
        // and it has to be able to read a best-star result for anything reachable.
        ProgressStore.AllLevels = levels;
        campaignCount = levels?.Count(l => l != null && !l.isTestLevel) ?? 0;

        ui = BattleUI.Create();
        ui.OnRetry = () => EnterLevel(levelIndex);
        ui.OnNext = () => EnterLevel(levelIndex + 1);
        ui.SetCoins(ProgressStore.Coins());

        BuildPools();
        EnterLevel(0);
    }

    /// <summary>
    /// The highest index navigation may reach — the end of the campaign, or the end of everything
    /// once the rigs are unlocked. Guards against a campaign of zero, which would otherwise clamp
    /// to -1 and index out of the array.
    /// </summary>
    int LastReachableIndex => (showRigs || campaignCount <= 0 ? levels.Length : campaignCount) - 1;

    /// <summary>
    /// Swaps the whole battle over to another level: new state, new scenery, pools emptied.
    ///
    /// The pools themselves are built ONCE and survive the switch — minting render slots mid
    /// session is the failure the Filament build paid for repeatedly, and there is no reason to
    /// repeat it here. What has to be reset is everything that reads a slot's PREVIOUS occupant:
    /// a hidden slot still holds the last level's pose, position and animation state.
    /// </summary>
    /// <summary>
    /// Opens the loadout picker for a level, then loads it with whatever squad comes back.
    ///
    /// This is the entry point for every PLAYER-facing level change. The ◀ ▶ debug stepper calls
    /// LoadLevel directly and skips the picker on purpose — sweeping 29 levels for missing
    /// geometry should not stop to ask about troops twenty-nine times.
    /// </summary>
    void EnterLevel(int index)
    {
        int clamped = Mathf.Clamp(index, 0, LastReachableIndex);
        var target = levels[clamped];

        if (roster == null || target.isTestLevel)
        {
            // No roster authored, or a rig: field the level exactly as written.
            loadoutGroups = null;
            LoadLevel(clamped);
            return;
        }

        ui.Hide();
        var picks = Loadout.Default(target, roster, ProgressStore.IsUnitUnlocked);
        ui.ShowLoadout(target, roster, picks, chosen =>
        {
            loadoutGroups = Loadout.ToPlayerGroups(target, chosen);
            LoadLevel(clamped);
        });
    }

    public void LoadLevel(int index)
    {
        if (levels == null || levels.Length == 0) { Debug.LogError("[Battle] no levels"); return; }
        levelIndex = Mathf.Clamp(index, 0, LastReachableIndex);
        level = levels[levelIndex];

        // battleId advances per load so nothing keyed on it can collide with the level before it.
        state = LevelBuilder.BuildInitialState(level, ++battleId, campaignCount, random,
                                              playerGroupsOverride: loadoutGroups);
        // The ammo choice is a STANDING preference, so it survives a level change and a restart.
        // Read through ProgressStore, which downgrades a selection the player no longer owns to
        // Standard — a reset wipes unlocks, and a state pointing at a locked type would fire an
        // ammo that is not owned.
        state = state with
        {
            Phase = GamePhase.Playing,
            TurnPhase = TurnPhase.Aiming,
            SelectedAmmo = ProgressStore.SelectedAmmo(),
        };

        scenery.Build(level, state.Structures);
        structureObjects.Clear();
        foreach (var st in state.Structures)
        {
            var go = scenery.Structure(st.Id);
            if (go != null) structureObjects.Add((st.Id, go));
        }

        // The scorch mark is tinted from the LEVEL's ground colour, so the pool has to be
        // re-materialled on every switch — a mark carrying the previous biome's shade is either
        // invisible or a black sticker.
        if (scenery.ScorchMaterial != null)
            foreach (var s in scorchSlots)
                s.GetComponent<MeshRenderer>().sharedMaterial = scenery.ScorchMaterial;

        TintShadows();
        HideAll();
        // The end panel belongs to the battle that raised it. It must come down here rather than
        // on the button that caused the switch, because the ◀ ▶ stepper leaves a finished battle
        // too and would otherwise carry a stale VICTORY card onto the next level.
        ui.Hide();
        ui.SetCoins(ProgressStore.Coins());
        enemyWindup = 0f;
        dragging = false;
        aimVel = Vector3.zero;
        aimPoseDegrees = 0f;
        arc.Clear();

        Debug.Log($"[Battle] L{level.levelNumber} {level.displayName}: " +
                  $"{state.PlayerUnits.Count} player, {state.EnemyUnits.Count} enemy, " +
                  $"{state.Structures.Count} structures");
    }

    void HideAll()
    {
        foreach (var go in playerUnits.All) go.SetActive(false);
        foreach (var go in enemyUnits.All) go.SetActive(false);
        foreach (var go in playerGuns) go.SetActive(false);
        foreach (var go in enemyGuns) go.SetActive(false);
        foreach (var kv in shotPools) foreach (var go in kv.Value) go.SetActive(false);
        foreach (var go in blastSlots) go.SetActive(false);
        foreach (var go in scorchSlots) go.SetActive(false);
        foreach (var go in debrisSlots) go.SetActive(false);
        foreach (var b in healthBars) b.Root.SetActive(false);
        foreach (var sh in shadowSlots) sh.SetActive(false);
        foreach (var f in flameSlots) f.Root.SetActive(false);
        // The guttering clocks are keyed by UNIT ID, and the next level re-uses those ids from 0.
        // Left standing, a corpse's leftover half-second would be inherited by whichever soldier
        // happens to be given that id on the new level — the same class of bug as a recycled slot
        // coming back holding the last occupant's pose.
        flameOut.Clear();
    }

    /// <summary>
    /// A side's unit render slots, POOLED PER CLASS.
    ///
    /// One pool per side no longer works once the classes have different geometry: a slot is a
    /// GameObject wrapping one model, and swapping the model on it is exactly the mid-session
    /// mint the Filament build kept paying for. So the pool is a table, and a unit is handed a
    /// slot of its OWN class.
    ///
    /// Sizes come from the level data rather than a constant — see ClassCounts. A pool that runs
    /// out drops a soldier off the screen silently, and a constant big enough to be safe for every
    /// class would be seven times the objects.
    /// </summary>
    sealed class UnitSlots
    {
        readonly Dictionary<string, List<GameObject>> byClass = new();
        readonly Dictionary<string, int> used = new();
        /// <summary>Every slot, for the blanket hide on a level switch.</summary>
        public readonly List<GameObject> All = new();
        /// <summary>The slots handed out this frame, in roster order — what a volley fires.</summary>
        public readonly List<GameObject> Live = new();

        /// <summary>
        /// The scale the prefab was normalised to, per class. Kept because `renderScale` is a
        /// MULTIPLIER on it — a hero is 1.9x a crowd unit — and the normalisation factor differs
        /// per model, so the two cannot be collapsed into one number.
        /// </summary>
        readonly Dictionary<string, float> baseScale = new();

        public void Add(string key, GameObject go, float normalisedScale)
        {
            if (!byClass.TryGetValue(key, out var list)) byClass[key] = list = new List<GameObject>();
            list.Add(go);
            All.Add(go);
            baseScale[key] = normalisedScale;
        }

        public float BaseScale(string key) => baseScale.TryGetValue(key, out var s) ? s : 1f;

        public void BeginFrame()
        {
            Live.Clear();
            // Rebuilt rather than cleared-and-reinserted: the key set never changes after the
            // pools are built, so this is a fixed handful of writes.
            foreach (var key in byClass.Keys) used[key] = 0;
        }

        /// <summary>
        /// The next free slot of this class, or null. Null is a REAL possibility — a class the
        /// level data never places has no pool at all — so every caller has to handle it rather
        /// than assume the roster and the pools agree.
        /// </summary>
        public GameObject Take(string key)
        {
            if (!byClass.TryGetValue(key, out var list)) return null;
            int n = used[key];
            if (n >= list.Count) return null;
            used[key] = n + 1;
            return list[n];
        }

        /// <summary>Hides everything this frame did not hand out.</summary>
        public void HideRest()
        {
            foreach (var kv in byClass)
            {
                var list = kv.Value;
                for (int i = used[kv.Key]; i < list.Count; i++)
                    if (list[i].activeSelf) list[i].SetActive(false);
            }
        }
    }

    /// <summary>
    /// How many slots each class needs, per side: the most that class is ever placed on one
    /// level, across every level in the build.
    ///
    /// Live units and RAGDOLLS share a pool, and that is why the count is everything the level
    /// ever SPAWNS rather than everything alive at once — a corpse holds its slot while the live
    /// roster shrinks, so at the end of a battle the two together still add up to the roster the
    /// level started with. Reinforcement waves and boss phases are counted for the same reason,
    /// even though nothing in the port spawns them yet: the day something does, a garrison
    /// arriving to find no slots left would show up as units that simply never appear.
    /// </summary>
    Dictionary<string, int> ClassCounts(bool playerSide)
    {
        var max = new Dictionary<string, int>();
        foreach (var lv in levels)
        {
            if (lv == null) continue;
            var per = new Dictionary<string, int>();
            void Count(IEnumerable<EnemyGroup> groups)
            {
                if (groups == null) return;
                foreach (var g in groups)
                {
                    if (g?.definition == null) continue;
                    string key = LevelScenery.ModelKey(g.definition.modelAsset);
                    per[key] = (per.TryGetValue(key, out var n) ? n : 0) + Mathf.Max(0, g.count);
                }
            }
            Count(playerSide ? lv.playerGroups : lv.enemyGroups);
            if (!playerSide)
            {
                foreach (var w in lv.reinforcementWaves) Count(w?.spawnGroups);
                foreach (var b in lv.bossPhases) Count(b?.spawnGroups);
            }
            foreach (var kv in per)
                if (!max.TryGetValue(kv.Key, out var m) || kv.Value > m) max[kv.Key] = kv.Value;
        }
        return max;
    }

    void BuildPools()
    {
        playerUnits = BuildUnitSlots(true);
        enemyUnits = BuildUnitSlots(false);
        for (int i = 0; i < UnitPoolSize; i++)
        {
            playerGuns.Add(Spawn(gunPrefab, $"pg{i}"));
            enemyGuns.Add(Spawn(gunPrefab, $"eg{i}"));
        }
        foreach (var (type, prefab) in new[]
        {
            (ProjectileType.Bullet,  bulletPrefab  ? bulletPrefab  : projectilePrefab),
            (ProjectileType.Rocket,  rocketPrefab  ? rocketPrefab  : projectilePrefab),
            (ProjectileType.Grenade, grenadePrefab ? grenadePrefab : projectilePrefab),
            (ProjectileType.Shell,   projectilePrefab),
        })
        {
            var pool = new List<GameObject>();
            for (int i = 0; i < ProjectilePoolSize; i++) pool.Add(Spawn(prefab, $"{type}{i}"));
            shotPools[type] = pool;
        }
        BuildHealthBars();
        BuildShadows();
        BuildFlames();
        for (int i = 0; i < 32; i++) blastSlots.Add(Spawn(explosionPrefab, $"x{i}"));
        for (int i = 0; i < BattleTick.ScorchSlots; i++) scorchSlots.Add(Spawn(scorchPrefab, $"sc{i}"));
        for (int i = 0; i < BattleTick.DebrisSlots; i++) debrisSlots.Add(Spawn(debrisPrefab, $"db{i}"));
    }

    /// <summary>
    /// Builds one side's per-class pools. A class the art does not cover falls back to that
    /// side's single prefab rather than being skipped: the roster is the Kotlin's to change, and
    /// a unit with no model must still render as SOMETHING — an invisible soldier that still
    /// shoots is the worst of the failure modes available here.
    /// </summary>
    UnitSlots BuildUnitSlots(bool playerSide)
    {
        var slots = new UnitSlots();
        var prefabs = playerSide ? playerUnitClassPrefabs : enemyUnitClassPrefabs;
        var fallback = playerSide ? playerUnitPrefab : enemyUnitPrefab;
        char tag = playerSide ? 'p' : 'e';

        var byKey = new Dictionary<string, GameObject>();
        for (int i = 0; i < unitClassKeys.Length && i < prefabs.Length; i++)
            if (prefabs[i] != null) byKey[unitClassKeys[i]] = prefabs[i];

        foreach (var kv in ClassCounts(playerSide))
        {
            var prefab = byKey.TryGetValue(kv.Key, out var p) ? p : fallback;
            if (prefab == null) continue;
            // A little headroom over the largest single level. Nothing in the tick lets a side
            // exceed what the level placed, so this is slack rather than a bound — but a pool
            // that runs one short drops a soldier with no error anywhere.
            int size = kv.Value + 2;
            float normalised = prefab.transform.localScale.x;
            for (int i = 0; i < size; i++)
                slots.Add(kv.Key, Spawn(prefab, $"{tag}_{kv.Key}{i}"), normalised);
        }
        return slots;
    }

    /// <summary>
    /// Pre-warms one bar per unit that could ever be on the field at once, both sides together.
    /// Built ONCE with everything else — a bar minted the frame a unit is first wounded is a
    /// render slot created mid-gameplay, which is the failure the Filament build paid for
    /// repeatedly.
    ///
    /// Two quads each, via QuadMesh: NEVER GameObject.CreatePrimitive in runtime code, because
    /// IL2CPP strips the collider classes it silently attaches and the call takes the whole level
    /// build down on device.
    /// </summary>
    void BuildHealthBars()
    {
        int need = 0;
        foreach (var lv in levels)
        {
            if (lv == null) continue;
            int n = 0;
            foreach (var g in lv.playerGroups) n += Mathf.Max(0, g?.count ?? 0);
            foreach (var g in lv.enemyGroups) n += Mathf.Max(0, g?.count ?? 0);
            foreach (var w in lv.reinforcementWaves)
                foreach (var g in w.spawnGroups) n += Mathf.Max(0, g?.count ?? 0);
            foreach (var b in lv.bossPhases)
                foreach (var g in b.spawnGroups) n += Mathf.Max(0, g?.count ?? 0);
            need = Mathf.Max(need, n);
        }

        for (int i = 0; i < need; i++)
        {
            var root = new GameObject($"hb{i}");
            root.transform.SetParent(poolRoot, false);
            // A 180° turn about X, not about Y. The shared quad's normal faces -Z and has to be
            // turned to face the camera; turning about Y would ALSO mirror local x, and the fill
            // anchors to one end, so the bar would drain right-to-left. Flipping about X mirrors
            // the vertical instead, which a symmetric bar cannot tell apart.
            root.transform.rotation = Quaternion.Euler(180f, 0f, 0f);

            var back = QuadMesh.Create("back", root.transform, healthBarSource);
            back.transform.localScale = new Vector3(BarWidth, BarHeight, 1f);

            var fill = QuadMesh.Create("fill", root.transform, healthBarSource);
            // Nearer the camera in WORLD terms, which after the X-flip is local -z.
            fill.transform.localPosition = new Vector3(0f, 0f, -0.002f);

            root.SetActive(false);
            healthBars.Add((root, fill.transform, fill.GetComponent<MeshRenderer>(),
                            back.GetComponent<MeshRenderer>()));
        }
    }

    /// <summary>
    /// One shadow per unit that can be on the field at once, pre-warmed with everything else, and
    /// ONE shared runtime material — every shadow on a level is the same colour, so a property
    /// block per instance would be per-frame work for no difference.
    /// </summary>
    void BuildShadows()
    {
        if (shadowPrefab == null) return;
        for (int i = 0; i < healthBars.Count; i++)
        {
            var go = Spawn(shadowPrefab, $"sh{i}");
            // The quad's own normal faces -Z; a 90° pitch lays it flat on the ground.
            go.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            // After the 90-degree pitch the quad's local Y runs along world Z, so localScale.y
            // is the DEPTH of the ellipse — the axis that buys on-screen height at this camera.
            go.transform.localScale =
                new Vector3(ShadowDiameter, ShadowDiameter * ShadowDepthStretch, 1f);
            var r = go.GetComponent<MeshRenderer>();
            if (shadowMat == null) shadowMat = new Material(r.sharedMaterial);
            r.sharedMaterial = shadowMat;
            shadowSlots.Add(go);
        }
    }

    /// <summary>
    /// One flame per ENEMY unit that could ever be on the field at once — only enemies burn, since
    /// the incendiary is a round the player fires.
    ///
    /// A BOUNDED POOL, pre-warmed with everything else and never added to. That is the hard rule
    /// here: minting a render slot mid-session is the failure the Filament build paid for
    /// repeatedly, and a flame is minted at exactly the worst moment for it — the frame a volley
    /// lands, with the blast, scorch and debris pools all being drawn from at once.
    /// </summary>
    void BuildFlames()
    {
        if (flamePrefab == null) return;

        int need = 0;
        foreach (var lv in levels)
        {
            if (lv == null) continue;
            int n = 0;
            foreach (var g in lv.enemyGroups) n += Mathf.Max(0, g?.count ?? 0);
            // Reinforcements and boss phases can put a man on the field who is not in the opening
            // formation, and he can be set alight like anyone else — the same reasoning that sizes
            // the health-bar pool.
            foreach (var w in lv.reinforcementWaves)
                foreach (var g in w.spawnGroups) n += Mathf.Max(0, g?.count ?? 0);
            foreach (var b in lv.bossPhases)
                foreach (var g in b.spawnGroups) n += Mathf.Max(0, g?.count ?? 0);
            need = Mathf.Max(need, n);
        }

        for (int i = 0; i < need; i++)
        {
            var go = Spawn(flamePrefab, $"fl{i}");
            var outer = go.transform.Find("outer");
            var inner = go.transform.Find("inner");
            if (outer == null || inner == null)
            {
                // The prefab is built by SpikeSceneBattle.MakeFlamePrefab and these two names are
                // the contract between it and this method. Say so at BUILD time rather than
                // silently drawing nothing — the whole point of this feature is that a burn is no
                // longer invisible.
                Debug.LogError("[Battle] flamePrefab has no outer/inner tongue — flames disabled");
                return;
            }
            flameSlots.Add((go, outer, inner,
                            outer.GetComponent<MeshRenderer>(), inner.GetComponent<MeshRenderer>()));
        }
    }

    /// <summary>
    /// Puts a flame on every burning enemy, and keeps one guttering for half a second on anyone
    /// whose burn has just resolved.
    ///
    /// The set is the tick's, so this cannot disagree with who actually takes the damage.
    /// </summary>
    void SyncFlames(float dt)
    {
        if (flameSlots.Count == 0) return;
        flameProps ??= new MaterialPropertyBlock();

        burningNow.Clear();
        foreach (var id in state.BurningEnemyIds) burningNow.Add(id);

        // Age the guttering flames first, and re-arm anyone still alight. A unit hit by a second
        // incendiary round while already burning must not be handed a half-dead flame.
        if (flameOut.Count > 0)
        {
            expiredFlames.Clear();
            foreach (var id in flameOut.Keys) expiredFlames.Add(id);
            foreach (var id in expiredFlames)
            {
                float left = flameOut[id] - dt;
                if (left <= 0f) flameOut.Remove(id); else flameOut[id] = left;
            }
        }

        // Time.time, not an accumulated phase. dt VARIES, and a flicker integrated per frame
        // would run at a different rate on a stuttering one; sampling the clock cannot.
        float now = Time.time;
        int used = 0;
        foreach (var u in state.EnemyUnits)
        {
            bool alight = burningNow.Contains(u.Id);
            if (alight) flameOut[u.Id] = FlameRig.OutSeconds;
            else if (!flameOut.ContainsKey(u.Id)) continue;
            used = Burn(u.Id, u.X, u.Y, u.Z, u.Definition, alight, used);
        }

        // AND ON THE BODIES THE BURN KILLED. A man the fire finishes leaves EnemyUnits on the
        // frame he dies, so drawing only the living would snuff his flame out at the exact moment
        // it did the most — the one frame in the whole feature where the player is looking at it.
        // The ragdoll carries the same Id, so it keeps its own guttering half-second and the fire
        // falls with him.
        foreach (var d in state.DyingUnits)
            if (!d.IsPlayerSide && flameOut.ContainsKey(d.Id))
                used = Burn(d.Id, d.X, d.Y, d.Z, d.Definition, false, used);

        for (int i = used; i < flameSlots.Count; i++)
            if (flameSlots[i].Root.activeSelf) flameSlots[i].Root.SetActive(false);

        int Burn(int id, float x, float y, float z, UnitDefinitionSO def, bool alight, int slot)
        {
            if (slot >= flameSlots.Count) return slot;

            // Full strength while alight, then guttering. Squared, so the fire spends most of the
            // half-second visibly dying rather than snapping to half and drifting away.
            float k = alight ? 1f : flameOut[id] / FlameRig.OutSeconds;
            float alpha = k * k;

            var (root, outer, inner, outerRenderer, innerRenderer) = flameSlots[slot++];
            root.SetActive(true);

            // The flame DOES scale with renderScale, unlike the health bar: a bar is UI and must
            // not grow with the man, but fire is a physical thing engulfing a body and a hero is
            // 1.9x the size of that body.
            FlameRig.Place(root.transform, outer, inner, x, y, z,
                           def != null ? def.renderScale : 1f, now, id);

            // Per-instance, because every slot shares the one Flame material — tinting the
            // material itself would fade every flame on the field together.
            flameProps.SetColor(BaseColorId, new Color(1f, 1f, 1f, alpha));
            outerRenderer.SetPropertyBlock(flameProps);
            innerRenderer.SetPropertyBlock(flameProps);
            return slot;
        }
    }

    /// <summary>
    /// Re-tints the shadows from THIS level's ground, so the ellipse reads as shade rather than
    /// as a dark sticker: the same grey that works on snow is a black blob on the ash of
    /// CityRuins and invisible on the Forest's dark green.
    ///
    /// The factors are the Filament build's, and they are not uniform. 0.70 was too light to
    /// register on snow — Winter's ground is nearly white, so a 70% scale of it is still nearly
    /// white — and BLUE is kept highest so the shade COOLS rather than muddies. Snow shadow goes
    /// blue, not grey-brown.
    /// </summary>
    void TintShadows()
    {
        if (shadowMat == null) return;
        var g = level != null && level.background != null
            ? level.background.groundColor
            : new Color(0.7f, 0.7f, 0.7f);
        // Fully opaque: the softness comes from the texture's own falloff, and dropping the
        // material alpha on top of it just washed the whole ellipse out on the bright biomes,
        // which is where it is needed most.
        shadowMat.color = new Color(g.r * 0.58f, g.g * 0.62f, g.b * 0.72f, 1f);
    }

    /// <summary>
    /// Puts a shadow under every LIVING unit. Ragdolls get none: a corpse is falling and then
    /// lying down, and a crisp ellipse pinned under a tumbling body reads as a sticker.
    /// </summary>
    void SyncShadows()
    {
        int used = 0;
        used = PlaceShadows(state.PlayerUnits, used);
        used = PlaceShadows(state.EnemyUnits, used);
        for (int i = used; i < shadowSlots.Count; i++)
            if (shadowSlots[i].activeSelf) shadowSlots[i].SetActive(false);
    }

    int PlaceShadows(IReadOnlyList<UnitEntity> units, int used)
    {
        foreach (var u in units)
        {
            if (used >= shadowSlots.Count) break;
            var go = shadowSlots[used++];
            go.SetActive(true);
            // At the unit's FOOT, not at its body: a garrison stands on a deck, so the shadow
            // follows the unit's own y rather than the world floor.
            go.transform.position = GameSpace.ToUnity(u.X, u.Y + ShadowY, u.Z);
            float scale = u.Definition != null ? u.Definition.renderScale : 1f;
            go.transform.localScale = new Vector3(ShadowDiameter * scale,
                                                  ShadowDiameter * ShadowDepthStretch * scale, 1f);
        }
        return used;
    }

    /// <summary>
    /// Puts a bar over every unit that has taken damage, on both sides.
    ///
    /// Both sides on purpose: the tactically useful reading is which ENEMY is one round from
    /// dying when the next volley is being aimed, and the player's own line has to answer the
    /// same question when it is being shot at. The bar is driven from the ENTITY, not from a
    /// render slot, so it does not care that slots are handed out per class.
    /// </summary>
    void SyncHealthBars()
    {
        barProps ??= new MaterialPropertyBlock();
        int used = 0;
        used = PlaceBars(state.PlayerUnits, used);
        used = PlaceBars(state.EnemyUnits, used);
        for (int i = used; i < healthBars.Count; i++)
            if (healthBars[i].Root.activeSelf) healthBars[i].Root.SetActive(false);
    }

    int PlaceBars(IReadOnlyList<UnitEntity> units, int used)
    {
        foreach (var u in units)
        {
            // Driven by the since-hit CLOCK, not by "is wounded". A bar that stays up for as long
            // as a unit is damaged becomes a second HUD laid over the army — by then the player
            // has read the hit, and what is left is clutter competing with the soldiers.
            if (u.LastHitAge < 0f) continue;
            if (used >= healthBars.Count) break;

            int max = u.Definition != null ? Mathf.Max(u.Definition.maxHp, 1) : 1;
            var (root, fill, fillRenderer, backRenderer) = healthBars[used++];
            root.SetActive(true);

            float scale = u.Definition != null ? u.Definition.renderScale : 1f;
            // The bar clears the HELMET, so its height offset follows renderScale — but the bar
            // ITSELF does not scale with it. A hero is 1.9x a crowd unit and a 1.9x bar would
            // read as a different, more important kind of information.
            root.transform.position = GameSpace.ToUnity(
                u.X, u.Y + UnitGeometry.UnitScaleUnits * scale + BarGap, u.Z);

            float frac = Mathf.Clamp01((float)u.Hp / max);
            // COLOUR is chosen from the true fraction, WIDTH from the floored one. Flooring both
            // would make a dying unit read as merely wounded, which is the opposite of the fix.
            float shown = Mathf.Max(frac, BarMinFill);
            float inner = BarWidth * (1f - BarBorder);
            fill.localScale = new Vector3(inner * shown, BarHeight * (1f - BarBorder * 2f), 1f);
            // Anchored to the bar's left edge rather than centred, so damage eats it from one
            // side. A centred fill shrinks toward the middle from both ends, which reads as a
            // charging meter rather than a wound.
            // `shown`, matching the scale above — anchoring on the true fraction while scaling on
            // the floored one would slide the fill off its own left edge.
            fill.localPosition = new Vector3(-(inner - inner * shown) * 0.5f, 0f, -0.002f);

            // BOTH quads fade, not just the fill — fading the fill alone leaves the dark backing
            // plate behind as a floating black tick over the soldier's head, which is a worse
            // artefact than the bar it was trying to retire.
            float alpha = CosmeticSystems.HealthBarAlpha(u.LastHitAge);
            var c = frac > 0.6f ? BarHigh : frac > 0.3f ? BarMid : BarLow;
            c.a = alpha;
            barProps.SetColor(BaseColorId, c);
            fillRenderer.SetPropertyBlock(barProps);

            // The track fades FASTER than the fill — see HealthBarTrackAlpha. Equal alpha leaves
            // the dark track as the last thing standing, so the bar ends its life as a black
            // rectangle rather than as a colour dissolving away.
            var back = BarBackColor;
            back.a = CosmeticSystems.HealthBarTrackAlpha(u.LastHitAge);
            barProps.SetColor(BaseColorId, back);
            backRenderer.SetPropertyBlock(barProps);
        }
        return used;
    }

    GameObject Spawn(GameObject prefab, string name)
    {
        var go = Instantiate(prefab, poolRoot);
        go.name = name;
        go.SetActive(false);
        return go;
    }

    void Update()
    {
        // THERE IS NO BATTLE YET while the BOOT loadout picker is open: Start opens the picker and
        // returns, and `state` is not built until the player presses BEGIN and LoadLevel runs. Every
        // frame of that screen used to enter the tick with a null state and throw out of
        // BattleTick.Step on its first line (`s.SelectedAmmo`) — 195 NullReferenceExceptions in 3
        // seconds on device, and ZERO in battle, which is what finally placed it.
        //
        // Nothing looked broken, because the picker is uGUI on its own canvas and both HandleInput
        // and OnGUI already stand down while it is open. It cost a thrown exception and a stack
        // capture per frame on the one screen where the player is sitting still and reading.
        //
        // The guard is on `state`, not on `ui.LoadoutOpen`: a LATER picker (RETRY, NEXT LEVEL) opens
        // over a state that exists and ticks through it perfectly well. What must not run is a tick
        // with nothing to tick.
        if (state == null) return;

        float dt = Time.deltaTime;
        smoothedDt += (Time.unscaledDeltaTime - smoothedDt) * 0.05f;

        HandleInput();
        // Unscaled: the free camera is a tool, and it has to keep flying on a paused or
        // finished battle — which is most of when it gets used.
        if (freeCamOn) StepFreeCam(Time.unscaledDeltaTime);

        // The enemy turn runs itself: a windup beat, then its volley.
        if (state.Phase == GamePhase.Playing && state.TurnPhase == TurnPhase.EnemyWindup)
        {
            enemyWindup += dt;
            if (enemyWindup >= TurnFlow.EnemyWindupSeconds)
            {
                enemyWindup = 0f;
                state = BattleTick.FireEnemyVolley(state, random);
                VolleyAnim(playerSide: false);
                // The enemy volley had no fire sound at all — PlayVolleyFire was only wired to
                // the player's release path, so half the battle fired silently.
                if (audioFx != null) audioFx.PlayVolleyFire();
            }
        }

        var before = state;
        state = BattleTick.Step(state, dt, level, random, ammoCatalog);

        // Stand the line down as soon as it is the player's move again — the held pose belongs to
        // a volley that has now resolved.
        if (!dragging && state.TurnPhase == TurnPhase.Aiming) aimPoseDegrees = 0f;
        DriveAudio(before, state);

        // Mid-battle events, and the turn handover, both go through the ONE banner channel.
        // Two competing banners is how a game ends up telling the player nothing at all.
        //
        // An event outranks the turn: "Their heavies are here!" matters more than "enemy turn",
        // and they land on the same frame when a wave arrives on the handover.
        if (state.BossAnnouncement != before.BossAnnouncement &&
            !string.IsNullOrEmpty(state.BossAnnouncement))
            Debug.Log($"[Battle] EVENT: {state.BossAnnouncement} " +
                      $"(enemies {before.EnemyUnits.Count} -> {state.EnemyUnits.Count})");

        if (before.TurnPhase != TurnPhase.EnemyWindup && state.TurnPhase == TurnPhase.EnemyWindup)
            turnBanner = ThreatLine(state);
        else if (state.TurnPhase == TurnPhase.Aiming) turnBanner = null;

        ui.SetEvents(state.BossAnnouncement ?? turnBanner, state.TelegraphText);

        ResolveBattleEnd();

        Render();
        ApplyCamera();

        if (dragging) { dragFrames++; if (dragFrames > 2) worstDragDt = Mathf.Max(worstDragDt, dt); }
    }

    void HandleInput()
    {
        // The picker is a modal screen; a drag behind it would fire a volley into a battle
        // the player has not agreed to start yet.
        if (ui != null && ui.LoadoutOpen) return;
        if (state.Phase != GamePhase.Playing) return;
        if (Input.touchCount == 0)
        {
            if (dragging) { dragging = false; Release(); }
            return;
        }

        var t = Input.GetTouch(0);
        if (t.phase == TouchPhase.Began)
        {
            if (state.TurnPhase != TurnPhase.Aiming) return;
            // Input.touch counts y from the BOTTOM, IMGUI rects from the top.
            var fromTop = new Vector2(t.position.x, Screen.height - t.position.y);
            if (freeCamOn && FreeCamPadRect.Contains(fromTop)) return;
            // Same trap the free-cam pad paid for: a tap that lands on a button must not ALSO
            // start an aim drag, or picking ammo throws a volley and ends the turn.
            if (AmmoSelectorRect.Contains(fromTop)) return;
            dragging = true; dragStart = t.position; dragFrames = 0; worstDragDt = 0f;
        }
        else if (dragging && (t.phase == TouchPhase.Moved || t.phase == TouchPhase.Stationary))
        {
            var w = AimSystem.DragToWorld(t.position - dragStart);
            aimVel = AimSystem.AimVelocity(w.x, w.y);
            aimPoseDegrees = AimSystem.AngleDegrees(aimVel);
            var origin = MuzzleOrigin();
            TrajectoryPhysics.SampleArc(origin, aimVel, 7, 0.05f, arc);
        }
        else if (dragging && (t.phase == TouchPhase.Ended || t.phase == TouchPhase.Canceled))
        {
            dragging = false;
            Release();
        }
    }

    void Release()
    {
        arc.Clear();
        if (aimVel.sqrMagnitude < 0.01f) return;
        if (state.TurnPhase != TurnPhase.Aiming) return;
        state = BattleTick.FireVolley(state, aimVel, random, ammoCatalog);
        if (audioFx != null) audioFx.PlayVolleyFire();
        VolleyAnim(playerSide: true);
        Debug.Log($"[Battle] volley: {state.Projectiles.Count} rounds at " +
                  $"{AimSystem.StrengthPercent(aimVel):F0}% / {AimSystem.AngleDegrees(aimVel):F1}deg");
    }

    Vector3 MuzzleOrigin()
    {
        if (state.PlayerUnits.Count == 0) return new Vector3(-9.5f, 0.9f, 0f);
        return new Vector3(state.PlayerUnits.Average(u => u.X), 0.9f, 0f);
    }

    /// <summary>
    /// What the enemy turn is about to do, in one line the player can act on.
    ///
    /// PRODUCT_DIRECTION 0.6: "fear is engagement, silence is punishment". The enemy turn used to
    /// pass with nothing on screen but a HUD word, so a charge closing to contact and an ordinary
    /// volley looked identical until the damage landed.
    ///
    /// The ADVANCE is named first because it is the only thing the player can lose the level to
    /// this turn — a marching group that reaches the line fights in melee, and no amount of
    /// counting rifles matters if it arrives.
    /// </summary>
    static string ThreatLine(GameState state)
    {
        int advancing = state.EnemyUnits.Count(u => u.AdvancePerTurn > 0f);
        if (advancing > 0) return $"{advancing} closing on your line";
        return state.EnemyUnits.Count > 0 ? "Enemy turn" : null;
    }

    /// <summary>
    /// Pays the battle out and raises the end panel.
    ///
    /// This is the call the port never had. EconomyStore, ProgressStore and TurnFlow.AwardVictory
    /// were all ported, tested and correct — and reached by nothing, so no coin was ever earned
    /// and no star ever recorded in a running build. One call site turns the whole meta layer on.
    ///
    /// Deliberately keyed on battleId rather than on a Playing->over EDGE. An edge is a single
    /// frame and the award has to survive everything that keeps ticking afterwards; keying on the
    /// battle makes "pay once per battle" the literal invariant instead of a consequence of one.
    /// </summary>
    void ResolveBattleEnd()
    {
        if (state.Phase == GamePhase.Playing) return;
        if (awardedBattleId == battleId) return;
        awardedBattleId = battleId;

        if (state.Phase == GamePhase.Victory)
        {
            var award = TurnFlow.AwardVictory(level, state.PlayerUnits.Count,
                                              state.InitialPlayerCount);
            // NEXT is bounded by the CAMPAIGN, never by the array — winning the last campaign
            // level must not offer to walk the player into the unit parade.
            ui.ShowVictory(award, state.PlayerUnits.Count, state.InitialPlayerCount,
                           hasNextLevel: levelIndex < campaignCount - 1);
            Debug.Log($"[Battle] victory: {award.Stars}★, +{award.Coins} coins" +
                      (award.BonusTag != null ? $" ({award.BonusTag})" : "") +
                      $", balance {ProgressStore.Coins()}");
        }
        else
        {
            int coins = TurnFlow.AwardDefeat(level);
            ui.ShowDefeat(coins);
            Debug.Log($"[Battle] defeat: +{coins} coins, balance {ProgressStore.Coins()}");
        }
        // The pill is NOT snapped to the new balance here — the panel's count-up climbs it, and
        // setting it now would give the animation nothing left to show.
    }

    /// <summary>
    /// Sound is driven from STATE DELTAS rather than from the systems raising events. The tick
    /// stays engine-independent that way, and a replayed or rewound state produces the same
    /// audio as a live one.
    /// </summary>
    void DriveAudio(GameState before, GameState after)
    {
        if (audioFx == null) return;

        int deaths = (after.TotalPlayerKills - before.TotalPlayerKills)
                   + (after.TotalEnemyKills - before.TotalEnemyKills);
        if (deaths > 0) audioFx.PlayUnitDeath();

        // EXPLOSION only for real blasts — splash weapons and structure hits. A rifle round
        // striking a soldier is a hit, not a bang; playing the explosion clip for every
        // detonation is what made an ordinary rifle volley sound like artillery.
        if (after.TotalBlasts > before.TotalBlasts) audioFx.PlayExplosion();

        // A wounded survivor gets the hit sound, provided nobody died this tick (the death
        // scream would bury it anyway).
        if (deaths == 0 && after.TotalWoundedHits > before.TotalWoundedHits) audioFx.PlayUnitHit();

        // Ground impacts are counted by the tick, NOT inferred from the projectile list
        // shrinking — a round culled on the side bounds sailed off the field and never landed,
        // and playing dirt-thuds for those made every overshot volley sound like it connected.
        int dirt = after.TotalGroundImpacts - before.TotalGroundImpacts;
        if (dirt > 0) audioFx.PlayGroundImpact(dirt);

        if (before.Phase == GamePhase.Playing && after.Phase == GamePhase.Victory) audioFx.PlayVictory();
        if (before.Phase == GamePhase.Playing && after.Phase == GamePhase.Defeat) audioFx.PlayDefeat();
    }

    void Render()
    {
        playerUnits.BeginFrame();
        enemyUnits.BeginFrame();
        SyncUnits(state.PlayerUnits, playerUnits, playerGuns, aimingRight: true);
        SyncUnits(state.EnemyUnits, enemyUnits, enemyGuns, aimingRight: false);

        // Ragdolls draw from the SAME per-class pools as the living, after them — a corpse is
        // still a soldier of its class, and giving it a slot of some other class would have a
        // sniper fall over as a rifleman.
        foreach (var d in state.DyingUnits)
        {
            var slots = d.IsPlayerSide ? playerUnits : enemyUnits;
            string key = UnitClassKey(d.Definition);
            var go = slots.Take(key);
            if (go == null) continue;
            go.SetActive(true);
            float dScale = slots.BaseScale(key) *
                           (d.Definition != null ? d.Definition.renderScale : 1f);
            if (!Mathf.Approximately(go.transform.localScale.x, dScale))
                go.transform.localScale = Vector3.one * dScale;
            go.transform.position = GameSpace.ToUnity(d.X, d.Y, d.Z);
            // An animated unit FALLS OVER in its own clip, so the ragdoll's topple rotation is
            // the no-animation workaround for exactly this and must not be applied on top —
            // a body folding to the ground while also spinning flat reads as a glitch, not a death.
            if (go.TryGetComponent<UnitAnim>(out var dyingAnim))
            {
                // A FRACTION of the tumble, capped — not identity and not the whole spin. Full
                // identity flew the body backwards perfectly upright, like a statue on rails;
                // the full 220 deg/s made it fold AND cartwheel. See RagdollLeanDegrees.
                go.transform.rotation = Quaternion.Euler(0f, 0f,
                    -CosmeticSystems.RagdollLeanDegrees(d.Rotation, d.IsPlayerSide));
                dyingAnim.Set(UnitAnim.Die);
            }
            else go.transform.rotation = Quaternion.Euler(0f, 0f, -d.Rotation);
        }
        playerUnits.HideRest();
        enemyUnits.HideRest();
        SyncHealthBars();
        SyncShadows();
        SyncFlames(Time.deltaTime);
        // Guns follow the LIVE roster only — a ragdoll drops its weapon rather than carrying
        // one through a tumble, which is also what the shipping build does.
        for (int i = state.PlayerUnits.Count; i < playerGuns.Count; i++) playerGuns[i].SetActive(false);
        for (int i = state.EnemyUnits.Count; i < enemyGuns.Count; i++) enemyGuns[i].SetActive(false);

        // Rounds render as their OWN type — a rifle volley is bullets, not nine tank shells.
        foreach (var kv in shotPools) foreach (var go in kv.Value) if (go.activeSelf) go.SetActive(false);
        var used = new Dictionary<ProjectileType, int>();
        foreach (var pr in state.Projectiles)
        {
            if (!shotPools.TryGetValue(pr.Type, out var pool)) continue;
            used.TryGetValue(pr.Type, out int n);
            if (n >= pool.Count) continue;
            used[pr.Type] = n + 1;

            var go = pool[n];
            go.SetActive(true);
            go.transform.position = GameSpace.ToUnity(pr.X, pr.Y, pr.Z);
            float deg = Mathf.Atan2(pr.Vy, -pr.Vx) * Mathf.Rad2Deg;
            go.transform.rotation = Quaternion.Euler(0f, 0f, deg);
        }

        // Explosions: swell fast, then FADE. Without the fade an opaque sphere just sits there
        // and vanishes, which reads as a solid ball rather than as fire. Alpha is set per
        // instance via a property block — pooled slots share one material, so tinting the
        // material itself would fade every live blast together.
        blastProps ??= new MaterialPropertyBlock();
        for (int i = 0; i < blastSlots.Count; i++)
        {
            if (i < state.Explosions.Count)
            {
                var x = state.Explosions[i];
                float p2 = Mathf.Clamp01(x.Progress);
                blastSlots[i].SetActive(true);
                blastSlots[i].transform.position = GameSpace.ToUnity(x.X, x.Y, x.Z);

                // Swell fast then ease — a linear grow reads as a balloon inflating.
                float swell = 0.35f + 0.85f * Mathf.Sqrt(p2);
                blastSlots[i].transform.localScale = Vector3.one * x.Scale * swell;

                // Hold full opacity briefly, then fall away over the back half.
                float alpha = 1f - Mathf.Clamp01((p2 - 0.25f) / 0.75f);
                var r = blastSlots[i].GetComponent<MeshRenderer>();
                r.GetPropertyBlock(blastProps);
                blastProps.SetColor("_BaseColor", new Color(1f, 0.55f + 0.25f * (1f - p2), 0.15f, alpha));
                r.SetPropertyBlock(blastProps);
            }
            else blastSlots[i].SetActive(false);
        }

        // Scorch marks lie FLAT on the ground, lifted a hair to avoid z-fighting with it.
        for (int i = 0; i < scorchSlots.Count; i++)
        {
            if (i < state.Scorches.Count)
            {
                var sc = state.Scorches[i];
                scorchSlots[i].SetActive(true);
                scorchSlots[i].transform.position = GameSpace.ToUnity(sc.X, 0.012f, sc.Z);
                scorchSlots[i].transform.rotation = Quaternion.Euler(90f, 0f, 0f);
                scorchSlots[i].transform.localScale =
                    Vector3.one * CosmeticSystems.ScorchWorldRadius * 2f * sc.Scale;
            }
            else scorchSlots[i].SetActive(false);
        }

        for (int i = 0; i < debrisSlots.Count; i++)
        {
            if (i < state.Debris.Count)
            {
                var d = state.Debris[i];
                debrisSlots[i].SetActive(true);
                debrisSlots[i].transform.position = GameSpace.ToUnity(d.X, d.Y, d.Z);
                debrisSlots[i].transform.rotation = Quaternion.Euler(0f, 0f, -d.Rotation);
                // Squash is 1 for a tumbling chunk and ~0.3 for a settled ruin slab.
                debrisSlots[i].transform.localScale =
                    new Vector3(d.Size, d.Size * d.Squash, d.Size);
            }
            else debrisSlots[i].SetActive(false);
        }

        // Structures are static for the battle's life — one object each, hidden on destruction.
        var liveIds = new HashSet<int>(state.Structures.Select(s2 => s2.Id));
        foreach (var (id, go) in structureObjects)
        {
            bool live = liveIds.Contains(id);
            if (go.activeSelf != live) go.SetActive(live);
        }

        // A damaged structure LOSES the geometry it just shed. The count comes from the tick's
        // own ShedChunks rather than being recomputed here — the two used to derive it separately
        // in the Filament build, which is the kind of duplication that drifts until a building
        // drops a piece it still has, or loses one with nothing falling off it.
        //
        // Chunks are proud add-ons over an intact core, so hiding one exposes bare structure
        // rather than opening a hole. Renderers are toggled, not the GameObjects: a chunk node
        // may carry children, and deactivating it would take them with it.
        foreach (var st in state.Structures)
        {
            var groups = scenery.ChunkGroups(st.Id);
            for (int g = 0; g < groups.Length; g++)
            {
                bool shown = g >= st.ShedChunks;
                var list = groups[g];
                for (int r = 0; r < list.Count; r++)
                    if (list[r] != null && list[r].enabled != shown) list[r].enabled = shown;
            }
        }
    }

    /// <summary>
    /// Fires the shoot one-shot on every unit of a side. The game throws FULL-ROSTER volleys, so
    /// "the whole line fires at once" is not an approximation here — it is what happens.
    /// </summary>
    void VolleyAnim(bool playerSide)
    {
        // The LIVE list, not the first N of a pool: slots are per class now, so "the first N
        // slots" is not the roster any more — it is the first N of whichever class happens to be
        // enumerated first, which would fire some soldiers twice and leave others still.
        foreach (var go in (playerSide ? playerUnits : enemyUnits).Live)
            if (go.TryGetComponent<UnitAnim>(out var a)) a.Fire();
    }

    /// <summary>Which rigged silhouette a unit renders as. The DATA still names the old
    /// unriggable models, so the key is the bare model name and the `_rigged` suffix lives on the
    /// asset — see RiggedUnits.Models.</summary>
    static string UnitClassKey(UnitDefinitionSO def)
        => def == null ? "unit_rifleman" : LevelScenery.ModelKey(def.modelAsset);

    void SyncUnits(IReadOnlyList<UnitEntity> units, UnitSlots slots,
                   List<GameObject> guns, bool aimingRight)
    {
        // BOTH lines elevate, from different quantities, because they aim differently.
        //
        // The PLAYER's whole line shares one angle: FireVolley hands every unit
        // `aimVelocity + jitter`, with no per-unit solve, so the drag angle is exactly what the
        // arms depict. The ENEMY solves a fresh random arc PER UNIT inside EnemyAI.AimAt, so its
        // line does not share an angle at all — each soldier reads back the elevation of the
        // round it actually fired, which is why the enemy rank fans across a spread of arcs
        // instead of moving as one.
        //
        // Neither of these is the tank. Its shell is solved for arrival and leaves on a
        // different angle than the drag; pointing the barrel at the drag angle is a bug this
        // project has already shipped once.
        float aimPose = aimingRight ? aimPoseDegrees : 0f;

        // The weapon sits at chest height, offset toward the side the unit faces. X is mirrored
        // by GameSpace, so the offset is applied in GAME space and converted with the body —
        // applying it after conversion would put every gun on the wrong shoulder.
        float sign = aimingRight ? 1f : -1f;
        for (int i = 0; i < units.Count; i++)
        {
            var u = units[i];
            // A unit whose class has no pool renders as nothing. That is a data/art disagreement
            // rather than a runtime condition, and PortSelfTest asserts against it — say so once
            // here rather than leaving a silently missing soldier to be found on a device.
            string key = UnitClassKey(u.Definition);
            var go = slots.Take(key);
            if (go == null)
            {
                Debug.LogWarning($"[Battle] no render slot for {u.Definition?.id} ({key})");
                continue;
            }
            slots.Live.Add(go);
            bool wasHidden = !go.activeSelf;
            go.SetActive(true);
            go.transform.position = GameSpace.ToUnity(u.X, u.Y, u.Z);
            go.transform.rotation = Quaternion.identity;

            // `renderScale` reached the port as a FORMATION number only — it spread the heroes
            // apart and never made them bigger, so a hero authored at 1.9x the crowd rendered at
            // exactly crowd size. Harmless while every class shared one model; the moment the
            // hero has its own greatcoat-and-cap silhouette, drawing it at 55px throws away the
            // whole point of a body that shares no geometry with the crowd.
            float want = slots.BaseScale(key) *
                         (u.Definition != null ? u.Definition.renderScale : 1f);
            if (!Mathf.Approximately(go.transform.localScale.x, want))
                go.transform.localScale = Vector3.one * want;
            // Slots are recycled, so a slot that was last used by a CORPSE comes back still
            // holding the death pose. Re-arm it on hidden→visible, the same rule the Android
            // build's culling repair uses, and stagger the idle so the line is not a chorus line.
            if (go.TryGetComponent<UnitAnim>(out var anim))
            {
                if (wasHidden) anim.Desync(u.Id);
                else anim.Set(UnitAnim.Idle);
                anim.AimDegrees = aimingRight
                    ? aimPose
                    : state.EnemyAimDegrees.TryGetValue(u.Id, out var ea) ? ea : 0f;
            }

            // A rigged unit carries its weapon on its own arm, so the pooled gun would be a
            // second rifle hanging in the air beside it.
            if (i >= guns.Count) continue;
            if (anim != null) { guns[i].SetActive(false); continue; }
            guns[i].SetActive(true);
            guns[i].transform.position = GameSpace.ToUnity(
                u.X + sign * 0.14f, u.Y + 0.30f, u.Z - 0.02f);
            guns[i].transform.rotation = Quaternion.Euler(0f, 0f, aimingRight ? 0f : 180f);
        }
    }

    // ---- free camera (debug tool) --------------------------------------------------------

    /// <summary>
    /// DEBUG-ONLY free camera, ported from the Android build's `ui/battle/DebugCamera.kt`.
    ///
    /// Why it exists: the gameplay camera is entirely state-driven — it frames the player line
    /// while aiming and the enemy cluster on scout and resolve — so LOOKING at something (a
    /// garrison that is floating, a structure's deck, a prop that is the wrong size) means firing
    /// a volley and catching the one moment the camera happens to swing past it, then hunting for
    /// that frame in a screen recording. The floating-garrison and detached-turret bugs were both
    /// confirmed in seconds once the camera could simply be parked in front of them.
    ///
    /// It HOLDS, including through volleys and the victory screen. That is the whole feature —
    /// a camera that resumes its solve the moment something happens is the problem, not the tool.
    ///
    /// x/y are world units and z is camera distance, exactly as on Android. The ground-plane solve
    /// still runs against them (BattleCamera.Apply reads the camera's ACTUAL height, not the
    /// constant), so the horizon stays where the backdrop puts it at any height.
    ///
    /// X IS GAME SPACE, NOT UNITY SPACE, and both halves of that matter. `GameSpace.CameraX`
    /// negates — Unity is left-handed and screen-right is -x — so a raw Unity x made the "→"
    /// button pan the view LEFT, which it visibly did on the first device run. And the readout
    /// exists to be written down and compared against level data, which is authored in game x; a
    /// tool that reports the mirror image of the coordinate you are hunting is worse than no
    /// readout.
    /// </summary>
    struct FreeCam
    {
        public float X, Y, Z;
        public FreeCam Pan(float d) { X += d; return this; }
        public FreeCam Lift(float d) { Y = Mathf.Clamp(Y + d, 0.1f, 30f); return this; }
        public FreeCam Dolly(float d) { Z = Mathf.Clamp(Z + d, 1.5f, 60f); return this; }
    }

    // RATES, in world units per second, not per-tap steps. The pad is held down, so the movement
    // has to be integrated against dt or it changes speed with the frame rate — the same rule
    // DecayPerTick60 exists for.
    //
    // The base rates are set so a QUICK TAP still behaves like the old discrete step: ~120ms of
    // pan at 4/s is 0.48 units, against the 0.5 it used to move per tap. Fine positioning is
    // unchanged; it is only the long flights that got faster.
    const float PanRate = 4f, LiftRate = 2f, DollyRate = 4f;

    // Held longer, moves faster. Without this a hold is honest but still slow — crossing a level
    // from the player line to the enemy is ~15 units, which at the base rate is nearly four
    // seconds. Ramped it is about one and a half, and the first moments are still slow enough to
    // place the camera precisely, which is what the tool is for.
    const float FreeCamRampSeconds = 1.2f;
    const float FreeCamMaxRamp = 4f;

    bool freeCamOn;
    FreeCam freeCam;

    /// <summary>
    /// Which way the pad is being held this frame: x pan, y lift, z dolly, each -1/0/+1.
    ///
    /// OR-ed in from OnGUI and CONSUMED by Update, rather than moving the camera inside OnGUI
    /// directly. OnGUI runs several times per frame — once per input event plus Layout and
    /// Repaint — so acting on the button there applies the movement an unpredictable number of
    /// times per frame, and the camera's speed then depends on how much input the OS delivered.
    /// </summary>
    Vector3 freeCamHeld;
    float freeCamHoldSeconds;

    // Pad layout, shared by the drawing and by the touch exclusion below — two copies of these
    // numbers would drift and the dead zone would stop matching the buttons.
    const float PadX = 30f, PadButton = 96f, PadButtonH = 84f, PadGap = 8f;
    static float PadTop => Screen.height - 560f;

    /// <summary>
    /// The pad's footprint, in GUI coordinates (origin TOP-left, unlike Input.touch).
    ///
    /// It exists so a press-and-hold on the pad is not also an aim drag. With tap-to-step that
    /// never mattered — Release() ignores a drag under a threshold, and a tap barely moves — but
    /// a finger resting on OUT for two seconds drifts on the glass, and on release that would
    /// fire a volley and end the turn. The camera tool must not be able to play the game.
    /// </summary>
    /// <summary>
    /// The ammo selector's footprint. ONE definition, read by both the drawing and the drag
    /// exclusion — two copies drift, and the failure mode is a tap that both picks ammo and
    /// fires the volley.
    /// </summary>
    Rect AmmoSelectorRect => ammoCatalog == null || ammoCatalog.slots.Count == 0
        ? new Rect(0f, 0f, 0f, 0f)
        : new Rect(30f, Screen.height - 348f, Screen.width - 60f, 104f);

    Rect FreeCamPadRect => new(PadX, PadTop,
                               3f * PadButton + 2f * PadGap, 2f * PadButtonH + PadGap);

    void ApplyCamera()
    {
        if (freeCamOn)
        {
            // No shake. Shake is a per-frame random offset, and a tool for judging whether a thing
            // is in the right PLACE cannot have the view jittering underneath it.
            BattleCamera.Apply(cam, GameSpace.CameraX(freeCam.X), freeCam.Y, freeCam.Z);
            return;
        }

        // The tick keeps CameraFollowX continuous now, so there is no fallback to snap to.
        float camXGame = state.CameraFollowX ?? state.PlayerCamXAnchor;
        float camZ = state.CameraFollowZ ?? 11f;

        // Shake is a RENDER offset only — it must never enter the simulation.
        float sx = 0f, sy = 0f;
        if (state.ShakeIntensity > 0f)
        {
            sx = (Random.value - 0.5f) * state.ShakeIntensity * 0.12f;
            sy = (Random.value - 0.5f) * state.ShakeIntensity * 0.06f;
        }

        BattleCamera.Apply(cam, GameSpace.CameraX(camXGame) + sx,
                           BattleCamera.CameraY + sy, camZ);
    }

    /// <summary>
    /// The battle HUD. Mirrors what the shipping build shows, in the order it shows it: unit
    /// counts and structure HP first (what you are trying to change), turn state next (whose
    /// move it is), and the aim readout only while dragging.
    ///
    /// The aim readout is deliberately ANGLE AND POWER rather than a landing marker. Guessing
    /// the angle and power IS the mechanic — a predicted landing point was tried in the Android
    /// build and reverted, because it turns aiming into reading.
    /// </summary>
    void DrawHud()
    {
        // PER STRUCTURE, not a total. A single "Structure HP" sum cannot say WHICH building is
        // still standing, and on a two-structure level that is the whole decision: keep firing
        // here, or shift. Measured cost of the old readout during the 2026-08-07 balance audit —
        // four volleys were fired into the site of an already-destroyed bunker while the barracks
        // sat untouched, because the falling total looked like progress.
        //
        // Destroyed structures are removed from state.Structures by the tick, so listing what
        // survives is automatically the list of what is left to do.
        var enemyStructures = new List<StructureEntity>();
        foreach (var st in state.Structures)
            if (!st.Definition.isPlayerSide) enemyStructures.Add(st);

        var big = new GUIStyle(style) { fontSize = 40 };
        var small = new GUIStyle(style) { fontSize = 28, normal = { textColor = new Color(0.8f, 0.8f, 0.85f) } };

        float y = 24f;
        GUI.Label(new Rect(28, y, 900, 60), $"Your units: {state.PlayerUnits.Count}", big); y += 46;
        GUI.Label(new Rect(28, y, 900, 60), $"Enemy units: {state.EnemyUnits.Count}", big); y += 46;
        // Nearest first, which is also left-to-right on screen, so the list reads in the order
        // the structures appear on the field.
        enemyStructures.Sort((a, b) => a.X.CompareTo(b.X));
        foreach (var st in enemyStructures)
        {
            // displayName, not the asset name: "Command Bunker" is what the player sees on the
            // field, and the id (command_bunker) is authoring vocabulary.
            string name = string.IsNullOrEmpty(st.Definition.displayName)
                          ? st.Definition.id : st.Definition.displayName;

            // DUPLICATE NAMES ARE REAL and would rebuild the exact ambiguity this list exists to
            // remove: L12 places FortressTierSmall and FortressTierWide, and BOTH are called
            // "Fortress Tier". Qualify by position only when a name actually collides, so the
            // common case stays clean.
            int same = 0, rank = 0;
            foreach (var other in enemyStructures)
            {
                string otherName = string.IsNullOrEmpty(other.Definition.displayName)
                                   ? other.Definition.id : other.Definition.displayName;
                if (otherName != name) continue;
                same++;
                if (other.X < st.X) rank++;
            }
            if (same > 1) name += same == 2 ? (rank == 0 ? " (near)" : " (far)") : $" #{rank + 1}";

            GUI.Label(new Rect(28, y, 900, 60), $"{name}: {st.Hp}", big);
            y += 46;
        }

        // The tank's ammo is FINITE and there is no other way to know it is running out — the
        // shell just stops appearing in the volley, which reads as the gun having broken. Shown
        // only on levels that field a cannon at all.
        if (state.TankShellsRemaining > 0 || HasCannon())
        {
            GUI.Label(new Rect(28, y, 900, 60), $"Tank shells: {state.TankShellsRemaining}", big);
            y += 46;
        }

        string turn = state.Phase switch
        {
            GamePhase.Victory => "VICTORY",
            GamePhase.Defeat => "DEFEAT",
            _ => state.TurnSide == TurnSide.Player
                 ? (state.TurnPhase == TurnPhase.Aiming ? "Your turn" : "Firing...")
                 : "Enemy turn",
        };
        var turnStyle = new GUIStyle(big)
        {
            normal = { textColor = state.Phase == GamePhase.Defeat ? new Color(1f, 0.45f, 0.4f)
                                 : state.Phase == GamePhase.Victory ? new Color(0.6f, 1f, 0.6f)
                                 : new Color(1f, 0.86f, 0.3f) },
        };
        GUI.Label(new Rect(28, y, 900, 60), turn, turnStyle); y += 52;

        if (dragging)
        {
            GUI.Label(new Rect(28, y, 900, 50),
                $"power {AimSystem.StrengthPercent(aimVel):F0}%    angle {AimSystem.AngleDegrees(aimVel):F0}°",
                small);
        }

        // Diagnostics, deliberately bottom-right and dim — useful while porting, not part of
        // the game's own presentation.
        GUI.Label(new Rect(Screen.width - 430, Screen.height - 90, 420, 80),
            $"{1f / Mathf.Max(smoothedDt, 0.0001f):F0} fps   drag {worstDragDt * 1000f:F0}ms   " +
            $"turn {state.TurnNumber}", small);
    }

    void OnGUI()
    {
        style ??= new GUIStyle(GUI.skin.label) { fontSize = 30, normal = { textColor = Color.white } };

        // The loadout picker is MODAL and IMGUI always draws after the canvas, so none of this
        // may run while it is open — the same trap the RESTART / NEXT buttons fell into. It is
        // not merely ugly: the ◀ ▶ stepper would sit on top of the panel and stay tappable, so a
        // player could change level out from under the squad they were choosing.
        if (ui != null && ui.LoadoutOpen) return;
        if (dot == null)
        {
            dot = new Texture2D(1, 1);
            dot.SetPixel(0, 0, new Color(1f, 0.85f, 0.3f));
            dot.Apply();
        }

        if (dragging && arc.Count > 1)
        {
            foreach (var a in arc)
            {
                var sp = cam.WorldToScreenPoint(GameSpace.ToUnity(a));
                if (sp.z <= 0f) continue;
                GUI.DrawTexture(new Rect(sp.x - 5f, Screen.height - sp.y - 5f, 10f, 10f), dot);
            }
        }

        // AUTO — the debug driver. Deliberately labelled and placed like the shipping build's,
        // and deliberately NOT something to judge balance from: every round lands, which no
        // real drag does.
        bool canAuto = state.Phase == GamePhase.Playing && state.TurnPhase == TurnPhase.Aiming
                    && !(ui != null && ui.LoadoutOpen);
        GUI.enabled = canAuto;
        if (GUI.Button(new Rect(30, Screen.height - 220, 300, 150), "AUTO"))
        {
            state = BattleTick.AutoFire(state);
            if (audioFx != null) audioFx.PlayVolleyFire();
            VolleyAnim(playerSide: true);
            Debug.Log($"[Battle] AUTO volley: {state.Projectiles.Count} rounds");
        }
        GUI.enabled = true;

        DrawAmmoSelector();
        DrawLevelNav();
        DrawFreeCamPad();
        DrawHud();
    }

    /// <summary>
    /// The ammo selector — `DYNAMISM_DESIGN.md` Phase A's "one free permanent choice per turn".
    ///
    /// Sits by the aim area, and the DRAG IS COMPLETELY UNCHANGED: this changes what the volley
    /// DOES when it arrives, never how it is thrown. Guessing angle and power stays the mechanic.
    ///
    /// Three rules, all from the spec:
    /// - **No mid-drag switching.** Disabled while `dragging`, so a finger already on the glass
    ///   cannot change the round it is about to throw.
    /// - **Aiming phase only.** Switching while the volley is in the air would change ammo the
    ///   rounds were already fired with.
    /// - **The choice PERSISTS**, across turns and battles, via ProgressStore. It is a standing
    ///   preference, not a per-turn prompt — a prompt every turn is a tax on the common case.
    ///
    /// It also SELLS. Purchase lives here rather than in the loadout panel because the coin
    /// balance is already on this HUD and the panel is a fixed eight-row layout; tapping a locked
    /// type buys it if the player can afford it. Buying mid-battle is deliberately allowed —
    /// coins are earned from victories, no ammo is ever REQUIRED to clear a level, and "I want
    /// that one now" is the impulse the coin sink exists to catch.
    /// </summary>
    void DrawAmmoSelector()
    {
        if (ammoCatalog == null || ammoCatalog.slots.Count == 0) return;
        if (state.Phase != GamePhase.Playing) return;

        bool canSwitch = state.TurnPhase == TurnPhase.Aiming && !dragging;

        var row = AmmoSelectorRect;
        const float Gap = 8f;
        float w = (row.width - Gap * (ammoCatalog.slots.Count - 1)) / ammoCatalog.slots.Count;

        var label = new GUIStyle(GUI.skin.button) { fontSize = 24, alignment = TextAnchor.MiddleCenter };
        for (int i = 0; i < ammoCatalog.slots.Count; i++)
        {
            var slot = ammoCatalog.slots[i];
            bool unlocked = ProgressStore.IsAmmoUnlocked(slot.type);
            bool selected = state.SelectedAmmo == slot.type;
            var r = new Rect(row.x + i * (w + Gap), row.y, w, row.height);

            // The SELECTED type is tinted rather than merely labelled: at gameplay distance a
            // ring of text all one colour reads as four disabled buttons.
            var prev = GUI.color;
            GUI.color = selected ? new Color(1f, 0.85f, 0.3f)
                      : unlocked ? Color.white
                      : new Color(0.75f, 0.75f, 0.8f);
            GUI.enabled = canSwitch && (unlocked || EconomyStore.Balance() >= slot.coinPrice);

            string caption = unlocked ? slot.displayName : $"{slot.displayName}\n{slot.coinPrice}c";
            if (GUI.Button(r, caption, label))
            {
                if (unlocked)
                {
                    ProgressStore.SetSelectedAmmo(slot.type);
                    state = state with { SelectedAmmo = slot.type };
                    Debug.Log($"[Ammo] selected {slot.type}");
                }
                else if (EconomyStore.PurchaseAmmo(new AmmoDefinition
                         { Type = slot.type, CoinPrice = slot.coinPrice }))
                {
                    // Bought AND selected in one tap. Buying a thing and then having to pick it
                    // is a second step with no decision in it.
                    ProgressStore.SetSelectedAmmo(slot.type);
                    state = state with { SelectedAmmo = slot.type };
                    if (ui != null) ui.SetCoins(EconomyStore.Balance());
                    Debug.Log($"[Ammo] purchased {slot.type} for {slot.coinPrice}");
                }
            }
            GUI.color = prev;
            GUI.enabled = true;
        }
    }

    /// <summary>
    /// Level navigation — the DEBUG switcher, and only that now.
    ///
    /// RESTART / NEXT used to be drawn here as IMGUI buttons at screen centre when the battle
    /// ended. They belong to the victory panel now (BattleUI), and they had to be REMOVED rather
    /// than left to be covered: IMGUI always draws after the canvas, so they would have painted
    /// straight over the card and gone on swallowing its taps.
    ///
    /// The ◀ ▶ stepper is the DEBUG switcher: the only way to sweep every level for crashes and
    /// missing geometry from adb without a three-minute rebuild each time. It is also why
    /// LoadLevel has to be correct from ANY phase, not just from a finished battle.
    ///
    /// It walks the CAMPAIGN ONLY until RIGS is pressed. Before that split the stepper ran off the
    /// end of the campaign straight into the unit parade, which is fine for a developer and not
    /// something a player should ever be one tap from.
    /// </summary>
    void DrawLevelNav()
    {
        // BELOW THE STATUS BAR. At y=24 these sat inside the display cutout inset (161px on this
        // panel), so an adb tap aimed at them lands on the system status bar and can pull the
        // notification shade down over the game — which is how earlier scripted sessions ended up
        // driving somebody's personal apps. Anything meant to be tapped from a script belongs
        // clear of the insets.
        const float NavTop = 190f;
        var nav = new GUIStyle(GUI.skin.button) { fontSize = 34 };
        if (GUI.Button(new Rect(Screen.width - 250f, NavTop, 100f, 90f), "◀", nav))
            LoadLevel(levelIndex - 1);
        if (GUI.Button(new Rect(Screen.width - 130f, NavTop, 100f, 90f), "▶", nav))
            LoadLevel(levelIndex + 1);
        // The readout counts within whatever block is reachable, so "3/7" means three of seven
        // CAMPAIGN levels rather than three of twenty-four assorted scenes.
        GUI.Label(new Rect(Screen.width - 250f, NavTop + 94f, 260f, 40f),
                  $"L{level.levelNumber} ({levelIndex + 1}/{LastReachableIndex + 1})" +
                  (level.isTestLevel ? " RIG" : ""), style);

        // RIGS unlocks the test levels for the stepper. The campaign is the only thing reachable
        // without it, which is what "test rigs are not the campaign" means in practice — but the
        // rigs stay one tap away in a RELEASE build, because sweeping them from adb is how
        // missing geometry gets found and a development build cannot be trusted for anything else.
        var rigStyle = new GUIStyle(nav) { fontSize = 26 };
        if (GUI.Button(new Rect(Screen.width - 490f, NavTop, 100f, 90f),
                       showRigs ? "RIGS\nON" : "RIGS", rigStyle))
        {
            showRigs = !showRigs;
            // Locking them again while standing on one would leave the session out of bounds.
            if (!showRigs && levelIndex > LastReachableIndex) LoadLevel(LastReachableIndex);
        }

        // CAM sits beside the stepper, matching the shipping build's placement.
        if (GUI.Button(new Rect(Screen.width - 370f, NavTop, 100f, 90f), "CAM", nav))
        {
            freeCamOn = !freeCamOn;
            // The end card yields to the free camera. Inspecting a FINISHED battle is most of
            // what this tool is for, and a full-screen dim over it would take that away.
            ui.SetVisible(!freeCamOn);
            // SEEDED FROM THE LIVE CAMERA, so switching it on does not move the picture. Starting
            // from a fixed home position means every investigation begins by flying back to
            // whatever you were already looking at.
            if (freeCamOn)
                freeCam = new FreeCam
                {
                    // Back through the same negation, so the seed is a game x like the readout.
                    X = GameSpace.CameraX(cam.transform.position.x),
                    Y = cam.transform.position.y,
                    Z = cam.transform.position.z,
                };
        }
    }

    /// <summary>Whether this level fields a player cannon at all — so the ammo readout appears
    /// on a level that HAS a tank and has spent its shells, and never on one that has none.</summary>
    bool HasCannon()
    {
        foreach (var st in state.Structures)
            if (st.Definition != null && st.Definition.isPlayerSide && st.Definition.hasCannon)
                return true;
        return false;
    }

    /// <summary>
    /// Flies the camera while the pad is held. Reads the direction OnGUI recorded, integrates it
    /// against dt, and clears it — so a frame in which the pad was not touched stops the camera
    /// and resets the ramp.
    /// </summary>
    void StepFreeCam(float dt)
    {
        if (freeCamHeld == Vector3.zero) { freeCamHoldSeconds = 0f; return; }

        freeCamHoldSeconds += dt;
        float ramp = Mathf.Lerp(1f, FreeCamMaxRamp,
                                Mathf.Clamp01(freeCamHoldSeconds / FreeCamRampSeconds));
        freeCam = freeCam.Pan(freeCamHeld.x * PanRate * ramp * dt);
        freeCam = freeCam.Lift(freeCamHeld.y * LiftRate * ramp * dt);
        freeCam = freeCam.Dolly(freeCamHeld.z * DollyRate * ramp * dt);
        freeCamHeld = Vector3.zero;
    }

    /// <summary>
    /// The free camera's control pad. BUTTONS rather than a drag gesture, because the aim mechanic
    /// already owns dragging. The READOUT is part of the tool, not decoration — it is how a
    /// position found by eye gets written down and reproduced next session.
    ///
    /// `RepeatButton`, not `Button`: a Button fires once, on RELEASE, so reaching anything far
    /// away meant thirty taps. These report held every frame the finger is down, and the camera
    /// accelerates the longer it is held.
    ///
    /// NOTE FOR SCRIPTED USE: `adb shell input tap` is too brief to register as much movement.
    /// Drive these with `input swipe X Y X Y 600` — same point twice, with a duration — which is
    /// how adb expresses a press-and-hold.
    /// </summary>
    void DrawFreeCamPad()
    {
        if (!freeCamOn) return;

        var b = new GUIStyle(GUI.skin.button) { fontSize = 34 };
        const float W = PadButton, H = PadButtonH, Gap = PadGap;
        float x0 = PadX, y0 = PadTop;

        Rect At(int col, int row) => new(x0 + col * (W + Gap), y0 + row * (H + Gap), W, H);

        // OR-ed, never assigned: OnGUI runs several times per frame and this button is only
        // "down" during some of those passes, so assigning would let a later pass wipe it.
        if (GUI.RepeatButton(At(0, 0), "↑", b)) freeCamHeld.y = 1f;
        if (GUI.RepeatButton(At(0, 1), "↓", b)) freeCamHeld.y = -1f;
        if (GUI.RepeatButton(At(1, 0), "←", b)) freeCamHeld.x = -1f;
        if (GUI.RepeatButton(At(1, 1), "→", b)) freeCamHeld.x = 1f;
        if (GUI.RepeatButton(At(2, 0), "IN", b)) freeCamHeld.z = -1f;
        if (GUI.RepeatButton(At(2, 1), "OUT", b)) freeCamHeld.z = 1f;

        GUI.Label(new Rect(x0, y0 - 46f, 700f, 44f),
                  $"CAM  x {freeCam.X:F2}   y {freeCam.Y:F2}   z {freeCam.Z:F2}", style);
    }
}
