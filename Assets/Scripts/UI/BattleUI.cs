using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using ArmedConflict.Data;
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

        GameObject eventBanner, telegraphStrip;
        TMP_Text eventText, telegraphText;
        string shownAnnouncement, shownTelegraph;
        Coroutine sequence, bannerPop;
        int shownBalance;

        GameObject loadoutPanel, beginButton, safeArea;
        TMP_Text loadoutTitle, loadoutSummary, loadoutBalance, loadoutFaction;
        LoadoutRow[] loadoutRows;
        LevelDefinitionSO loadoutLevel;
        FactionDefinitionSO loadoutFactionDef;
        RosterDefinitionSO loadoutRoster;
        List<Pick> loadoutPicks = new();
        ConsumableTile[] consumableTiles;
        CamoTile[] camoTiles;
        TMPro.TMP_Text consumableHeader, camoHeader;
        /// <summary>
        /// What the player is carrying INTO this battle. Owned inventory lives in ProgressStore
        /// and is not touched here — equipping is a choice about this battle, and nothing is spent
        /// until an item is actually used.
        /// </summary>
        readonly Dictionary<ConsumableType, int> loadoutConsumables = new();

        /// <summary>
        /// RIGS is on, so consumables, camo AND UNIT CLASSES are free to equip and nothing is
        /// ever bought or spent. See BattleRunner.TestSupply for why this exists and why it
        /// writes nothing.
        /// </summary>
        bool testSupply;

        /// <summary>
        /// Whether a class may be fielded. UNITS WERE NOT COVERED BY RIGS UNTIL 2026-08-12 — only
        /// consumables and camo were — so verifying one roster change cost a real 250-700 coin
        /// purchase on a build whose test protocol (uninstall/reinstall) wipes the balance every
        /// time. HANDOVER told two sessions to "buy both with RIGS on" and RIGS had never done
        /// that. Rob's call: make the code match the documented protocol.
        ///
        /// It grants ACCESS and writes NOTHING — no unlock is recorded, no coins move, and the
        /// buy button is never shown, so a test session cannot leave the wardrobe or the balance
        /// changed behind it. Same contract as the cosmetic and consumable paths beside it.
        /// </summary>
        bool UnitUnlocked(string unitId)
            => testSupply || ProgressStore.IsUnitUnlocked(unitId);
        Action<List<Pick>, IReadOnlyDictionary<ConsumableType, int>> onLoadoutBegin;

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
            BuildEventBanner();
            BuildLoadoutPanel();
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
            // The banners belong to the battle that raised them — a stale "reinforcements!" on
            // the next level would be a lie, and the strip would sit there forever.
            SetEvents(null, null);
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
        /// <summary>
        /// Raises the LOADOUT picker for a headless shot — the panel whose layout is the one thing
        /// this feature could break in the way the Kotlin did (Confirm laid out past the bottom of
        /// the screen, absent from the tree, unreachable). `carrying` taps a tile so the CARRYING
        /// state is in the picture too.
        /// </summary>
        public void PreviewLoadout(LevelDefinitionSO level, RosterDefinitionSO roster,
                                   ConsumableType? carrying = null, bool testSupply = false,
                                   FactionDefinitionSO faction = null, CosmeticSet? wearing = null)
        {
            var picks = Loadout.Default(level, roster, ProgressStore.IsUnitUnlocked);
            ShowLoadout(level, roster, picks, testSupply, (_, __) => { }, faction);
            if (carrying is ConsumableType type) TapConsumable(type);
            if (wearing is CosmeticSet set) TapCamo(set);
        }

        /// <summary>
        /// Whether the picker is OFFERING this class: its + stepper is on screen and a pick of
        /// one would be accepted. Preview/self-test surface only.
        ///
        /// Both halves are asked because the unlock state is read in two independent places — the
        /// row build decides whether to draw steppers or a buy button, and `Loadout.IsLegal`
        /// decides whether an edit is allowed. A change that fixes one and misses the other gives
        /// a class you can see but cannot field, or one you can field but cannot see.
        ///
        /// The legality half is asked with a pick of EXACTLY ONE of this class and nothing else,
        /// so the answer is about the LOCK and not about the level's point budget. Asking it
        /// against the standing 8/8 squad measured the budget instead and reported a locked class
        /// as blocked when it was merely unaffordable — which is what the first version of this
        /// did.
        /// </summary>
        public bool PreviewOffersClass(UnitDefinitionSO unit)
        {
            if (loadoutRoster == null || unit == null) return false;
            for (int i = 0; i < loadoutRows.Length && i < loadoutRoster.slots.Count; i++)
            {
                if (loadoutRoster.slots[i].unit != unit) continue;
                bool stepperShown = loadoutRows[i].Plus.activeSelf;
                bool oneIsLegal = Loadout.IsLegal(new List<Pick> { new Pick(unit, 1) },
                                                  loadoutLevel, loadoutRoster, UnitUnlocked);
                return stepperShown && oneIsLegal;
            }
            return false;
        }

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
            safeArea = safe.gameObject;
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

        /// <summary>
        /// Two channels, and the difference between them is the whole of pillar 7.
        ///
        /// The BANNER is a flash: something just happened ("Their heavies are here!"). The
        /// TELEGRAPH STRIP is a standing condition: something is ABOUT to happen, and it stays on
        /// screen for the entire turn the player is being warned about. A warning that fades has
        /// blindsided anyone who looked away, which is the thing the pillar exists to prevent.
        ///
        /// Both sit under the coin pill and above the battlefield, clear of the safe-area insets.
        /// </summary>
        void BuildEventBanner()
        {
            var safe = (RectTransform)transform.Find("SafeArea");

            var strip = NewRect("Telegraph", safe);
            strip.anchorMin = strip.anchorMax = new Vector2(0.5f, 1f);
            strip.pivot = new Vector2(0.5f, 1f);
            // BELOW the event banner, not above it. At -104 the strip ran straight through the
            // CAM / RIGS / stepper cluster and the level readout — harmless for input, since it
            // does not take raycasts, but it read as a broken layout.
            strip.anchoredPosition = new Vector2(0f, -286f);
            strip.sizeDelta = new Vector2(760f, 62f);
            var stripBg = strip.gameObject.AddComponent<Image>();
            stripBg.color = new Color(0.55f, 0.16f, 0.10f, 0.85f);
            stripBg.raycastTarget = false;
            telegraphStrip = strip.gameObject;

            telegraphText = NewText("Text", strip, 34f, new Color(1f, 0.90f, 0.80f),
                                    TextAlignmentOptions.Center);
            Stretch(telegraphText.rectTransform);
            telegraphStrip.SetActive(false);

            var banner = NewRect("EventBanner", safe);
            banner.anchorMin = banner.anchorMax = new Vector2(0.5f, 1f);
            banner.pivot = new Vector2(0.5f, 1f);
            banner.anchoredPosition = new Vector2(0f, -190f);
            banner.sizeDelta = new Vector2(860f, 84f);
            var bannerBg = banner.gameObject.AddComponent<Image>();
            bannerBg.color = new Color(0f, 0f, 0f, 0.62f);
            bannerBg.raycastTarget = false;
            eventBanner = banner.gameObject;

            eventText = NewText("Text", banner, 44f, new Color(1f, 0.86f, 0.35f),
                                TextAlignmentOptions.Center);
            Stretch(eventText.rectTransform);
            eventBanner.SetActive(false);
        }

        /// <summary>
        /// Drives both channels from the tick's state. Called every frame; does nothing unless
        /// something changed, so it costs a reference comparison in the common case.
        /// </summary>
        public void SetEvents(string announcement, string telegraph)
        {
            if (telegraph != shownTelegraph)
            {
                shownTelegraph = telegraph;
                bool on = !string.IsNullOrEmpty(telegraph);
                telegraphStrip.SetActive(on);
                if (on) telegraphText.text = telegraph;
            }

            if (announcement == shownAnnouncement) return;
            shownAnnouncement = announcement;
            bool show = !string.IsNullOrEmpty(announcement);
            eventBanner.SetActive(show);
            if (!show) return;
            eventText.text = announcement;
            if (bannerPop != null) StopCoroutine(bannerPop);
            if (isActiveAndEnabled) bannerPop = StartCoroutine(Pop(eventBanner.GetComponent<RectTransform>()));
        }

        void BuildLoadoutPanel()
        {
            var panel = NewRect("LoadoutPanel", transform);
            Stretch(panel);
            loadoutPanel = panel.gameObject;
            panel.gameObject.AddComponent<Image>().color = new Color(0.05f, 0.06f, 0.09f, 0.97f);

            loadoutTitle = NewText("Title", panel, 62f, Color.white, TextAlignmentOptions.Center);
            loadoutTitle.rectTransform.anchorMin = loadoutTitle.rectTransform.anchorMax
                = new Vector2(0.5f, 1f);
            loadoutTitle.rectTransform.pivot = new Vector2(0.5f, 1f);
            loadoutTitle.rectTransform.anchoredPosition = new Vector2(0f, -172f);
            loadoutTitle.rectTransform.sizeDelta = new Vector2(1000f, 86f);

            // WHO you are fighting, under the level's own name — Tier 2.1. The stack above the
            // roster rows is title / enemy / troops / balance, and it is sized to end exactly on
            // LoadoutRowTop: everything here is positioned from the panel's TOP, so a line added
            // in the middle of it moves the three below it and nothing else.
            loadoutFaction = NewText("Faction", panel, 34f, Color.white, TextAlignmentOptions.Center);
            loadoutFaction.rectTransform.anchorMin = loadoutFaction.rectTransform.anchorMax
                = new Vector2(0.5f, 1f);
            loadoutFaction.rectTransform.pivot = new Vector2(0.5f, 1f);
            loadoutFaction.rectTransform.anchoredPosition = new Vector2(0f, -256f);
            loadoutFaction.rectTransform.sizeDelta = new Vector2(1000f, 44f);

            loadoutSummary = NewText("Summary", panel, 40f, Gold, TextAlignmentOptions.Center);
            loadoutSummary.rectTransform.anchorMin = loadoutSummary.rectTransform.anchorMax
                = new Vector2(0.5f, 1f);
            loadoutSummary.rectTransform.pivot = new Vector2(0.5f, 1f);
            loadoutSummary.rectTransform.anchoredPosition = new Vector2(0f, -304f);
            loadoutSummary.rectTransform.sizeDelta = new Vector2(1000f, 56f);

            var bal = NewRect("Balance", panel);
            bal.anchorMin = bal.anchorMax = new Vector2(0.5f, 1f);
            bal.pivot = new Vector2(0.5f, 1f);
            bal.anchoredPosition = new Vector2(0f, -366f);
            bal.sizeDelta = new Vector2(260f, 64f);
            var coin = NewRect("Coin", bal);
            coin.anchorMin = coin.anchorMax = new Vector2(0f, 0.5f);
            coin.pivot = new Vector2(0f, 0.5f);
            coin.anchoredPosition = new Vector2(30f, 0f);
            coin.sizeDelta = new Vector2(38f, 38f);
            var ci = coin.gameObject.AddComponent<Image>();
            ci.sprite = CoinSprite(); ci.color = Gold; ci.raycastTarget = false;
            loadoutBalance = NewText("Text", bal, 38f, Gold, TextAlignmentOptions.Left);
            Stretch(loadoutBalance.rectTransform);
            loadoutBalance.margin = new Vector4(84f, 0f, 0f, 0f);

            // One row per pickable unit, laid out top-down. Six of them, so a scroll view would
            // be more machinery than the content needs.
            loadoutRows = new LoadoutRow[8];
            for (int i = 0; i < loadoutRows.Length; i++)
                loadoutRows[i] = BuildLoadoutRow(panel, -430f - i * 168f, i);

            BuildConsumableStrip(panel);
            BuildCamoStrip(panel);

            beginButton = NewButton("Begin", panel, new Vector2(0f, -BeginButtonY), "BEGIN",
                                    new Color(0.16f, 0.42f, 0.24f), OnBeginPressed, out _);
            var brt = (RectTransform)beginButton.transform;
            brt.sizeDelta = new Vector2(560f, 140f);

            loadoutPanel.SetActive(false);
        }

        /// <summary>
        /// Where the consumable strip's tiles start, measured down from the top of the panel, and
        /// how tall they are. Public so `PortSelfTest` can assert the strip clears both the roster
        /// rows above it and BEGIN below it.
        ///
        /// **This layout is the one the Kotlin got wrong.** Adding a consumables section there
        /// pushed Confirm past the bottom of the screen — not clipped, ABSENT from the tree and
        /// unreachable by any input, on a locked device with no way to start a battle. Everything
        /// here is positioned from the panel's own top rather than stacked after the roster rows,
        /// so a longer roster can never push this section anywhere.
        /// </summary>
        public const float ConsumableStripY = 1520f;
        public const float ConsumableStripHeight = 150f;
        public const float ConsumableHeaderY = 1462f;
        /// <summary>The CAMO strip (Tier 2.4), between the consumables and BEGIN. Adding it moved
        /// BEGIN down, which is the move the Kotlin failed to make — everything here is measured
        /// from the panel's top and pinned by PortSelfTest, so the button cannot be pushed off the
        /// bottom of the tree by a section added above it.</summary>
        public const float CamoHeaderY = 1700f;
        public const float CamoStripY = 1756f;
        public const float CamoStripHeight = 128f;
        public const float BeginButtonY = 1930f;
        /// <summary>Row 0's top, the per-row pitch and a row's height — see BuildLoadoutPanel.</summary>
        public const float LoadoutRowTop = 430f;
        public const float LoadoutRowPitch = 168f;
        public const float LoadoutRowHeight = 152f;

        void BuildConsumableStrip(RectTransform panel)
        {
            var header = NewText("ConsumablesHeader", panel, 30f, new Color(0.66f, 0.69f, 0.74f),
                                 TextAlignmentOptions.Center);
            header.rectTransform.anchorMin = header.rectTransform.anchorMax = new Vector2(0.5f, 1f);
            header.rectTransform.pivot = new Vector2(0.5f, 1f);
            header.rectTransform.anchoredPosition = new Vector2(0f, -ConsumableHeaderY);
            header.rectTransform.sizeDelta = new Vector2(1000f, 44f);
            consumableHeader = header;
            header.text = $"Consumables — carry up to {Consumables.MaxEquippedPerBattle}";

            var items = Consumables.All;
            consumableTiles = new ConsumableTile[items.Count];
            const float Gap = 14f;
            float w = (1000f - Gap * (items.Count - 1)) / items.Count;

            for (int i = 0; i < items.Count; i++)
            {
                var def = items[i];
                var tile = NewButton($"Consumable{i}", panel, Vector2.zero, "",
                                     new Color(0.16f, 0.17f, 0.21f), () => TapConsumable(def.Type),
                                     out var label);
                var rt = (RectTransform)tile.transform;
                rt.sizeDelta = new Vector2(w, ConsumableStripHeight);
                rt.anchoredPosition = new Vector2(-500f + w / 2f + i * (w + Gap),
                                                  -ConsumableStripY);
                label.fontSize = 26f;
                label.textWrappingMode = TextWrappingModes.Normal;

                consumableTiles[i] = new ConsumableTile
                {
                    Root = tile, Image = tile.GetComponent<Image>(), Label = label, Type = def.Type,
                };
            }
        }

        /// <summary>
        /// The camo shop — Tier 2.4. Four tiles: buy once, then select freely for ever.
        ///
        /// Each tile carries a SWATCH of the actual uniform colour, because the name of a camo set
        /// is not the thing being bought. Olive's swatch is the player prefab's own tone, restated
        /// here as a constant — the catalog stores null for it (it repaints to the build-time
        /// material rather than to a clone), and a tile with no swatch would be the one set you
        /// cannot see before choosing.
        /// </summary>
        void BuildCamoStrip(RectTransform panel)
        {
            var header = NewText("CamoHeader", panel, 30f, new Color(0.66f, 0.69f, 0.74f),
                                 TextAlignmentOptions.Center);
            header.rectTransform.anchorMin = header.rectTransform.anchorMax = new Vector2(0.5f, 1f);
            header.rectTransform.pivot = new Vector2(0.5f, 1f);
            header.rectTransform.anchoredPosition = new Vector2(0f, -CamoHeaderY);
            header.rectTransform.sizeDelta = new Vector2(1000f, 44f);
            camoHeader = header;

            var sets = Cosmetics.All;
            camoTiles = new CamoTile[sets.Count];
            const float Gap = 14f;
            float w = (1000f - Gap * (sets.Count - 1)) / sets.Count;

            for (int i = 0; i < sets.Count; i++)
            {
                var camo = sets[i];
                var tile = NewButton($"Camo{i}", panel, Vector2.zero, "",
                                     new Color(0.16f, 0.17f, 0.21f), () => TapCamo(camo.Set),
                                     out var label);
                var rt = (RectTransform)tile.transform;
                rt.sizeDelta = new Vector2(w, CamoStripHeight);
                rt.anchoredPosition = new Vector2(-500f + w / 2f + i * (w + Gap), -CamoStripY);
                label.fontSize = 24f;
                label.textWrappingMode = TextWrappingModes.Normal;
                // Below the swatch, not over it — text on a pale Arctic swatch is unreadable and
                // text on a dark one hides the colour being sold.
                label.margin = new Vector4(0f, 44f, 0f, 0f);

                var swatch = NewRect("Swatch", rt);
                swatch.anchorMin = swatch.anchorMax = new Vector2(0.5f, 1f);
                swatch.pivot = new Vector2(0.5f, 1f);
                swatch.anchoredPosition = new Vector2(0f, -10f);
                swatch.sizeDelta = new Vector2(w - 36f, 30f);
                var img = swatch.gameObject.AddComponent<Image>();
                img.color = camo.UniformColor ?? OliveSwatch;
                img.raycastTarget = false;

                camoTiles[i] = new CamoTile
                {
                    Root = tile, Image = tile.GetComponent<Image>(), Label = label, Set = camo.Set,
                };
            }
        }

        /// <summary>PlayerUniform.mat's own tone. Olive is the only set whose colour is not in the
        /// catalog, because it repaints to the material rather than to a clone of it.</summary>
        static readonly Color OliveSwatch = new Color(0.30f, 0.40f, 0.24f);

        class CamoTile
        {
            public GameObject Root;
            public Image Image;
            public TMP_Text Label;
            public CosmeticSet Set;
        }

        /// <summary>
        /// Buy it if it is not owned, wear it if it is. The same one-tap-two-meanings the ammo
        /// selector and the consumable tiles already use.
        ///
        /// Buying DOES also select here, unlike a consumable — a camo has no cap to spend and no
        /// reason to be owned and not worn, so making the player tap twice would be ceremony. The
        /// selection is persisted immediately rather than at BEGIN: it is a standing preference,
        /// like the ammo choice, and it survives backing out of the picker.
        /// </summary>
        void TapCamo(CosmeticSet set)
        {
            var camo = Cosmetics.For(set);
            if (camo == null) return;

            // TEST SUPPLY: wear it, buy nothing, store nothing. Not "grant it and then select it"
            // — that would write an unlock, which is the one thing this must never do.
            if (testSupply)
            {
                Cosmetics.TestOverride = set;
                RefreshLoadout();
                return;
            }

            if (!ProgressStore.IsCosmeticUnlocked(set))
            {
                if (!EconomyStore.PurchaseCosmetic(new CosmeticDefinition
                    { Set = set, CoinPrice = camo.CoinPrice })) { RefreshLoadout(); return; }
                SetCoins(EconomyStore.Balance());
            }
            ProgressStore.SetSelectedCosmetic(set);
            RefreshLoadout();
        }

        void RefreshCamo()
        {
            if (camoTiles == null) return;
            if (camoHeader != null)
                camoHeader.text = "Your camo — vanity only, no effect in battle"
                                + (testSupply ? "   [TEST SUPPLY — RIGS]" : "");

            var selected = Cosmetics.SelectedSet();
            foreach (var tile in camoTiles)
            {
                var camo = Cosmetics.For(tile.Set);
                bool owned = testSupply || ProgressStore.IsCosmeticUnlocked(tile.Set);
                bool worn = selected == tile.Set;

                // SAY SO ON SCREEN, as the consumable strip does: a free-wardrobe mode that looks
                // identical to the real one is how a "confirmed on device" result gets recorded
                // against a state the player can never be in.
                tile.Label.text = worn ? $"{camo.DisplayName}\nWORN"
                                : testSupply ? $"{camo.DisplayName}\nFREE"
                                : owned ? $"{camo.DisplayName}\nOwned"
                                : $"{camo.DisplayName}\n{camo.CoinPrice}c";
                tile.Label.color = worn ? Gold
                                 : owned ? Body
                                 : ProgressStore.Coins() >= camo.CoinPrice
                                     ? new Color(0.78f, 0.72f, 0.55f)
                                     : new Color(0.45f, 0.46f, 0.5f);
                tile.Image.color = worn ? new Color(0.26f, 0.30f, 0.20f)
                                        : new Color(0.16f, 0.17f, 0.21f);
            }
        }

        class ConsumableTile
        {
            public GameObject Root;
            public Image Image;
            public TMP_Text Label;
            public ConsumableType Type;
        }

        /// <summary>
        /// One tap, two meanings — buy it if you have none, otherwise carry it or put it back.
        ///
        /// The same double duty the ammo selector already does, and for the same reason: the
        /// alternative is a separate buy affordance on a tile this size, and there is no room for
        /// one that a thumb could hit. Buying does NOT also equip: the cap is two, so an automatic
        /// equip would silently spend a slot the player may want elsewhere.
        /// </summary>
        void TapConsumable(ConsumableType type)
        {
            var def = Consumables.For(type);
            if (def == null) return;

            // TEST SUPPLY: skip the shop entirely. Not "grant one and then buy it" — that would
            // write to the inventory, which is the one thing this must never do.
            int owned = testSupply ? 1 : ProgressStore.OwnedConsumables(type);
            if (owned <= 0)
            {
                if (EconomyStore.PurchaseConsumable(new ConsumableDefinition
                    { Type = type, CoinPrice = def.CoinPrice })) SetCoins(EconomyStore.Balance());
                RefreshLoadout();
                return;
            }

            loadoutConsumables.TryGetValue(type, out int equipped);
            if (equipped > 0)
            {
                loadoutConsumables.Remove(type);
            }
            else if (Consumables.TotalEquipped(loadoutConsumables) < Consumables.MaxEquippedPerBattle)
            {
                loadoutConsumables[type] = 1;
            }
            // Over the cap the tap is REFUSED rather than swapping something out — dropping
            // somebody else's pick to make room is the same decision-stealing the roster rows
            // already refuse to make.
            RefreshLoadout();
        }

        void RefreshConsumables()
        {
            if (consumableTiles == null) return;

            // SAY SO ON SCREEN. A free-consumables mode that looks identical to the real one is how
            // a "confirmed on device" result gets recorded against a state the player can never be
            // in — the whole point of testing on the release build is that it is the real thing.
            if (consumableHeader != null)
                consumableHeader.text =
                    $"Consumables — carry up to {Consumables.MaxEquippedPerBattle}"
                    + (testSupply ? "   [TEST SUPPLY — RIGS]" : "");

            foreach (var tile in consumableTiles)
            {
                var def = Consumables.For(tile.Type);
                int owned = testSupply ? 1 : ProgressStore.OwnedConsumables(tile.Type);
                loadoutConsumables.TryGetValue(tile.Type, out int equipped);

                // Three states, and each one has to be readable at arm's length: not owned (price),
                // owned and left behind (how many you have), owned and coming with you (gold).
                tile.Label.text = owned <= 0
                    ? $"{def.DisplayName}\n{def.CoinPrice}c"
                    : equipped > 0 ? $"{def.DisplayName}\nCARRYING"
                    : testSupply ? $"{def.DisplayName}\nFREE"
                    : $"{def.DisplayName}\nx{owned}";

                tile.Label.color = equipped > 0 ? Gold
                                 : owned > 0 ? Body
                                 : ProgressStore.Coins() >= def.CoinPrice
                                     ? new Color(0.78f, 0.72f, 0.55f)
                                     : new Color(0.45f, 0.46f, 0.5f);
                tile.Image.color = equipped > 0 ? new Color(0.26f, 0.30f, 0.20f)
                                                : new Color(0.16f, 0.17f, 0.21f);
            }
        }

        LoadoutRow BuildLoadoutRow(RectTransform parent, float y, int index)
        {
            var row = NewRect($"Row{index}", parent);
            row.anchorMin = row.anchorMax = new Vector2(0.5f, 1f);
            row.pivot = new Vector2(0.5f, 1f);
            row.anchoredPosition = new Vector2(0f, y);
            row.sizeDelta = new Vector2(1000f, 152f);
            var bg = row.gameObject.AddComponent<Image>();
            bg.color = new Color(1f, 1f, 1f, 0.05f);
            bg.raycastTarget = false;

            var name = NewText("Name", row, 40f, Body, TextAlignmentOptions.TopLeft);
            Stretch(name.rectTransform);
            name.margin = new Vector4(28f, 14f, 392f, 0f);

            var line = NewText("Line", row, 28f, new Color(0.66f, 0.69f, 0.74f),
                               TextAlignmentOptions.TopLeft);
            Stretch(line.rectTransform);
            // Clear of the BUY button (306 wide, 40 from the right edge) — at 320 the one-liner
            // ran underneath it and the last words vanished.
            line.margin = new Vector4(28f, 62f, 392f, 10f);
            line.textWrappingMode = TextWrappingModes.Normal;

            var count = NewText("Count", row, 48f, Gold, TextAlignmentOptions.Center);
            count.rectTransform.anchorMin = count.rectTransform.anchorMax = new Vector2(1f, 0.5f);
            count.rectTransform.pivot = new Vector2(1f, 0.5f);
            count.rectTransform.anchoredPosition = new Vector2(-150f, 0f);
            count.rectTransform.sizeDelta = new Vector2(90f, 90f);

            var entry = loadoutRoster != null && index < loadoutRoster.slots.Count
                        ? loadoutRoster.slots[index] : null;

            var minus = NewButton("Minus", row, new Vector2(-250f, 0f), "-",
                                  new Color(0.22f, 0.22f, 0.26f), () => AdjustRow(index, -1), out _);
            var plus = NewButton("Plus", row, new Vector2(-40f, 0f), "+",
                                 new Color(0.22f, 0.30f, 0.24f), () => AdjustRow(index, +1), out _);
            foreach (var b in new[] { minus, plus })
            {
                var rt = (RectTransform)b.transform;
                rt.anchorMin = rt.anchorMax = new Vector2(1f, 0.5f);
                rt.pivot = new Vector2(1f, 0.5f);
                rt.sizeDelta = new Vector2(96f, 96f);
                rt.anchoredPosition = new Vector2(b == minus ? -250f : -40f, 0f);
            }

            var buy = NewButton("Buy", row, new Vector2(-40f, 0f), "BUY",
                                new Color(0.36f, 0.28f, 0.10f), () => BuyRow(index), out var buyLabel);
            var buyRt = (RectTransform)buy.transform;
            buyRt.anchorMin = buyRt.anchorMax = new Vector2(1f, 0.5f);
            buyRt.pivot = new Vector2(1f, 0.5f);
            buyRt.sizeDelta = new Vector2(306f, 96f);
            buyRt.anchoredPosition = new Vector2(-40f, 0f);
            buyLabel.fontSize = 32f;

            return new LoadoutRow
            {
                Root = row.gameObject, Name = name, Line = line, Count = count,
                Minus = minus, Plus = plus, Buy = buy, BuyLabel = buyLabel,
            };
        }

        // Index-based so the closures do not capture a roster that has not been assigned yet —
        // the rows are built once, before any level has been chosen.
        void AdjustRow(int index, int delta)
        {
            if (loadoutRoster == null || index >= loadoutRoster.slots.Count) return;
            Adjust(loadoutRoster.slots[index], delta);
        }

        void BuyRow(int index)
        {
            if (loadoutRoster == null || index >= loadoutRoster.slots.Count) return;
            Buy(loadoutRoster.slots[index]);
        }

        void OnBeginPressed()
        {
            if (!Loadout.IsLegal(loadoutPicks, loadoutLevel, loadoutRoster,
                                 UnitUnlocked)) return;
            HideLoadout();
            onLoadoutBegin?.Invoke(loadoutPicks, ConsumableActions.Equip(loadoutConsumables));
        }

        class LoadoutRow
        {
            public GameObject Root, Minus, Plus, Buy;
            public TMP_Text Name, Line, Count, BuyLabel;
        }

        // ================================================================================
        // The loadout picker
        // ================================================================================

        /// <summary>
        /// Raises the pre-battle picker. `onBegin` receives the chosen squad.
        ///
        /// The panel opens on the DEFAULT loadout already filled in and BEGIN already enabled —
        /// pillar 8, "default paths cost nothing". A player who taps straight through gets
        /// precisely the squad every level is balanced against, and never has to learn what a
        /// point is.
        /// </summary>
        public void ShowLoadout(LevelDefinitionSO level, RosterDefinitionSO roster,
                                List<Pick> picks, bool testSupply,
                                System.Action<List<Pick>, IReadOnlyDictionary<ConsumableType, int>> onBegin,
                                FactionDefinitionSO faction = null)
        {
            this.testSupply = testSupply;
            loadoutLevel = level;
            loadoutFactionDef = faction;
            loadoutRoster = roster;
            loadoutPicks = picks;
            onLoadoutBegin = onBegin;
            // The carry selection does NOT persist across levels. An item survives a battle it was
            // never used in — it is still in the inventory — but silently re-equipping it would
            // spend it on a level the player never chose it for.
            loadoutConsumables.Clear();
            loadoutPanel.SetActive(true);
            // The in-battle furniture belongs to a battle that has not started. The picker
            // carries its own balance, so leaving the pill up double-printed it and ghosted
            // through the panel's 97% fill.
            if (safeArea != null) safeArea.SetActive(false);
            RefreshLoadout();
        }

        public void HideLoadout()
        {
            loadoutPanel.SetActive(false);
            if (safeArea != null) safeArea.SetActive(true);
        }
        public bool LoadoutOpen => loadoutPanel != null && loadoutPanel.activeSelf;

        void RefreshLoadout()
        {
            int slots = Loadout.Slots(loadoutLevel);
            int budget = Loadout.Budget(loadoutLevel);
            int used = Loadout.UnitsUsed(loadoutPicks);
            int points = Loadout.PointsUsed(loadoutPicks, loadoutRoster);

            loadoutTitle.text = $"{loadoutLevel.displayName}";
            // Hidden rather than blanked when a level has no faction (every test rig, and any
            // stage still unpainted): an empty line reads as a layout hole, and the rows below are
            // anchored to the panel's top, so nothing moves either way.
            loadoutFaction.gameObject.SetActive(loadoutFactionDef != null);
            if (loadoutFactionDef != null)
            {
                loadoutFaction.text = $"Enemy: {loadoutFactionDef.displayName}";
                loadoutFaction.color = loadoutFactionDef.bannerColor;
            }
            loadoutSummary.text = $"{used}/{slots} troops     {points}/{budget} points"
                + (testSupply ? "   [TEST SUPPLY — RIGS]" : "");
            loadoutBalance.SetText("{0}", ProgressStore.Coins());

            for (int i = 0; i < loadoutRows.Length; i++)
            {
                var row = loadoutRows[i];
                if (i >= loadoutRoster.slots.Count) { row.Root.SetActive(false); continue; }
                row.Root.SetActive(true);

                var entry = loadoutRoster.slots[i];
                bool unlocked = UnitUnlocked(entry.unit.id);
                int count = loadoutPicks.FirstOrDefault(p => p.Unit == entry.unit).Count;

                row.Name.text = $"{entry.unit.displayName}  ({entry.pointCost}p)";
                row.Line.text = entry.oneLiner;
                row.Count.text = unlocked ? count.ToString() : "";

                // A LOCKED unit shows its price and stays visible — the "visible horizon" the
                // locks ask for. Hiding what you cannot afford removes the reason to earn coins.
                row.Buy.SetActive(!unlocked);
                row.Minus.SetActive(unlocked);
                row.Plus.SetActive(unlocked);
                row.BuyLabel.text = ProgressStore.Coins() >= entry.coinPrice
                    ? $"BUY {entry.coinPrice}" : $"{entry.coinPrice}";
                row.Name.color = unlocked ? Body : new Color(0.55f, 0.57f, 0.62f);
            }

            RefreshConsumables();
            RefreshCamo();

            bool legal = Loadout.IsLegal(loadoutPicks, loadoutLevel, loadoutRoster,
                                         UnitUnlocked);
            beginButton.GetComponent<Image>().color = legal
                ? new Color(0.16f, 0.42f, 0.24f) : new Color(0.22f, 0.22f, 0.24f);
        }

        void Adjust(RosterSlot entry, int delta)
        {
            int count = loadoutPicks.FirstOrDefault(p => p.Unit == entry.unit).Count;
            var next = loadoutPicks.Where(p => p.Unit != entry.unit).ToList();
            int wanted = Mathf.Max(0, count + delta);
            if (wanted > 0) next.Add(new Pick(entry.unit, wanted));

            // Refuse the edit rather than clamping it: silently dropping somebody else's trooper
            // to make room reads as the game taking a decision away.
            if (delta > 0 && !Loadout.IsLegal(next, loadoutLevel, loadoutRoster,
                                              UnitUnlocked)) return;
            if (delta < 0 && Loadout.UnitsUsed(next) == 0) return;

            loadoutPicks = next;
            RefreshLoadout();
        }

        void Buy(RosterSlot entry)
        {
            // Unreachable under RIGS — the row shows no buy button — but the contract is that a
            // test session writes NOTHING, and that is worth a guard rather than an inference.
            if (testSupply) return;
            if (!EconomyStore.PurchaseUnit(new RosterEntry
            {
                Unit = entry.unit, CoinPrice = entry.coinPrice,
                TierCosts = loadoutRoster.tierCosts,
            })) return;
            SetCoins(ProgressStore.Coins());
            RefreshLoadout();
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
