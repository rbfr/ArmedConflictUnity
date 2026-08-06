using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using ArmedConflict.Data;
using ArmedConflict.Game;

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
    /// <summary>All 29 levels, campaign then test rigs, in `LevelDefinitions.all` order — the
    /// order the level number indexes. A player has no AssetDatabase, so every level the session
    /// can reach has to be a serialized reference.</summary>
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
    [SerializeField] BattleAudio audioFx;
    [SerializeField] Transform poolRoot;

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

    void Start()
    {
        Application.targetFrameRate = 60;
        QualitySettings.vSyncCount = 0;

        random = new System.Random(12345);
        ProgressStore.AllLevels = levels;

        BuildPools();
        LoadLevel(0);
    }

    /// <summary>
    /// Swaps the whole battle over to another level: new state, new scenery, pools emptied.
    ///
    /// The pools themselves are built ONCE and survive the switch — minting render slots mid
    /// session is the failure the Filament build paid for repeatedly, and there is no reason to
    /// repeat it here. What has to be reset is everything that reads a slot's PREVIOUS occupant:
    /// a hidden slot still holds the last level's pose, position and animation state.
    /// </summary>
    public void LoadLevel(int index)
    {
        if (levels == null || levels.Length == 0) { Debug.LogError("[Battle] no levels"); return; }
        levelIndex = Mathf.Clamp(index, 0, levels.Length - 1);
        level = levels[levelIndex];

        // battleId advances per load so nothing keyed on it can collide with the level before it.
        state = LevelBuilder.BuildInitialState(level, ++battleId, levels.Length, random);
        state = state with { Phase = GamePhase.Playing, TurnPhase = TurnPhase.Aiming };

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

        HideAll();
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

    GameObject Spawn(GameObject prefab, string name)
    {
        var go = Instantiate(prefab, poolRoot);
        go.name = name;
        go.SetActive(false);
        return go;
    }

    void Update()
    {
        float dt = Time.deltaTime;
        smoothedDt += (Time.unscaledDeltaTime - smoothedDt) * 0.05f;

        HandleInput();

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
        state = BattleTick.Step(state, dt, level, random);

        // Stand the line down as soon as it is the player's move again — the held pose belongs to
        // a volley that has now resolved.
        if (!dragging && state.TurnPhase == TurnPhase.Aiming) aimPoseDegrees = 0f;
        DriveAudio(before, state);

        Render();
        ApplyCamera();

        if (dragging) { dragFrames++; if (dragFrames > 2) worstDragDt = Mathf.Max(worstDragDt, dt); }
    }

    void HandleInput()
    {
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
        state = BattleTick.FireVolley(state, aimVel, random);
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
                go.transform.rotation = Quaternion.identity;
                dyingAnim.Set(UnitAnim.Die);
            }
            else go.transform.rotation = Quaternion.Euler(0f, 0f, -d.Rotation);
        }
        playerUnits.HideRest();
        enemyUnits.HideRest();
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
                debrisSlots[i].transform.localScale = Vector3.one * d.Size;
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

    void ApplyCamera()
    {
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
        int structureHp = 0, structureMax = 0;
        foreach (var st in state.Structures)
        {
            if (st.Definition.isPlayerSide) continue;
            structureHp += st.Hp;
            structureMax += st.MaxHp;
        }

        var big = new GUIStyle(style) { fontSize = 40 };
        var small = new GUIStyle(style) { fontSize = 28, normal = { textColor = new Color(0.8f, 0.8f, 0.85f) } };

        float y = 24f;
        GUI.Label(new Rect(28, y, 900, 60), $"Your units: {state.PlayerUnits.Count}", big); y += 46;
        GUI.Label(new Rect(28, y, 900, 60), $"Enemy units: {state.EnemyUnits.Count}", big); y += 46;
        if (structureMax > 0)
        {
            GUI.Label(new Rect(28, y, 900, 60), $"Structure HP: {structureHp}", big);
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
        bool canAuto = state.Phase == GamePhase.Playing && state.TurnPhase == TurnPhase.Aiming;
        GUI.enabled = canAuto;
        if (GUI.Button(new Rect(30, Screen.height - 220, 300, 150), "AUTO"))
        {
            state = BattleTick.AutoFire(state);
            if (audioFx != null) audioFx.PlayVolleyFire();
            VolleyAnim(playerSide: true);
            Debug.Log($"[Battle] AUTO volley: {state.Projectiles.Count} rounds");
        }
        GUI.enabled = true;

        DrawLevelNav();
        DrawHud();
    }

    /// <summary>
    /// Level navigation. Two parts, and they answer different needs.
    ///
    /// RESTART / NEXT appear when the battle is over, because a game that ends on a victory
    /// screen with nowhere to go is not a game — this was the port's largest hole, and every
    /// session before it was L1 or nothing.
    ///
    /// The ◀ ▶ stepper is always on, and is the DEBUG switcher the shipping build also carries:
    /// it is the only way to sweep 29 levels for crashes and missing geometry from adb without a
    /// three-minute rebuild each time. It is also why LoadLevel has to be correct from ANY phase,
    /// not just from a finished battle.
    /// </summary>
    void DrawLevelNav()
    {
        bool over = state.Phase == GamePhase.Victory || state.Phase == GamePhase.Defeat;
        if (over)
        {
            float w = 300f, h = 130f, y = Screen.height * 0.5f;
            if (GUI.Button(new Rect(Screen.width * 0.5f - w - 20f, y, w, h), "RESTART"))
                LoadLevel(levelIndex);
            GUI.enabled = levelIndex < levels.Length - 1;
            if (GUI.Button(new Rect(Screen.width * 0.5f + 20f, y, w, h), "NEXT LEVEL"))
                LoadLevel(levelIndex + 1);
            GUI.enabled = true;
        }

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
        GUI.Label(new Rect(Screen.width - 250f, NavTop + 94f, 220f, 40f),
                  $"L{level.levelNumber} ({levelIndex + 1}/{levels.Length})", style);
    }
}
