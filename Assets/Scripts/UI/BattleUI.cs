using System;
using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using ArmedConflict.Game;

namespace ArmedConflict.UI
{
    /// <summary>
    /// The battle's uGUI layer: a persistent coin balance, and the victory/defeat sequence that
    /// turns the award into something the player can feel.
    ///
    /// BUILT IN CODE, at runtime, not authored as a prefab. That is a deliberate choice for this
    /// project rather than the usual Unity practice, and it buys one specific thing: a UI change
    /// stays a CODE-ONLY change. No serialized references means no scene rebuild, and the editor
    /// GUI here runs over VNC on llvmpipe where laying out a canvas by hand is genuinely painful.
    /// The cost is that a designer cannot nudge this in the inspector — there is no designer, and
    /// every other visual in this build is already constructed by script.
    ///
    /// It is still real retained-mode uGUI: the hierarchy is built ONCE in Awake, nothing
    /// allocates per frame, and the whole canvas batches. This is not IMGUI with extra steps —
    /// the IMGUI HUD it sits beside rebuilds its entire layout every single frame.
    /// </summary>
    public class BattleUI : MonoBehaviour
    {
        /// <summary>Invoked by the panel's buttons. BattleRunner owns what they actually do.</summary>
        public Action OnNext, OnRetry;

        // --- palette --------------------------------------------------------------------
        static readonly Color Gold = new(1f, 0.82f, 0.28f);
        static readonly Color StarEmpty = new(1f, 1f, 1f, 0.16f);
        static readonly Color Dim = new(0f, 0f, 0f, 0.72f);
        static readonly Color CardBg = new(0.07f, 0.08f, 0.11f, 0.96f);
        static readonly Color Body = new(0.86f, 0.88f, 0.92f);
        static readonly Color Win = new(0.60f, 1f, 0.60f);
        static readonly Color Loss = new(1f, 0.45f, 0.40f);

        /// <summary>
        /// How long the finished battle is left alone before the card comes up.
        ///
        /// The killing blow is the loudest moment in the game — a structure coming down, the last
        /// of a garrison falling — and covering it with a full-screen dim the same frame it lands
        /// throws away the spectacle the whole turn was building toward. The award is granted
        /// immediately either way; only the presentation waits.
        /// </summary>
        const float VictoryHold = 1.1f;
        /// <summary>Shorter: a defeat has no collapse to watch, and waiting reads as the game
        /// hesitating rather than as a beat.</summary>
        const float DefeatHold = 0.6f;

        // --- built widgets --------------------------------------------------------------
        Canvas canvas;
        RectTransform cardRect;
        TMP_Text coinBalanceText;
        GameObject endPanel, starRow;
        TMP_Text titleText, reasonText, coinsText, bonusText;
        Image[] starImages;
        GameObject nextButton, retryButton;
        TMP_Text retryLabel;

        Coroutine sequence;
        int shownBalance;

        /// <summary>
        /// Creates the canvas and its EventSystem. Called once from BattleRunner.Start — there is
        /// no UI in the scene asset at all, by design (see the class comment).
        /// </summary>
        public static BattleUI Create()
        {
            // uGUI buttons are dead without an EventSystem, and this project has never had one.
            // activeInputHandler is 0 (the legacy Input Manager), so the legacy module is the
            // matching one; the new InputSystemUIInputModule would silently do nothing here.
            if (EventSystem.current == null)
            {
                var es = new GameObject("EventSystem",
                                        typeof(EventSystem), typeof(StandaloneInputModule));
                // DontDestroyOnLoad is an error outside play mode, and BattleUIPreview builds
                // this same canvas from an editor method to prove it actually draws.
                if (Application.isPlaying) DontDestroyOnLoad(es);
            }

            var go = new GameObject("BattleUI", typeof(RectTransform));
            var ui = go.AddComponent<BattleUI>();
            ui.Build();
            return ui;
        }

        /// <summary>
        /// Builds the hierarchy. Called explicitly by Create rather than from Awake, because
        /// Awake does NOT run in edit mode without [ExecuteAlways] — which left every widget null
        /// when BattleUIPreview built this canvas from an editor method. Making construction a
        /// plain call removes the lifecycle from the question entirely.
        /// </summary>
        void Build()
        {
            if (canvas != null) return;

            canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            // Above the IMGUI HUD conceptually, though IMGUI always draws last — which is why
            // the IMGUI RESTART/NEXT buttons had to be removed rather than merely covered.
            canvas.sortingOrder = 100;

            var scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080f, 2400f);
            // 0.5 so the layout survives both a tall phone and a squat editor window; matching
            // width alone makes the card enormous on a 16:9 device.
            scaler.matchWidthOrHeight = 0.5f;

            gameObject.AddComponent<GraphicRaycaster>();

            BuildCoinPill();
            BuildEndPanel();
            endPanel.SetActive(false);
        }

        // ================================================================================
        // Public API
        // ================================================================================

        public void SetCoins(int coins)
        {
            shownBalance = coins;
            coinBalanceText.SetText("{0}", coins);
        }

        public void ShowVictory(TurnFlow.VictoryAward award, int survivors, int initialCount,
                                bool hasNextLevel)
        {
            titleText.text = "VICTORY";
            titleText.color = Win;
            reasonText.text = TurnFlow.StarReason(survivors, initialCount);
            retryLabel.text = award.Stars < 3 ? "RETRY FOR 3 STARS" : "REPLAY";
            nextButton.SetActive(hasNextLevel);
            Play(award.Stars, award.Coins, award.BonusTag, showStars: true, hold: VictoryHold);
        }

        public void ShowDefeat(int coins)
        {
            titleText.text = "DEFEAT";
            titleText.color = Loss;
            // Teaches rather than scolds, and still pays — a loss that reads as pure punishment
            // is what makes a casual player close the app instead of retrying.
            reasonText.text = "Your line was overrun — thin them out before they close.";
            retryLabel.text = "RETRY";
            nextButton.SetActive(false);
            // No star row on a defeat: there are no stars to fill, and running the beats anyway
            // spent most of a second staring at three empty outlines.
            Play(0, coins, null, showStars: false, hold: DefeatHold);
        }

        public void Hide()
        {
            if (sequence != null) { StopCoroutine(sequence); sequence = null; }
            endPanel.SetActive(false);
        }

        /// <summary>
        /// Hides the whole canvas while the FREE CAMERA is flying.
        ///
        /// The free camera's stated job is to hold through volleys and the victory screen so a
        /// finished battle can be inspected — half this project's visual bugs were confirmed by
        /// parking it in front of the thing. A full-screen 72% dim over that is the one change
        /// that would quietly take the tool away, so the card yields to it.
        /// </summary>
        public void SetVisible(bool visible) => canvas.enabled = visible;

        /// <summary>
        /// Jumps straight to a finished end-card, skipping the sequence. Editor preview only —
        /// coroutines do not run outside play mode, so BattleUIPreview cannot reach this state by
        /// calling ShowVictory. Nothing in the game should use it.
        /// </summary>
        public void PreviewEndCard(bool victory, int stars, int coins, string bonusTag,
                                   int survivors, int initialCount)
        {
            if (victory) ShowVictory(new TurnFlow.VictoryAward
            {
                Stars = stars, Coins = coins, BonusTag = bonusTag,
            }, survivors, initialCount, hasNextLevel: true);
            else ShowDefeat(coins);

            if (sequence != null) { StopCoroutine(sequence); sequence = null; }
            endPanel.SetActive(true);
            for (int i = 0; i < starImages.Length; i++)
                starImages[i].color = i < stars ? Gold : StarEmpty;
            coinsText.SetText("+{0}", coins);
            bonusText.text = bonusTag ?? "";
            SetCoins(shownBalance + coins);
        }

        // ================================================================================
        // The sequence
        // ================================================================================

        /// <summary>
        /// Positions the card's contents for a result WITH or WITHOUT a star row.
        ///
        /// Two things go wrong if the victory layout is reused for a defeat: the removed star row
        /// leaves a conspicuous hole under the title, and RETRY stays parked in the left-hand
        /// button slot as though a second button had failed to load. A screen with one button
        /// centres it.
        /// </summary>
        void LayoutFor(bool showStars)
        {
            float shift = showStars ? 0f : -130f;   // pull everything up into the star row's space
            cardRect.sizeDelta = new Vector2(880f, showStars ? 980f : 720f);

            Place(reasonText.rectTransform, 0f, -360f - shift, 800f, 120f);
            Place(coinsText.rectTransform, 0f, -500f - shift, 800f, 100f);
            Place(bonusText.rectTransform, 0f, -600f - shift, 800f, 70f);

            var retry = (RectTransform)retryButton.transform;
            var next = (RectTransform)nextButton.transform;
            bool twoButtons = nextButton.activeSelf;
            retry.anchoredPosition = new Vector2(twoButtons ? -210f : 0f, -770f - shift);
            next.anchoredPosition = new Vector2(210f, -770f - shift);
        }

        void Play(int stars, int coins, string bonusTag, bool showStars, float hold)
        {
            LayoutFor(showStars);
            // Stays hidden until the hold expires — the coroutine raises it.
            endPanel.SetActive(false);
            starRow.SetActive(showStars);
            foreach (var s in starImages) s.color = StarEmpty;
            coinsText.text = "";
            bonusText.text = "";
            if (sequence != null) StopCoroutine(sequence);
            sequence = StartCoroutine(Sequence(stars, coins, bonusTag, showStars, hold));
        }

        /// <summary>
        /// Stars land one at a time, then the coins count up, then the bonus banner. The order is
        /// the point: paying out all at once reads as a receipt, paying out in beats reads as a
        /// result. The buttons are live throughout — an impatient player must never be made to
        /// watch this, which is what "cheap retry" means in practice.
        ///
        /// Unscaled time throughout, so a paused or slowed battle cannot strand the panel
        /// half-played.
        /// </summary>
        IEnumerator Sequence(int stars, int coins, string bonusTag, bool showStars, float hold)
        {
            yield return WaitUnscaled(hold);
            endPanel.SetActive(true);

            if (showStars)
                for (int i = 0; i < starImages.Length; i++)
                {
                    yield return WaitUnscaled(i == 0 ? 0.25f : 0.30f);
                    if (i < stars)
                    {
                        starImages[i].color = Gold;
                        yield return Pop(starImages[i].rectTransform);
                    }
                }

            yield return WaitUnscaled(0.15f);

            // Count-up. The BALANCE climbs with the award rather than snapping afterwards — the
            // point of the beat is watching the number you keep go up, not being shown a receipt
            // and then, separately, a total.
            //
            // The string is rebuilt only when the displayed integer actually changes, so a 0.7s
            // animation costs a few dozen allocations rather than one per frame.
            int balanceFrom = shownBalance;
            const float dur = 0.7f;
            int last = -1;
            for (float t = 0f; t < dur; t += Time.unscaledDeltaTime)
            {
                int shown = Mathf.RoundToInt(Mathf.Lerp(0f, coins, t / dur));
                if (shown != last)
                {
                    coinsText.SetText("+{0}", shown);
                    SetCoins(balanceFrom + shown);
                    last = shown;
                }
                yield return null;
            }
            coinsText.SetText("+{0}", coins);
            SetCoins(balanceFrom + coins);

            if (!string.IsNullOrEmpty(bonusTag))
            {
                yield return WaitUnscaled(0.12f);
                bonusText.text = bonusTag;
                yield return Pop(bonusText.rectTransform);
            }

            sequence = null;
        }

        static IEnumerator WaitUnscaled(float seconds)
        {
            for (float t = 0f; t < seconds; t += Time.unscaledDeltaTime) yield return null;
        }

        /// <summary>A short overshoot on arrival. Light — PRODUCT_DIRECTION asks for a punch, not
        /// a slow-motion spam.</summary>
        static IEnumerator Pop(RectTransform rt)
        {
            const float dur = 0.18f;
            for (float t = 0f; t < dur; t += Time.unscaledDeltaTime)
            {
                float k = t / dur;
                rt.localScale = Vector3.one * (1f + 0.35f * Mathf.Sin(k * Mathf.PI));
                yield return null;
            }
            rt.localScale = Vector3.one;
        }

        // ================================================================================
        // Construction
        // ================================================================================

        void BuildCoinPill()
        {
            // Fitted to the SAFE AREA rather than to a hardcoded inset. This panel's display
            // cutout is 161px, but that number belongs to one device — Screen.safeArea is the
            // question actually being asked, and it is right on every device.
            var safe = NewRect("SafeArea", transform);
            var sa = Screen.safeArea;
            safe.anchorMin = new Vector2(sa.xMin / Screen.width, sa.yMin / Screen.height);
            safe.anchorMax = new Vector2(sa.xMax / Screen.width, sa.yMax / Screen.height);
            safe.offsetMin = safe.offsetMax = Vector2.zero;

            var pill = NewRect("CoinPill", safe);
            pill.anchorMin = pill.anchorMax = new Vector2(0.5f, 1f);
            pill.pivot = new Vector2(0.5f, 1f);
            pill.anchoredPosition = new Vector2(0f, -16f);
            pill.sizeDelta = new Vector2(260f, 74f);
            var bg = pill.gameObject.AddComponent<Image>();
            bg.color = new Color(0f, 0f, 0f, 0.45f);
            bg.raycastTarget = false;

            // A DRAWN coin, for the same reason the stars are drawn: ◆ is outside the default TMP
            // font asset's ASCII coverage and rendered as a missing-glyph box on the very first
            // card this project produced.
            var coin = NewRect("CoinIcon", pill);
            coin.anchorMin = coin.anchorMax = new Vector2(0f, 0.5f);
            coin.pivot = new Vector2(0f, 0.5f);
            coin.anchoredPosition = new Vector2(22f, 0f);
            coin.sizeDelta = new Vector2(42f, 42f);
            var coinImg = coin.gameObject.AddComponent<Image>();
            coinImg.sprite = CoinSprite();
            coinImg.color = Gold;
            coinImg.raycastTarget = false;

            coinBalanceText = NewText("Balance", pill, 40f, Gold, TextAlignmentOptions.Left);
            Stretch(coinBalanceText.rectTransform);
            coinBalanceText.margin = new Vector4(80f, 0f, 16f, 0f);
            coinBalanceText.SetText("{0}", 0);
        }

        void BuildEndPanel()
        {
            var panel = NewRect("EndPanel", transform);
            Stretch(panel);
            endPanel = panel.gameObject;
            // The dim catches taps so a stray touch cannot reach the battle underneath.
            panel.gameObject.AddComponent<Image>().color = Dim;

            var card = NewRect("Card", panel);
            card.anchorMin = card.anchorMax = new Vector2(0.5f, 0.5f);
            // Height is set per result by LayoutFor — a defeat has no star row and must not leave
            // a hole where one would have been.
            card.sizeDelta = new Vector2(880f, 980f);
            cardRect = card;
            var cardBg = card.gameObject.AddComponent<Image>();
            cardBg.color = CardBg;
            cardBg.raycastTarget = false;

            titleText = NewText("Title", card, 92f, Win, TextAlignmentOptions.Center);
            Place(titleText.rectTransform, 0f, -70f, 800f, 110f);

            // --- stars ---
            var starRowRt = NewRect("Stars", card);
            Place(starRowRt, 0f, -210f, 600f, 150f);
            starRow = starRowRt.gameObject;
            starImages = new Image[3];
            for (int i = 0; i < 3; i++)
            {
                var s = NewRect($"Star{i}", starRowRt);
                s.anchorMin = s.anchorMax = new Vector2(0.5f, 0.5f);
                s.sizeDelta = new Vector2(140f, 140f);
                s.anchoredPosition = new Vector2((i - 1) * 170f, 0f);
                var img = s.gameObject.AddComponent<Image>();
                img.sprite = StarSprite();
                img.color = StarEmpty;
                img.raycastTarget = false;
                starImages[i] = img;
            }

            reasonText = NewText("Reason", card, 40f, Body, TextAlignmentOptions.Center);
            Place(reasonText.rectTransform, 0f, -360f, 800f, 120f);
            reasonText.textWrappingMode = TextWrappingModes.Normal;

            coinsText = NewText("Coins", card, 76f, Gold, TextAlignmentOptions.Center);
            Place(coinsText.rectTransform, 0f, -500f, 800f, 100f);

            bonusText = NewText("Bonus", card, 44f, new Color(0.55f, 0.9f, 1f),
                                TextAlignmentOptions.Center);
            Place(bonusText.rectTransform, 0f, -600f, 800f, 70f);

            retryButton = NewButton("Retry", card, new Vector2(-210f, -770f), "RETRY",
                                    new Color(0.20f, 0.22f, 0.28f), () => OnRetry?.Invoke(),
                                    out retryLabel);
            nextButton = NewButton("Next", card, new Vector2(210f, -770f), "NEXT",
                                   new Color(0.16f, 0.42f, 0.24f), () => OnNext?.Invoke(), out _);
        }

        // ================================================================================
        // Small builders
        // ================================================================================

        static RectTransform NewRect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            return rt;
        }

        static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
        }

        /// <summary>Positions against the parent's TOP-CENTRE, which is how the card is laid out.</summary>
        static void Place(RectTransform rt, float x, float y, float w, float h)
        {
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.anchoredPosition = new Vector2(x, y);
            rt.sizeDelta = new Vector2(w, h);
        }

        static TMP_Text NewText(string name, Transform parent, float size, Color color,
                                TextAlignmentOptions align)
        {
            var rt = NewRect(name, parent);
            var t = rt.gameObject.AddComponent<TextMeshProUGUI>();
            t.font = TMP_Settings.defaultFontAsset;
            t.fontSize = size;
            t.color = color;
            t.alignment = align;
            t.textWrappingMode = TextWrappingModes.NoWrap;
            t.raycastTarget = false;
            return t;
        }

        static GameObject NewButton(string name, Transform parent, Vector2 pos, string label,
                                    Color bg, UnityEngine.Events.UnityAction onClick,
                                    out TMP_Text labelText)
        {
            var rt = NewRect(name, parent);
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.anchoredPosition = pos;
            rt.sizeDelta = new Vector2(400f, 120f);

            var img = rt.gameObject.AddComponent<Image>();
            img.color = bg;
            var button = rt.gameObject.AddComponent<Button>();
            button.targetGraphic = img;
            button.onClick.AddListener(onClick);

            labelText = NewText("Label", rt, 36f, Color.white, TextAlignmentOptions.Center);
            Stretch(labelText.rectTransform);
            labelText.text = label;
            return rt.gameObject;
        }

        // ================================================================================
        // The star sprite
        // ================================================================================

        static Sprite starSprite;

        /// <summary>
        /// A five-pointed star, generated once.
        ///
        /// It is NOT the ★ glyph: the default LiberationSans SDF font asset TMP ships with is
        /// built over ASCII only, so U+2605 renders as a missing-glyph box — which is exactly the
        /// kind of thing that looks fine in the editor's fallback and ships broken. Drawing it
        /// avoids depending on font coverage at all.
        /// </summary>
        static Sprite StarSprite()
        {
            if (starSprite != null) return starSprite;

            const int N = 96;
            const int Super = 3;      // 3x3 supersampling — enough to hide the stair-stepping
            var pts = new Vector2[10];
            for (int i = 0; i < 10; i++)
            {
                // Alternating outer/inner radius; the inner ratio is close to a regular
                // pentagram's 0.382 and reads better slightly fattened.
                float r = (i % 2 == 0) ? 0.48f : 0.21f;
                float a = Mathf.PI * 0.5f + i * Mathf.PI / 5f;   // first point straight up
                pts[i] = new Vector2(0.5f + Mathf.Cos(a) * r, 0.5f + Mathf.Sin(a) * r);
            }

            var tex = new Texture2D(N, N, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave,
            };

            var pixels = new Color32[N * N];
            for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                int hits = 0;
                for (int sy = 0; sy < Super; sy++)
                for (int sx = 0; sx < Super; sx++)
                {
                    float px = (x + (sx + 0.5f) / Super) / N;
                    float py = (y + (sy + 0.5f) / Super) / N;
                    if (Inside(pts, px, py)) hits++;
                }
                byte a = (byte)(255 * hits / (Super * Super));
                pixels[y * N + x] = new Color32(255, 255, 255, a);
            }
            tex.SetPixels32(pixels);
            tex.Apply(false, true);

            starSprite = Sprite.Create(tex, new Rect(0, 0, N, N), new Vector2(0.5f, 0.5f));
            starSprite.hideFlags = HideFlags.HideAndDontSave;
            return starSprite;
        }

        static Sprite coinSprite;

        /// <summary>
        /// A plain disc with a thin inner ring, tinted gold at use. Drawn rather than typed for
        /// the same reason as the star — ◆ is not in the default font asset's ASCII coverage.
        /// </summary>
        static Sprite CoinSprite()
        {
            if (coinSprite != null) return coinSprite;

            const int N = 64;
            const int Super = 3;
            var tex = new Texture2D(N, N, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave,
            };

            var pixels = new Color32[N * N];
            for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                int hits = 0, ring = 0;
                for (int sy = 0; sy < Super; sy++)
                for (int sx = 0; sx < Super; sx++)
                {
                    float px = (x + (sx + 0.5f) / Super) / N - 0.5f;
                    float py = (y + (sy + 0.5f) / Super) / N - 0.5f;
                    float d = Mathf.Sqrt(px * px + py * py);
                    if (d <= 0.46f) hits++;
                    if (d > 0.30f && d <= 0.35f) ring++;
                }
                int n = Super * Super;
                byte a = (byte)(255 * hits / n);
                // The ring is a darker band inside the disc, not a hole — it reads as a struck
                // edge at 42px and costs nothing.
                byte v = (byte)(255 - 90 * ring / n);
                pixels[y * N + x] = new Color32(v, v, v, a);
            }
            tex.SetPixels32(pixels);
            tex.Apply(false, true);

            coinSprite = Sprite.Create(tex, new Rect(0, 0, N, N), new Vector2(0.5f, 0.5f));
            coinSprite.hideFlags = HideFlags.HideAndDontSave;
            return coinSprite;
        }

        /// <summary>Standard crossing-count point-in-polygon.</summary>
        static bool Inside(Vector2[] poly, float x, float y)
        {
            bool inside = false;
            for (int i = 0, j = poly.Length - 1; i < poly.Length; j = i++)
            {
                if ((poly[i].y > y) == (poly[j].y > y)) continue;
                float t = (y - poly[i].y) / (poly[j].y - poly[i].y);
                if (x < poly[i].x + t * (poly[j].x - poly[i].x)) inside = !inside;
            }
            return inside;
        }
    }
}
