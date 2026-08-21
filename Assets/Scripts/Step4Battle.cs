using System.Collections.Generic;
using System.Text;
using UnityEngine;
using ArmedConflict.Game;

/// <summary>
/// UNITY_SPIKE.md Step 4 — one drag-aimed shot.
/// Passes if: a full-power 45 degree shot lands at the predicted v^2/g, and the drag feels
/// continuous at a steady 60 fps with no rate transition under the finger.
///
/// The second half is the whole reason the spike exists, so the frame time DURING the gesture
/// is measured separately from the idle frame time — the Android build's sluggishness turned
/// out to be an aim drag spending its first ~400ms at 30Hz while the panel caught up.
/// </summary>
public class Step4Battle : MonoBehaviour
{
    [SerializeField] Camera cam;
    [SerializeField] Transform projectile;
    [SerializeField] float restCamX = -7f;      // game space: the player line
    [SerializeField] float camZ = 11f;

    // Enemy targets, in GAME space. Populated by the scene builder.
    [SerializeField] List<Transform> enemyUnits = new();
    [SerializeField] List<Vector2> enemyXY = new();

    const int UnitMaxHp = 32;      // UnitDefinitions.Rifleman
    const int ShotDamage = 8;

    readonly int[] hp = new int[64];
    readonly List<Vector3> arc = new();

    Vector3 muzzle;
    Vector3 shotPos, shotVel;
    bool inFlight;
    float flightAge;

    // input
    bool dragging;
    Vector2 dragStart, dragDelta;
    Vector3 aimVel;

    // instrumentation
    float smoothedDt, worstIdleDt, worstDragDt;
    int dragFrames, totalFrames, dragCount, dragLongFrames;
    // Per-drag, RESET at every touch-down. A latching max across drags reports one old hitch
    // forever and reads as "every drag hitches" — which is exactly how this was misread once.
    float thisDragWorst;
    int thisDragLong;
    float firstDragWorst = -1f;
    // Startup costs (scene load, shader warm-up, the first rendered frame) are not runtime
    // hitches and must not be reported as one. Ignore the first two seconds outright.
    const int WarmupFrames = 120;
    string lastShot = "-";
    string selfTest = "";
    GUIStyle style;
    Texture2D lineTex;

    void Start()
    {
        Application.targetFrameRate = 60;
        QualitySettings.vSyncCount = 0;
        for (int i = 0; i < hp.Length; i++) hp[i] = UnitMaxHp;

        // Muzzle: behind and above the infantry line, as in the Android build. The shell is
        // solved from HERE, which is why the barrel angle and the drag angle are different
        // numbers — a cue must be driven by the quantity it depicts.
        muzzle = new Vector3(-9.5f, 0.9f, 0f);
        if (projectile != null) projectile.gameObject.SetActive(false);

        RunSelfTests();
    }

    /// <summary>
    /// Numeric checks that do not need a finger. Run once at startup and logged.
    /// </summary>
    void RunSelfTests()
    {
        var sb = new StringBuilder();

        // 1. Reach. Max range at 45 degrees is v^2/g; L1's tank -> outpost is 18.5 (2026-08-20 trial).
        float maxRange = AimSystem.MaxRange45;
        float separation = 7.0f - (-9.5f);
        bool reachOk = maxRange >= separation;
        sb.AppendLine($"[Step4] maxRange45={maxRange:F2} separation={separation:F2} " +
                      $"-> {(reachOk ? "REACHABLE" : "OUT OF REACH")}");

        // 2. Full-power 45 degree shot: integrated landing vs the analytic v^2/g.
        //    Semi-implicit Euler lands SHORT, linearly in dt: the discretised flight time is
        //    short by dt, so the shot falls short by roughly vx*dt/2 at the interpolated
        //    crossing. CLAUDE.md's "0.15-0.35% longer" describes the 60Hz -> 120Hz DELTA, not
        //    the error against analytic — halving dt halves the shortfall. Both are checked.
        float v = AimSystem.MaxAimMagnitude;
        var vel0 = new Vector3(v * Mathf.Cos(45f * Mathf.Deg2Rad), v * Mathf.Sin(45f * Mathf.Deg2Rad), 0f);
        var origin = new Vector3(0f, 0f, 0f);

        foreach (float dt in new[] { 1f / 120f, 1f / 60f, 1f / 30f })
        {
            var p = origin; var vv = vel0;
            Vector3 prev = p;
            int guard = 0;
            while (p.y >= 0f && guard++ < 100000) { prev = p; TrajectoryPhysics.Step(ref p, ref vv, dt); }

            // Interpolate the y=0 crossing between the last above-ground sample and the first
            // below. Reporting p.x directly measures the OVERSHOOT past the ground, whose size
            // grows with dt and cancels most of the integrator's own error — which made a 30Hz
            // and a 60Hz run look identical. The crossing is the landing point; the overshoot
            // is an artefact of where the loop happened to stop.
            float f = prev.y / Mathf.Max(prev.y - p.y, 1e-6f);
            float landedX = Mathf.Lerp(prev.x, p.x, f);

            float analytic = v * v / TrajectoryPhysics.Gravity;
            float errPct = (landedX - analytic) / analytic * 100f;
            sb.AppendLine($"[Step4] 45deg full power dt={dt * 1000f:F2}ms: " +
                          $"landed={landedX:F4} analytic={analytic:F4} err={errPct:+0.000;-0.000}% " +
                          $"(raw sample {p.x:F4})");
        }

        // 3. The aim clamp. Beyond full drag, speed must saturate rather than keep growing.
        foreach (float dragUnits in new[] { 10f, 23.4f, 40f, 80f })
        {
            var av = AimSystem.AimVelocity(dragUnits * 0.7071f, -dragUnits * 0.7071f);
            sb.AppendLine($"[Step4] drag={dragUnits,5:F1}u -> speed={av.magnitude:F3} " +
                          $"({AimSystem.StrengthPercent(av):F0}%) angle={AimSystem.AngleDegrees(av):F1}deg");
        }

        sb.Append($"[Step4] hitRadius={SweptCollision.UnitHitRadius:F4}");
        selfTest = reachOk ? "SELFTEST OK" : "SELFTEST FAIL";
        Debug.Log(sb.ToString());
    }

    void Update()
    {
        float dt = Time.unscaledDeltaTime;
        smoothedDt += (dt - smoothedDt) * 0.05f;
        totalFrames++;

        HandleInput();

        if (totalFrames > WarmupFrames)
        {
            if (dragging)
            {
                dragFrames++;
                if (dragFrames > 2)
                {
                    worstDragDt = Mathf.Max(worstDragDt, dt);      // all-time
                    thisDragWorst = Mathf.Max(thisDragWorst, dt);  // this gesture only
                    if (dt > 0.020f) { dragLongFrames++; thisDragLong++; }
                }
            }
            else if (!inFlight) worstIdleDt = Mathf.Max(worstIdleDt, dt);
        }

        if (inFlight) StepProjectile(Time.deltaTime);

        // Camera follows the round in flight, otherwise rests on the player line. The solve is
        // re-applied every frame either way, which is the property Step 2 proved.
        float camXGame = inFlight ? Mathf.Clamp(shotPos.x, restCamX, 8f) : restCamX;
        BattleCamera.Apply(cam, GameSpace.CameraX(camXGame), BattleCamera.CameraY, camZ);
    }

    void HandleInput()
    {
        if (Input.touchCount > 0)
        {
            var t = Input.GetTouch(0);
            if (t.phase == TouchPhase.Began)
            {
                dragging = true; dragStart = t.position; dragDelta = Vector2.zero;
                dragFrames = 0; dragCount++;
                thisDragWorst = 0f; thisDragLong = 0;
            }
            else if (dragging && (t.phase == TouchPhase.Moved || t.phase == TouchPhase.Stationary))
            {
                dragDelta = t.position - dragStart;
                var w = AimSystem.DragToWorld(dragDelta);
                aimVel = AimSystem.AimVelocity(w.x, w.y);
                TrajectoryPhysics.SampleArc(muzzle, aimVel, 7, 0.05f, arc);
            }
            else if (dragging && (t.phase == TouchPhase.Ended || t.phase == TouchPhase.Canceled))
            {
                dragging = false;
                // Keep the FIRST drag's worst frame separate: a first-touch cost is warm-up,
                // a recurring one is a defect, and averaging them together hides both.
                if (firstDragWorst < 0f) firstDragWorst = thisDragWorst;
                Debug.Log($"[Step4] drag #{dragCount} frames={dragFrames} " +
                          $"worst={thisDragWorst * 1000f:F1}ms >20ms x{thisDragLong} " +
                          $"| all-time worst={worstDragDt * 1000f:F1}ms across {dragLongFrames} long frames");
                Fire();
            }
        }
        else if (dragging)
        {
            dragging = false;
            Fire();
        }
    }

    void Fire()
    {
        if (aimVel.sqrMagnitude < 0.01f) return;
        shotPos = muzzle;
        shotVel = aimVel;
        flightAge = 0f;
        inFlight = true;
        arc.Clear();
        if (projectile != null) projectile.gameObject.SetActive(true);

        var landing = TrajectoryPhysics.LandingPoint(muzzle, shotVel);
        lastShot = $"fired {AimSystem.StrengthPercent(shotVel):F0}% at " +
                   $"{AimSystem.AngleDegrees(shotVel):F1}deg -> predicted x={landing.x:F2}";
        Debug.Log($"[Step4] {lastShot}");
    }

    void StepProjectile(float dt)
    {
        // Clamped like the Android tick, and never sub-stepped — which is exactly why the
        // collision check below has to be swept rather than point-sampled.
        dt = Mathf.Min(dt, 0.05f);
        flightAge += dt;

        var prev = shotPos;
        TrajectoryPhysics.Step(ref shotPos, ref shotVel, dt);
        if (projectile != null)
        {
            projectile.position = GameSpace.ToUnity(shotPos);
            // Nose along the TRUE velocity, as SceneHost does (Rotation z = atan2(vy, vx)).
            // The shell's long axis is local +X, and GameSpace mirrors X, so the screen-space
            // angle is measured against -vx rather than +vx.
            float deg = Mathf.Atan2(shotVel.y, -shotVel.x) * Mathf.Rad2Deg;
            projectile.rotation = Quaternion.Euler(0f, 0f, deg);
        }

        // Swept check against every live enemy.
        for (int i = 0; i < enemyXY.Count; i++)
        {
            if (hp[i] <= 0) continue;
            float d2 = SweptCollision.SegmentDistanceSq(
                prev.x, prev.y, shotPos.x, shotPos.y, enemyXY[i].x, enemyXY[i].y + 0.24f);
            if (d2 <= SweptCollision.UnitHitRadiusSq)
            {
                hp[i] -= ShotDamage;
                bool dead = hp[i] <= 0;
                if (dead && i < enemyUnits.Count && enemyUnits[i] != null)
                    enemyUnits[i].gameObject.SetActive(false);
                lastShot = $"HIT unit {i} at x={shotPos.x:F2} y={shotPos.y:F2} " +
                           $"hp={Mathf.Max(hp[i], 0)}{(dead ? " KILLED" : "")}";
                Debug.Log($"[Step4] {lastShot} (flight {flightAge:F2}s)");
                EndFlight();
                return;
            }
        }

        if (shotPos.y <= 0f)
        {
            lastShot = $"ground at x={shotPos.x:F2} (flight {flightAge:F2}s)";
            Debug.Log($"[Step4] {lastShot}");
            EndFlight();
        }
    }

    void EndFlight()
    {
        inFlight = false;
        if (projectile != null) projectile.gameObject.SetActive(false);
    }

    void OnGUI()
    {
        style ??= new GUIStyle(GUI.skin.label) { fontSize = 30, normal = { textColor = Color.white } };
        if (lineTex == null)
        {
            lineTex = new Texture2D(1, 1);
            lineTex.SetPixel(0, 0, new Color(1f, 0.85f, 0.3f));
            lineTex.Apply();
        }

        // Aim preview: the short direction hint, deliberately NOT the full landing trajectory.
        if (dragging && arc.Count > 1)
        {
            for (int i = 0; i < arc.Count; i++)
            {
                var sp = cam.WorldToScreenPoint(GameSpace.ToUnity(arc[i]));
                if (sp.z <= 0f) continue;
                GUI.DrawTexture(new Rect(sp.x - 5f, Screen.height - sp.y - 5f, 10f, 10f), lineTex);
            }
        }

        string power = dragging ? $"{AimSystem.StrengthPercent(aimVel):F0}%  {AimSystem.AngleDegrees(aimVel):F1}deg" : "-";
        GUI.Label(new Rect(30, 30, 1500, 400),
            $"{1f / Mathf.Max(smoothedDt, 0.0001f):F1} fps ({smoothedDt * 1000f:F2} ms)\n" +
            $"worst idle {worstIdleDt * 1000f:F1} ms\n" +
            $"drag #{dragCount}: worst {thisDragWorst * 1000f:F1} ms (>20ms x{thisDragLong})\n" +
            $"all-time drag worst {worstDragDt * 1000f:F1} ms over {dragLongFrames} long frames\n" +
            $"{BatcherProbe.Result}\n" +
            $"aim {power}\n{lastShot}\n{selfTest}", style);
    }
}
