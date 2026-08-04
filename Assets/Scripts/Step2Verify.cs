using System.Text;
using UnityEngine;

/// <summary>
/// UNITY_SPIKE.md Step 2 — the check that proves the camera solve ported correctly.
/// Numeric test, not an eyeball test:
///   Passes if the ground plane lands at 0.685 of screen height AT SEVERAL CAMERA DISTANCES,
///   and an object of known size measures the predicted pixel height.
///   Fails if alignment holds at only one zoom — the per-frame solve did not port.
/// </summary>
public class Step2Verify : MonoBehaviour
{
    [SerializeField] Camera cam;
    [SerializeField] Transform referencePole;   // exactly 1.0 world unit tall, base at y=0, z=0
    [SerializeField] float restingCamZ = 10.4f; // aiming framing

    // camZ is clamped to 4..40 in SceneHost; sweep the usable span including both ends.
    static readonly float[] Distances = { 4f, 6f, 8.4f, 10.4f, 14f, 20f, 40f };

    void Start()
    {
        Application.targetFrameRate = 60;
        QualitySettings.vSyncCount = 0;

        var sb = new StringBuilder();
        sb.AppendLine($"[Step2] viewport={Screen.width}x{Screen.height} vfov={cam.fieldOfView} " +
                      $"expectedFraction={BattleCamera.GroundScreenFraction}");
        sb.AppendLine("[Step2]  camZ | groundFracFromTop |     err | poleMeas | poleExact |   err | doc 1200/z");

        float worstFracErr = 0f, worstScaleErrPct = 0f;

        foreach (float z in Distances)
        {
            BattleCamera.Apply(cam, 0f, BattleCamera.CameraY, z);

            // Where does the ground point straight ahead (0,0,0) actually land?
            Vector3 ground = cam.WorldToScreenPoint(Vector3.zero);
            float fracFromTop = 1f - (ground.y / Screen.height);
            float fracErr = fracFromTop - BattleCamera.GroundScreenFraction;

            // A pole of exactly 1.0 world unit at z=0: how many pixels tall does it measure?
            float measured = cam.WorldToScreenPoint(new Vector3(0f, 1f, 0f)).y
                           - cam.WorldToScreenPoint(new Vector3(0f, 0f, 0f)).y;

            // Cross-checked against the analytic projection, NOT against the doc's on-axis
            // approximation — that approximation is what made this read as a failure first time.
            float exact = BattleCamera.ExpectedScreenY(1f, 0f, BattleCamera.CameraY, z)
                        - BattleCamera.ExpectedScreenY(0f, 0f, BattleCamera.CameraY, z);
            float scaleErrPct = (measured - exact) / exact * 100f;

            worstFracErr = Mathf.Max(worstFracErr, Mathf.Abs(fracErr));
            worstScaleErrPct = Mathf.Max(worstScaleErrPct, Mathf.Abs(scaleErrPct));

            sb.AppendLine($"[Step2] {z,5:F1} | {fracFromTop,17:F5} | {fracErr,7:F5} | " +
                          $"{measured,8:F1} | {exact,9:F1} | {scaleErrPct,5:F2}% | " +
                          $"{BattleCamera.PixelsPerWorldUnit(z),8:F1}");
        }

        // NOT compared against CLAUDE.md's 89 px: that figure was measured at the OLD
        // UNIT_SCALE_UNITS of 0.77 (89/115.6 = 0.77), and the 0.77 -> 0.48 shrink held apparent
        // size roughly constant only because the camera closed in to compensate. Comparing a
        // 0.48 unit at the old camZ mixes two eras and proves nothing.
        BattleCamera.Apply(cam, 0f, BattleCamera.CameraY, 10.4f);
        float poleAt104 = BattleCamera.ExpectedScreenY(1f, 0f, BattleCamera.CameraY, 10.4f)
                        - BattleCamera.ExpectedScreenY(0f, 0f, BattleCamera.CameraY, 10.4f);
        sb.AppendLine($"[Step2] ground-standing 1.0-unit object at camZ 10.4 = {poleAt104:F1} px " +
                      $"vs doc approximation {BattleCamera.PixelsPerWorldUnit(10.4f):F1} px " +
                      $"({(poleAt104 / BattleCamera.PixelsPerWorldUnit(10.4f) - 1f) * 100f:F1}% larger)");

        bool pass = worstFracErr < 0.002f && worstScaleErrPct < 1f;
        sb.AppendLine($"[Step2] worstGroundFracErr={worstFracErr:F5} " +
                      $"worstScaleErr={worstScaleErrPct:F2}% -> {(pass ? "PASS" : "FAIL")}");

        Debug.Log(sb.ToString());

        BattleCamera.Apply(cam, 0f, BattleCamera.CameraY, restingCamZ);
    }

    void Update()
    {
        // Re-solved every frame, exactly as SceneHost does it.
        BattleCamera.Apply(cam, 0f, BattleCamera.CameraY, restingCamZ);
    }

    Texture2D lineTex;

    void OnGUI()
    {
        // Draw the target line where the painted horizon would be. If the solve is right the
        // 3D ground meets this line exactly — the visual twin of the numeric check above.
        if (lineTex == null)
        {
            lineTex = new Texture2D(1, 1);
            lineTex.SetPixel(0, 0, new Color(1f, 0.25f, 0.2f));
            lineTex.Apply();
        }
        float lineY = Screen.height * BattleCamera.GroundScreenFraction;
        GUI.DrawTexture(new Rect(0, lineY - 1.5f, Screen.width, 3f), lineTex);

        var style = new GUIStyle(GUI.skin.label)
        {
            fontSize = 34,
            normal = { textColor = Color.white },
        };
        GUI.Label(new Rect(30, 30, 1200, 400),
            $"camZ {restingCamZ:F1}   {BattleCamera.PixelsPerWorldUnit(restingCamZ):F1} px/unit\n" +
            $"ground should sit at {BattleCamera.GroundScreenFraction:P1} down the screen",
            style);
    }
}
