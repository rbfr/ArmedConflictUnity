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
    [SerializeField] LevelDefinitionSO level;
    [SerializeField] GameObject playerUnitPrefab;
    [SerializeField] GameObject enemyUnitPrefab;
    [SerializeField] GameObject projectilePrefab;
    [SerializeField] GameObject gunPrefab;
    [SerializeField] GameObject explosionPrefab;
    [SerializeField] BattleAudio audioFx;
    [SerializeField] Transform poolRoot;

    const int UnitPoolSize = 48;
    const int ProjectilePoolSize = 64;

    GameState state;
    System.Random random;
    readonly List<GameObject> playerSlots = new();
    readonly List<GameObject> enemySlots = new();
    readonly List<GameObject> shotSlots = new();
    readonly List<GameObject> playerGuns = new();
    readonly List<GameObject> enemyGuns = new();
    readonly List<GameObject> blastSlots = new();
    readonly List<GameObject> structureObjects = new();

    // input
    bool dragging;
    Vector2 dragStart;
    Vector3 aimVel;
    readonly List<Vector3> arc = new();

    // enemy turn pacing
    float enemyWindup;

    float smoothedDt;
    float worstDragDt;
    int dragFrames;
    GUIStyle style;
    Texture2D dot;

    void Start()
    {
        Application.targetFrameRate = 60;
        QualitySettings.vSyncCount = 0;

        random = new System.Random(12345);
        ProgressStore.AllLevels = new List<LevelDefinitionSO> { level };

        state = LevelBuilder.BuildInitialState(level, battleId: 1, totalLevels: 29, random: random);
        state = state with { Phase = GamePhase.Playing, TurnPhase = TurnPhase.Aiming };

        BuildPools();
        Debug.Log($"[Battle] {level.displayName}: {state.PlayerUnits.Count} player, " +
                  $"{state.EnemyUnits.Count} enemy, {state.Structures.Count} structures");
    }

    void BuildPools()
    {
        for (int i = 0; i < UnitPoolSize; i++)
        {
            playerSlots.Add(Spawn(playerUnitPrefab, $"p{i}"));
            enemySlots.Add(Spawn(enemyUnitPrefab, $"e{i}"));
            playerGuns.Add(Spawn(gunPrefab, $"pg{i}"));
            enemyGuns.Add(Spawn(gunPrefab, $"eg{i}"));
        }
        for (int i = 0; i < ProjectilePoolSize; i++) shotSlots.Add(Spawn(projectilePrefab, $"s{i}"));
        for (int i = 0; i < 32; i++) blastSlots.Add(Spawn(explosionPrefab, $"x{i}"));

        // Structures are static for the battle's life — one object each, hidden on destruction.
        foreach (var st in state.Structures)
        {
            var go = GameObject.Find($"struct_{st.Id}");
            if (go != null) structureObjects.Add(go);
        }
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
            }
        }

        var before = state;
        state = BattleTick.Step(state, dt, level, random);
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

        // A NEW blast this tick. Explosions are also held one extra tick at progress 1, so
        // compare counts of freshly-spawned ones rather than list length.
        int newBlasts = 0;
        foreach (var e in after.Explosions) if (e.Progress <= 0f) newBlasts++;
        if (newBlasts > 0) audioFx.PlayExplosion();

        // Rounds that vanished without a blast hit the dirt.
        int gone = before.Projectiles.Count - after.Projectiles.Count;
        if (gone > newBlasts && gone > 0) audioFx.PlayGroundImpact(gone - newBlasts);

        // A wounded survivor: damage taken with nobody dying.
        if (deaths == 0 && after.TotalWoundedHits > before.TotalWoundedHits) audioFx.PlayUnitHit();

        if (before.Phase == GamePhase.Playing && after.Phase == GamePhase.Victory) audioFx.PlayVictory();
        if (before.Phase == GamePhase.Playing && after.Phase == GamePhase.Defeat) audioFx.PlayDefeat();
    }

    void Render()
    {
        SyncUnits(state.PlayerUnits, playerSlots, playerGuns, aimingRight: true);
        SyncUnits(state.EnemyUnits, enemySlots, enemyGuns, aimingRight: false);

        // Ragdolls reuse the same pools past the live roster.
        int p = state.PlayerUnits.Count, e = state.EnemyUnits.Count;
        foreach (var d in state.DyingUnits)
        {
            var pool = d.IsPlayerSide ? playerSlots : enemySlots;
            int idx = d.IsPlayerSide ? p++ : e++;
            if (idx >= pool.Count) continue;
            var go = pool[idx];
            go.SetActive(true);
            go.transform.position = GameSpace.ToUnity(d.X, d.Y, d.Z);
            go.transform.rotation = Quaternion.Euler(0f, 0f, -d.Rotation);
        }
        for (int i = p; i < playerSlots.Count; i++) playerSlots[i].SetActive(false);
        for (int i = e; i < enemySlots.Count; i++) enemySlots[i].SetActive(false);
        // Guns follow the LIVE roster only — a ragdoll drops its weapon rather than carrying
        // one through a tumble, which is also what the shipping build does.
        for (int i = state.PlayerUnits.Count; i < playerGuns.Count; i++) playerGuns[i].SetActive(false);
        for (int i = state.EnemyUnits.Count; i < enemyGuns.Count; i++) enemyGuns[i].SetActive(false);

        for (int i = 0; i < shotSlots.Count; i++)
        {
            if (i < state.Projectiles.Count)
            {
                var pr = state.Projectiles[i];
                shotSlots[i].SetActive(true);
                shotSlots[i].transform.position = GameSpace.ToUnity(pr.X, pr.Y, pr.Z);
                float deg = Mathf.Atan2(pr.Vy, -pr.Vx) * Mathf.Rad2Deg;
                shotSlots[i].transform.rotation = Quaternion.Euler(0f, 0f, deg);
            }
            else shotSlots[i].SetActive(false);
        }

        // Explosions: a sphere that swells and fades over its progress.
        for (int i = 0; i < blastSlots.Count; i++)
        {
            if (i < state.Explosions.Count)
            {
                var x = state.Explosions[i];
                blastSlots[i].SetActive(true);
                blastSlots[i].transform.position = GameSpace.ToUnity(x.X, x.Y, x.Z);
                // Swell fast, then hold — a blast that grows linearly reads as a balloon.
                float t2 = Mathf.Sqrt(Mathf.Clamp01(x.Progress));
                blastSlots[i].transform.localScale = Vector3.one * x.Scale * (0.4f + 1.6f * t2);
            }
            else blastSlots[i].SetActive(false);
        }

        var liveIds = new HashSet<int>(state.Structures.Select(s2 => s2.Id));
        foreach (var go in structureObjects)
        {
            int id = int.Parse(go.name.Substring("struct_".Length));
            if (go.activeSelf != liveIds.Contains(id)) go.SetActive(liveIds.Contains(id));
        }
    }

    void SyncUnits(IReadOnlyList<UnitEntity> units, List<GameObject> pool,
                   List<GameObject> guns, bool aimingRight)
    {
        // The weapon sits at chest height, offset toward the side the unit faces. X is mirrored
        // by GameSpace, so the offset is applied in GAME space and converted with the body —
        // applying it after conversion would put every gun on the wrong shoulder.
        float sign = aimingRight ? 1f : -1f;
        for (int i = 0; i < units.Count && i < pool.Count; i++)
        {
            var u = units[i];
            pool[i].SetActive(true);
            pool[i].transform.position = GameSpace.ToUnity(u.X, u.Y, u.Z);
            pool[i].transform.rotation = Quaternion.identity;

            if (i >= guns.Count) continue;
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

        string banner = state.Phase switch
        {
            GamePhase.Victory => "VICTORY",
            GamePhase.Defeat => "DEFEAT",
            _ => $"{state.TurnSide} / {state.TurnPhase}",
        };

        GUI.Label(new Rect(30, 30, 1500, 400),
            $"{1f / Mathf.Max(smoothedDt, 0.0001f):F1} fps   worst drag {worstDragDt * 1000f:F1} ms\n" +
            $"{banner}   turn {state.TurnNumber}\n" +
            $"player {state.PlayerUnits.Count}   enemy {state.EnemyUnits.Count}   " +
            $"structures {state.Structures.Count}\n" +
            $"rounds {state.Projectiles.Count}   bodies {state.DyingUnits.Count}\n" +
            (dragging ? $"aim {AimSystem.StrengthPercent(aimVel):F0}%  " +
                        $"{AimSystem.AngleDegrees(aimVel):F1}deg" : "drag to aim"),
            style);
    }
}
