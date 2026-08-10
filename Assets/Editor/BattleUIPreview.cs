using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using ArmedConflict.UI;
using ArmedConflict.Game;
using ArmedConflict.Data;

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

        LoadoutShots();
    }

    /// <summary>
    /// The LOADOUT picker, with and without consumables owned.
    ///
    /// This is the panel the Kotlin's equivalent feature broke: adding a consumables section
    /// pushed Confirm past the bottom of the screen — not clipped, ABSENT from the compose tree
    /// and unreachable by any input, found on a locked device. `PortSelfTest` pins the arithmetic;
    /// this shows the thing itself, in seconds, before a three-minute device build.
    /// </summary>
    static void LoadoutShots()
    {
        var level = AssetDatabase.FindAssets("t:LevelDefinitionSO")
            .Select(g => AssetDatabase.LoadAssetAtPath<ArmedConflict.Data.LevelDefinitionSO>(
                AssetDatabase.GUIDToAssetPath(g)))
            .Where(l => l != null && !l.isTestLevel)
            .OrderBy(l => l.levelNumber).FirstOrDefault();
        var roster = AssetDatabase.LoadAssetAtPath<ArmedConflict.Data.RosterDefinitionSO>(
            "Assets/GameData/Roster.asset");
        if (level == null || roster == null)
        {
            Debug.LogError("[BattleUIPreview] no campaign level or roster to preview the loadout on");
            return;
        }

        // The owned counts come out of PlayerPrefs, so the shot has to stage them — and PUT THEM
        // BACK. A preview that quietly grants the editor three airstrikes would make every later
        // run of this file a lie about what a real player sees.
        var staged = new[] { ConsumableType.Airstrike, ConsumableType.TraumaKit };
        var before = staged.ToDictionary(t => t, ProgressStore.OwnedConsumables);
        try
        {
            LoadoutShot("loadout-unowned", level, roster, null);
            foreach (var t in staged) ProgressStore.AddConsumable(t, 2);
            LoadoutShot("loadout-owned", level, roster, null);
            LoadoutShot("loadout-carrying", level, roster, ConsumableType.Airstrike);

            // TEST SUPPLY, and this shot does two jobs.
            //
            // It lengthens the strip's header, and this preview exists precisely because a longer
            // consumables section is what pushed the Kotlin's Confirm off the bottom of the screen
            // — absent from the tree and unreachable, found on a locked device.
            //
            // AND IT ASSERTS THE PROPERTY THE WHOLE DESIGN RESTS ON: equipping under test supply
            // must write NOTHING. PortSelfTest cannot reach this — it does not drive MonoBehaviours
            // — so this is the only harness that can, and the failure it guards is severe and silent:
            // a test mode that quietly bought the item would spend a real player's coins.
            // THE PURSE HAS TO BE FULL FOR THIS TO MEAN ANYTHING. The first version of this check
            // ran on the editor's real balance, which is zero — so a fall-through to the genuine
            // purchase path simply failed to afford the item, wrote nothing, and the check passed
            // against deliberately broken code. It was a check that could not fail. Staging coins
            // is what gives the fall-through something to spend.
            var spySupply = ConsumableType.SmokeScreen;
            int stakeCoins = Consumables.For(spySupply).CoinPrice * 4;
            ProgressStore.AddCoins(stakeCoins);
            int ownedBefore = ProgressStore.OwnedConsumables(spySupply);
            int coinsBefore = ProgressStore.Coins();
            LoadoutShot("loadout-testsupply", level, roster, spySupply, testSupply: true);
            int ownedAfter = ProgressStore.OwnedConsumables(spySupply);
            int coinsAfter = ProgressStore.Coins();
            if (ownedAfter != ownedBefore || coinsAfter != coinsBefore)
                Debug.LogError($"[BattleUIPreview] TEST SUPPLY WROTE TO THE REAL ECONOMY — " +
                               $"{spySupply} owned {ownedBefore}->{ownedAfter}, " +
                               $"coins {coinsBefore}->{coinsAfter}. It must equip without buying.");
            else
                Debug.Log($"[BattleUIPreview] test supply equipped {spySupply} and wrote nothing " +
                          $"(owned {ownedBefore}, coins {coinsBefore} — enough to have bought it)");
            ProgressStore.AddCoins(-stakeCoins);
        }
        finally
        {
            foreach (var kv in before)
                ProgressStore.AddConsumable(kv.Key, kv.Value - ProgressStore.OwnedConsumables(kv.Key));
        }
    }

    static void LoadoutShot(string name, ArmedConflict.Data.LevelDefinitionSO level,
                            ArmedConflict.Data.RosterDefinitionSO roster, ConsumableType? carrying,
                            bool testSupply = false)
    {
        const int W = 1080, H = 2400;
        var camGo = new GameObject("PreviewCam", typeof(Camera));
        var cam = camGo.GetComponent<Camera>();
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = Backdrop;

        var ui = BattleUI.Create();
        ui.SetCoins(1240);
        ui.PreviewLoadout(level, roster, carrying, testSupply);

        var canvas = ui.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceCamera;
        canvas.worldCamera = cam;
        canvas.planeDistance = 10f;
        ui.GetComponent<CanvasScaler>().uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
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
        File.WriteAllBytes($"{OutDir}/{name}.png", tex.EncodeToPNG());

        // THE CHECK THE KOTLIN NEEDED: every interactive control must be ON SCREEN. A control
        // laid out past the bottom is not merely ugly, it is untappable, and the panel that
        // starts the battle is the worst place in the game for that.
        int offScreen = 0;
        var corners = new Vector3[4];
        foreach (var b in ui.GetComponentsInChildren<Button>(false))
        {
            ((RectTransform)b.transform).GetWorldCorners(corners);
            var sp = corners.Select(c => cam.WorldToScreenPoint(c)).ToList();
            if (sp.Max(p => p.y) < 0f || sp.Min(p => p.y) > H ||
                sp.Max(p => p.x) < 0f || sp.Min(p => p.x) > W)
            {
                offScreen++;
                Debug.LogError($"[BattleUIPreview] {name}: '{b.name}' is laid out OFF SCREEN "
                               + "and cannot be tapped");
            }
        }

        int labels = 0, laidOut = 0;
        foreach (var t in ui.GetComponentsInChildren<TMPro.TMP_Text>(false))
        {
            if (string.IsNullOrEmpty(t.text)) continue;
            labels++;
            t.ForceMeshUpdate();
            if (t.textInfo.characterCount > 0) laidOut++;
        }
        Debug.Log($"[BattleUIPreview] {OutDir}/{name}.png  labels={labels} laidOut={laidOut} "
                  + $"buttons={ui.GetComponentsInChildren<Button>(false).Length} offScreen={offScreen}");
        if (laidOut < labels)
            Debug.LogError($"[BattleUIPreview] {name}: {labels - laidOut} labels laid out no glyphs");

        cam.targetTexture = null;
        Object.DestroyImmediate(ui.gameObject);
        Object.DestroyImmediate(camGo);
        Object.DestroyImmediate(rt);
        Object.DestroyImmediate(tex);
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
