using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// UNITY_SPIKE.md Step 3 verification. The pass bar that matters is NOT fps alone — it is
/// "all units render every frame, and no unit is missing its head, arms or gun." Three separate
/// Filament bugs produced exactly that symptom while every readable piece of state looked
/// correct, so this counts geometry rather than trusting the scene to be what it claims.
/// </summary>
public class Step3Probe : MonoBehaviour
{
    [SerializeField] Camera cam;
    [SerializeField] int expectedUnits;
    [SerializeField] float gameCamX = 6.0f;
    [SerializeField] float camZ = 14f;

    float smoothedDt;
    float worstDt;
    int frames;
    string verdict = "measuring...";
    GUIStyle style;

    void Start()
    {
        Application.targetFrameRate = 60;
        QualitySettings.vSyncCount = 0;
        BattleCamera.Apply(cam, GameSpace.CameraX(gameCamX), BattleCamera.CameraY, camZ);
        AuditGeometry();
    }

    /// <summary>
    /// Every rifleman GLB carries the same five nodes (accent_Rifleman, Rifleman,
    /// accent_upper_Rifleman, upper_Rifleman, skin_upper_Rifleman). A unit that lost its
    /// upper half — the exact Filament culling symptom — has fewer. Count them all.
    /// </summary>
    void AuditGeometry()
    {
        var units = GameObject.Find("L1")?.transform;
        if (units == null) { verdict = "FAIL: no L1 root"; return; }

        var byName = new Dictionary<int, int>();
        int unitCount = 0, gunCount = 0, missing = 0;
        var detail = new List<string>();

        foreach (Transform child in units)
        {
            var rends = child.GetComponentsInChildren<MeshRenderer>(includeInactive: true);
            bool isUnit = child.name.Contains("_") && !child.name.EndsWith("_gun")
                          && (child.name.StartsWith("P_") || child.name.StartsWith("E_"));
            bool isGun = child.name.EndsWith("_gun");

            if (isUnit)
            {
                unitCount++;
                byName[rends.Length] = byName.GetValueOrDefault(rends.Length) + 1;
                if (rends.Length < 5) { missing++; detail.Add($"{child.name}={rends.Length}"); }
                // A renderer with no mesh, or a mesh with no vertices, draws nothing.
                foreach (var r in rends)
                {
                    var mf = r.GetComponent<MeshFilter>();
                    if (mf == null || mf.sharedMesh == null || mf.sharedMesh.vertexCount == 0)
                    {
                        missing++;
                        detail.Add($"{child.name}/{r.name}=EMPTY");
                    }
                }
            }
            else if (isGun) gunCount++;
        }

        int totalRenderers = FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None).Length;
        string histogram = string.Join(", ", byName.OrderBy(k => k.Key)
                                                  .Select(k => $"{k.Key}rend x{k.Value}"));

        bool ok = unitCount == expectedUnits && gunCount == expectedUnits && missing == 0;
        verdict = ok ? "GEOMETRY PASS" : "GEOMETRY FAIL";

        Debug.Log($"[Step3] units={unitCount}/{expectedUnits} guns={gunCount}/{expectedUnits} " +
                  $"missingOrEmpty={missing} totalRenderers={totalRenderers}\n" +
                  $"[Step3] per-unit renderer histogram: {histogram}\n" +
                  $"[Step3] detail: {(detail.Count == 0 ? "none" : string.Join(" ", detail.Take(20)))}\n" +
                  $"[Step3] {verdict}");
    }

    void Update()
    {
        BattleCamera.Apply(cam, GameSpace.CameraX(gameCamX), BattleCamera.CameraY, camZ);

        smoothedDt += (Time.unscaledDeltaTime - smoothedDt) * 0.05f;
        frames++;
        if (frames > 60) worstDt = Mathf.Max(worstDt, Time.unscaledDeltaTime);

        if (frames == 600)
        {
            Debug.Log($"[Step3] after {frames} frames: avg={smoothedDt * 1000f:F2}ms " +
                      $"({1f / smoothedDt:F1} fps) worstFrame={worstDt * 1000f:F2}ms");
        }
    }

    void OnGUI()
    {
        style ??= new GUIStyle(GUI.skin.label) { fontSize = 34, normal = { textColor = Color.white } };
        GUI.Label(new Rect(30, 30, 1400, 300),
            $"{1f / Mathf.Max(smoothedDt, 0.0001f):F1} fps  ({smoothedDt * 1000f:F2} ms)  " +
            $"worst {worstDt * 1000f:F1} ms\n{verdict}\ncamZ {camZ:F1}", style);
    }
}
