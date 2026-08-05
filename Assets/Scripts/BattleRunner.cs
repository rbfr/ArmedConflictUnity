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
    [SerializeField] Transform poolRoot;

    const int UnitPoolSize = 48;
    const int ProjectilePoolSize = 64;

    GameState state;
    System.Random random;
    readonly List<GameObject> playerSlots = new();
    readonly List<GameObject> enemySlots = new();
    readonly List<GameObject> shotSlots = new();
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
        }
        for (int i = 0; i < ProjectilePoolSize; i++) shotSlots.Add(Spawn(projectilePrefab, $"s{i}"));

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

        state = BattleTick.Step(state, dt, level, random);

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
        Debug.Log($"[Battle] volley: {state.Projectiles.Count} rounds at " +
                  $"{AimSystem.StrengthPercent(aimVel):F0}% / {AimSystem.AngleDegrees(aimVel):F1}deg");
    }

    Vector3 MuzzleOrigin()
    {
        if (state.PlayerUnits.Count == 0) return new Vector3(-9.5f, 0.9f, 0f);
        return new Vector3(state.PlayerUnits.Average(u => u.X), 0.9f, 0f);
    }

    void Render()
    {
        SyncUnits(state.PlayerUnits, playerSlots);
        SyncUnits(state.EnemyUnits, enemySlots);

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

        var liveIds = new HashSet<int>(state.Structures.Select(s2 => s2.Id));
        foreach (var go in structureObjects)
        {
            int id = int.Parse(go.name.Substring("struct_".Length));
            if (go.activeSelf != liveIds.Contains(id)) go.SetActive(liveIds.Contains(id));
        }
    }

    void SyncUnits(IReadOnlyList<UnitEntity> units, List<GameObject> pool)
    {
        for (int i = 0; i < units.Count && i < pool.Count; i++)
        {
            var u = units[i];
            pool[i].SetActive(true);
            pool[i].transform.position = GameSpace.ToUnity(u.X, u.Y, u.Z);
            pool[i].transform.rotation = Quaternion.identity;
        }
    }

    void ApplyCamera()
    {
        float camXGame = state.CameraFollowX
            ?? (state.TurnPhase == TurnPhase.Aiming ? state.PlayerCamXAnchor : state.EnemyCamXAnchor);
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
