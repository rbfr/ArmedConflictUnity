using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// UNITY_SPIKE.md Step 1 — renderer sanity on the real device.
/// Passes if: renders correctly under at least one graphics API, steady 60 fps, clean logcat.
/// </summary>
public class Step1Probe : MonoBehaviour
{
    [SerializeField] Transform spinner;

    float smoothedDt;
    GUIStyle style;

    void Awake()
    {
        // Hold ONE steady rate, never varying by game state. The one-line analogue of the
        // fix committed to ArmedConflict 2026-08-04.
        Application.targetFrameRate = 60;
        QualitySettings.vSyncCount = 0;

        Debug.Log($"[Step1] graphicsDeviceType={SystemInfo.graphicsDeviceType} " +
                  $"name={SystemInfo.graphicsDeviceName} " +
                  $"version={SystemInfo.graphicsDeviceVersion} " +
                  $"srp={GraphicsSettings.currentRenderPipeline?.name ?? "none"} " +
                  $"screen={Screen.width}x{Screen.height}@{Screen.currentResolution.refreshRateRatio}");
    }

    void Update()
    {
        smoothedDt += (Time.unscaledDeltaTime - smoothedDt) * 0.1f;
        if (spinner != null) spinner.Rotate(0f, 30f * Time.deltaTime, 0f, Space.World);
    }

    void OnGUI()
    {
        style ??= new GUIStyle(GUI.skin.label) { fontSize = 48, normal = { textColor = Color.white } };
        float fps = smoothedDt > 0f ? 1f / smoothedDt : 0f;
        GUI.Label(new Rect(40, 40, 900, 200),
            $"{fps:F1} fps  ({smoothedDt * 1000f:F2} ms)\n{SystemInfo.graphicsDeviceType}", style);
    }
}
