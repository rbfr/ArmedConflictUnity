using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using ArmedConflict.UI;

/// <summary>
/// Renders the victory and defeat cards offscreen, headless.
///
/// Run: -batchmode -quit -executeMethod BattleUIPreview.Shots
///
/// It exists for ONE failure this phase introduced a real risk of: TextMesh Pro renders nothing
/// at all — silently, no error, every label blank — when its settings asset or default font is
/// missing or mis-GUIDed. TMP's essentials had to be unpacked from the package tarball by hand
/// here (tools/import_tmp_essentials.py), because AssetDatabase.ImportPackage is asynchronous and
/// imports nothing under -quit. A build that compiles proves none of that landed correctly.
///
/// This is EVIDENCE, NOT PROOF. BackdropPreview rendered every biome as bare sky and ground for a
/// whole session while looking entirely plausible, and the rule that came out of it stands: never
/// judge a visual from the preview alone. What this can settle is the blank-text question, which
/// it answers by asking TMP how many glyphs it actually laid out — not by looking at the image.
/// </summary>
public static class BattleUIPreview
{
    const string OutDir = "Builds/ui";
    static readonly Color Backdrop = new(0.16f, 0.20f, 0.26f);

    public static void Shots()
    {
        Directory.CreateDirectory(OutDir);
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        Shot("victory-2star", victory: true, stars: 2, coins: 340,
             bonus: "First Clear!", survivors: 10, initial: 14);
        Shot("victory-3star", victory: true, stars: 3, coins: 650,
             bonus: "Daily Bonus!", survivors: 14, initial: 14);
        Shot("defeat", victory: false, stars: 0, coins: 15,
             bonus: null, survivors: 0, initial: 14);
    }

    static void Shot(string name, bool victory, int stars, int coins, string bonus,
                     int survivors, int initial)
    {
        // 1080x2400 — the reference resolution the CanvasScaler is authored against. Screen
        // itself reports a placeholder desktop size in batchmode and must never be read here.
        const int W = 1080, H = 2400;

        var camGo = new GameObject("PreviewCam", typeof(Camera));
        var cam = camGo.GetComponent<Camera>();
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = Backdrop;

        var ui = BattleUI.Create();
        ui.SetCoins(1240);
        ui.PreviewEndCard(victory, stars, coins, bonus, survivors, initial);

        // A ScreenSpaceOverlay canvas is composited straight to the display and never appears in
        // a camera's target texture, so an offscreen shot of one comes back empty. Borrowing the
        // preview camera for the render is what makes it capturable at all.
        var canvas = ui.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceCamera;
        canvas.worldCamera = cam;
        canvas.planeDistance = 10f;
        var scaler = ui.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
        Canvas.ForceUpdateCanvases();

        var rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32);
        cam.targetTexture = rt;
        cam.Render();

        var prev = RenderTexture.active;
        RenderTexture.active = rt;
        var tex = new Texture2D(W, H, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, W, H), 0, 0);
        tex.Apply();
        RenderTexture.active = prev;

        string path = $"{OutDir}/{name}.png";
        File.WriteAllBytes(path, tex.EncodeToPNG());

        // ASK TMP, do not measure pixels. A first attempt counted pixels differing from the
        // backdrop and reported 98.5% — which proves nothing at all, because the card's
        // full-screen dim covers every pixel by construction whether a single glyph resolved or
        // not. characterCount is the question actually being asked: it is non-zero only when a
        // font asset resolved AND the string laid out into glyphs.
        // ACTIVE labels only. Counting inactive ones too reported the defeat card as broken:
        // its NEXT button is deliberately hidden, and a disabled object never lays out glyphs —
        // a false alarm about the one thing this check exists to be trusted on.
        int labels = 0, missingFont = 0, laidOut = 0;
        foreach (var t in ui.GetComponentsInChildren<TMPro.TMP_Text>(false))
        {
            if (string.IsNullOrEmpty(t.text)) continue;
            labels++;
            if (t.font == null) missingFont++;
            t.ForceMeshUpdate();
            if (t.textInfo.characterCount > 0) laidOut++;
        }

        int sprited = 0;
        foreach (var img in ui.GetComponentsInChildren<Image>(true))
            if (img.sprite != null) sprited++;

        Debug.Log($"[BattleUIPreview] {path}  labels={labels} laidOut={laidOut} " +
                  $"missingFont={missingFont} spritedImages={sprited}");
        if (missingFont > 0 || laidOut < labels)
            Debug.LogError($"[BattleUIPreview] {name}: TEXT DID NOT RENDER — " +
                           $"{missingFont} labels have no font asset, {labels - laidOut} laid " +
                           "out no glyphs. TMP essential resources are missing or mis-GUIDed.");

        // Order matters: the camera owns the target texture, so it has to be detached before
        // anything is destroyed. Releasing it after destroying the camera throws.
        cam.targetTexture = null;
        Object.DestroyImmediate(ui.gameObject);
        Object.DestroyImmediate(camGo);
        Object.DestroyImmediate(rt);
        Object.DestroyImmediate(tex);
    }
}
