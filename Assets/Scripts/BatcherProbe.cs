using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Confirms the SRP Batcher is actually doing work, by measuring with it ON and OFF on the
/// real device rather than reading a checkbox.
///
/// `UnityStats` (batches / setPassCalls) is EDITOR ONLY, and the Frame Debugger needs the editor
/// GUI, which on this machine runs over VNC on llvmpipe. Toggling
/// GraphicsSettings.useScriptableRenderPipelineBatching at runtime and measuring frame time is a
/// better test anyway: it answers "does it cost less" rather than "is it enabled".
///
/// CONCLUSION (2026-08-04): this approach CANNOT answer the question on Android. The swap is
/// tied to the display, so wall-clock frame time only ever lands on multiples of the panel
/// period (8.33ms at 120Hz). Measured: 19 units -> 8.333ms both arms; 3,101 renderers ->
/// 8.333ms both arms; 20,000 renderers -> 25.035ms vs 16.670ms, which is 3 quanta vs 2 and
/// therefore ordering, not magnitude. Use the editor Frame Debugger for the mechanism.
/// What this DID establish is headroom, which is the practically important part.
///
/// Do NOT attach this to an interactive scene: it uncaps the frame rate and toggles global
/// batching state mid-session.
///
/// The frame rate cap MUST come off for the measurement. At a 60 fps target both configurations
/// sit at 16.67ms and the difference is invisible — the cap hides exactly the thing being
/// measured.
/// </summary>
public class BatcherProbe : MonoBehaviour
{
    const int WarmupFrames = 120;
    const int SampleFrames = 400;

    public static string Result = "batcher: measuring...";

    /// <summary>
    /// The L1 scene renders inside a vsync quantum on this device, so BOTH arms measure the
    /// panel rate and the batcher's effect is invisible. Cloning the roster loads the frame
    /// until it is genuinely draw-call bound, which is the only condition where the question
    /// "does the SRP Batcher collapse real work" can be answered by a stopwatch.
    /// Clones spread in Z so they stay inside the frustum and actually draw.
    /// </summary>
    [SerializeField] int stressClones = 0;

    void Start() => StartCoroutine(Measure());

    void Stress()
    {
        var root = GameObject.Find("L1");
        if (root == null || stressClones <= 0) return;
        for (int i = 0; i < stressClones; i++)
        {
            var c = Instantiate(root, root.transform.parent);
            c.name = $"L1_clone_{i}";
            c.transform.position = new Vector3((i % 2 == 0 ? 0.18f : -0.18f) * i, 0f, -0.22f * (i + 1));
        }
        int renderers = FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None).Length;
        Debug.Log($"[Batcher] stress: {stressClones} clones, {renderers} renderers total");
    }

    IEnumerator Measure()
    {
        Debug.Log("[Batcher] probe started");
        Stress();
        for (int i = 0; i < WarmupFrames; i++) yield return null;

        int cappedTo = Application.targetFrameRate;
        // NOT -1: on Android that means "platform default", which measured as exactly 30fps
        // (33.33ms in BOTH arms) and hid the whole difference. An explicit high target is the
        // only way to actually uncap.
        Application.targetFrameRate = 300;
        QualitySettings.vSyncCount = 0;
        for (int i = 0; i < 60; i++) yield return null;

        // Interleave ON/OFF/ON/OFF so thermal drift affects both arms equally rather than
        // loading onto whichever ran second.
        var on = new List<float>();
        var off = new List<float>();
        for (int rep = 0; rep < 2; rep++)
        {
            Debug.Log($"[Batcher] rep {rep} ON...");
            yield return Sample(true, on);
            Debug.Log($"[Batcher] rep {rep} OFF...");
            yield return Sample(false, off);
        }

        GraphicsSettings.useScriptableRenderPipelineBatching = true;
        Application.targetFrameRate = cappedTo <= 0 ? 60 : cappedTo;

        float medOn = Median(on), medOff = Median(off);
        float deltaPct = (medOff - medOn) / medOn * 100f;
        // If both arms land on the same vsync quantum the measurement is invalid, not null.
        // Both arms landing on the same value while sitting exactly on a panel quantum means
        // the frame never became the bottleneck — the result is INVALID, not "no effect".
        // Android ties the swap to the display, so wall-clock frame time can ONLY land on
        // multiples of the panel period — 8.33ms here. Any result that sits on a quantum is
        // measuring vsync, not work. This probe therefore cannot resolve sub-vsync differences
        // at all; treat a "delta" between two quantised values as ordering, never magnitude.
        float q = 1f / 120f;
        bool onQuantum = Mathf.Abs(medOn / q - Mathf.Round(medOn / q)) < 0.02f
                      && Mathf.Abs(medOff / q - Mathf.Round(medOff / q)) < 0.02f;
        bool vsyncBound = onQuantum;
        Result = $"SRP Batcher ON {medOn * 1000f:F2}ms | OFF {medOff * 1000f:F2}ms " +
                 (vsyncBound ? " [VSYNC BOUND — INVALID] " : "") +
                 $"({deltaPct:+0.0;-0.0}% slower without)";

        Debug.Log($"[Batcher] uncappedTo={Application.targetFrameRate} " +
                  $"panel={Screen.currentResolution.refreshRateRatio.value:F1}Hz");
        Debug.Log($"[Batcher] gfx={SystemInfo.graphicsDeviceType} " +
                  $"medianFrame ON={medOn * 1000f:F3}ms OFF={medOff * 1000f:F3}ms " +
                  $"delta={deltaPct:+0.0;-0.0}% samples={on.Count}/{off.Count}");
        Debug.Log($"[Batcher] {(vsyncBound ? "INCONCLUSIVE — both arms sit on vsync quanta; wall clock cannot resolve this on Android. Use the editor Frame Debugger." : deltaPct > 3f ? "CONFIRMED — the batcher is collapsing real work" : "NO MEASURABLE EFFECT at this load")}");
    }

    IEnumerator Sample(bool enabled, List<float> into)
    {
        GraphicsSettings.useScriptableRenderPipelineBatching = enabled;
        for (int i = 0; i < 30; i++) yield return null;      // settle after the toggle
        for (int i = 0; i < SampleFrames; i++)
        {
            into.Add(Time.unscaledDeltaTime);
            yield return null;
        }
    }

    static float Median(List<float> v)
    {
        if (v.Count == 0) return 0f;
        var c = new List<float>(v);
        c.Sort();
        return c[c.Count / 2];
    }
}
