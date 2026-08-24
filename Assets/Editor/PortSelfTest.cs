using System.Collections.Generic;
using System.Linq;
using System.Text;
using TMPro;
using UnityEditor;
using UnityEngine;
using ArmedConflict.Game;
using ArmedConflict.Data;
using ArmedConflict.Render;

/// <summary>
/// Behavioural checks on the ported game/ modules. "It compiles" is not evidence a port is
/// faithful — these assert the properties the Kotlin originals were written to guarantee.
/// Run: -batchmode -quit -executeMethod PortSelfTest.Run
/// </summary>
public static class PortSelfTest
{
    static int failed;
    static readonly StringBuilder Log = new();

    static void Check(bool ok, string what)
    {
        if (!ok) failed++;
        if (what == null) return;          // loop-body assertions log only their summary line
        Log.AppendLine($"  [{(ok ? "ok  " : "FAIL")}] {what}");
    }

    static void Near(float a, float b, float eps, string what)
        => Check(Mathf.Abs(a - b) <= eps, $"{what} ({a:F5} vs {b:F5})");

    /// <summary>
    /// The incendiary FLAME — the cue that a burning unit is burning.
    ///
    /// Two separate things are checked, and the split matters. The flicker is arithmetic and can
    /// be asserted directly. The SHAPE cannot: a flame drawn on a quad is only a flame because of
    /// its alpha, and the failure mode this guards is precisely "the texture is wrong, so the
    /// game draws an orange RECTANGLE over every burning soldier" — which no test of the
    /// generator's inputs can see.
    ///
    /// So the shape checks ASK THE TEXTURE, per the standing rule, and they carry their own
    /// negative case: the same tests are run against a plain white square, which must FAIL every
    /// one of them. A check never seen to fail is not evidence, and a shape test that a bare quad
    /// also passes is testing nothing.
    /// </summary>
    static void CheckFlame()
    {
        // --- the flicker ------------------------------------------------------------------

        // A stable, well-spread phase per unit. Keyed on the unit's ID, so it must not move
        // between frames — a phase that re-rolled would make the whole field strobe.
        Check(CosmeticSystems.FlamePhase(17) == CosmeticSystems.FlamePhase(17),
              "a unit's flame phase is stable across calls");

        // The failure this guards is a LINE of soldiers flickering as one, or as a travelling wave
        // along the rank. Consecutive ids are the common case — a group spawns in a run — so it is
        // consecutive ids that have to come out scattered.
        //
        // Asserted as a DISTRIBUTION, not as a floor on the closest pair. A floor is the wrong
        // test and its first version failed for the right reason: among 40 random phases some pair
        // is almost certainly within a few hundredths of a radian, which is simply what randomness
        // looks like and is invisible in a crowd of thirty. What would actually be seen is
        // CLUSTERING — phases piling into one part of the cycle — or a linear ramp.
        const int Ids = 400, Octants = 8;
        var occupancy = new int[Octants];
        int nearSync = 0;
        for (int id = 0; id < Ids; id++)
        {
            float p = CosmeticSystems.FlamePhase(id);
            occupancy[Mathf.Min(Octants - 1, (int)(p / (Mathf.PI * 2f) * Octants))]++;
            if (Mathf.Abs(CosmeticSystems.FlamePhase(id + 1) - p) < 0.30f) nearSync++;
        }
        int thinnest = occupancy.Min();
        Check(thinnest > Ids / Octants / 2,
              $"flame phases cover the whole cycle rather than clustering " +
              $"(thinnest octant {thinnest} of a fair {Ids / Octants})");
        // A random pair falls within 0.30 rad about 9.5% of the time; a ramp or a shared phase
        // puts this near 100%. THIS is the check that catches a travelling wave, and the pair
        // above is not — proved by running both against `unitId * 0.1f`, which scored 394 of 400
        // here and a perfectly even 47-of-50 on occupancy. A ramp does not cluster; it marches.
        //
        // A third check lived here and was DELETED for failing exactly that test: it asserted the
        // spread between the largest and smallest neighbour gap, and a ramp sailed through it at
        // 6.08 rad because the wrap-around manufactures one enormous gap. A check that names a
        // failure it cannot detect is worse than none — it reads as coverage.
        Check(nearSync < Ids / 5,
              $"and neighbouring units rarely flicker together ({nearSync} of {Ids} pairs)");

        // Height and width swing in ANTIPHASE: the tongue narrows as it stretches. Swung
        // together, the whole flame zooms in and out and reads as a throbbing sticker.
        bool antiphase = true, outerMoves = false, tonguesDiffer = false;
        float minH = float.MaxValue, maxH = 0f;
        for (float t = 0f; t < 1f; t += 1f / 2000f)
        {
            var o = CosmeticSystems.FlameScale(t, 0f, false);
            var i = CosmeticSystems.FlameScale(t, 0f, true);
            if ((o.y - 1f) * (o.x - 1f) > 1e-6f) antiphase = false;
            if (Mathf.Abs(o.y - i.y) > 0.05f) tonguesDiffer = true;
            minH = Mathf.Min(minH, o.y);
            maxH = Mathf.Max(maxH, o.y);
        }
        outerMoves = maxH - minH > 0.1f;
        Check(antiphase, "a flame tongue NARROWS as it stretches, never both at once");
        Check(outerMoves, $"the flame actually flickers (height {minH:F2}..{maxH:F2})");
        Check(tonguesDiffer,
              "the two tongues are out of step, so the pair reads as fire and not as one shape");
        Near(maxH - 1f, CosmeticSystems.FlameHeightSwing, 1e-3f,
             "and it swings by the authored amount");

        // --- the shape, asked of the texture itself ----------------------------------------

        var flame = SpikeSceneBattle.FlameTexture();
        var square = new Texture2D(flame.width, flame.height, TextureFormat.RGBA32, false);
        var white = new Color[flame.width * flame.height];
        for (int i = 0; i < white.Length; i++) white[i] = Color.white;
        square.SetPixels(white);
        square.Apply();

        ShapeChecks(flame, true);
        ShapeChecks(square, false);
        Object.DestroyImmediate(flame);
        Object.DestroyImmediate(square);

        // BuildFlames finds the two tongues BY NAME, so those names are a contract between the
        // scene builder and the runtime. Break it and the pool logs an error on device and draws
        // nothing — which is the state this whole feature exists to leave behind.
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Flame.prefab");
        if (prefab == null)
            Log.AppendLine("  [ok  ] flame prefab not built yet — run SpikeSceneBattle.Build");
        else
            Check(prefab.transform.Find("outer") != null && prefab.transform.Find("inner") != null,
                  "the flame prefab carries the outer and inner tongues BattleRunner looks up");

        // `wanted` false means this is the NEGATIVE run: every assertion below must come out the
        // other way for a plain quad, and the check passes only if it does.
        void ShapeChecks(Texture2D tex, bool wanted)
        {
            string who = wanted ? "the flame texture" : "a plain white quad (negative case)";
            int w = tex.width, h = tex.height;

            // Row 0 is the flame's foot and row h-1 its tip — the generator runs bottom-up, which
            // the prefab's 180-degree X flip turns the right way up.
            float Width(int row)
            {
                int lit = 0;
                for (int x = 0; x < w; x++) if (tex.GetPixel(x, row).a > 0.5f) lit++;
                return lit / (float)w;
            }

            // A TAPER. This is the whole difference between a flame and a rectangle: the top must
            // be narrower than the bottom, and the very tip must be empty.
            bool tapers = Width(h / 4) > Width(h * 3 / 4) + 0.1f;
            Check(tapers == wanted,
                  $"{who}: {(wanted ? "tapers" : "does NOT taper")} toward the tip " +
                  $"({Width(h / 4):F2} -> {Width(h * 3 / 4):F2} of the quad)");

            bool emptyTip = Width(h - 1) < 0.02f;
            Check(emptyTip == wanted, $"{who}: the tip row is {(wanted ? "empty" : "solid")}");

            // A NECK. The widest point sits low but not at the very bottom — a flame pinches in
            // where it meets what it is burning.
            int widest = 0;
            for (int y = 1; y < h; y++) if (Width(y) > Width(widest)) widest = y;
            bool necked = widest > 0 && widest < h / 2 && Width(0) < Width(widest) - 0.05f;
            Check(necked == wanted,
                  $"{who}: {(wanted ? "necks in at the foot and is widest low" : "has no neck")} " +
                  $"(widest row {widest} of {h})");

            // HOT AT THE CORE. The colour ramp is what separates fire from an orange triangle, and
            // it is baked into the texture rather than tinted, so it is checkable here.
            var foot = tex.GetPixel(w / 2, 2);
            var tip = tex.GetPixel(w / 2, h * 4 / 5);
            bool hotCore = foot.g > tip.g + 0.15f;
            Check(hotCore == wanted,
                  $"{who}: the core is {(wanted ? "hotter than the tips" : "one flat colour")} " +
                  $"(green {foot.g:F2} vs {tip.g:F2})");

            // And it must not read as a TEAM COLOUR. Fire that looks like a faction tells the
            // player the wrong thing about who is burning.
            bool fireColoured = foot.r > 0.8f && foot.g > 0.5f && foot.b < 0.6f;
            Check(fireColoured == wanted,
                  $"{who}: {(wanted ? "reads as fire, not as a side's red or green" : "is neutral white")}");
        }
    }

    /// <summary>
    /// The CONSUMABLES — `PROGRESSION_DESIGN.md` Phase 2 and `DYNAMISM_DESIGN.md` Phase C, wired
    /// on 2026-08-10 after being found fully ported and reached by nothing (the sixth such system).
    ///
    /// Each check asserts what the PLAYER would notice — HP restored, men added, where the round
    /// lands, how wide their volley scatters — never that a multiplier is present. Related facts
    /// are asserted TOGETHER rather than one per line: a failure message that names three
    /// properties is as diagnostic as three checks, and the suite is read by people.
    /// </summary>
    /// <summary>
    /// ENEMY FACTIONS — Tier 2.1. The feature is "the army you are fighting LOOKS different on
    /// stage 2", so every check here asks about pixels-in-waiting (the colour a renderer ends up
    /// wearing) rather than about the data that chose it. Asserting `stage.faction != null` would
    /// be asserting the input, and this file has three separate entries about doing that.
    ///
    /// The repaint is exercised on the SHIPPED enemy prefab, and the case that matters is the
    /// SECOND paint: pools are built once and survive a level switch, so the failure to catch is a
    /// slot that keeps the previous stage's uniform. A single paint can never show it.
    /// </summary>
    /// <summary>
    /// How far apart two army colours read — brightness, red-vs-green and blue-vs-yellow as three
    /// separate axes.
    ///
    /// **The first version of this was a luma-weighted rgb distance and it was WRONG.** Luma
    /// weights blue at 0.11, so it scored steel blue-grey at 0.082 from the player's olive green
    /// and indicted a palette the Kotlin build shipped and played fine. Two tones of equal
    /// brightness and opposite HUE are trivially told apart, and hue is the axis both the faction
    /// and the camo features work in.
    ///
    /// It is a COARSE FLOOR against someone authoring two palettes that are genuinely the same
    /// colour, and nothing more — the device is the judge of whether an army reads.
    /// </summary>
    static float OpponentDistance(Color a, Color b)
    {
        float dl = (0.30f * a.r + 0.59f * a.g + 0.11f * a.b)
                 - (0.30f * b.r + 0.59f * b.g + 0.11f * b.b);
        float drg = (a.r - a.g) - (b.r - b.g);
        float dby = (0.5f * (a.r + a.g) - a.b) - (0.5f * (b.r + b.g) - b.b);
        return Mathf.Sqrt(dl * dl + drg * drg + dby * dby);
    }

    /// <summary>
    /// PLAYER CAMO — Tier 2.4. Vanity, so the checks are about two things only: that it changes
    /// nothing in the simulation, and that it never dresses your army as somebody else's.
    /// </summary>
    static void CheckCosmetics()
    {
        var sets = Cosmetics.All;
        var olive = Cosmetics.For(CosmeticSet.Olive);

        // Olive is free, is the default, and — the Kotlin's own device bug — is UNLOCKED without
        // ever being stored, exactly like Standard ammo. A local "is it in the unlocked set?" test
        // that misses that special case shows the free item as buyable, which is what shipped there.
        Check(sets.Count == 4 && sets.Select(c => c.Set).Distinct().Count() == 4
              && olive.CoinPrice == 0 && olive.UniformColor == null
              && ProgressStore.IsCosmeticUnlocked(CosmeticSet.Olive)
              && !ProgressStore.UnlockedCosmetics().Contains(CosmeticSet.Olive.ToString())
              && sets.Where(c => c.Set != CosmeticSet.Olive)
                     .All(c => c.CoinPrice > 0 && c.UniformColor != null && c.GearColor != null),
              $"four camo sets, Olive free and unlocked without being stored ({sets.Count})");

        // A SELECTION THE PLAYER DOES NOT OWN MUST FALL BACK, not render a set they never bought.
        // The check asserts its own precondition: with the set already unlocked in the editor's
        // prefs there is nothing to fall back FROM, and it would pass while testing nothing.
        {
            var stored = ProgressStore.SelectedCosmetic();
            bool reachable = !ProgressStore.IsCosmeticUnlocked(CosmeticSet.Arctic);
            ProgressStore.SetSelectedCosmetic(CosmeticSet.Arctic);
            var got = ProgressStore.SelectedCosmetic();
            ProgressStore.SetSelectedCosmetic(stored);
            Check(reachable && got == CosmeticSet.Olive,
                  $"an unowned selection falls back to Olive (got {got}, Arctic owned="
                  + $"{!reachable} — if it IS owned this check is testing nothing, clear the "
                  + "editor's PlayerPrefs)");
        }

        // RIGS TEST SUPPLY must dress the army and write NOTHING — the same bargain the consumable
        // supply strikes. Asserted against the store, not against the flag: the failure worth
        // catching is a "free" wardrobe that quietly unlocks or persists a set, which would
        // survive RIGS being switched off and corrupt a real save.
        {
            var storedSel = ProgressStore.SelectedCosmetic();
            var ownedBefore = ProgressStore.UnlockedCosmetics().ToList();
            Cosmetics.TestOverride = CosmeticSet.Arctic;
            bool worn = Cosmetics.Selected().Set == CosmeticSet.Arctic;
            bool wroteNothing = ProgressStore.SelectedCosmetic() == storedSel
                && !ProgressStore.IsCosmeticUnlocked(CosmeticSet.Arctic)
                && ProgressStore.UnlockedCosmetics().Count == ownedBefore.Count;
            Cosmetics.TestOverride = null;
            Check(worn && wroteNothing && Cosmetics.Selected().Set == storedSel,
                  $"the RIGS wardrobe wears a set it does not own and writes nothing "
                  + $"(worn={worn}, storeUntouched={wroteNothing}, back to {storedSel})");
        }

        // --- NO CAMO MAY READ AS THE ENEMY ------------------------------------------------------
        //
        // The one way this feature damages the game rather than merely looking dull. Two systems
        // now paint two armies, so every camo is measured against every faction AND against the
        // enemy's build-time red, which is what a level in no stage still fields.
        var factions = AssetDatabase.FindAssets("t:FactionDefinitionSO")
            .Select(g => AssetDatabase.LoadAssetAtPath<FactionDefinitionSO>(
                AssetDatabase.GUIDToAssetPath(g)))
            .Where(f => f != null).ToArray();
        var enemyMat = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/EnemyUniform.mat");
        var playerMat = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/PlayerUniform.mat");
        if (enemyMat == null || playerMat == null || factions.Length == 0)
        { Check(false, "the side materials and the faction assets"); return; }

        var enemyTones = factions.Select(f => (f.name, f.uniformColor))
                                 .Append(("the enemy default", enemyMat.color)).ToArray();
        const float Apart = 0.15f;
        float worst = float.MaxValue;
        string worstPair = "";
        foreach (var camo in sets)
        {
            var tone = camo.UniformColor ?? playerMat.color;
            foreach (var (name, enemyTone) in enemyTones)
            {
                float d = OpponentDistance(tone, enemyTone);
                if (d < worst) { worst = d; worstPair = $"{camo.DisplayName} vs {name}"; }
            }
        }
        Check(worst >= Apart,
              $"no camo reads as an enemy army — closest is {worstPair} at {worst:F3} "
              + $"(need {Apart})");

        // Camo sets must also be told apart from EACH OTHER, or the 400-coin one is a 400-coin
        // nothing. Measured against the whole set including Olive, which is what they are bought
        // as an alternative to.
        float closest = float.MaxValue;
        for (int i = 0; i < sets.Count; i++)
            for (int j = i + 1; j < sets.Count; j++)
                closest = Mathf.Min(closest, OpponentDistance(
                    sets[i].UniformColor ?? playerMat.color,
                    sets[j].UniformColor ?? playerMat.color));
        Check(closest >= Apart, $"every camo is distinct from every other ({closest:F3})");

        // --- VANITY MEANS VANITY ----------------------------------------------------------------
        //
        // Asserted on the OUTPUT of a real volley rather than on the absence of stat fields in the
        // Camo class: the same seed, the same drag, two different camo selections, and every
        // number the battle produces must match. A cosmetic that quietly reached a stat would show
        // up here as a different enemy HP total.
        var level = AssetDatabase.FindAssets("t:LevelDefinitionSO")
            .Select(g => AssetDatabase.LoadAssetAtPath<LevelDefinitionSO>(
                AssetDatabase.GUIDToAssetPath(g)))
            .Where(l => l != null && !l.isTestLevel)
            .OrderBy(l => l.levelNumber).FirstOrDefault();
        if (level != null)
        {
            // ARCTIC HAS TO BE OWNED FOR THIS TO TEST ANYTHING. SelectedCosmetic validates on
            // read, so selecting a set the player does not own silently returns Olive — and the
            // first version of this check compared Olive with Olive and passed against a build
            // where the camo really did buff damage 50%. Unlocked here, locked again below.
            var was = ProgressStore.SelectedCosmetic();
            bool ownedBefore = ProgressStore.IsCosmeticUnlocked(CosmeticSet.Arctic);
            ProgressStore.UnlockCosmetic(CosmeticSet.Arctic);
            var fresh = LevelBuilder.BuildInitialState(level, 1, 12, new System.Random(7));
            int hpBefore = fresh.EnemyUnits.Sum(u => u.Hp) + fresh.Structures.Sum(st => st.Hp);

            int[] Run(CosmeticSet set)
            {
                ProgressStore.SetSelectedCosmetic(set);
                var s = LevelBuilder.BuildInitialState(level, 1, 12, new System.Random(7))
                    with { Phase = GamePhase.Playing, TurnPhase = TurnPhase.Aiming,
                           TurnSide = TurnSide.Player };
                // This aim LANDS on L1, which is the whole requirement — a volley that misses
                // makes both runs identical for free, and the check would test nothing.
                s = BattleTick.FireVolley(s, new Vector3(6f, 6f, 0f), new System.Random(3), null);
                for (int i = 0; i < 600; i++) s = BattleTick.Step(s, 1f / 60f, level,
                                                                 new System.Random(5));
                return new[] { s.EnemyUnits.Sum(u => u.Hp), s.PlayerUnits.Sum(u => u.Hp),
                               s.Structures.Sum(st => st.Hp), s.EnemyUnits.Count };
            }
            var oliveRun = Run(CosmeticSet.Olive);
            var arcticRun = Run(CosmeticSet.Arctic);
            // Read back what the store actually holds — the fallback is silent, and "I asked for
            // Arctic" is not the same fact as "Arctic is what the run wore".
            bool reallyWorn = ProgressStore.SelectedCosmetic() == CosmeticSet.Arctic;
            if (!ownedBefore) ProgressStore.LockCosmetic(CosmeticSet.Arctic);
            ProgressStore.SetSelectedCosmetic(was);

            // THE VOLLEY MUST HAVE HURT SOMETHING, asserted as part of the condition. The first
            // version of this check compared two volleys that both missed: it passed against a
            // deliberately broken build where Arctic hit 15% harder, because 0 damage equals 0
            // damage. Same family as the airstrike check that had to assert its own aim was short.
            int hpAfter = oliveRun[0] + oliveRun[2];
            Check(hpAfter < hpBefore && reallyWorn && oliveRun.SequenceEqual(arcticRun),
                  $"a camo changes NOTHING a volley does — and the volley LANDED ({hpBefore} hp "
                  + $"-> {hpAfter}) while the camo was really WORN ({reallyWorn}); enemy "
                  + $"{oliveRun[0]}/{arcticRun[0]}, structures {oliveRun[2]}/{arcticRun[2]}, "
                  + $"survivors {oliveRun[3]}/{arcticRun[3]}");
        }

        // --- the repaint reaches the PLAYER's art, and Olive is a real destination ---------------
        var pGear = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/PlayerGear.mat");
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
            "Assets/Prefabs/PlayerUnit_unit_rifleman.prefab");
        if (prefab == null || pGear == null) { Check(false, "the player rifleman prefab"); return; }

        var soldier = Object.Instantiate(prefab);
        try
        {
            var wearsUniform = new List<Renderer>();
            var wearsGear = new List<Renderer>();
            FactionPaint.Classify(new[] { soldier }, playerMat, pGear, wearsUniform, wearsGear);
            var desert = Cosmetics.For(CosmeticSet.Desert);

            FactionPaint.Apply(wearsUniform, wearsGear,
                               FactionPaint.Recolour(playerMat, desert.UniformColor.Value),
                               FactionPaint.Recolour(pGear, desert.GearColor.Value));
            bool painted = wearsUniform.Count > 0
                && OpponentDistance(wearsUniform[0].sharedMaterial.color,
                                    desert.UniformColor.Value) < 1e-3f;

            // Olive repaints back to the build-time ASSETS themselves, not to a clone that
            // matches them — a default you can return to has to be a real destination, and this
            // is the half a "paint it once" implementation gets wrong.
            FactionPaint.Apply(wearsUniform, wearsGear, playerMat, pGear);
            bool home = wearsUniform[0].sharedMaterial == playerMat
                     && wearsGear[0].sharedMaterial == pGear;

            Check(painted && home,
                  $"the player's rifleman wears his camo and comes home to Olive — "
                  + $"{wearsUniform.Count} uniform / {wearsGear.Count} gear renderers, "
                  + $"desert {(painted ? "ok" : "NO")}, olive {(home ? "ok" : "NO")}");
        }
        finally
        {
            Object.DestroyImmediate(soldier);
        }
    }

    static void CheckFactions()
    {
        var stages = AssetDatabase.FindAssets("t:StageDefinitionSO")
            .Select(g => AssetDatabase.LoadAssetAtPath<StageDefinitionSO>(
                AssetDatabase.GUIDToAssetPath(g)))
            .Where(s => s != null).ToArray();
        var levels = AssetDatabase.FindAssets("t:LevelDefinitionSO")
            .Select(g => AssetDatabase.LoadAssetAtPath<LevelDefinitionSO>(
                AssetDatabase.GUIDToAssetPath(g)))
            .Where(l => l != null).ToArray();
        if (stages.Length == 0 || levels.Length == 0) { Check(false, "stage and level assets"); return; }

        // --- who wears what -------------------------------------------------------------------
        //
        // Both halves in one check. A campaign level with no faction renders the default red while
        // its neighbours do not, which reads as an art bug; a RIG resolving to one would mean the
        // lookup is matching on something other than stage membership, since a rig is in no stage.
        var campaign = levels.Where(l => !l.isTestLevel).ToArray();
        var rigs = levels.Where(l => l.isTestLevel).ToArray();
        int painted = campaign.Count(l => Factions.For(l, stages) != null);
        int rigsPainted = rigs.Count(l => Factions.For(l, stages) != null);
        Check(painted == campaign.Length && rigsPainted == 0,
              $"every campaign level fields a faction and no rig does " +
              $"({painted}/{campaign.Length} campaign, {rigsPainted}/{rigs.Length} rigs)");

        // --- the colours are actually different armies ------------------------------------------
        //
        // The point of the feature is a stage you can tell apart AT A GLANCE, so the assertion is a
        // distance, not an inequality: two factions three hundredths apart would pass `!=` and look
        // identical on a phone.
        //
        // OPPONENT-COLOUR distance — brightness, red-vs-green and blue-vs-yellow as three separate
        // axes. The first version of this check was a LUMA-WEIGHTED rgb distance and it was wrong,
        // in the way this file keeps warning about: it weights blue at 0.11, so it scored steel
        // blue-grey as 0.082 from the player's olive green and indicted a palette the Kotlin build
        // shipped and played fine. Two tones of equal brightness and opposite HUE are trivially
        // told apart, and hue is the axis this whole feature works in. A metric invented in the
        // same hour as the thing it judges is not the artefact — the device is, and this is only a
        // coarse floor against someone authoring two palettes that are genuinely the same colour.
        //
        // The PLAYER's tone is read from the .mat asset rather than restated here, and it is in the
        // comparison because "the enemy now wears something close to your own green" is the one way
        // this feature breaks the GAME rather than merely looking dull.
        var Dist = (System.Func<Color, Color, float>)OpponentDistance;
        var playerMat = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/PlayerUniform.mat");
        var factions = stages.Select(s => s.faction).Where(f => f != null).Distinct().ToArray();
        const float Apart = 0.15f;
        float closestPair = float.MaxValue, closestToPlayer = float.MaxValue;
        for (int i = 0; i < factions.Length; i++)
        {
            if (playerMat != null)
                closestToPlayer = Mathf.Min(closestToPlayer,
                                            Dist(factions[i].uniformColor, playerMat.color));
            for (int j = i + 1; j < factions.Length; j++)
                closestPair = Mathf.Min(closestPair,
                                        Dist(factions[i].uniformColor, factions[j].uniformColor));
        }
        Check(factions.Length >= 2 && closestPair >= Apart && closestToPlayer >= Apart,
              $"{factions.Length} factions, each visibly its own army — closest pair {closestPair:F3}, " +
              $"closest to the PLAYER's green {closestToPlayer:F3} (need {Apart})");

        // Gear is the dark half of the two-tone. A faction authored with gear lighter than its
        // uniform inverts the silhouette's reading — the webbing stops being webbing.
        Check(factions.All(f => f.gearColor.grayscale < f.uniformColor.grayscale),
              "every faction's gear is darker than its uniform");

        // --- the repaint reaches the ART, and survives a recycled slot --------------------------
        var uniformMat = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/EnemyUniform.mat");
        var gearMat = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/EnemyGear.mat");
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
            "Assets/Prefabs/EnemyUnit_unit_rifleman.prefab");
        if (prefab == null || uniformMat == null || gearMat == null)
        {
            Check(false, "the shipped enemy rifleman prefab and its two side-materials");
            return;
        }

        var uniformWas = uniformMat.color;
        var gearWas = gearMat.color;
        var soldier = Object.Instantiate(prefab);
        try
        {
            var all = soldier.GetComponentsInChildren<Renderer>(true);
            var wearsUniform = new List<Renderer>();
            var wearsGear = new List<Renderer>();
            FactionPaint.Classify(new[] { soldier }, uniformMat, gearMat, wearsUniform, wearsGear);

            // The renderers a faction must NEVER touch: skin is shared flesh and the per-class TRIM
            // is what says which class a man is. Captured BEFORE any paint, by identity, so the
            // check is "these exact materials are still on these exact renderers" afterwards.
            var untouched = all.Where(r => !wearsUniform.Contains(r) && !wearsGear.Contains(r))
                               .Select(r => (r, r.sharedMaterial)).ToArray();
            Check(wearsUniform.Count > 0 && wearsGear.Count > 0 && untouched.Length > 0,
                  $"the rifleman splits into uniform / gear / neither " +
                  $"({wearsUniform.Count} / {wearsGear.Count} / {untouched.Length} renderers)");

            var red = factions.FirstOrDefault(f => f.id == "stage_valley") ?? factions[0];
            var steel = factions.FirstOrDefault(f => f != red) ?? factions[0];

            Color UniformNow() => wearsUniform[0].sharedMaterial.color;
            Color GearNow() => wearsGear[0].sharedMaterial.color;

            void Paint(FactionDefinitionSO f) => FactionPaint.Apply(
                wearsUniform, wearsGear,
                FactionPaint.Recolour(uniformMat, f.uniformColor),
                FactionPaint.Recolour(gearMat, f.gearColor));

            Paint(red);
            bool first = Dist(UniformNow(), red.uniformColor) < 1e-3f
                      && Dist(GearNow(), red.gearColor) < 1e-3f;
            Paint(steel);
            bool second = Dist(UniformNow(), steel.uniformColor) < 1e-3f
                       && Dist(GearNow(), steel.gearColor) < 1e-3f;
            // Back to the build-time pair, which is what a level in NO stage gets. A rig entered
            // after a campaign level is the third state, and the one a "paint it once at startup"
            // implementation gets wrong.
            FactionPaint.Apply(wearsUniform, wearsGear, uniformMat, gearMat);
            bool back = Dist(UniformNow(), uniformMat.color) < 1e-3f;

            Check(first && second && back,
                  $"a pooled soldier wears the stage he is IN, repaint after repaint — " +
                  $"{red.displayName} {(first ? "ok" : "NO")}, {steel.displayName} " +
                  $"{(second ? "ok" : "NO")}, then a rig's default {(back ? "ok" : "NO")}");

            Check(untouched.All(u => u.r.sharedMaterial == u.Item2),
                  "a faction repaint leaves SKIN and the per-class TRIM alone");

            // The source assets must come out of all that unchanged. Recolour clones for exactly
            // this reason: tinting in place would edit EnemyUniform.mat on disk, and every faction
            // in the game would then be whichever one was painted last — including in the build.
            Check(Dist(uniformMat.color, uniformWas) < 1e-4f && Dist(gearMat.color, gearWas) < 1e-4f,
                  $"the shared EnemyUniform/EnemyGear ASSETS come out of it unchanged " +
                  $"({uniformMat.color} was {uniformWas})");
        }
        finally
        {
            Object.DestroyImmediate(soldier);
        }
    }

    static void CheckConsumables()
    {
        var level = AssetDatabase.FindAssets("t:LevelDefinitionSO")
            .Select(g => AssetDatabase.LoadAssetAtPath<LevelDefinitionSO>(
                AssetDatabase.GUIDToAssetPath(g)))
            .Where(l => l != null && !l.isTestLevel)
            .OrderBy(l => l.levelNumber).FirstOrDefault();
        if (level == null) { Check(false, "a campaign level to test consumables on"); return; }

        var fresh = LevelBuilder.BuildInitialState(level, 1, 12, new System.Random(7))
            with { Phase = GamePhase.Playing, TurnPhase = TurnPhase.Aiming,
                   TurnSide = TurnSide.Player };
        GameState Carrying(ConsumableType t) => fresh with
            { LoadedConsumables = new Dictionary<ConsumableType, int> { { t, 1 } } };

        Check(Consumables.All.Count == 4 && Consumables.All.All(c => c.CoinPrice > 0)
              && Consumables.All.Select(c => c.Type).Distinct().Count() == 4,
              $"four consumables ship, each priced and listed once ({Consumables.All.Count})");

        // OVERWATCH FLARE MUST STAY OUT until something advances — it halves an advance budget,
        // and nothing in this port ever banks one. Both halves in one check: the day advancing
        // squads land, this goes red and adding the catalog entry is the fix.
        var advanced = fresh with { Phase = GamePhase.Playing };
        for (int i = 0; i < 120; i++)
            advanced = BattleTick.Step(advanced, 1f / 60f, level, new System.Random(2));
        Check(Consumables.For(ConsumableType.OverwatchFlare) == null
              && advanced.EnemyUnits.All(u => u.AdvanceRemaining == 0f),
              "Overwatch Flare is NOT sold, and nothing advances for it to halve");

        // --- TRAUMA KIT: the HP comes back, on the front rank only, clamped ------------------
        {
            var hurt = Carrying(ConsumableType.TraumaKit);
            hurt = hurt with { PlayerUnits = hurt.PlayerUnits
                .Select((u, i) => u with { Hp = i == 0 ? u.Definition.maxHp : 1 }).ToList() };
            var frontIds = hurt.PlayerUnits.Where(u => u.StandingOnStructureId == null)
                .OrderByDescending(u => u.X).Take(ConsumableActions.TraumaKitFrontRank)
                .Select(u => u.Id).ToHashSet();

            var healed = ConsumableActions.UseTraumaKit(hurt);
            int gained = healed.PlayerUnits.Sum(u => u.Hp) - hurt.PlayerUnits.Sum(u => u.Hp);
            Check(gained > 0
                  && healed.PlayerUnits.All(u => u.Hp <= u.Definition.maxHp)
                  && healed.PlayerUnits.Where(u => !frontIds.Contains(u.Id))
                           .All(u => u.Hp == hurt.PlayerUnits.First(h => h.Id == u.Id).Hp)
                  && Consumables.Equipped(healed, ConsumableType.TraumaKit) == 0,
                  $"the trauma kit heals the FRONT RANK only (+{gained}), clamps to max HP, "
                  + "and is consumed");

            // Every refusal in one place. Each returns the state UNCHANGED, so reference equality
            // is the test — and the states are built ONCE, because `x with {}` allocates and an
            // earlier version of this compared two fresh records and could never fail.
            var midVolley = hurt with { TurnPhase = TurnPhase.Resolving };
            var theirTurn = hurt with { TurnSide = TurnSide.Enemy };
            Check(ReferenceEquals(ConsumableActions.UseTraumaKit(fresh), fresh)
                  && ReferenceEquals(ConsumableActions.UseTraumaKit(midVolley), midVolley)
                  && ReferenceEquals(ConsumableActions.UseTraumaKit(theirTurn), theirTurn),
                  "an item does nothing when it is not carried, mid-volley, or on their turn");
        }

        // --- EARLY REINFORCEMENTS: men arrive, and they ARRIVE -------------------------------
        {
            var carrying = Carrying(ConsumableType.EarlyReinforcements);
            int before = carrying.PlayerUnits.Count;
            var called = ConsumableActions.UseEarlyReinforcements(carrying);
            int added = called.PlayerUnits.Count - before;

            Check(added == Mathf.CeilToInt(carrying.InitialPlayerCount
                                           * ConsumableActions.ReinforcementSizeFraction)
                  && called.ReinforcementsSent
                  && called.PlayerUnits.Skip(before).All(
                         u => u.Id >= ConsumableActions.ReinforcementIdBase
                              && u.MarchTargetX != null
                              && u.X < called.PlayerUnits.Take(before).Max(p => p.X)),
                  $"the relief squad is a quarter of the roster ({added}), enters from BEHIND the "
                  + "line in its own id band, and sets the one-per-battle flag");
            Check(ReferenceEquals(ConsumableActions.UseEarlyReinforcements(called), called),
                  "...so a second call does nothing, carried or not");

            // THE PART THAT MAKES IT WORTH BUYING: they have to reach their slots. Without the
            // march step they stand a formation's width behind the line, off the framed edge, for
            // the rest of the battle — bought, paid for and out of the fight. Clearing the target
            // also releases IsVisuallyIdle, which is a LATCH nothing else would clear.
            var slots = called.PlayerUnits.Skip(before)
                              .ToDictionary(u => u.Id, u => u.MarchTargetX.Value);
            var marching = called with { Projectiles = new List<ProjectileEntity>() };
            bool idleDuringMarch = marching.IsVisuallyIdle;
            for (int i = 0; i < 600 && !marching.IsVisuallyIdle; i++)
                marching = BattleTick.Step(marching, 1f / 60f, level, new System.Random(2));
            Check(!idleDuringMarch && !marching.PlayerMarchInProgress
                  && marching.PlayerUnits.Where(u => slots.ContainsKey(u.Id))
                             .All(u => u.MarchTargetX == null
                                       && Mathf.Abs(u.X - slots[u.Id]) < 1e-3f),
                  "every reinforcement runs in and stops on the exact slot it was sent to");

            // ...and keeps running once the battle is decided: that tick path re-frames onto the
            // survivors, so a man frozen mid-stride is on screen.
            var over = called with { Phase = GamePhase.Victory,
                                     Projectiles = new List<ProjectileEntity>() };
            Check(BattleTick.Step(over, 1f / 60f, level, new System.Random(2))
                      .PlayerUnits.Last().X > over.PlayerUnits.Last().X,
                  "a reinforcement keeps running after the battle is over");
        }

        // --- AIRSTRIKE: a round in the air, where the volley is going -------------------------
        {
            var armed = ConsumableActions.ToggleArmed(Carrying(ConsumableType.Airstrike),
                                                      ConsumableType.Airstrike);
            Check(armed.AirstrikeArmed
                  && Consumables.Equipped(armed, ConsumableType.Airstrike) == 1
                  && !ConsumableActions.ToggleArmed(fresh, ConsumableType.Airstrike).AirstrikeArmed,
                  "arming does NOT spend it — the HUD count must survive the tap, or the button "
                  + "vanishes the instant it is pressed (found on a device, in the Kotlin)");

            var aim = new Vector3(6f, 6f, 0f);
            var plain = BattleTick.FireVolley(fresh, aim, new System.Random(3));
            var struck = BattleTick.FireVolley(armed, aim, new System.Random(3));

            var muzzle = new Vector3(fresh.PlayerUnits.Average(u => u.X),
                                     fresh.PlayerUnits.Average(u => u.Y) + BattleTick.InfantryMuzzleY,
                                     0f);
            var volleyTarget = TrajectoryPhysics.LandingPoint(muzzle, aim);

            // AN ARMED RELEASE FLIES THE PLANE NOW AND HOLDS THE VOLLEY.
            // Enter over the player line; the infantry fire after it leaves.
            float playerLeft = fresh.PlayerUnits.Min(u => u.X);
            Check(struck.TurnPhase == TurnPhase.AirstrikeRun
                  && struck.Projectiles.Count(p => !p.IsStrafe && !p.IsAirstrike) == 0
                  && struck.AirstrikeSpawnDelay <= 0f
                  && struck.AirstrikePlane != null
                  && !struck.AirstrikePlane.HasDropped
                  && struck.AirstrikePlane.X < playerLeft
                  && !struck.AirstrikeArmed
                  && Consumables.Equipped(struck, ConsumableType.Airstrike) == 0,
                  $"an armed release flies the plane from the PLAYER LEFT and holds the volley "
                  + $"(spawn {struck.AirstrikePlane?.X:F2} vs line {playerLeft:F2})");
            float cap = BattleTick.AirstrikeCameraCap(struck.AirstrikePlane);
            float right = cap + CameraDirector.AirstrikeRunHalfWidth
                              + CameraDirector.FramePad
                              + BattleTick.PlaneExitOvershoot;
            Check(struck.AirstrikePlane.ExitX <= right + 0.05f
                  && struck.AirstrikePlane.ExitX > cap,
                  $"the plane leaves off the RIGHT of the held enemy frame "
                  + $"(exit {struck.AirstrikePlane.ExitX:F2}, cap {cap:F2}, right {right:F2})");

            // Camera SPRINGS with the plane. A cut to the enemy is the
            // old beat — the plane then appeared mid-frame. Start on
            // the player line (a real battle's pose) and demand the
            // camera is still there after one tick, then riding the
            // aircraft a third of a second later.
            {
                var aimed = struck with { CameraFollowX = fresh.PlayerCamXAnchor };
                var flying = BattleTick.Step(aimed, 1f / 60f, null, new System.Random(3));
                float cam0 = flying.CameraFollowX ?? 0f;
                Check(Mathf.Abs(cam0 - fresh.PlayerCamXAnchor) < 1.5f
                      && flying.AirstrikePlane != null
                      && flying.AirstrikePlane.X < cam0,
                      $"the plane ENTERS from the left of the player frame "
                      + $"(cam {cam0:F2}, spawn {flying.AirstrikePlane?.X:F2}, "
                      + $"player {fresh.PlayerCamXAnchor:F2}) — a cut to the enemy "
                      + "is how it used to appear mid-frame");

                // Wait until the plane has crossed the player line —
                // the first third of a second it is still LEFT of the
                // camera, so a "moved right" check would fail the
                // correct approach.
                var rode = flying;
                for (int i = 0; i < 90 && rode.AirstrikePlane != null
                     && rode.AirstrikePlane.X < fresh.PlayerCamXAnchor; i++)
                    rode = BattleTick.Step(rode, 1f / 60f, null, new System.Random(3));
                for (int i = 0; i < 20; i++)
                    rode = BattleTick.Step(rode, 1f / 60f, null, new System.Random(3));
                float cam1 = rode.CameraFollowX ?? 0f;
                float planeX = rode.AirstrikePlane?.X ?? 0f;
                Check(rode.AirstrikePlane != null
                      && cam1 > fresh.PlayerCamXAnchor + 0.4f
                      && Mathf.Abs(cam1 - (planeX + BattleTick.PlaneCameraBias)) < 2.5f,
                      $"the camera RIDES the plane ({cam0:F2} -> {cam1:F2}, plane {planeX:F2})");

                // Once the plane is past the enemy the camera HOLDS —
                // it does not chase it into empty ground.
                var held = rode;
                for (int i = 0; i < 180 && held.AirstrikePlane != null
                     && held.AirstrikePlane.X < BattleTick.AirstrikeCameraCap(held.AirstrikePlane) + 2f; i++)
                    held = BattleTick.Step(held, 1f / 60f, null, new System.Random(3));
                for (int i = 0; i < 20 && held.AirstrikePlane != null; i++)
                    held = BattleTick.Step(held, 1f / 60f, null, new System.Random(3));
                float camHold = held.CameraFollowX ?? 0f;
                float capNow = held.AirstrikePlane != null
                    ? BattleTick.AirstrikeCameraCap(held.AirstrikePlane)
                    : BattleTick.AirstrikeCameraCap(struck.AirstrikePlane);
                float planeHold = held.AirstrikePlane?.X ?? 0f;
                Check(camHold < capNow + 1.2f
                      && (held.AirstrikePlane == null || planeHold > capNow + 0.4f),
                      $"the camera STOPS at the enemy ({camHold:F2} vs cap {capNow:F2}, "
                      + $"plane {planeHold:F2}) — it does not chase off the right");
            }

            // ...and an UNARMED release is untouched by any of it. Without this the check above
            // would still pass if the airstrike path had eaten the ordinary volley.
            Check(plain.TurnPhase == TurnPhase.Resolving
                  && plain.Projectiles.Count > fresh.Projectiles.Count
                  && plain.AirstrikePlane == null
                  && plain.Projectiles.All(p => !p.IsAirstrike),
                  "an unarmed release still fires immediately, with no aircraft and no bomb");

            // Now fly it. Stepping the real tick rather than calling the private step directly, so
            // this exercises the path the game takes — including the phase gate that decides
            // whether the run advances at all.
            var run = struck;
            var strafeSeen = new List<ProjectileEntity>();
            ProjectileEntity bomb = null;
            float bombReleaseX = 0f;
            int guard = 0;
            while (run.TurnPhase == TurnPhase.AirstrikeRun && guard++ < 2000)
            {
                run = BattleTick.Step(run, 1f / 60f, null, new System.Random(3));
                // The burst is SHORT-LIVED — 0.30s — so by the time the run ends most of it has
                // already been culled. Collect as it goes, or the check below sees an empty list
                // and passes for the wrong reason.
                foreach (var p in run.Projectiles.Where(p => p.Id >= 45000))
                    if (!strafeSeen.Any(q => q.Id == p.Id)) strafeSeen.Add(p);

                var live = run.Projectiles.FirstOrDefault(p => p.IsAirstrike);
                if (live != null && bomb == null)
                {
                    bomb = live;
                    bombReleaseX = run.AirstrikePlane?.X ?? 0f;
                }
            }

            // KEEP STEPPING PAST THE HANDOVER. The rake covers the enemy position, which reaches
            // past the bomb's impact whenever the player aims short of the far end — so the last
            // rounds are fired AFTER the phase ends. Stopping collection at the handover, as this
            // loop used to, would count only the rounds fired before it and call a truncated burst
            // complete. That is the precise bug this restructure exists to make impossible, so the
            // check must be in a state where it could see it.
            var afterRun = run;
            for (int i = 0; i < 400 && afterRun.AirstrikePlane != null; i++)
            {
                afterRun = BattleTick.Step(afterRun, 1f / 60f, null, new System.Random(3));
                foreach (var p in afterRun.Projectiles.Where(p => p.Id >= 45000))
                    if (!strafeSeen.Any(q => q.Id == p.Id)) strafeSeen.Add(p);
            }

            // THE PLANE MUST STILL BE FLYING when the volley launches. Not a decorative detail:
            // there is a deliberate fallback that hands the turn over if the aircraft is ever
            // missing, and without this clause a run that NEVER detected its bomb landing would
            // still pass here — it would simply hand over later, when the plane left the frame.
            // That is exactly what happened on the negative run, so this clause exists because the
            // check it strengthens was caught passing against broken code.
            Check(bomb != null && run.TurnPhase == TurnPhase.Resolving && guard < 2000
                  && run.AirstrikePlane == null
                  && run.Projectiles.Any(p => !p.IsStrafe && !p.IsAirstrike && p.OwnerIsPlayer),
                  "the volley fires AFTER the plane has left, from the player's turn");

            if (bomb != null)
            {
                var origin = new Vector3(bomb.X, bomb.Y, bomb.Z);
                var velocity = new Vector3(bomb.Vx, bomb.Vy, bomb.Vz);

                // THE DROP LEAD IS THE THING THAT CAN SILENTLY MISS. The bomb inherits the
                // aircraft's forward speed, so it has to be released short of the target by
                // exactly speed * fall — and it must still LAND on the volley's own landing point,
                // which is the property the player actually experiences.
                Near(TrajectoryPhysics.LandingPoint(origin, velocity).x, volleyTarget.x, 0.15f,
                     "the bomb still lands on the volley's own landing point");
                Check(bomb.Vx > 0f
                      && bomb.SplashRadius > 0f
                      && bomb.Damage >= BattleTick.AirstrikeDamage
                      && Mathf.Abs(bombReleaseX
                                   - (volleyTarget.x - BattleTick.PlaneSpeed * BattleTick.BombFallTime))
                         < 0.25f,
                      "...released SHORT of the target by speed x fall, carrying the aircraft's "
                      + "forward speed, as a splash round of undiminished damage");

                // The fall is its own fixed time and legible means SECONDS. The absolute floor is
                // not decoration: asserted only against the constant that defines it, a 0.18s
                // value passed cleanly — that is this file's own self-referential-check trap.
                float fall = TrajectoryPhysics.FlightTime(origin, velocity);
                Check(Mathf.Abs(fall - BattleTick.BombFallTime) < 0.05f
                      && BattleTick.BombFallTime >= 0.8f,
                      $"the fall takes its own fixed time ({BattleTick.BombFallTime:F2}s, measured "
                      + $"{fall:F2}s) and legible means SECONDS, not frames");

                // The camera rides the plane, so the drop happens in
                // front of it — not in a held frame over the aim.
                Check(Mathf.Abs(bombReleaseX
                                - (volleyTarget.x - BattleTick.PlaneSpeed * BattleTick.BombFallTime))
                      < 0.25f,
                      "the drop still happens at the lead that puts the bomb on the aim");
            }

            // THE STRAFING BURST. Asserted as a WALK OF LANDING POINTS ending on the bomb's own
            // impact point, because that is the thing the player sees — a line of hits marching
            // into the target. Counting rounds, or checking a constant, would pass just as happily
            // on a burst that landed every round in the same spot or trailed off behind the plane.
            {
                var strafe = run.Projectiles.Concat(strafeSeen)
                                .Where(p => p.Id >= 45000)
                                .GroupBy(p => p.Id).Select(g => g.First())
                                .OrderBy(p => p.Id).ToList();

                var landings = strafe
                    .Select(p => TrajectoryPhysics.LandingPoint(
                        new Vector3(p.X, p.Y, p.Z), new Vector3(p.Vx, p.Vy, p.Vz)).x)
                    .ToList();

                bool walksForward = true;
                for (int i = 1; i < landings.Count; i++)
                    if (landings[i] <= landings[i - 1]) walksForward = false;

                // EVERY ROUND MUST ACTUALLY BE FIRED. The burst outlives its own phase now, and
                // when the firing loop lived inside that phase the surplus rounds were dropped in
                // silence — no error, no log, just a shorter burst. Counted against the constant
                // here on purpose: the failure is "fewer than asked for", so the number asked for
                // is exactly the right thing to compare against.
                Check(strafe.Count == BattleTick.StrafeRounds
                      && walksForward
                      && strafe.All(p => p.Damage == BattleTick.StrafeDamage
                                      && p.SplashRadius == 0f),
                      $"all {BattleTick.StrafeRounds} rounds are fired and walk FORWARD "
                      + $"({strafe.Count} seen, {(landings.Count > 0 ? landings[0] : 0):F2} -> "
                      + $"{(landings.Count > 0 ? landings[^1] : 0):F2})");

                // ...AND AGAIN FROM AN AIM THAT LANDS SHORT, which is the ONLY state where the
                // failure this guards is reachable.
                //
                // The check above uses an aim landing PAST the enemy's far edge, so the rake
                // finishes before the bomb does and confining the firing loop to the run's own
                // phase drops nothing. Run that way it passed against exactly the broken code it
                // was written for. Aim short — the ordinary case — and the last rounds are fired
                // after handover, which is when a phase-bound loop silently truncates the burst.
                // The landing is asserted to BE short, so the check cannot quietly stop testing
                // this if the geometry ever moves.
                {
                    var shortAim = new Vector3(3f, 7f, 0f);
                    var shortMuzzle = new Vector3(
                        fresh.PlayerUnits.Average(u => u.X),
                        fresh.PlayerUnits.Average(u => u.Y) + BattleTick.InfantryMuzzleY, 0f);
                    float shortTargetX =
                        TrajectoryPhysics.LandingPoint(shortMuzzle, shortAim).x;

                    var shortRun = BattleTick.FireVolley(armed, shortAim, new System.Random(3));
                    var shortSeen = new List<ProjectileEntity>();
                    for (int i = 0; i < 900 && shortRun.AirstrikePlane != null; i++)
                    {
                        shortRun = BattleTick.Step(shortRun, 1f / 60f, null, new System.Random(3));
                        foreach (var p in shortRun.Projectiles.Where(p => p.Id >= 45000))
                            if (!shortSeen.Any(q => q.Id == p.Id)) shortSeen.Add(p);
                    }

                    Check(shortTargetX < struck.AirstrikePlane.StrafeToX
                          && shortSeen.Count == BattleTick.StrafeRounds,
                          $"a shot aimed SHORT of the enemy's far edge still fires the whole burst "
                          + $"({shortSeen.Count}/{BattleTick.StrafeRounds}) — the rake outlives the "
                          + $"bomb (impact {shortTargetX:F2} vs rake end "
                          + $"{struck.AirstrikePlane.StrafeToX:F2}), so the guns cannot live in "
                          + "the run's phase");
                }

                // THE RAKE COVERS THE ENEMY POSITION, STRUCTURES INCLUDED. This is the contract
                // Rob asked for — "it should start from the left, strafe should cover the whole
                // enemy position and its structures" — and it is asserted against the enemy's own
                // extents rather than against the constants that produce them.
                //
                // Structure EDGES, not centres: an outpost is 2 units wide and raking to its
                // middle leaves half the building untouched, which is most of the way back to the
                // bug this replaced.
                var enemyEdges = fresh.EnemyUnits.Select(u => u.X).ToList();
                foreach (var st in fresh.Structures.Where(st => !st.Definition.isPlayerSide))
                {
                    float hw = (st.Definition.hasHitWidth ? st.Definition.hitWidth
                                                          : st.Definition.size) / 2f;
                    enemyEdges.Add(st.X - hw);
                    enemyEdges.Add(st.X + hw);
                }

                Check(landings.Count > 0
                      && landings[0] < enemyEdges.Min()
                      && landings[^1] > enemyEdges.Max(),
                      $"the rake COVERS the whole enemy position including its structures — "
                      + $"impacts {landings[0]:F2} -> {landings[^1]:F2} against an enemy footprint "
                      + $"of {enemyEdges.Min():F2} -> {enemyEdges.Max():F2}");

                // AND IT IS INDEPENDENT OF WHERE THE PLAYER AIMED, which is the whole design
                // change. Asserted by FIRING IT TWICE AT DIFFERENT AIMS and demanding the same
                // walk — the one thing no arrangement of aim-relative constants can fake. A check
                // on "the walk covers the enemy" alone would pass on a burst that happened to
                // reach the enemy from a lucky aim.
                var elsewhere = BattleTick.FireVolley(armed, new Vector3(3f, 9f, 0f),
                                                      new System.Random(3));
                var otherAim = elsewhere.AirstrikePlane;
                Check(otherAim != null
                      && Mathf.Abs(otherAim.StrafeFromX - struck.AirstrikePlane.StrafeFromX) < 0.01f
                      && Mathf.Abs(otherAim.StrafeToX - struck.AirstrikePlane.StrafeToX) < 0.01f,
                      $"the rake is INDEPENDENT of the player's aim — a shot aimed elsewhere rakes "
                      + $"the identical ground ([{otherAim?.StrafeFromX:F2}, {otherAim?.StrafeToX:F2}] "
                      + $"vs [{struck.AirstrikePlane.StrafeFromX:F2}, "
                      + $"{struck.AirstrikePlane.StrafeToX:F2}])");

                // The firing POSITIONS are asserted too, because a walk of landing points can be
                // produced by rounds all fired from ONE spot: the loop fires every round whose
                // point the aircraft has already passed, so a spawn too far forward dumps them in
                // a single tick. That is a literal burst, and it is invisible in the landings.
                float landSpan = landings.Count > 0 ? landings[^1] - landings[0] : 0f;
                float fireSpan = strafe.Count > 0
                    ? strafe.Max(p => p.SpawnX) - strafe.Min(p => p.SpawnX) : 0f;

                Check(landSpan >= 5.5f && fireSpan >= 5f,
                      $"the burst RAKES rather than clustering — impacts span {landSpan:F2} units "
                      + $"and the rounds are fired from {fireSpan:F2} units of DIFFERENT "
                      + "positions, not one");

                int strafeTotal = strafe.Sum(p => p.Damage);
                Check(strafe.Count >= 12 && strafeTotal >= 24 && strafeTotal <= 32,
                      $"the burst is a strafing RUN, not a tap ({strafe.Count} rounds), and its "
                      + $"damage budget is unchanged ({strafeTotal}, held at 28) — count is "
                      + "presentation, total is balance, and raising one must not raise the other");

                // ...and they must not hold the beat open. The run hands over on the BOMB, so a
                // burst still in the air has to be irrelevant to that — which is the whole reason
                // the strafe carries its own id band.
                Check(strafe.All(p => p.Id < 40000 || p.Id >= 41000),
                      "strafe rounds sit clear of the bomb's id band, so a tracer still in the air "
                      + "cannot hold the volley back");

                // EXACTLY ONE ROUND IS THE BOMB. The bomb and the cannon rounds are both bullets
                // now — the grenade was too dark to follow — so the ONLY thing telling them apart
                // on screen is IsAirstrike, which the renderer scales by. Flag the burst too and
                // the player gets seven giant tracers and no payload; flag nothing and the payload
                // is a rifle round. Neither is visible to any test of the renderer, so it is
                // asserted here, on the flag itself.
                // Three shapes out of one pool and one prefab, and the flags are the whole of the
                // distinction: the payload is a big round dot (IsAirstrike), the cannon fire is a
                // stretched streak (IsStrafe), an infantry round is neither. The pair must be
                // MUTUALLY EXCLUSIVE — a round wearing both would be scaled by whichever branch
                // the renderer tested first, which is the kind of bug that only ever shows up as
                // "something looked odd once".
                Check(bomb != null && bomb.IsAirstrike && !bomb.IsStrafe
                      && bomb.Type == ProjectileType.Bullet
                      && strafe.All(p => p.IsStrafe && !p.IsAirstrike
                                         && p.Type == ProjectileType.Bullet)
                      && run.Projectiles.All(p => !(p.IsAirstrike && p.IsStrafe)),
                      "the BOMB alone is IsAirstrike and the CANNON FIRE alone is IsStrafe, never "
                      + "both — those two flags are all the renderer has to draw a payload, a "
                      + "tracer streak and a rifle round out of one pooled prefab");
            }

            // THE AIRCRAFT MUST LEAVE. Motion lives on the always-run
            // path so a phase change cannot freeze it mid-air.
            {
                var gone = struck;
                int flyGuard = 0;
                float x0 = gone.AirstrikePlane?.X ?? float.NaN;
                while (gone.AirstrikePlane != null && flyGuard++ < 2000)
                    gone = BattleTick.Step(gone, 1f / 60f, null, new System.Random(3));
                Check(!float.IsNaN(x0) && gone.AirstrikePlane == null && flyGuard < 2000,
                      $"the aircraft leaves the field ({x0:F2} -> gone in {flyGuard} ticks)");
            }

            // THE RUN MUST BE FRAMED WIDER THAN THE AIM. This is a regression guard on a bug the
            // device found in the first build of the beat: TurnPhase.AirstrikeRun fell through to
            // PhaseHalfWidth's default, took the AIMING framing — the tightest in the game — and a
            // 4.5-unit aircraft banked 45 degrees was clipped by the top of the frame for half its
            // pass. Asserted as camera DISTANCE, which is what the clipping is a function of,
            // rather than as "the switch has a case for it".
            {
                float aimingZ = CameraDirector.TargetZ(
                    CameraDirector.PhaseHalfWidth(TurnPhase.Aiming, TurnSide.Player,
                                                  3f, 3f, 3f, 0f, false, 3f, false)
                    + CameraDirector.FramePad,
                    false, 0f);
                float runZ = CameraDirector.TargetZ(
                    CameraDirector.PhaseHalfWidth(TurnPhase.AirstrikeRun, TurnSide.Player,
                                                  3f, 3f, 3f, 0f, false, 3f, false)
                    + CameraDirector.FramePad,
                    false, 0f);
                float wideRunZ = CameraDirector.TargetZ(
                    CameraDirector.PhaseHalfWidth(TurnPhase.AirstrikeRun, TurnSide.Player,
                                                  3f, 9f, 3f, 0f, false, 3f, false)
                    + CameraDirector.FramePad,
                    false, 0f);
                // 11 is the camZ PlanePreview judged the aircraft fits. The old
                // `>= 13` was 5.1+1.2 through TargetZ; FramePad shrank, the
                // relationship (run wider than aim, still clears 11) is the bar.
                Check(runZ > aimingZ + 2f && runZ > 11f && wideRunZ >= runZ,
                      $"the airstrike run is framed WIDER than the aim (camZ {runZ:F1} vs "
                      + $"{aimingZ:F1}), and a wide enemy cluster only pulls it further back — "
                      + "the tight framing clipped the aircraft off the top of the frame on device");
            }

            // The volley that finally launches is the one the player aimed, unchanged by having
            // waited. Compared against the unarmed volley fired from the same aim and seed.
            //
            // THE BURST IS EXCLUDED, and it did not used to have to be. The rake now carries past
            // the aim point, so its last rounds land AFTER the bomb does and are still in the air
            // when the phase hands over — where a count of "everything that is not the bomb" swept
            // them into the volley's total and this check went red. That is the check doing its
            // job: it noticed the burst outliving the run before anyone looked at a device.
            Check(run.PendingVolleyAim == null && run.PendingVolleyDelay <= 0f,
                  "no aim and no volley are left held once the run is over");

            // THE VOLLEY WAITS. Plane gone, camera home, then they fire.
            // Asked as times, not as the delay constant.
            {
                var t = struck with { CameraFollowX = fresh.PlayerCamXAnchor };
                float volleyAt = -1f;
                float camAtFire = float.NaN;
                bool planeGone = false;
                for (int i = 0; i < 1800 && volleyAt < 0f; i++)
                {
                    t = BattleTick.Step(t, 1f / 60f, null, new System.Random(3));
                    if (t.AirstrikePlane == null) planeGone = true;
                    bool volley2 = t.Projectiles.Any(p => !p.IsAirstrike && !p.IsStrafe
                                                       && p.OwnerIsPlayer);
                    if (volley2 && volleyAt < 0f)
                    {
                        volleyAt = (i + 1) / 60f;
                        camAtFire = t.CameraFollowX ?? 0f;
                    }
                }

                Check(planeGone && volleyAt > 0f
                      && Mathf.Abs(camAtFire - fresh.PlayerCamXAnchor) < 4.0f,
                      $"the volley fires AFTER the plane, from the player line "
                      + $"(t={volleyAt:F2}s, cam {camAtFire:F2} vs player "
                      + $"{fresh.PlayerCamXAnchor:F2})");
            }
        }

        // --- SMOKE SCREEN: their volley really does go wider -----------------------------------
        {
            // THE SPREAD OF WHERE THE ROUNDS ACTUALLY GO, over many volleys — not "the multiplier
            // was passed". Wired to the wrong knob, or to nothing, the landing points do not move
            // and this is the only check that would see it.
            float SpreadOf(GameState s)
            {
                var landings = new List<float>();
                for (int seed = 0; seed < 40; seed++)
                    foreach (var p in BattleTick.FireEnemyVolley(s, new System.Random(seed))
                                                .Projectiles.Where(p => !p.OwnerIsPlayer))
                        landings.Add(TrajectoryPhysics.LandingPoint(
                            new Vector3(p.X, p.Y, p.Z), new Vector3(p.Vx, p.Vy, p.Vz)).x);
                float mean = landings.Average();
                return Mathf.Sqrt(landings.Sum(x => (x - mean) * (x - mean)) / landings.Count);
            }

            var theirTurn = fresh with { TurnSide = TurnSide.Enemy };
            float clear = SpreadOf(theirTurn);
            float smoked = SpreadOf(theirTurn with { SmokeScreenArmed = true });
            Check(smoked > clear * 1.3f,
                  $"a volley fired through smoke lands measurably WIDER ({clear:F2} -> {smoked:F2})");

            var armed = ConsumableActions.ToggleArmed(Carrying(ConsumableType.SmokeScreen),
                                                      ConsumableType.SmokeScreen);
            var after = BattleTick.FireEnemyVolley(armed with { TurnSide = TurnSide.Enemy },
                                                  new System.Random(3));
            Check(Consumables.Equipped(armed, ConsumableType.SmokeScreen) == 1
                  && !after.SmokeScreenArmed
                  && Consumables.Equipped(after, ConsumableType.SmokeScreen) == 0,
                  "smoke covers exactly ONE volley, and is spent by it rather than by the arming");
        }

        // --- THE CAP, and the panel that has to fit it ------------------------------------------
        {
            var two = new Dictionary<ConsumableType, int>
                { { ConsumableType.Airstrike, 1 }, { ConsumableType.TraumaKit, 1 } };
            var three = new Dictionary<ConsumableType, int>(two)
                { { ConsumableType.SmokeScreen, 1 } };
            Check(Consumables.TotalEquipped(ConsumableActions.Equip(two)) == 2
                  && Consumables.TotalEquipped(ConsumableActions.Equip(three)) == 0
                  && Consumables.TotalEquipped(ConsumableActions.Equip(null)) == 0,
                  "two may be carried and a third is refused OUTRIGHT — which two to keep is the "
                  + "player's decision, not something to truncate silently");

            // Adding a consumables section is what pushed the Kotlin's Confirm button off the
            // bottom of the screen: not clipped, ABSENT from the tree and unreachable by input.
            // BattleUIPreview renders the panel and fails on any off-screen Button; this pins the
            // arithmetic against the live roster.
            var roster = AssetDatabase.LoadAssetAtPath<RosterDefinitionSO>(
                "Assets/GameData/Roster.asset");
            int rows = roster != null ? roster.slots.Count : 6;
            float lastRowBottom = ArmedConflict.UI.BattleUI.LoadoutRowTop
                                + (rows - 1) * ArmedConflict.UI.BattleUI.LoadoutRowPitch
                                + ArmedConflict.UI.BattleUI.LoadoutRowHeight;
            Check(lastRowBottom <= ArmedConflict.UI.BattleUI.ConsumableHeaderY
                  && ArmedConflict.UI.BattleUI.ConsumableStripY
                     + ArmedConflict.UI.BattleUI.ConsumableStripHeight
                     <= ArmedConflict.UI.BattleUI.CamoHeaderY
                  && ArmedConflict.UI.BattleUI.CamoStripY
                     + ArmedConflict.UI.BattleUI.CamoStripHeight
                     <= ArmedConflict.UI.BattleUI.BeginButtonY,
                  $"the panel stacks without collision — {rows} roster rows, then consumables, "
                  + "then camo, and BEGIN below all of it");
        }
    }

    public static void Run()
    {
        failed = 0;

        // --- SpringFollow: the REST DEADBAND is the whole point of not using Mathf.SmoothDamp.
        {
            float v = 0f, vel = 0f;
            for (int i = 0; i < 2000; i++) SpringFollow.Step(ref v, ref vel, 10f, 1f / 60f, 0.25f);
            Check(v == 10f, "spring lands EXACTLY on target (bit-identical, not merely close)");
            Check(vel == 0f, "spring velocity reaches exactly zero");

            float before = v;
            SpringFollow.Step(ref v, ref vel, 10f, 1f / 60f, 0.25f);
            Check(v == before && vel == 0f, "a settled spring does not change on further ticks");

            // Critically damped: must never overshoot a static target.
            float p = 0f, pv = 0f, maxSeen = 0f;
            for (int i = 0; i < 600; i++)
            {
                SpringFollow.Step(ref p, ref pv, 1f, 1f / 60f, 0.3f);
                maxSeen = Mathf.Max(maxSeen, p);
            }
            Check(maxSeen <= 1f + 1e-5f, "spring never overshoots a static target");

            // Stability across wildly different dt — the reason for this formula.
            float q = 0f, qv = 0f;
            SpringFollow.Step(ref q, ref qv, 5f, 2f, 0.25f);
            Check(!float.IsNaN(q) && Mathf.Abs(q) <= 5.001f, "spring stable at an absurd dt");
        }

        // --- Formation.Grid: centred, correct row/column split.
        {
            var g = Formation.Grid(8, anchorX: 0f, anchorZ: 0f);
            Check(g.Count == 8, "grid returns every unit");
            Near(g.Take(5).Average(p => p.x), 0f, 1e-4f, "full row is centred on the anchor");
            Check(g.Take(5).All(p => Mathf.Abs(p.y - g[0].y) < 1e-5f), "first five share a row");
            Check(Mathf.Abs(g[5].y - g[0].y) > 1e-3f, "sixth unit starts a new row");
            Near(Mathf.Abs(g[1].x - g[0].x), Formation.DefaultColumnSpacing, 1e-5f,
                 "column spacing is DefaultColumnSpacing");
        }

        // --- Formation.Mounted: nobody may stand off the deck.
        {
            const float width = 1.2f;
            foreach (int n in new[] { 3, 5, 9, 14 })
            {
                var m = Formation.Mounted(n, anchorX: 0f, width: width);
                Check(m.Count == n, $"mounted({n}) returns every unit");
                bool onDeck = m.All(p => Mathf.Abs(p.x) <= width / 2f + 1e-4f);
                Check(onDeck, $"mounted({n}) keeps every defender on the deck");
            }
            Check(Formation.Mounted(4, 0f, 1.2f).All(p => Mathf.Abs(p.y) < 1e-6f),
                  "fewer than 5 defenders stand in ONE rank");
            Check(Formation.Mounted(6, 0f, 1.2f).Select(p => p.y).Distinct().Count() == 2,
                  "5+ defenders pack into TWO ranks (reference: castle tiers)");

            // An anchor off the side of the deck must be pulled back onto it.
            var shoved = Formation.Mounted(5, anchorX: 4f, width: width, deckCenterX: 0f);
            Check(shoved.All(p => Mathf.Abs(p.x) <= width / 2f + 1e-4f),
                  "an off-deck anchor is clamped back onto the deck");
        }

        // --- Formation.Clustered: gaps between clumps must exceed spacing within one.
        {
            var c = Formation.Clustered(9, 0f, random: new System.Random(1234));
            Check(c.Count == 9, "clustered returns every unit");
            var xs = c.Select(p => p.x).OrderBy(x => x).ToList();
            var gaps = Enumerable.Range(1, xs.Count - 1).Select(i => xs[i] - xs[i - 1]).ToList();
            Check(gaps.Max() > gaps.Where(g => g > 1e-4f).Min() * 1.4f,
                  "clumps separate — largest gap clearly exceeds intra-clump spacing");
        }

        // --- CameraFraming
        {
            Near(CameraFraming.HalfWidth(0f, new List<float> { -3f, 2f }), 3f, 1e-5f,
                 "half-width covers the furthest point from the anchor");
            Near(CameraFraming.HalfWidth(10f, new List<float> { -3f, 3f }), 13f, 1e-5f,
                 "off-centre anchor still frames the whole set");
            Check(CameraFraming.HalfWidth(0f, new List<float>()) == 0f, "empty set gives zero");
        }

        // --- EnemyAI: jitter is the ONLY inaccuracy, and speed is capped.
        {
            Near(EnemyAI.JitterRadius(2f), 4f, 1e-5f, "smoke screen doubles the jitter radius");
            Near(EnemyAI.AdvanceBudget(2f, true), 1f, 1e-5f, "overwatch flare halves advance budget");
            Near(EnemyAI.AdvanceBudget(2f, false), 2f, 1e-5f, "no flare leaves advance budget alone");
            bool capped = true;
            for (int i = 0; i < 500; i++)
            {
                var v = EnemyAI.AimAt(Vector3.zero, new Vector3(30f, 0f, 0f));
                if (v.magnitude > 12.0001f) capped = false;
            }
            Check(capped, "launch speed never exceeds the cap, however far the target");
        }

        // --- TrajectoryPhysics / SweptCollision (ported in Step 4, re-checked here)
        {
            Near(SweptCollision.UnitHitRadius, 0.5f * (0.48f / 0.77f) * 1.22f, 1e-6f,
                 "hit radius stays 1.22x body-proportional");
            // The sweep must catch a target the endpoints both miss — the tunnelling case.
            float d2 = SweptCollision.SegmentDistanceSq(0f, 5f, 0f, -5f, 0f, 0f);
            Check(d2 < 1e-6f, "swept segment catches a target passed BETWEEN two ticks");
        }

        // --- GameState / entities
        {
            // Records must behave like Kotlin data classes: `with` = copy(), value equality.
            var u = new UnitEntity(1, null, 1f, 0f, 0f, 32, true);
            var moved = u with { X = 5f };
            Check(u.X == 1f, "`with` does not mutate the original (copy semantics)");
            Check(moved.X == 5f && moved.Hp == 32, "`with` carries unchanged fields through");
            Check(u == new UnitEntity(1, null, 1f, 0f, 0f, 32, true), "records compare by value");
            Check(u != moved, "records with different values are unequal");
            Check(u.KnockbackAge == -1f, "knockback defaults to inactive (-1), not 0");

            // MaxHp defaults to Hp by construction — the fix for hpScale placements whose
            // damage fraction was taken against the DEFINITION's maxHp and went negative.
            var s4 = new StructureEntity(1, null, 0f, 0f, 0f, 340);
            Near(s4.HpFraction, 1f, 1e-6f, "a fresh structure reads full health");
            var hurt = s4 with { Hp = 170 };
            Near(hurt.HpFraction, 0.5f, 1e-6f, "damage is a fraction of the PLACEMENT's max hp");
            var scaled = new StructureEntity(2, null, 0f, 0f, 0f, 1360) { MaxHp = 1360 };
            Near(scaled.HpFraction, 1f, 1e-6f, "a 4x hpScale placement still reads 1.0 at full health");
            Check(StructureDamage.ShedChunkCount(scaled.HpFraction, 3) == 0,
                  "a 4x placement sheds NOTHING at full health (the negative-fraction bug)");

            // Shed curve: first group after ~1/(n+1) of the damage, last just before death.
            Check(StructureDamage.ShedChunkCount(1.0f, 3) == 0, "full health sheds nothing");
            Check(StructureDamage.ShedChunkCount(0.0f, 3) == 3, "destroyed sheds every group");
            Check(StructureDamage.ShedChunkCount(0.5f, 3) == 2, "half health sheds two of three");
            Check(StructureDamage.ShedChunkCount(-0.5f, 3) == 3, "over-damage clamps, never exceeds");

            // IsVisuallyIdle — both traps that permanently disabled it in the Android build.
            var idle = new GameState
            {
                Phase = GamePhase.Playing,
                TurnPhase = TurnPhase.Aiming,
            };
            Check(idle.IsVisuallyIdle, "an empty playing/aiming state is visually idle");

            var withRubble = idle with
            {
                Debris = new List<DebrisPiece>
                {
                    new(1, "x", false, 0, 0, 0, 0, 0, 0, 0, 0.2f, float.MaxValue) { Asleep = true },
                },
            };
            Check(withRubble.IsVisuallyIdle,
                  "SLEEPING rubble does not block idle (it persists for the whole level)");

            var withLiveDebris = idle with
            {
                Debris = new List<DebrisPiece>
                {
                    new(1, "x", false, 0, 0, 0, 0, 0, 0, 0, 0.2f, 1f),
                },
            };
            Check(!withLiveDebris.IsVisuallyIdle, "moving debris DOES block idle");

            var settledWreck = idle with
            {
                Wrecks = new List<WreckEntity>
                {
                    new(1, "x", 0, 0, 0, 1, 1) { Age = GameState.WreckCollapseSeconds },
                },
            };
            Check(settledWreck.IsVisuallyIdle,
                  "a wreck past the COLLAPSE WINDOW does not block idle forever");

            var collapsing = idle with
            {
                Wrecks = new List<WreckEntity> { new(1, "x", 0, 0, 0, 1, 1) { Age = 0.1f } },
            };
            Check(!collapsing.IsVisuallyIdle, "a still-collapsing wreck DOES block idle");

            var gliding = idle with { CameraFollowXVelocity = 0.5f };
            Check(!gliding.IsVisuallyIdle, "a camera still gliding blocks idle");
        }

        // --- ProgressStore / EconomyStore (persistence)
        {
            var lvl = ScriptableObject.CreateInstance<LevelDefinitionSO>();
            lvl.id = "selftest_level"; lvl.levelBase = 100; lvl.isTestLevel = false;
            var testRig = ScriptableObject.CreateInstance<LevelDefinitionSO>();
            testRig.id = "selftest_rig"; testRig.levelBase = 100; testRig.isTestLevel = true;
            ProgressStore.AllLevels = new List<LevelDefinitionSO> { lvl, testRig };
            ProgressStore.ResetAll();

            Check(ProgressStore.BestStars(lvl.id) == 0, "a fresh level has no stars");
            Check(ProgressStore.RecordStars(lvl.id, 2), "recording a first result sets a best");
            Check(!ProgressStore.RecordStars(lvl.id, 1), "a WORSE run does not overwrite the best");
            Check(ProgressStore.BestStars(lvl.id) == 2, "the best survives a worse run");
            Check(ProgressStore.RecordStars(lvl.id, 3), "a better run does set a new best");

            ProgressStore.RecordStars(testRig.id, 3);
            Check(ProgressStore.TotalStars() == 3,
                  "TEST levels are excluded from the total (they must never unlock a stage)");

            // Coins
            ProgressStore.ResetAll();
            ProgressStore.AddCoins(100);
            Check(ProgressStore.Coins() == 100, "coins accumulate");
            Check(!ProgressStore.SpendCoins(101), "cannot overspend");
            Check(ProgressStore.Coins() == 100, "a failed spend does not deduct");
            Check(ProgressStore.SpendCoins(60) && ProgressStore.Coins() == 40, "a valid spend deducts");

            // Set storage — the delimited-string replacement for getStringSet.
            ProgressStore.ResetAll();
            Check(ProgressStore.IsUnitUnlocked("rifleman"), "Rifleman is unlocked without being stored");
            Check(!ProgressStore.IsUnitUnlocked("sniper"), "other units start locked");
            ProgressStore.UnlockUnit("sniper");
            ProgressStore.UnlockUnit("grenadier");
            ProgressStore.UnlockUnit("sniper");
            Check(ProgressStore.UnlockedUnitIds().Count == 2, "unlocking twice does not duplicate");
            Check(ProgressStore.IsUnitUnlocked("sniper") && ProgressStore.IsUnitUnlocked("grenadier"),
                  "multiple unlocks round-trip through one stored string");

            // Ammo: locked types must never be returned as selected.
            ProgressStore.ResetAll();
            Check(ProgressStore.SelectedAmmo() == AmmoType.Standard, "ammo defaults to Standard");
            ProgressStore.SetSelectedAmmo(AmmoType.Incendiary);
            Check(ProgressStore.SelectedAmmo() == AmmoType.Standard,
                  "a LOCKED ammo type never comes back as selected");
            ProgressStore.UnlockAmmo(AmmoType.Incendiary);
            Check(ProgressStore.SelectedAmmo() == AmmoType.Incendiary,
                  "once unlocked, the stored selection is honoured");

            // Victory payout — the ORDERING trap.
            ProgressStore.ResetAll();
            var first = EconomyStore.GrantVictoryPayout(lvl, starsEarned: 3, previousBestStars: 0);
            Check(first.FirstClear && first.First3Star, "a first 3-star clear pays both bonuses");
            Check(first.Coins == 200 + 100 + 150, "first 3-star = 2.0x base + base + 1.5x base");

            var repeat = EconomyStore.GrantVictoryPayout(lvl, starsEarned: 3, previousBestStars: 3);
            Check(!repeat.FirstClear && !repeat.First3Star, "a repeat clear pays no bonuses");
            Check(repeat.Coins == 200, "a repeat 3-star pays the multiplier only");

            var twoStar = EconomyStore.GrantVictoryPayout(lvl, starsEarned: 2, previousBestStars: 1);
            Check(twoStar.Coins == 150, "a 2-star repeat pays 1.5x base");
            Check(EconomyStore.GrantDefeatPayout(lvl) == 15, "defeat still pays 15% of base");

            // Milestones: one call may cross several thresholds, and must pay each exactly once.
            ProgressStore.ResetAll();
            var levels = new List<LevelDefinitionSO>();
            for (int i = 0; i < 20; i++)
            {
                var l = ScriptableObject.CreateInstance<LevelDefinitionSO>();
                l.id = $"ms_{i}"; l.levelBase = 10;
                levels.Add(l);
            }
            ProgressStore.AllLevels = levels;
            ProgressStore.ResetAll();
            foreach (var l in levels) ProgressStore.RecordStars(l.id, 3);   // 60 stars
            var crossed = EconomyStore.CheckMilestones();
            Check(crossed.Count == 2, "60 stars crosses BOTH the 25 and 50 milestones in one call");
            Check(EconomyStore.CheckMilestones().Count == 0,
                  "milestones are idempotent — a second call pays nothing");

            ProgressStore.ResetAll();
            ProgressStore.AllLevels = new List<LevelDefinitionSO>();
        }

        // --- LevelBuilder, against the REAL imported L1 asset
        {
            // Campaign level assets are named for their IDENTITY, not their number — the
            // ordering moves as the beat chart is authored against, and Level4.asset meaning
            // "level 7" was a trap waiting to happen.
            var l1 = AssetDatabase.LoadAssetAtPath<LevelDefinitionSO>(
                "Assets/GameData/Levels/PatrolEncounter.asset");
            if (l1 == null) { Check(false, "PatrolEncounter asset present"); }
            else
            {
                var st = LevelBuilder.BuildInitialState(l1, battleId: 1, totalLevels: 29,
                                                        random: new System.Random(7));
                Check(st.PlayerUnits.Count == 10, $"L1 builds 10 player units (2 on tank + 8 line)");
                // 14 since the crowd split (Tier 2.2 part four): the outpost's five riflemen
                // became ten crowd bodies at half HP and half damage each, so this number moved
                // while L1's enemy HP and damage per volley did not. The old message said
                // "6 line + 3 garrison", which did not even agree with the garrison check below.
                Check(st.EnemyUnits.Count == 14, "L1 builds 14 enemy units (4 line + 10 garrison)");
                Check(st.Structures.Count == 2, "L1 builds 2 structures");
                Check(st.Phase == GamePhase.Preview, "a new battle starts in Preview");

                // Aiming is the empty beat (measured 2026-08-18: 6.5% content /
                // 0.64% edges vs scout 20% / 1.65%). Backdrop strips sit at
                // z=-30 and cannot fill the tan. Tall play-space flanks do.
                // L1's car slot is SIGNED — a variety pass must not move it.
                var car = l1.props.FirstOrDefault(p =>
                    p.modelAsset != null && p.modelAsset.Contains("wreck_car"));
                Check(car != null && car.keepColors,
                      "L1 plants the signed wrecked car (keepColors)");
                if (car != null)
                {
                    Near(car.x, -5.15f, 0.05f, "L1 car x");
                    Near(car.z, -8.4f, 0.05f, "L1 car z (mid-ground, not the play plane)");
                    Near(car.scale, 3.3f, 0.05f, "L1 car scale");
                }
                var scenery = l1.props.Where(p => p.keepColors && p.z <= -6f).ToList();
                Check(scenery.Count >= 2 && scenery.All(p => p.scale >= 1.4f),
                      scenery.Count >= 2
                          ? $"L1 plants {scenery.Count} mid-ground scenery props (keepColors)"
                          : "L1 is missing mid-ground scenery");

                // Id bands must not collide — the tick relies on globally unique ids.
                var allIds = st.PlayerUnits.Select(u => u.Id)
                    .Concat(st.EnemyUnits.Select(u => u.Id))
                    .Concat(st.Structures.Select(s2 => s2.Id)).ToList();
                Check(allIds.Distinct().Count() == allIds.Count, "unit and structure ids never collide");

                // The garrison must stand on the outpost's measured deck, not on `size`.
                var outpost = st.Structures.First(s2 => s2.Definition.id == "outpost");
                var garrison = st.EnemyUnits.Where(u => u.StandingOnStructureId != null).ToList();
                // 5 since the Phase D authoring pass — composition rule 5 wants the
                // majority of the roster on the structure, even on the teaching level — and 10
                // since the crowd split doubled the BODIES without moving the HP or the damage.
                Check(garrison.Count == 10, "10 enemies are garrisoned on the outpost");
                float deck = 0.560f * 2.5f;   // deckY already scaled at import
                foreach (var g in garrison)
                    Near(g.Y, deck, 1e-3f, "garrison stands on the measured deckY, not on size");

                // Ground units stand at y=0.
                Check(st.EnemyUnits.Where(u => u.StandingOnStructureId == null).All(u => u.Y == 0f),
                      "ground units stand at y = 0");

                // Garrison must sit ON the deck horizontally too.
                float halfDeck = outpost.Definition.standWidth / 2f;
                foreach (var g in garrison)
                    Check(Mathf.Abs(g.X - outpost.X) <= halfDeck + 1e-3f,
                          "garrison is clamped onto the deck horizontally");

                // Structure entity y centres the collision box on the placement.
                Near(outpost.Y, 0f * outpost.Definition.worldScale + outpost.Definition.size / 2f,
                     1e-4f, "structure y centres its box on the placement");

                // hpScale carries into MaxHp so damage fractions have the right denominator.
                Check(st.Structures.All(s2 => s2.MaxHp == s2.Hp), "structures start at full health");
                Check(st.Structures.All(s2 => Mathf.Approximately(s2.HpFraction, 1f)),
                      "every structure reads fraction 1.0 at build time");

                // Camera anchors: player is the GROUND LINE, not the tank crew. Rule 1.
                Check(st.PlayerCamXAnchor < 0f && st.EnemyCamXAnchor > 0f,
                      "camera anchors put the player left and the enemy right (game space)");
                var groundXs = st.PlayerUnits.Where(u => u.StandingOnStructureId == null)
                                             .Select(u => u.X).ToList();
                Near(st.PlayerCamXAnchor, groundXs.Average(), 1e-4f,
                     "player anchor is the mean of the ground line, not the tank crew");
                Near(st.PlayerCamHalfWidth,
                     Mathf.Max((groundXs.Max() - groundXs.Min()) / 2f, 1.5f), 1e-4f,
                     "player half-width is the ground line's span");

                // The tank's cannon ammo becomes the battle's shell count.
                Check(st.TankShellsRemaining > 0, "the player tank contributes its cannon shells");

                Check(st.Helicopter == null, "no helicopter while HeliEnabled is false");

                // Determinism: same seed, same layout. Formation jitter must not leak randomness.
                var again = LevelBuilder.BuildInitialState(l1, 1, 29, new System.Random(7));
                Check(again.PlayerUnits.Select(u => u.X).SequenceEqual(st.PlayerUnits.Select(u => u.X)),
                      "the same seed rebuilds an identical formation");

                // THE AUTHORED COLLAPSE. Killing the outpost must spawn a wreck and must NOT
                // place the cube-slab ruin — those slabs under the clip were a second wreck
                // on the same footprint. Transient flying chunks still throw.
                Check(!string.IsNullOrEmpty(outpost.Definition.wreckModelAsset),
                      "the outpost has an authored collapse");
                var wreckGo = AssetDatabase.LoadAssetAtPath<GameObject>(
                    "Assets/Models/outpost_collapse.glb");
                Check(wreckGo != null, "outpost_collapse.glb is imported");
                var clips = wreckGo == null
                    ? System.Array.Empty<AnimationClip>()
                    : AssetDatabase.LoadAllAssetsAtPath("Assets/Models/outpost_collapse.glb")
                        .OfType<AnimationClip>()
                        .Where(c => !c.name.StartsWith("__preview"))
                        .ToArray();
                Check(clips.Any(c => c.name == WreckAnim.Collapse || c.name.Contains("collapse")),
                      clips.Length == 0
                          ? "outpost_collapse carries a collapse clip"
                          : $"outpost_collapse clip is named collapse (got {clips[0].name})");
                // OUTPUT: playing the clip MOVES a wall. A wreck that sits at rest
                // looks like the killing hit never landed — the live hut hides and
                // an identical intact wreck takes its place. Seen against Mecanim
                // Play() on a controller-less import: angle 0.
                {
                    var inst = (GameObject)PrefabUtility.InstantiatePrefab(wreckGo);
                    try
                    {
                        var wa = inst.GetComponent<WreckAnim>()
                                 ?? inst.AddComponent<WreckAnim>();
                        wa.SendMessage("Awake", SendMessageOptions.DontRequireReceiver);
                        Transform wall = null;
                        foreach (var t in inst.GetComponentsInChildren<Transform>(true))
                            if (t.name == "wall_front" || t.name.Contains("wall_front"))
                            { wall = t; break; }
                        var body = inst.GetComponent<Animation>()
                                   ?? inst.GetComponentInChildren<Animation>(true);
                        Check(wall != null && body != null && body[WreckAnim.Collapse] != null,
                              "the wreck prefab can play collapse on a named wall");
                        if (wall != null && body != null && body[WreckAnim.Collapse] != null)
                        {
                            var rest = wall.localRotation;
                            wa.Play();
                            body[WreckAnim.Collapse].time = body[WreckAnim.Collapse].length;
                            body.Sample();
                            float travel = Quaternion.Angle(rest, wall.localRotation);
                            Check(travel > 20f,
                                  $"collapse MOVES the hut (wall_front {travel:F1} deg from rest)");
                        }

                        // Same fire/smoke as L4's city strip, on the wreck. Fade-in
                        // starts at 0 so it does not sit on the intact hut.
                        var fade = AssetDatabase.LoadAssetAtPath<Material>(
                            "Assets/Materials/BackdropFadeSource.mat");
                        var ownedFx = new List<Object>();
                        try
                        {
                            var kit = RuinFx.MakeKit(fade, ownedFx);
                            var sess = RuinFx.AttachWreck(inst.transform, kit, 1);
                            Check(sess.Fires.Length == 3 && sess.Smokes.Length == 1,
                                  $"wreck plants city-style fire+smoke "
                                  + $"({sess.Fires.Length} fires, {sess.Smokes.Length} plumes)");
                            sess.BornAt = 0f;
                            sess.Tick(0f);
                            float h0 = sess.Fires[0].Outer.localScale.y;
                            sess.Tick(0.40f);
                            float h1 = sess.Fires[0].Outer.localScale.y;
                            Check(h1 > h0 + 0.05f,
                                  $"wreck fire fades IN (h {h0:F2} -> {h1:F2})");
                            var fp = sess.Fires[0].Outer.parent.localPosition;
                            float mz0 = 1e9f, mz1 = -1e9f;
                            foreach (var r in inst.GetComponentsInChildren<MeshRenderer>(true))
                            {
                                if (!r.enabled || r.name == "outer" || r.name == "inner"
                                    || r.name.StartsWith("WreckFire") || r.name.StartsWith("WreckSmoke"))
                                    continue;
                                var b = r.bounds;
                                var a = inst.transform.InverseTransformPoint(b.min);
                                var c = inst.transform.InverseTransformPoint(b.max);
                                mz0 = Mathf.Min(mz0, a.z, c.z);
                                mz1 = Mathf.Max(mz1, a.z, c.z);
                            }
                            Check(fp.y > 0.02f && fp.z < mz1 - 0.03f && fp.z > mz0 - 0.05f,
                                  $"wreck fire sits IN the pile (y={fp.y:F2} z={fp.z:F2} "
                                  + $"in [{mz0:F2},{mz1:F2}]), not proud of the front");
                        }
                        finally
                        {
                            foreach (var o in ownedFx)
                                if (o != null) Object.DestroyImmediate(o);
                        }
                    }
                    finally
                    {
                        Object.DestroyImmediate(inst);
                    }
                }
                var killed = st with
                {
                    Phase = GamePhase.Playing,
                    TurnPhase = TurnPhase.Aiming,
                    Structures = st.Structures
                        .Select(s2 => s2.Definition.id == "outpost" ? s2 with { Hp = 0 } : s2)
                        .ToList(),
                };
                var afterKill = BattleTick.Step(killed, 1f / 60f, l1, new System.Random(1));
                int asleep = afterKill.Debris.Count(d => d.Asleep);
                int wreckN = afterKill.Wrecks.Count(w => w.DefinitionId == "outpost");
                Check(wreckN == 1 && asleep == 0 && afterKill.Debris.Count > 0,
                      $"killing the outpost plays the wreck, not cube slabs "
                      + $"(wrecks {wreckN}, asleep {asleep}, debris {afterKill.Debris.Count})");
                float maxFly = afterKill.Debris
                    .Where(d => !d.Asleep)
                    .Select(d => d.Size)
                    .DefaultIfEmpty(0f)
                    .Max();
                Check(maxFly > 0.02f && maxFly <= 0.14f + 1e-4f,
                      $"flying chunks are pebbles, not crates (max {maxFly:F2})");
                Check(afterKill.Debris.Where(d => !d.Asleep).All(
                          d => d.Ttl >= CosmeticSystems.DebrisTtlSeconds - 1e-3f),
                      "those pebbles persist through the next aim");
                var aged = afterKill;
                for (int i = 0; i < 40; i++)
                    aged = BattleTick.Step(aged, 1f / 60f, l1, new System.Random(1));
                Check(aged.Wrecks.Any(w => w.DefinitionId == "outpost"
                                           && w.Age >= GameState.WreckCollapseSeconds),
                      "the collapse ages to the hold, then stays");
            }
        }

        // Collapse hold: killing a garrisoned structure rides the falling
        // garrison, then releases the camera so it pans back to whoever is
        // still standing. The old check glued the cam to post.X — that
        // hid a static ruin frame that never followed the throw.
        {
            Check(CameraDirector.CollapseIsFollowing(CameraDirector.CollapseHoldSeconds)
                  && !CameraDirector.CollapseIsFollowing(
                          CameraDirector.CollapseHoldSeconds
                          - CameraDirector.CollapseFollowSeconds)
                  && !CameraDirector.CollapseIsFollowing(0f),
                  "the follow window is the FIRST beat of the hold");

            var l2 = AssetDatabase.LoadAssetAtPath<LevelDefinitionSO>(
                "Assets/GameData/Levels/GarrisonPost.asset");
            Check(l2 != null, "GarrisonPost asset present");
            if (l2 == null) { /* skip the rest of this block */ }
            else
            {
            var built = LevelBuilder.BuildInitialState(l2, 1, 12, new System.Random(3));
            var post = built.Structures.First(st => st.Definition != null
                                                    && !st.Definition.isPlayerSide);
            int onPost = built.EnemyUnits.Count(u => u.StandingOnStructureId == post.Id);
            int onGround = built.EnemyUnits.Count(u => u.StandingOnStructureId == null);
            Check(onPost > 0 && onGround > 0,
                  $"L2's post has a garrison AND a ground line ({onPost} + {onGround})");
            var killed = built with
            {
                Phase = GamePhase.Playing,
                TurnPhase = TurnPhase.Resolving,
                TurnSide = TurnSide.Player,
                Projectiles = new List<ProjectileEntity>(),
                CameraFollowX = post.X,
                Structures = built.Structures
                    .Select(st => st.Id == post.Id ? st with { Hp = 0 } : st).ToList(),
            };
            var after = BattleTick.Step(killed, 1f / 60f, l2, new System.Random(3));
            Check(after.CollapseHold > CameraDirector.CollapseHoldSeconds - 0.05f
                  && after.DyingUnits.Count >= onPost
                  && CameraDirector.CollapseIsFollowing(after.CollapseHold)
                  && Mathf.Abs(after.CollapseHoldAnchorX - post.X) < 2f,
                  $"killing the post ARMS a collapse hold "
                  + $"({after.CollapseHold:F2}s, {after.DyingUnits.Count} bodies, "
                  + $"anchor {after.CollapseHoldAnchorX:F2} vs post {post.X:F2})");

            var held = after;
            for (int i = 0; i < 30; i++)
                held = BattleTick.Step(held, 1f / 60f, l2, new System.Random(3));
            float bodyMean = held.DyingUnits.Where(d => d.Tumble)
                .Select(d => d.X).DefaultIfEmpty(post.X).Average();
            float liveMean = held.EnemyUnits.Count > 0
                ? held.EnemyUnits.Average(u => u.X) : post.X;
            float camRide = held.CameraFollowX ?? 0f;
            Check(held.CollapseHold > 0f
                  && CameraDirector.CollapseIsFollowing(held.CollapseHold)
                  && Mathf.Abs(camRide - bodyMean) < Mathf.Abs(camRide - liveMean),
                  "half a second later the camera is RIDING the fall "
                  + $"(cam {camRide:F2}, bodies {bodyMean:F2}, live {liveMean:F2}, "
                  + $"{held.CollapseHold:F2}s left)");

            var panned = held;
            float followLeftSec = held.CollapseHold
                - (CameraDirector.CollapseHoldSeconds
                   - CameraDirector.CollapseFollowSeconds);
            int followLeft = Mathf.Max(0, Mathf.CeilToInt(followLeftSec * 60f)) + 1;
            for (int i = 0; i < followLeft + 45; i++)
                panned = BattleTick.Step(panned, 1f / 60f, l2, new System.Random(3));
            float camBack = panned.CameraFollowX ?? 0f;
            float liveNow = panned.EnemyUnits.Count > 0
                ? panned.EnemyUnits.Average(u => u.X) : liveMean;
            float bodiesNow = panned.DyingUnits.Where(d => d.Tumble)
                .Select(d => d.X).DefaultIfEmpty(bodyMean).Average();
            Check(panned.CollapseHold > 0f
                  && !CameraDirector.CollapseIsFollowing(panned.CollapseHold)
                  && Mathf.Abs(camBack - liveNow) < Mathf.Abs(camBack - bodiesNow),
                  "then the camera pans back to the live line "
                  + $"(cam {camBack:F2}, live {liveNow:F2}, bodies {bodiesNow:F2}, "
                  + $"{panned.CollapseHold:F2}s left)");

            var done = panned;
            for (int i = 0; i < 120 && done.CollapseHold > 0f; i++)
                done = BattleTick.Step(done, 1f / 60f, l2, new System.Random(3));
            Check(done.CollapseHold <= 0f,
                  "the collapse hold expires rather than freezing");

            // Wreck.Y used to be the standing CENTRE (size/2). Lid then sat
            // at ~1.6 and a body that stayed over the footprint froze in
            // mid-air while the collapse mesh lay on the dirt.
            var wreck = after.Wrecks.FirstOrDefault(w => w.Id == post.Id);
            Check(wreck != null && wreck.Y < 0.15f
                  && Mathf.Abs(CosmeticSystems.WreckLidY(wreck)
                               - CosmeticSystems.WreckRestY) < 0.05f,
                  $"the wreck lid is the mound on the dirt "
                  + $"(y {wreck?.Y:F2}, lid {CosmeticSystems.WreckLidY(wreck):F2}, "
                  + $"centre was {post.Y:F2})");

            var settled = after;
            for (int i = 0; i < 200; i++)
                settled = BattleTick.Step(settled, 1f / 60f, l2, new System.Random(3));
            var grounded = settled.DyingUnits
                .Where(d => d.Tumble && !CosmeticSystems.RagdollAirborne(d))
                .ToList();
            int hung = settled.DyingUnits.Count(d => d.Tumble
                && d.Y > 0.80f
                && Mathf.Abs(d.Vy) < 0.15f
                && Mathf.Abs(d.Vx) < 0.15f);
            float minY = settled.DyingUnits.Select(d => d.Y).DefaultIfEmpty(-99f).Min();
            float maxY = settled.DyingUnits.Select(d => d.Y).DefaultIfEmpty(-99f).Max();
            float maxSup = settled.DyingUnits.Select(d => d.SupportY).DefaultIfEmpty(-99f).Max();
            Check(grounded.Count > 0 && hung == 0
                  && grounded.Max(d => d.Y) < CosmeticSystems.WreckRestY + 0.25f,
                  $"no collapsed garrison rests in mid-air "
                  + $"(landed {grounded.Count}/{settled.DyingUnits.Count}, "
                  + $"y {minY:F2}..{maxY:F2}, support {maxSup:F2}, hung {hung})");
            }
        }

        // --- CollisionSystem: the RESOLUTION ORDER, which is where the behaviour lives
        {
            var unitDef = ScriptableObject.CreateInstance<UnitDefinitionSO>();
            unitDef.id = "t"; unitDef.maxHp = 32; unitDef.damage = 8;
            var wallDef = ScriptableObject.CreateInstance<StructureDefinitionSO>();
            wallDef.id = "wall"; wallDef.maxHp = 100; wallDef.size = 2f;
            wallDef.hasHitWidth = true; wallDef.hitWidth = 2.4f; wallDef.isPlayerSide = false;

            UnitEntity Enemy(int id, float x, float y, int hp = 32)
                => new(id, unitDef, x, y, 0f, hp, false);
            ProjectileEntity Shot(int id, float fromX, float fromY, float toX, float toY,
                                  float splash = 0f, int dmg = 8)
                => new(id, toX, toY, 0f, toX - fromX, toY - fromY, 0f, dmg, true)
                   { PrevX = fromX, PrevY = fromY, PrevZ = 0f, SplashRadius = splash };

            // A UNIT IN FRONT OF A WALL takes the round. Resolving structures first "shielded"
            // any ground unit inside a wide structure's AABB, so front-rank defenders read as
            // taking direct hits that dealt no damage.
            var wall = new StructureEntity(100, wallDef, 5f, 1f, 0f, 100);
            var inFront = Enemy(1, 5f, 0.3f);
            var r1 = CollisionSystem.ResolveHits(
                new[] { Shot(1, 5f, 3f, 5f, 0.3f) }, new[] { inFront },
                new List<UnitEntity>(), new[] { wall });
            Check(r1.UnitDamage.ContainsKey(1), "a unit standing inside a wall's box still takes the hit");
            Check(!r1.StructureDamage.ContainsKey(100), "the wall behind it takes NO damage");

            // ARMOUR — the shield bearer's mechanic, Tier 2.3, 2026-08-12.
            //
            // The class was sold for 500 coins as "walks forward and fights hand to hand" while
            // melee was unported, which made it measurably a rifleman with more hp and less
            // damage — the audit's one true duplicate. `damageTakenMultiplier` is what it has
            // instead. Asserted against the DAMAGE RESOLVED, never against the field: a
            // multiplier read correctly and applied on only one of the two damage paths is
            // exactly how the incendiary burn and the structure multiplier each went missing.
            //
            // One check, four facts: the direct path soaks, the SPLASH path soaks too, an
            // unarmoured unit is untouched by any of it, and the floor holds. The floor is the
            // one that matters most — rounding a small round down to 0 would not make the class
            // tough, it would make it IMMORTAL, and a battle that cannot end is not a balance
            // bug that damage assertions would ever catch.
            var armouredDef = ScriptableObject.CreateInstance<UnitDefinitionSO>();
            armouredDef.id = "armoured"; armouredDef.maxHp = 40; armouredDef.damage = 4;
            armouredDef.damageTakenMultiplier = 0.5f;

            UnitEntity Armoured(int id, float x, float y) => new(id, armouredDef, x, y, 0f, 40, false);

            var direct = CollisionSystem.ResolveHits(
                new[] { Shot(90, 9f, 3f, 9f, 0.3f, dmg: 8) },
                new[] { Armoured(90, 9f, 0.3f) }, new List<UnitEntity>(), new StructureEntity[0]);
            var bare = CollisionSystem.ResolveHits(
                new[] { Shot(91, 9f, 3f, 9f, 0.3f, dmg: 8) },
                new[] { Enemy(91, 9f, 0.3f) }, new List<UnitEntity>(), new StructureEntity[0]);
            var splashed = CollisionSystem.ResolveHits(
                new[] { Shot(92, 9f, 3f, 9f, 0.3f, splash: 1.5f, dmg: 8) },
                new[] { Armoured(92, 9f, 0.3f) }, new List<UnitEntity>(), new StructureEntity[0]);
            var tiny = CollisionSystem.ResolveHits(
                new[] { Shot(93, 9f, 3f, 9f, 0.3f, dmg: 1) },
                new[] { Armoured(93, 9f, 0.3f) }, new List<UnitEntity>(), new StructureEntity[0]);

            direct.UnitDamage.TryGetValue(90, out int armouredHit);
            bare.UnitDamage.TryGetValue(91, out int bareHit);
            splashed.UnitDamage.TryGetValue(92, out int armouredSplash);
            tiny.UnitDamage.TryGetValue(93, out int floorHit);
            Check(bareHit == 8 && armouredHit == 4 && armouredSplash == 4 && floorHit == 1,
                  $"armour halves the damage RESOLVED, on both damage paths: an 8-damage round " +
                  $"does {armouredHit} to an armoured body and {bareHit} to a bare one, splash " +
                  $"does {armouredSplash}, and a 1-damage round still does {floorHit} " +
                  "(never 0 — that would be immortality, not toughness)");

            // ...AND THE AUTHORED ENEMY CARRIES IT. The block above proves the MECHANIC on a
            // synthetic definition; it says nothing about the assets that ship. On 2026-08-21
            // `EnemyShieldBearer.asset` had no `damageTakenMultiplier` at all — the player's
            // shield bearer soaked and the enemy's, the one that actually CHARGES, was a bare
            // 40 hp body that a converged volley wiped on arrival. Rob: "the melee force should
            // not die immediately." Same family as the machine gunner's burst: a signature that
            // lives on one side's asset only.
            //
            // Resolved through the SHIPPED assets, and stated as survival rather than as a
            // multiplier — the thing the player sees is that the charge arrives wounded.
            var authored = AssetDatabase.FindAssets("t:UnitDefinitionSO")
                .Select(AssetDatabase.GUIDToAssetPath)
                .Select(AssetDatabase.LoadAssetAtPath<UnitDefinitionSO>)
                .Where(u => u != null && u.meleeDamage > 0)
                .ToList();
            Check(authored.Count >= 2,
                  $"both sides field a melee class at all ({authored.Count}) — else this is untested");
            // STATED IN ROUNDS-TO-KILL, which is the only form of this the player experiences,
            // and BOTH ENDS ARE REAL LIMITS:
            //
            //   lower — the class must outlast a bare body of the same HP, or its whole
            //     signature is missing again. That is the bug this block was written for.
            //   upper — and the ENEMY's must NOT take twice as many. Rob, 2026-08-24: "the
            //     shield bearers should not have double hp but just a bit more than they
            //     originally had." 0.5 was exactly double (10 rounds against 5) and this check
            //     goes RED on it — the value it was written against is the value it forbids.
            //
            // THE UPPER BOUND IS ENEMY-ONLY, and that is not a loophole. The PLAYER's shield
            // bearer is sold on being double: its roster line reads "Takes half damage from
            // every round. Outlasts two riflemen." Holding both sides to one number would
            // either break that promise or re-permit the thing Rob just asked to remove.
            // Sides are read off the ROSTER — the loadout's own menu — because
            // UnitDefinitionSO has no side flag and an id prefix is a naming convention, not
            // a fact.
            //
            // WHY IT IS A BAND AND NOT A NUMBER: `damageTakenMultiplier` is QUANTISED. Soaked
            // rounds to an int, so against an 8-damage rifle round every multiplier in
            // [0.6875, 0.8125) resolves to the SAME 6, and 0.50 and 0.55 are indistinguishable.
            // Asserting a multiplier would be asserting an input that the engine does not
            // actually honour at that resolution; rounds-to-kill is what it honours.
            var pickable = new HashSet<UnitDefinitionSO>(
                AssetDatabase.FindAssets("t:RosterDefinitionSO")
                    .Select(AssetDatabase.GUIDToAssetPath)
                    .Select(AssetDatabase.LoadAssetAtPath<RosterDefinitionSO>)
                    .Where(r => r != null)
                    .SelectMany(r => r.slots)
                    .Select(sl => sl.unit)
                    .Where(u => u != null));
            Check(pickable.Count > 0,
                  $"the roster names the player's own classes ({pickable.Count}) — without it "
                  + "the enemy-only bound below cannot tell the sides apart and is vacuous");

            int fewest = int.MaxValue, most = 0; string thinnest = null, thickest = null;
            foreach (var def in authored)
            {
                var body = new UnitEntity(94, def, 9f, 0.3f, 0f, def.maxHp, false);
                var hit = CollisionSystem.ResolveHits(
                    new[] { Shot(94, 9f, 3f, 9f, 0.3f, dmg: 8) },
                    new[] { body }, new List<UnitEntity>(), new StructureEntity[0]);
                hit.UnitDamage.TryGetValue(94, out int took);
                int rounds = took > 0 ? Mathf.CeilToInt((float)def.maxHp / took) : int.MaxValue;
                int bareRounds = Mathf.CeilToInt(def.maxHp / 8f);
                if (rounds - bareRounds < fewest) { fewest = rounds - bareRounds; thinnest = def.id; }
                if (!pickable.Contains(def) && rounds >= bareRounds * 2 && rounds > most)
                { most = rounds; thickest = def.id; }
            }
            Check(fewest > 0,
                  $"every authored MELEE class SOAKS — it outlasts a bare body of its own HP by "
                  + $"{fewest} rifle round(s) at the thinnest ({thinnest}). The enemy asset "
                  + "shipped with no armour at all once, and the charge died on approach");
            Check(most == 0,
                  $"...and no ENEMY melee class is DOUBLE a bare body ({thickest ?? "none"}). "
                  + "Rob asked for a bit more, not twice as much. The player's own shield "
                  + "bearer is exempt — being double is what its roster line sells");

            // A shot that strikes no unit is blocked by the wall.
            var r2 = CollisionSystem.ResolveHits(
                new[] { Shot(2, 5f, 3f, 5f, 1f) }, new List<UnitEntity>(),
                new List<UnitEntity>(), new[] { wall });
            Check(r2.StructureDamage.TryGetValue(100, out int wd) && wd == 8,
                  "a shot hitting nothing but the wall damages it");

            // OWN-SIDE structures never block — a garrison fires clean over its own fortress.
            var playerWallDef = ScriptableObject.CreateInstance<StructureDefinitionSO>();
            playerWallDef.id = "pw"; playerWallDef.size = 2f; playerWallDef.isPlayerSide = true;
            playerWallDef.hasHitWidth = true; playerWallDef.hitWidth = 2.4f;
            var ownWall = new StructureEntity(101, playerWallDef, 5f, 1f, 0f, 100);
            var r3 = CollisionSystem.ResolveHits(
                new[] { Shot(3, 5f, 3f, 5f, 1f) }, new List<UnitEntity>(),
                new List<UnitEntity>(), new[] { ownWall });
            Check(r3.StructureDamage.Count == 0, "a player shot passes through a PLAYER structure");

            // Damage accumulates against CURRENT hp, not maxHp: a half-health unit must not
            // soak a fresh unit's worth of hits.
            var half = Enemy(2, 0f, 0.3f, hp: 8);
            var r4 = CollisionSystem.ResolveHits(
                new[] { Shot(4, 0f, 3f, 0f, 0.3f), Shot(5, 0f, 3f, 0f, 0.3f) },
                new[] { half }, new List<UnitEntity>(), new List<StructureEntity>());
            Check(r4.UnitDamage[2] == 8, "a second round does not pile onto an already-dead unit");
            Check(r4.HitProjectileIds.Count == 1, "the second round is left free to fly on");

            // The detonation lands at the CONTACT POINT, not the overshot tick-end position.
            var far = Enemy(3, 0f, 0.5f);
            var overshoot = Shot(6, 0f, 4f, 0f, -4f);
            var r5 = CollisionSystem.ResolveHits(new[] { overshoot }, new[] { far },
                                                 new List<UnitEntity>(), new List<StructureEntity>());
            Check(r5.Detonations.Count == 1, "the swept hit produces one detonation");
            Near(r5.Detonations[0].Y, 0.5f, 0.05f,
                 "detonation sits at the contact point, not the tick-end position");

            // Splash damages everyone in radius including the trigger target, and marks them
            // for the knockback hop; a plain bullet marks nobody.
            var a = Enemy(4, 0f, 0.3f); var b = Enemy(5, 0.6f, 0.3f); var c = Enemy(6, 4f, 0.3f);
            var r6 = CollisionSystem.ResolveHits(
                new[] { Shot(7, 0f, 3f, 0f, 0.3f, splash: 1.5f) },
                new[] { a, b, c }, new List<UnitEntity>(), new List<StructureEntity>());
            Check(r6.UnitDamage.ContainsKey(4) && r6.UnitDamage.ContainsKey(5),
                  "splash catches the trigger target and its neighbour");
            Check(!r6.UnitDamage.ContainsKey(6), "splash does not reach outside its radius");
            Check(r6.ExplosiveHitUnitIds.Contains(5), "splash victims are marked for knockback");
            Check(r1.ExplosiveHitUnitIds.Count == 0, "a plain bullet marks nobody for knockback");

            // A splash weapon detonates on the ground rather than wasting into the dirt.
            var ground = new ProjectileEntity(8, 2f, -0.1f, 0f, 0f, -5f, 0f, 8, true)
                { PrevX = 2f, PrevY = 0.4f, SplashRadius = 1.5f };
            var near = Enemy(7, 2.5f, 0.3f);
            var r7 = CollisionSystem.ResolveHits(new[] { ground }, new[] { near },
                                                 new List<UnitEntity>(), new List<StructureEntity>());
            Check(r7.Detonations.Count == 1 && r7.Detonations[0].IsGroundBurst,
                  "a splash round that hits nothing detonates on the ground");
            Check(r7.UnitDamage.ContainsKey(7), "a ground burst still damages units in radius");

            // Collapse propagation, transitively up a stack.
            var tierDef = ScriptableObject.CreateInstance<StructureDefinitionSO>();
            tierDef.id = "tier"; tierDef.size = 1f;
            var t1 = new StructureEntity(1, tierDef, 0f, 0f, 0f, 10);
            var t2 = new StructureEntity(2, tierDef, 0f, 1f, 0f, 10) { RestsOnId = 1 };
            var t3 = new StructureEntity(3, tierDef, 0f, 2f, 0f, 10) { RestsOnId = 2 };
            var linked = new StructureEntity(4, tierDef, 5f, 0f, 0f, 10) { CollapseWith = 1 };
            var collapsed = CollisionSystem.PropagateCollapse(
                new[] { t2, t3, linked }, new[] { 1 });
            Check(collapsed.Contains(2) && collapsed.Contains(3),
                  "destroying the base collapses the whole stack transitively");
            Check(collapsed.Contains(4), "an explicit collapseWith partner comes down too");

            // A GARRISON ON A ROOF MUST BE SHOOTABLE. The collision box rises to the measured
            // deck, not to `size` — otherwise the gap between them is invisible masonry sitting
            // on top of the defenders, and a round descending on them is spent on the wall edge
            // instead. The only way to kill them then is to destroy the building.
            var outpost = ScriptableObject.CreateInstance<StructureDefinitionSO>();
            outpost.id = "outpost"; outpost.size = 2f;
            outpost.hasHitWidth = true; outpost.hitWidth = 3.25f;
            outpost.hasDeckY = true; outpost.deckY = 1.4f;      // roof well below `size`
            var post = new StructureEntity(200, outpost, 7f, 1f, 0f, 90);   // centre of a 2-tall box

            var defenderDef = ScriptableObject.CreateInstance<UnitDefinitionSO>();
            defenderDef.id = "d"; defenderDef.maxHp = 32;
            var defender = new UnitEntity(9, defenderDef, 7f, 1.4f, 0f, 32, false);

            // A round plunging onto the deck from above.
            var plunge = new ProjectileEntity(90, 7f, 1.45f, 0f, 0f, -9f, 0f, 8, true)
                { PrevX = 7f, PrevY = 2.6f };
            var roofHit = CollisionSystem.ResolveHits(new[] { plunge }, new[] { defender },
                                                      new List<UnitEntity>(), new[] { post });
            Check(roofHit.UnitDamage.ContainsKey(9), "a garrison on the roof CAN be shot directly");
            Check(!roofHit.StructureDamage.ContainsKey(200),
                  "and the round is not swallowed by phantom masonry above the roof");

            // The building itself must still block a round aimed at its BODY.
            var atWall = new ProjectileEntity(91, 6f, 0.7f, 0f, 3f, -3f, 0f, 8, true)
                { PrevX = 5f, PrevY = 1.2f };
            var wallHit = CollisionSystem.ResolveHits(new[] { atWall }, new List<UnitEntity>(),
                                                      new List<UnitEntity>(), new[] { post });
            Check(wallHit.StructureDamage.ContainsKey(200),
                  "the structure still blocks rounds that strike its actual body");

            // And a round passing well ABOVE the roof hits nothing at all.
            var over = new ProjectileEntity(92, 7f, 1.9f, 0f, 3f, 0f, 0f, 8, true)
                { PrevX = 6f, PrevY = 1.9f };
            var overHit = CollisionSystem.ResolveHits(new[] { over }, new List<UnitEntity>(),
                                                      new List<UnitEntity>(), new[] { post });
            Check(overHit.StructureDamage.Count == 0, "a round clearing the roof passes over it");

            var independent = new StructureEntity(5, tierDef, 9f, 0f, 0f, 10);
            var c2 = CollisionSystem.PropagateCollapse(new[] { independent }, new[] { 1 });
            Check(!c2.Contains(5), "an unrelated structure is left standing");
        }

        // --- ProjectileSystem: stepping, culling, and above all the ORDER
        {
            var unitDef = ScriptableObject.CreateInstance<UnitDefinitionSO>();
            unitDef.id = "t"; unitDef.maxHp = 32; unitDef.damage = 8;

            var flying = new ProjectileEntity(1, 0f, 5f, 0f, 4f, 0f, 0f, 8, true);
            var stepped = ProjectileSystem.StepAll(new[] { flying }, 1f / 60f, 0f);
            Check(stepped.Count == 1, "stepping preserves the round");
            Check(stepped[0].PrevX == 0f && stepped[0].PrevY == 5f,
                  "the pre-step position is recorded as Prev (the swept collision segment)");
            Check(stepped[0].X > 0f, "horizontal motion advances");
            Check(stepped[0].Vy < 0f, "gravity is applied to vertical velocity");
            Near(stepped[0].Age, 1f / 60f, 1e-6f, "age accumulates by dt");

            Check(ProjectileSystem.ClampDt(1f) == ProjectileSystem.MaxTickSeconds,
                  "a huge dt is clamped (never sub-stepped — hence swept collision)");

            // THE ORDERING GUARANTEE: a round that crosses y=0 THROUGH a target on the same tick
            // must register the hit. Cull runs last and honours the hit set, so the round is
            // removed as a hit rather than silently vanishing into the floor.
            var target = new UnitEntity(1, unitDef, 0f, 0.2f, 0f, 32, false);
            var plunging = new ProjectileEntity(2, 0f, -0.3f, 0f, 0f, -8f, 0f, 8, true)
                { PrevX = 0f, PrevY = 0.6f, PrevZ = 0f };
            var hits = CollisionSystem.ResolveHits(new[] { plunging }, new[] { target },
                                                   new List<UnitEntity>(), new List<StructureEntity>());
            Check(hits.UnitDamage.ContainsKey(1),
                  "a round crossing the floor THROUGH a target still registers the hit");
            var afterCull = ProjectileSystem.Cull(new[] { plunging }, hits.HitProjectileIds,
                                                  new List<UnitEntity>(), new[] { target },
                                                  new List<StructureEntity>());
            Check(afterCull.Count == 0, "and is then culled as a hit, not as a floor miss");

            // A round that reaches the floor having hit nothing is a ground impact, then culled.
            var missed = new ProjectileEntity(3, 1f, -0.1f, 0f, 0f, -8f, 0f, 8, true)
                { PrevX = 1f, PrevY = 0.5f };
            var noHits = new HashSet<int>();
            var ground = ProjectileSystem.GroundImpacts(new[] { missed }, noHits);
            Check(ground.Count == 1, "a round that reached the floor is a ground impact");
            Check(ProjectileSystem.Cull(new[] { missed }, noHits, new List<UnitEntity>(),
                                        new List<UnitEntity>(), new List<StructureEntity>()).Count == 0,
                  "and is culled");
            Check(ProjectileSystem.GroundImpacts(new[] { missed }, new HashSet<int> { 3 }).Count == 0,
                  "a round that HIT something is not also counted as a ground impact");

            // Side bounds: an overshoot can never hit anything, and must not hold the phase open.
            var player = new UnitEntity(2, unitDef, -8f, 0f, 0f, 32, true);
            var enemy = new UnitEntity(3, unitDef, 8f, 0f, 0f, 32, false);
            var overshot = new ProjectileEntity(4, 50f, 5f, 0f, 9f, 0f, 0f, 8, true);
            var inField = new ProjectileEntity(5, 3f, 5f, 0f, 9f, 0f, 0f, 8, true);
            var kept = ProjectileSystem.Cull(new[] { overshot, inField }, noHits,
                                             new[] { player }, new[] { enemy },
                                             new List<StructureEntity>());
            Check(kept.Count == 1 && kept[0].Id == 5, "a round far past the enemy is culled");

            var behind = new ProjectileEntity(6, -50f, 5f, 0f, -9f, 0f, 0f, 8, false);
            Check(ProjectileSystem.Cull(new[] { behind }, noHits, new[] { player }, new[] { enemy },
                                        new List<StructureEntity>()).Count == 0,
                  "a round far behind the player is culled too");

            // Explosions: advance, then survive exactly ONE extra tick at progress 1.
            var boom = new ExplosionEntity(1, 0f, 0f, 0f) { Progress = 0.9f };
            var t1 = ProjectileSystem.AdvanceExplosions(new[] { boom }, 0.2f);
            Check(t1.Count == 1 && Mathf.Approximately(t1[0].Progress, 1f),
                  "an explosion reaching the end is HELD for one extra tick at progress 1");
            var t2 = ProjectileSystem.AdvanceExplosions(t1, 0.2f);
            Check(t2.Count == 0, "and is removed on the tick after that");
            var mid = ProjectileSystem.AdvanceExplosions(
                new[] { new ExplosionEntity(2, 0f, 0f, 0f) { Progress = 0.1f } }, 0.2f);
            Check(mid.Count == 1 && mid[0].Progress > 0.1f, "an unfinished explosion keeps advancing");
        }

        // --- TurnFlow
        {
            // Win/loss: structures are a damage objective, never a win condition.
            Check(TurnFlow.ResolvePhase(0, 5) == GamePhase.Defeat, "no player units = defeat");
            Check(TurnFlow.ResolvePhase(5, 0) == GamePhase.Victory, "no enemy units = victory");
            Check(TurnFlow.ResolvePhase(5, 5) == GamePhase.Playing, "both alive = still playing");
            Check(TurnFlow.ResolvePhase(0, 0) == GamePhase.Defeat,
                  "a mutual wipe resolves as DEFEAT — the player check comes first");

            // Stars: readable thresholds, not a formula.
            Check(TurnFlow.StarsFor(10, 10) == 3, "no losses = 3 stars");
            Check(TurnFlow.StarsFor(8, 10) == 3, "losing under a quarter still = 3 stars");
            Check(TurnFlow.StarsFor(7, 10) == 2, "losing more than a quarter = 2 stars");
            Check(TurnFlow.StarsFor(4, 10) == 2, "at 40% survival = 2 stars");
            Check(TurnFlow.StarsFor(3, 10) == 1, "below 40% survival = 1 star");
            Check(TurnFlow.StarsFor(1, 0) == 1, "a zero initial count cannot divide by zero");

            // Volley gating — the door gunner and melee blocks come FIRST.
            Check(TurnFlow.EvaluateVolley(3, 3, 0f, TurnSide.Player, 0, 0) == TurnFlow.VolleyGate.Busy,
                  "rounds still in the air keep the volley busy");
            Check(TurnFlow.EvaluateVolley(0, 3, 0f, TurnSide.Player, 0, 0) == TurnFlow.VolleyGate.JustLanded,
                  "the volley emptying this tick starts the post-volley pause");
            Check(TurnFlow.EvaluateVolley(0, 0, 0.8f, TurnSide.Player, 0, 0) == TurnFlow.VolleyGate.Pausing,
                  "the pause runs down before handover");
            Check(TurnFlow.EvaluateVolley(0, 0, 0f, TurnSide.Player, 0, 0) == TurnFlow.VolleyGate.ReadyToHandOver,
                  "an empty sky and an expired pause hands the turn over");
            Check(TurnFlow.EvaluateVolley(0, 0, 0f, TurnSide.Enemy, 2, 0) == TurnFlow.VolleyGate.Busy,
                  "a door gunner mid-burst blocks handover even with an empty sky");
            Check(TurnFlow.EvaluateVolley(0, 0, 0f, TurnSide.Player, 2, 0) == TurnFlow.VolleyGate.ReadyToHandOver,
                  "the gunner block applies to the ENEMY turn only");
            Check(TurnFlow.EvaluateVolley(0, 0, 0f, TurnSide.Player, 0, 1) == TurnFlow.VolleyGate.Busy,
                  "an unresolved melee skirmish blocks handover");

            // Victory award — the ORDERING contract, end to end through the real stores.
            var lvl = ScriptableObject.CreateInstance<LevelDefinitionSO>();
            lvl.id = "turnflow_level"; lvl.levelBase = 100;
            ProgressStore.AllLevels = new List<LevelDefinitionSO> { lvl };
            ProgressStore.ResetAll();

            var first = TurnFlow.AwardVictory(lvl, survivors: 10, initialCount: 10);
            Check(first.Stars == 3, "a clean win awards 3 stars");
            Check(first.BonusTag == "Daily Bonus!",
                  "the daily bonus overwrites the tag — only one banner is shown");
            Check(first.Coins == 450 + 50, "first 3-star clear + daily = 450 + 50");
            Check(ProgressStore.BestStars(lvl.id) == 3, "the star result is recorded");

            var second = TurnFlow.AwardVictory(lvl, survivors: 10, initialCount: 10);
            Check(second.Coins == 200, "a repeat clear pays the multiplier only, daily spent");
            Check(second.BonusTag == null, "and carries no bonus banner");

            ProgressStore.ResetAll();
            Check(TurnFlow.AwardDefeat(lvl, new GameState()).Coins == 15,
                  "defeat still pays 15% of base");

            // Encounter ammo: AP after L2, Incendiary after L4, pre-select only if the
            // player has never tapped a chip. A fake levelNumber 0 must not grant (the
            // checks above already ran AwardVictory on one).
            ProgressStore.ResetAll();
            var l2 = ScriptableObject.CreateInstance<LevelDefinitionSO>();
            l2.id = "encounter_l2"; l2.levelNumber = 2; l2.levelBase = 40;
            var l4 = ScriptableObject.CreateInstance<LevelDefinitionSO>();
            l4.id = "encounter_l4"; l4.levelNumber = 4; l4.levelBase = 80;
            ProgressStore.AllLevels = new List<LevelDefinitionSO> { l2, l4 };
            TurnFlow.AwardVictory(l2, survivors: 8, initialCount: 8);
            Check(ProgressStore.IsAmmoUnlocked(AmmoType.AP),
                  "clearing L2 unlocks AP — the next fight is a structure");
            Check(ProgressStore.SelectedAmmo() == AmmoType.AP,
                  "AP is pre-selected after L2 while the player has never picked");
            Check(!ProgressStore.IsAmmoUnlocked(AmmoType.Incendiary),
                  "Incendiary stays locked until L4");
            TurnFlow.AwardVictory(l4, survivors: 8, initialCount: 8);
            Check(ProgressStore.IsAmmoUnlocked(AmmoType.Incendiary),
                  "clearing L4 unlocks Incendiary");
            Check(ProgressStore.SelectedAmmo() == AmmoType.Incendiary,
                  "Incendiary replaces the previous gift when the player never tapped");
            ProgressStore.ResetAll();
            ProgressStore.UnlockAmmo(AmmoType.AP);
            ProgressStore.SetSelectedAmmo(AmmoType.AP);
            ProgressStore.MarkAmmoPickedByPlayer();
            TurnFlow.AwardVictory(l4, survivors: 8, initialCount: 8);
            Check(ProgressStore.IsAmmoUnlocked(AmmoType.Incendiary),
                  "L4 still unlocks Incendiary after a player pick");
            Check(ProgressStore.SelectedAmmo() == AmmoType.AP,
                  "a player ammo choice is not overwritten by the L4 gift");
            var l5 = ScriptableObject.CreateInstance<LevelDefinitionSO>();
            l5.levelNumber = 5;
            EncounterUnlocks.GrantAmmoForLevel(l5);
            Check(ProgressStore.SelectedAmmo() == AmmoType.AP,
                  "reaching L5 does not steal a player-picked AP");
            ProgressStore.ResetAll();
            EncounterUnlocks.GrantAmmoForLevel(l5);
            Check(ProgressStore.IsAmmoUnlocked(AmmoType.AP)
                  && ProgressStore.IsAmmoUnlocked(AmmoType.Incendiary),
                  "reaching L5 without playing L2/L4 still hands over both gifts");
            Check(ProgressStore.SelectedAmmo() == AmmoType.Incendiary,
                  "the later gift is the one pre-selected when both arrive at once");
            Object.DestroyImmediate(l2);
            Object.DestroyImmediate(l4);
            Object.DestroyImmediate(l5);
            ProgressStore.ResetAll();

            // Star REASONS — PRODUCT_DIRECTION 0.5 shows the player why, every time, and the
            // number it promises has to be the number the award code actually pays on.
            bool survivorsAgree = true;
            for (int n = 1; n <= 30; n++)
                for (int want = 2; want <= 3; want++)
                {
                    int need = TurnFlow.SurvivorsFor(want, n);
                    if (TurnFlow.StarsFor(need, n) < want) survivorsAgree = false;
                    if (need > 0 && TurnFlow.StarsFor(need - 1, n) >= want) survivorsAgree = false;
                }
            Check(survivorsAgree,
                  "SurvivorsFor is the FEWEST survivors StarsFor still rewards, for every roster " +
                  "size — the promise on the victory card and the payout cannot disagree");

            Check(TurnFlow.SurvivorsFor(3, 14) == 11, "3★ on a 14-unit roster needs 11 alive");
            Check(TurnFlow.StarReason(10, 14) == "Lost 4 of 14 — keep 11 alive for 3 stars",
                  "a 2-star result names the shortfall and the next threshold in whole units");
            Check(TurnFlow.StarReason(10, 14).IndexOf('★') < 0,
                  "and says it in ASCII — the default TMP font renders ★ as a missing-glyph box");
            Check(TurnFlow.StarReason(14, 14).Contains("14 of 14"),
                  "a clean sweep reports what was kept and promises nothing further");
            Check(!TurnFlow.StarReason(14, 14).Contains("for 4"),
                  "and never dangles a fourth star");
            Check(TurnFlow.StarReason(3, 0) == "", "a zero-unit roster has no reason to give");

            // FAIL SEQUENCE — teaches the blow, nudges a sold consumable after a repeat,
            // never Overwatch Flare. First loss has no nudge; a win clears the streak.
            Check(TurnFlow.DefeatReason(new GameState
                  { LastPlayerDeathCause = CasualtyCause.Charge }) == TurnFlow.DefeatCharge,
                  "a melee wipe names the charge");
            Check(TurnFlow.DefeatReason(new GameState
                  { LastPlayerDeathCause = CasualtyCause.Volley }) == TurnFlow.DefeatVolley,
                  "a shooting wipe names the volley");
            var garrison = new UnitEntity(1, null, 4f, 2f, 0f, 8, false)
                { StandingOnStructureId = 9 };
            Check(TurnFlow.DefeatReason(new GameState
                  {
                      LastPlayerDeathCause = CasualtyCause.Charge,
                      EnemyUnits = new List<UnitEntity> { garrison },
                  }) == TurnFlow.DefeatCharge,
                  "a melee wipe names the charge even if a garrison is left");
            Check(TurnFlow.DefeatReason(new GameState
                  {
                      LastPlayerDeathCause = CasualtyCause.Volley,
                      EnemyUnits = new List<UnitEntity> { garrison },
                  }) == TurnFlow.DefeatGarrison,
                  "a leftover majority garrison outranks a volley line");
            Check(TurnFlow.DefeatReason(new GameState()) == TurnFlow.DefeatOverrun,
                  "an empty leftover with no recorded blow uses the overrun line");
            Check(TurnFlow.NudgeItem(new GameState
                  { LastPlayerDeathCause = CasualtyCause.Charge }) == ConsumableType.TraumaKit,
                  "a charge loss nudges Trauma Kit");
            Check(TurnFlow.NudgeItem(new GameState
                  {
                      LastPlayerDeathCause = CasualtyCause.Volley,
                      EnemyUnits = new List<UnitEntity> { garrison },
                  }) == ConsumableType.Airstrike,
                  "a garrison leftover nudges Airstrike");
            Check(TurnFlow.NudgeItem(new GameState
                  { LastPlayerDeathCause = CasualtyCause.Volley }) == ConsumableType.SmokeScreen,
                  "a volley loss nudges Smoke Screen");
            Check(TurnFlow.NudgeItem(new GameState()) != ConsumableType.OverwatchFlare
                  && TurnFlow.NudgeItem(new GameState
                      { LastPlayerDeathCause = CasualtyCause.Charge })
                      != ConsumableType.OverwatchFlare,
                  "the nudge never sells Overwatch Flare");
            Check(TurnFlow.NudgeLine(ConsumableType.TraumaKit, 0).IndexOf("would") >= 0,
                  "an unowned kit is offered as a would-have-helped, not a wall");
            Check(TurnFlow.NudgeLine(ConsumableType.TraumaKit, 1).IndexOf("You have") >= 0,
                  "an owned kit is a take-it-on-retry, not a shop trip");
            Check(TurnFlow.NudgeLine(ConsumableType.Airstrike, 1).IndexOf("an Airstrike") >= 0,
                  "owned Airstrike uses an, not a — seen on device as 'a Airstrike'");

            ProgressStore.AllLevels = new List<LevelDefinitionSO> { lvl };
            ProgressStore.ResetAll();
            var miss = TurnFlow.AwardDefeat(lvl, new GameState
                { LastPlayerDeathCause = CasualtyCause.Volley });
            Check(miss.Nudge == null, "the first loss has no consumable nudge");
            Check(ProgressStore.FailStreak(lvl.id) == 1, "and starts a streak of 1");
            var repeat = TurnFlow.AwardDefeat(lvl, new GameState
                { LastPlayerDeathCause = CasualtyCause.Volley });
            Check(repeat.Nudge != null && repeat.Nudge.IndexOf("Smoke") >= 0,
                  "the second loss on the same level offers Smoke");
            Check(ProgressStore.FailStreak(lvl.id) == TurnFlow.FailNudgeAfter,
                  "the streak is the threshold, not one past it");
            TurnFlow.AwardVictory(lvl, survivors: 10, initialCount: 10);
            Check(ProgressStore.FailStreak(lvl.id) == 0, "a win clears the fail streak");
            var afterWin = TurnFlow.AwardDefeat(lvl, new GameState
                { LastPlayerDeathCause = CasualtyCause.Charge });
            Check(afterWin.Nudge == null, "a loss after a win is a miss again, not a streak");
            Check(afterWin.Reason == TurnFlow.DefeatCharge,
                  "and still names the charge that ended it");

            // PUT IT IN A STATE WHERE IT COULD FAIL: nothing owned in the real economy, RIGS on.
            // The you-have-one branch is the one that read "a Airstrike" on device, and without
            // the test supply reaching AwardDefeat it cannot be shown on a release build at all
            // without earning 250 coins. afterWin left the streak at 1, so this loss is the
            // repeat that arms the nudge.
            var garrisonLoss = new GameState
            {
                LastPlayerDeathCause = CasualtyCause.Volley,
                EnemyUnits = new List<UnitEntity> { garrison },
            };
            Check(ProgressStore.OwnedConsumables(ConsumableType.Airstrike) == 0,
                  "the economy really is empty — the supply below is RIGS, not a balance");
            var onRigs = TurnFlow.AwardDefeat(lvl, garrisonLoss, testSupply: true);
            Check(onRigs.Nudge != null && onRigs.Nudge.Contains("You have an Airstrike"),
                  "RIGS carries the fail card too — it reads 'You have an Airstrike', the "
                  + "line that shipped as 'a Airstrike'");
            Check(ProgressStore.OwnedConsumables(ConsumableType.Airstrike) == 0,
                  "and the test supply still writes nothing to the economy");
            var offRigs = TurnFlow.AwardDefeat(lvl, garrisonLoss);
            Check(offRigs.Nudge != null && offRigs.Nudge.Contains("would"),
                  "with RIGS off and none owned it is still a would-have-helped, not a lie");

            ProgressStore.ResetAll();
            ProgressStore.AllLevels = new List<LevelDefinitionSO>();
        }

        // --- CameraDirector
        {
            var ud = ScriptableObject.CreateInstance<UnitDefinitionSO>();
            ud.id = "u"; ud.maxHp = 32;

            // Per-phase framing: Aiming frames the PLAYER LINE ONLY, scout frames the enemy.
            float p = 3f, e = 9f, shooter = 5f, march = 6f, reinforce = 7f;
            Check(CameraDirector.PhaseHalfWidth(TurnPhase.Aiming, TurnSide.Player,
                      p, e, shooter, march, false, reinforce, false) == p,
                  "Aiming frames the player line only");
            Check(CameraDirector.PhaseHalfWidth(TurnPhase.PlayerScout, TurnSide.Player,
                      p, e, shooter, march, false, reinforce, false) == e,
                  "PlayerScout frames the enemy cluster");

            // THE OPENING SCOUT RUNS. LoadLevel used to jump to Aiming, so the player never
            // saw the layout. Rob, 2026-08-13. The tick must hold PlayerScout for the beat
            // and refuse a volley, then hand over to Aiming.
            {
                var l1 = AssetDatabase.LoadAssetAtPath<LevelDefinitionSO>(
                    "Assets/GameData/Levels/PatrolEncounter.asset");
                Check(l1 != null, "L1 exists so the scout beat can be stepped");
                if (l1 != null)
                {
                    var scout = LevelBuilder.BuildInitialState(l1, 90, 12, new System.Random(1))
                        with
                        {
                            Phase = GamePhase.Playing,
                            TurnPhase = TurnPhase.PlayerScout,
                            ScoutTimer = TurnFlow.PlayerScoutSeconds,
                        };
                    var fired = BattleTick.FireVolley(scout, new Vector3(8f, 8f, 0f),
                                                      new System.Random(1));
                    Check(fired.TurnPhase == TurnPhase.PlayerScout
                          && fired.Projectiles.Count == 0,
                          "a drag during the scout does not fire");
                    for (int i = 0; i < 60; i++)
                        scout = BattleTick.Step(scout, 1f / 60f, l1, new System.Random(1));
                    Check(scout.TurnPhase == TurnPhase.PlayerScout && scout.ScoutTimer > 1f,
                          $"the scout still holds at 1s ({scout.ScoutTimer:F2}s left)");
                    for (int i = 0; i < 90; i++)
                        scout = BattleTick.Step(scout, 1f / 60f, l1, new System.Random(1));
                    Check(scout.TurnPhase == TurnPhase.Aiming && scout.ScoutTimer <= 0f,
                          "then the first aim is handed over");

                    // TANK ROLL-IN. A level with a cannon starts left of its park; the crew
                    // ride the same delta; a drag does not fire; after the beat the vehicle
                    // is ON its authored slot and the signed-off scout begins.
                    var arrive = TurnFlow.StartBattle(
                        LevelBuilder.BuildInitialState(l1, 91, 12, new System.Random(1)));
                    var tank0 = arrive.Structures.First(st => st.Definition.hasCannon);
                    var riders0 = arrive.PlayerUnits
                        .Where(u => u.StandingOnStructureId == tank0.Id).ToList();
                    Check(arrive.TurnPhase == TurnPhase.TankArrive
                          && Mathf.Abs(tank0.X - (arrive.TankParkX - TurnFlow.TankArriveDistance)) < 0.001f,
                          $"L1 opens on the tank roll ({arrive.TurnPhase}, x {tank0.X:F2} vs park {arrive.TankParkX:F2})");
                    Check(riders0.Count > 0 && riders0.All(u => u.X < arrive.TankParkX),
                          $"the crew ride the hull ({riders0.Count} on deck, starting left of the park)");
                    var arriveFired = BattleTick.FireVolley(arrive, new Vector3(8f, 8f, 0f),
                                                            new System.Random(1));
                    Check(arriveFired.TurnPhase == TurnPhase.TankArrive
                          && arriveFired.Projectiles.Count == 0,
                          "a drag during the roll-in does not fire");
                    var parked = arrive;
                    int arriveSteps = Mathf.CeilToInt(TurnFlow.TankArriveSeconds * 60f) + 2;
                    for (int i = 0; i < arriveSteps; i++)
                        parked = BattleTick.Step(parked, 1f / 60f, l1, new System.Random(1));
                    var tank1 = parked.Structures.First(st => st.Definition.hasCannon);
                    var riders1 = parked.PlayerUnits
                        .Where(u => u.StandingOnStructureId == tank1.Id).ToList();
                    float gap0 = riders0[0].X - tank0.X;
                    float gap1 = riders1[0].X - tank1.X;
                    Check(parked.TurnPhase == TurnPhase.PlayerScout
                          && Mathf.Abs(tank1.X - parked.TankParkX) < 0.02f,
                          $"the tank parks and the scout begins (phase {parked.TurnPhase}, x {tank1.X:F2})");
                    Check(Mathf.Abs(gap1 - gap0) < 0.02f,
                          $"the crew stay on the deck (gap {gap1:F2} vs {gap0:F2})");
                    var line0 = arrive.PlayerUnits.Where(u => u.StandingOnStructureId == null).ToList();
                    var line1 = parked.PlayerUnits.Where(u => u.StandingOnStructureId == null).ToList();
                    Check(line0.Count > 0 && line0.All(u => u.MarchTargetX != null),
                          $"the ground line jogs in ({line0.Count} with a march slot)");
                    Check(line1.All(u => u.MarchTargetX == null)
                          && Mathf.Abs(line1[0].X - line0[0].MarchTargetX.Value) < 0.02f,
                          $"the ground line is on its slots when the scout begins");
                }
            }
            Check(CameraDirector.PhaseHalfWidth(TurnPhase.Aiming, TurnSide.Player,
                      p, e, shooter, march, false, reinforce, true) == reinforce,
                  "Aiming widens while reinforcements are still marching in");
            Check(CameraDirector.PhaseHalfWidth(TurnPhase.EnemyWindup, TurnSide.Enemy,
                      p, e, shooter, march, false, reinforce, false) == shooter,
                  "EnemyWindup frames the SHOOTERS when nobody is marching");
            Check(CameraDirector.PhaseHalfWidth(TurnPhase.EnemyWindup, TurnSide.Enemy,
                      p, e, shooter, march, true, reinforce, false) == march,
                  "EnemyWindup follows the marchers when any are moving");
            Check(CameraDirector.PhaseHalfWidth(TurnPhase.Resolving, TurnSide.Enemy,
                      p, e, shooter, march, false, reinforce, false) == p,
                  "Resolving an ENEMY volley frames the player being shot at");
            Check(CameraDirector.PhaseHalfWidth(TurnPhase.Resolving, TurnSide.Player,
                      p, e, shooter, march, false, reinforce, false) == e,
                  "Resolving a PLAYER volley frames the enemy being shot at");

            // A settled melee unit far up the field must not widen the shooter frame.
            var shooters = new List<float> { 6f, 6.5f, 7f };
            var withStructures = new List<float> { 6f, 6.5f, 7f, 8f };
            float reachNoMelee = CameraDirector.ShooterReach(shooters, withStructures);
            float reachWithMelee = CameraDirector.ShooterReach(shooters,
                withStructures.Concat(new[] { 8f }).ToList());
            Near(reachNoMelee, reachWithMelee, 1e-5f,
                 "a melee unit excluded from the shooter set cannot widen the frame");
            Check(CameraDirector.ShooterReach(new List<float>(), withStructures) == 0f,
                  "no shooters means no shooter reach");

            // March framing has a FLOOR so escorting one unit does not zoom to a keyhole.
            Check(CameraDirector.MarchHalfWidth(new List<float> { 5f }, new List<float>())
                      == CameraDirector.MarchHalfWidthMin,
                  "a single marcher still gets the minimum march frame");
            Check(CameraDirector.MarchHalfWidth(new List<float> { 0f, 20f }, new List<float>()) == 10f,
                  "a wide march spreads past the floor");

            // MARCH vs CONTACT. A far escort must not pull the camera back to the tank;
            // a fight at the line must still hold the whole player force.
            {
                var tankAndLine = new List<float> { -9.5f, -7.2f, -6.6f, -6.0f };
                float marchHalf = CameraDirector.AssaultFrame(
                    new List<float> { 3.2f, 3.5f, 3.8f }, new List<float>(),
                    tankAndLine, out float marchAt);
                Check(Mathf.Abs(marchAt - 3.5f) < 0.5f
                      && marchHalf <= CameraDirector.MarchHalfWidthMin + 0.01f
                      && Mathf.Abs(-9.5f - marchAt) > marchHalf,
                      $"a distant march frames the CHARGERS, not the tank " +
                      $"(cam {marchAt:F2} ±{marchHalf:F2})");

                float fightAt = tankAndLine.Max() + 0.75f;
                float fightHalf = CameraDirector.AssaultFrame(
                    new List<float>(), new List<float> { fightAt, tankAndLine.Max() },
                    tankAndLine, out float fightCam);
                Check(tankAndLine.All(x => Mathf.Abs(x - fightCam) <= fightHalf)
                      && Mathf.Abs(fightAt - fightCam) <= fightHalf,
                      $"contact still frames the WHOLE player force " +
                      $"(cam {fightCam:F2} ±{fightHalf:F2})");
            }

            // Half-width -> camera z, clamped into the usable band.
            Near(CameraDirector.TargetZ(4.5f, false, 19f), 10f, 1e-4f,
                 "half-width converts to distance through the half-FOV tangent");
            Check(CameraDirector.TargetZ(0.1f, false, 19f) == CameraDirector.ZMin,
                  "a tiny frame is clamped to the near limit");
            Check(CameraDirector.TargetZ(100f, false, 19f) == CameraDirector.GameplayZ,
                  "a huge frame is clamped to the far limit");
            Check(CameraDirector.TargetZ(100f, true, 12f) == 12f,
                  "staticCamera caps the zoom at the battlefield width");
            // The static zoom-in floor is applied AFTER the global ZMin clamp, so at ordinary
            // battlefield widths ZMin dominates and the static floor is inert — which is what
            // CLAUDE.md records about this flag on real levels.
            Check(CameraDirector.TargetZ(0.1f, true, 12f) == CameraDirector.ZMin,
                  "at a normal battlefield width the global near limit still wins");
            Check(CameraDirector.TargetZ(0.1f, true, 40f) == 40f * CameraDirector.StaticCameraZoomInFraction,
                  "on a very wide battlefield the static floor does bite (40 * 0.2 = 8 > ZMin)");

            // Volley follow: MONOTONIC pursuit — the camera only moves the way the volley flies.
            var pl = new List<UnitEntity> { new(1, ud, -8f, 0f, 0f, 32, true) };
            var en = new List<UnitEntity> { new(2, ud, 8f, 0f, 0f, 32, false) };
            var st = new List<StructureEntity>();
            var rounds = new List<ProjectileEntity>
            {
                new(1, 0f, 3f, 0f, 5f, 0f, 0f, 8, true),
            };
            float x0 = CameraDirector.FollowVolley(null, 0f, rounds, pl, en, st, false,
                                                   1f / 60f, out float v0);
            Near(x0, 0f, 1e-3f, "a fresh follow starts at the volley mean");

            // A player volley (flying right) must never be dragged BACKWARDS by a falling mean.
            var retreating = new List<ProjectileEntity>
            {
                new(1, -5f, 3f, 0f, 5f, 0f, 0f, 8, true),
            };
            float held = CameraDirector.FollowVolley(2f, 0f, retreating, pl, en, st, false,
                                                     1f / 60f, out _);
            Check(held >= 2f - 1e-4f,
                  "a rightward volley never drags the camera backwards (monotonic pursuit)");

            var enemyRounds = new List<ProjectileEntity>
            {
                new(1, 5f, 3f, 0f, -5f, 0f, 0f, 8, false),
            };
            float heldLeft = CameraDirector.FollowVolley(-2f, 0f, enemyRounds, pl, en, st, false,
                                                        1f / 60f, out _);
            Check(heldLeft <= -2f + 1e-4f, "a leftward enemy volley is monotonic the other way");

            // A melee reset drops the carried velocity, so the spring does not fling.
            CameraDirector.FollowVolley(5f, 99f, rounds, pl, en, st, true, 1f / 60f, out float vReset);
            Check(Mathf.Abs(vReset) < 50f, "a melee reset discards the carried velocity");
        }

        // --- CosmeticSystems
        {
            // RATE INDEPENDENCE: the same friction must decay the same amount per SECOND
            // regardless of tick rate, or a ragdoll that rolls on one device skids on another.
            float at60 = CosmeticSystems.DecayPerTick60(0.962f, 1f / 60f);
            Near(at60, 0.962f, 1e-5f, "at 60Hz the per-tick factor is used as authored");
            float oneSecAt60 = Mathf.Pow(CosmeticSystems.DecayPerTick60(0.962f, 1f / 60f), 60);
            float oneSecAt120 = Mathf.Pow(CosmeticSystems.DecayPerTick60(0.962f, 1f / 120f), 120);
            float oneSecAt30 = Mathf.Pow(CosmeticSystems.DecayPerTick60(0.962f, 1f / 30f), 30);
            Near(oneSecAt120, oneSecAt60, 1e-4f, "120Hz decays the same amount per second as 60");
            Near(oneSecAt30, oneSecAt60, 1e-4f, "30Hz decays the same amount per second as 60");

            // SHAKE must reach exactly zero, and must decay on EVERY tick path — a level ending
            // on a killing volley froze it forever and jittered the whole victory screen.
            Near(CosmeticSystems.AddShakeForKills(0f, 1), CosmeticSystems.ShakePerKill, 1e-5f,
                 "one kill is the per-kill punch only");
            Near(CosmeticSystems.AddShakeForKills(0f, 2), 2f * CosmeticSystems.ShakePerKill, 1e-5f,
                 "two kills do not yet add the multi-kill bonus");
            Near(CosmeticSystems.AddShakeForKills(0f, 3),
                 3f * CosmeticSystems.ShakePerKill + CosmeticSystems.ShakeMultiKillBonus, 1e-5f,
                 "three kills add the multi-kill punch — one scream is not a volley");
            float shake = CosmeticSystems.AddShakeForKills(0f, 4);
            Near(shake, 4f * CosmeticSystems.ShakePerKill + CosmeticSystems.ShakeMultiKillBonus, 1e-5f,
                 "four kills raise the shake");
            for (int i = 0; i < 200; i++) shake = CosmeticSystems.DecayShake(shake, 1f / 60f);
            Check(shake == 0f, "shake decays to EXACTLY zero, never a lingering epsilon");
            Check(CosmeticSystems.DecayShake(0f, 1f) == 0f, "decaying past zero cannot go negative");

            // HEALTH BAR CLOCK. Same failure shape as the shake: something the renderer tests for
            // and nothing clears leaves a bar over a soldier for the rest of the battle. -1 is the
            // only "off" value and it has to come back on its own.
            Check(CosmeticSystems.StepHitAge(-1f, 1f / 60f) == -1f,
                  "a unit that has not been hit never shows a bar");
            float age = 0f;
            int frames = 0;
            while (age >= 0f && frames < 100000) { age = CosmeticSystems.StepHitAge(age, 1f / 60f); frames++; }
            Check(age == -1f, "the bar retires, and at exactly -1 rather than an epsilon");
            Near(frames / 60f, CosmeticSystems.HealthBarSeconds, 2f / 60f,
                 $"the bar lasts its stated few seconds ({frames / 60f:F2}s)");
            // Rate independence — the panel is pinned to 60 today and must not be relied on.
            float at30 = 0f;
            int f30 = 0;
            while (at30 >= 0f && f30 < 100000) { at30 = CosmeticSystems.StepHitAge(at30, 1f / 30f); f30++; }
            Near(f30 / 30f, frames / 60f, 1f / 15f, "the bar lasts the same TIME at 30Hz");
            // And it FADES rather than blinking out: full early, zero at the end.
            Check(CosmeticSystems.HealthBarAlpha(0f) == 1f, "a fresh bar is fully opaque");
            Near(CosmeticSystems.HealthBarAlpha(CosmeticSystems.HealthBarSeconds), 0f, 1e-4f,
                 "and has faded to nothing by the time it retires");
            Check(CosmeticSystems.HealthBarAlpha(CosmeticSystems.HealthBarSeconds - 0.35f) < 1f,
                  "the last stretch is a fade, not a cut");
            // The TRACK must never outlive the FILL. Equal alpha is not equal legibility: a
            // near-black track holds contrast against every ground in this game long after a
            // saturated fill has washed out, so fading them together ends the bar as a black
            // rectangle — reported as "black means dead, right?".
            bool trackNeverLeads = true;
            for (float t = 0f; t <= CosmeticSystems.HealthBarSeconds; t += 0.05f)
                if (CosmeticSystems.HealthBarTrackAlpha(t) > CosmeticSystems.HealthBarAlpha(t) + 1e-5f)
                    trackNeverLeads = false;
            Check(trackNeverLeads, "the empty track is never more opaque than the fill on it");

            // RAGDOLL LEAN. A fraction of the tumble, capped — the two failure modes are the full
            // spin (a body that folds AND cartwheels) and zero (a statue flying backwards on
            // rails), and both have shipped.
            Check(CosmeticSystems.RagdollLeanDegrees(0f, true) == 0f,
                  "a body starts its fall upright");
            Check(Mathf.Abs(CosmeticSystems.RagdollLeanDegrees(180f, true)) < 0.5f,
                  "and a body that flopped to 180 lies FLAT — 180 is the other lying pose, "
                  + "not a 38-degree prop");
            Check(Mathf.Abs(CosmeticSystems.RagdollLeanDegrees(220f, true)) > 5f,
                  "and leans measurably within the first second");
            for (float spun = 0f; spun < 4000f; spun += 37f)
                Check(Mathf.Abs(CosmeticSystems.RagdollLeanDegrees(spun, true))
                          <= CosmeticSystems.RagdollLeanMaxDegrees + 1e-3f,
                      spun == 0f ? "the lean is CAPPED, so it never winds up into a cartwheel" : null);
            Check(CosmeticSystems.RagdollLeanDegrees(500f, true)
                  * CosmeticSystems.RagdollLeanDegrees(500f, false) < 0f,
                  "the two sides tip opposite ways, each the way it is thrown");
            var flyPose = new DyingUnitEntity(1, null, false, 0f, 3f, 0f, 1f, 1f, 140f)
            { Rotation = 40f, Yaw = 12f, SettleTilt = -8f, SupportY = -1f };
            var eul = CosmeticSystems.RagdollVisualEuler(flyPose);
            Check(Mathf.Approximately(eul.z, -40f) && Mathf.Approximately(eul.y, 12f)
                  && Mathf.Approximately(eul.x, -8f),
                  "airborne draw is the live 3-axis tumble, not a sit-down clip");

            // EACH BODY TAKES ITS OWN PATH. The tick used to throw every corpse with
            // Vx=±1.5, Vy=2.5, spin=220, so a volley fell as one chorus line at one angle.
            // Rob, 2026-08-13. Assert the OUTPUT (two neighbours differ; a rank fans) not
            // that a hash function exists.
            {
                var a = CosmeticSystems.ImpulseFor(10, false);
                var again = CosmeticSystems.ImpulseFor(10, false);
                var b = CosmeticSystems.ImpulseFor(11, false);
                Check(a.Vx == again.Vx && a.Vy == again.Vy && a.Vz == again.Vz,
                      "the same body always takes the same path");
                Check(a.Vx != b.Vx || a.Vy != b.Vy || a.Vz != b.Vz,
                      "two neighbours do not share a launch");
                Check(a.Vx > 0f && CosmeticSystems.ImpulseFor(10, true).Vx < 0f,
                      "each side is still thrown BACKWARDS");
                var seen = new HashSet<string>();
                for (int id = 1; id <= 16; id++)
                {
                    var i = CosmeticSystems.ImpulseFor(id, false);
                    seen.Add($"{i.Vx:F3}/{i.Vy:F3}/{i.Vz:F3}/{i.RotationSpeed:F0}");
                }
                Check(seen.Count >= 14,
                      $"a dying rank fans out ({seen.Count} distinct launches in 16 bodies)");
                Check(CosmeticSystems.DiesInATumble(1.4f, 3) && CosmeticSystems.DiesInATumble(0.6f, null)
                      && !CosmeticSystems.DiesInATumble(0f, null),
                      "deck and garrison deaths tumble; dirt deaths do not");
                var dirt = CosmeticSystems.ImpulseFor(10, false, tumble: false);
                var deck = CosmeticSystems.ImpulseFor(10, false, tumble: true);
                Check(dirt.Vy < 0.4f && dirt.Vx < 0f
                      && deck.Vy > dirt.Vy + 1f
                      && Mathf.Abs(dirt.YawSpeed) < 1f && Mathf.Abs(deck.YawSpeed) > 10f,
                      $"dirt tips AWAY from the building ({dirt.Vx:F2}, {dirt.Vy:F2} up); "
                      + $"a deck fall launches ({deck.Vy:F2})");
            }
            Check(CosmeticSystems.HealthBarTrackAlpha(CosmeticSystems.HealthBarSeconds - 0.2f)
                  < CosmeticSystems.HealthBarAlpha(CosmeticSystems.HealthBarSeconds - 0.2f),
                  "and it is visibly GONE first, so a bar dissolves to colour, not to black");

            CheckFlame();

            // Ragdoll rest height: a body must never sink through the floor at any rotation.
            for (int deg = 0; deg < 360; deg += 7)
            {
                float restY = CosmeticSystems.RagdollRestY(deg);
                Check(restY >= -1e-6f, deg == 0 ? "rest height is never negative" : null);
                if (restY < -1e-6f) break;
            }
            Check(CosmeticSystems.RagdollRestY(90f) > CosmeticSystems.RagdollRestY(0f),
                  "a body propped upright rests HIGHER than one lying flat");
            Near(CosmeticSystems.RagdollRestY(0f), CosmeticSystems.RagdollBodyHalfWidth, 1e-5f,
                 "a flat body rests on the ground");
            Near(CosmeticSystems.RagdollRestY(180f), CosmeticSystems.RagdollBodyHalfWidth, 1e-5f,
                 "and so does a body that flopped the other way — 180 is not a 0.5 hover");

            // --- A CORPSE MUST NOT LEVITATE ONTO A ROOF IT NEVER REACHED.
            //
            // Reported by Rob 2026-08-07 as "dead units can have physically impossible
            // interactions with structures". BlockOnStructures rested a body on a structure's
            // ROOF whenever it was horizontally over the footprint and at or above the box's
            // BASE — and a ground structure's base is the ground, so ANY corpse whose x fell
            // inside a building's footprint was snapped to roof height. The condition's own
            // comment says "a body that CLEARED THE WALL should land on the roof"; `y >= baseY`
            // is not that test, `y >= topY` is.
            //
            // Driven through the real Step, because the bug lives in the interaction and not in
            // either piece alone.
            {
                var corpseDef = ScriptableObject.CreateInstance<UnitDefinitionSO>();
                corpseDef.id = "corpse"; corpseDef.maxHp = 32; corpseDef.damage = 8;
                var towerDef = ScriptableObject.CreateInstance<StructureDefinitionSO>();
                towerDef.id = "tower"; towerDef.maxHp = 100; towerDef.size = 4f;
                towerDef.hasHitWidth = true; towerDef.hitWidth = 3f;
                // Ragdolls collide with standWidth, not hitWidth. Match the
                // projectile box so the existing face/roof/lip numbers hold.
                towerDef.standWidth = 3f; towerDef.isPlayerSide = false;

                // Box: baseY 0, topY 4, x from 3.5 to 6.5.
                var tower = new StructureEntity(900, towerDef, 5f, 2f, 0f, 100);
                CollisionSystem.StructureBox(tower, out _, out _, out float bY, out float tY);
                Check(Mathf.Approximately(bY, 0f) && Mathf.Approximately(tY, 4f),
                      $"test tower box is base {bY:F1} roof {tY:F1}");

                // A body IN THE AIR, thrown into the tower's face well BELOW its roof — the
                // real case. It is at y 1.5 against a roof of 4, arriving from the left. It must
                // be stopped by the face and fall; it must NOT be lifted up the wall.
                var thrown = new DyingUnitEntity(1, corpseDef, false, 3.4f, 1.5f, 0f, 4f, 0f, 0f)
                    { Tumble = true };
                var st = new GameState
                {
                    Phase = GamePhase.Playing,
                    Structures = new List<StructureEntity> { tower },
                    DyingUnits = new List<DyingUnitEntity> { thrown },
                };
                // Stepped until it actually crosses into the box — one tick is not enough to
                // reach the face, and a check that never reaches the code it is testing is the
                // kind of green light this repo has been burned by before.
                float peakY = 0f;
                var walk = st;
                for (int i = 0; i < 40; i++)
                {
                    walk = BattleTick.Step(walk, 1f / 60f, null, new System.Random(1));
                    var b = walk.DyingUnits.FirstOrDefault(d => d.Id == 1);
                    if (b == null) break;
                    peakY = Mathf.Max(peakY, b.Y);
                }
                Check(peakY <= 1.5f + 1e-3f,
                      $"a corpse thrown into a wall BELOW the roof is never lifted up it " +
                      $"(peak y {peakY:F2} from 1.50, roof {tY:F1})");

                // And the behaviour that must SURVIVE the fix: a body genuinely above the roof
                // still lands on it rather than falling through.
                var overRoof = new DyingUnitEntity(2, corpseDef, false, 5f, 5f, 0f, 0f, -3f, 0f);
                var st2 = st with { DyingUnits = new List<DyingUnitEntity> { overRoof } };
                for (int i = 0; i < 120; i++) st2 = BattleTick.Step(st2, 1f / 60f, null, new System.Random(1));
                var landed = st2.DyingUnits.FirstOrDefault(d => d.Id == 2);
                Check(landed == null || landed.Y >= tY - 0.05f,
                      $"a body falling from ABOVE still rests on the roof (y {landed?.Y:F2} " +
                      $"vs roof {tY:F1})");

                // Depth scatter is live, not a field nobody integrates. A body thrown only in
                // Z must actually travel in Z — the old Step wrote X/Y and left Vz sitting.
                var shoved = new DyingUnitEntity(3, corpseDef, false, 0f, 2f, 0f, 0f, 1f, 0f)
                    { Vz = 2f };
                var stZ = st with { DyingUnits = new List<DyingUnitEntity> { shoved },
                                    Structures = new List<StructureEntity>() };
                for (int i = 0; i < 12; i++)
                    stZ = BattleTick.Step(stZ, 1f / 60f, null, new System.Random(1));
                var moved = stZ.DyingUnits.FirstOrDefault(d => d.Id == 3);
                Check(moved != null && moved.Z > 0.3f,
                      $"a body thrown in Z travels in Z (z {moved?.Z:F2} from 0 after 0.2s)");

                // A BODY ON A ROOF IS NOT AIRBORNE. The renderer used to compare Y to
                // RagdollRestY (dirt). A garrison at y=4 then thrashed for the whole TTL.
                {
                    var onRoof = new DyingUnitEntity(4, corpseDef, false, 5f, tY, 0f, 0f, 0f, 0f)
                        { SupportY = tY };
                    Check(!CosmeticSystems.RagdollAirborne(onRoof),
                          "a body sitting on a roof is not airborne");
                    var falling = new DyingUnitEntity(5, corpseDef, false, 5f, tY, 0f, 0f, 0f, 0f)
                        { SupportY = -1f };
                    Check(CosmeticSystems.RagdollAirborne(falling),
                          "a body with no support still is");
                    var slidingDirt = new DyingUnitEntity(
                        5, corpseDef, false, 0f, 0.05f, 0f, 1.2f, 0f, 0f)
                        { SupportY = 0.05f };
                    Check(!CosmeticSystems.RagdollAirborne(slidingDirt),
                          "a body sliding on dirt is not airborne — leftover vx is not a flail");
                }

                // LIP: a body on the last half-metre of a roof walks off and falls.
                // The old face test killed vx the moment they dipped below topY, so
                // they sat on the lip forever. Step until they have actually left —
                // one tick is the same green-light-that-means-nothing as the levitation
                // check above.
                {
                    float lipX = 3.5f + 0.20f;   // 0.20 inside the left face (margin 0.55)
                    var onLip = new DyingUnitEntity(6, corpseDef, false, lipX, tY, 0f, 0f, 0f, 0f)
                        { SupportY = tY, Tumble = true };
                    var stLip = st with { DyingUnits = new List<DyingUnitEntity> { onLip } };
                    DyingUnitEntity fallen = onLip;
                    for (int i = 0; i < 90; i++)
                    {
                        stLip = BattleTick.Step(stLip, 1f / 60f, null, new System.Random(1));
                        fallen = stLip.DyingUnits.FirstOrDefault(d => d.Id == 6);
                        if (fallen == null) break;
                    }
                    Check(fallen != null && fallen.Y < tY - 0.4f && fallen.X <= 3.5f + 1e-3f,
                          $"a body on a roof LIP falls off "
                          + $"(y {fallen?.Y:F2} x {fallen?.X:F2}, roof {tY:F1} face 3.50)");
                    Check(fallen != null && fallen.Bend < 0f,
                          $"and folds OUT over the lip (bend {fallen?.Bend:F1})");
                }

                // A body that LANDS on the centre of a roof must keep its horizontal
                // speed — the old "spawned inside" branch zeroed it the first tick
                // they dipped below topY, which is why they could never reach a lip.
                {
                    var sliding = new DyingUnitEntity(7, corpseDef, false, 5f, tY + 0.05f, 0f,
                                                      1.6f, 0f, 0f);
                    var stSlide = st with { DyingUnits = new List<DyingUnitEntity> { sliding } };
                    for (int i = 0; i < 10; i++)
                        stSlide = BattleTick.Step(stSlide, 1f / 60f, null, new System.Random(1));
                    var slid = stSlide.DyingUnits.FirstOrDefault(d => d.Id == 7);
                    Check(slid != null && slid.Vx > 0.5f && slid.Y >= tY - 0.05f,
                          $"a body on the centre of a roof KEEPS sliding "
                          + $"(vx {slid?.Vx:F2}, y {slid?.Y:F2})");
                }

                // WALL: thrown into the left face below the roof, fold INTO it.
                {
                    var atFace = walk.DyingUnits.FirstOrDefault(d => d.Id == 1);
                    Check(atFace != null && atFace.Bend > 0f,
                          $"a body that hits a wall folds INTO it (bend {atFace?.Bend:F1})");
                }

                // INVISIBLE WALL: hitWidth hangs past the silhouette
                // (GarrisonPost 3.75 vs size 2.5 / stand 3.125). Bodies used
                // to stop at the projectile face and stack in empty air.
                // They must fly PAST that face and rest on the deck edge.
                {
                    var postDef = ScriptableObject.CreateInstance<StructureDefinitionSO>();
                    postDef.id = "post"; postDef.maxHp = 100; postDef.size = 2.5f;
                    postDef.hasHitWidth = true; postDef.hitWidth = 3.75f;
                    postDef.standWidth = 3.125f; postDef.isPlayerSide = false;
                    var post = new StructureEntity(901, postDef, 6.5f, 0f, 0f, 100);
                    // hit face 4.625, masonry face 4.9375.
                    var flung = new DyingUnitEntity(11, corpseDef, false, 4.40f, 1.2f, 0f,
                                                    5f, 0.4f, 0f);
                    var stWall = st with
                    {
                        Structures = new List<StructureEntity> { post },
                        DyingUnits = new List<DyingUnitEntity> { flung },
                    };
                    DyingUnitEntity body = flung;
                    for (int i = 0; i < 50; i++)
                    {
                        stWall = BattleTick.Step(stWall, 1f / 60f, null, new System.Random(1));
                        body = stWall.DyingUnits.FirstOrDefault(d => d.Id == 11);
                        if (body == null) break;
                    }
                    Check(body != null && body.X > 4.70f,
                          $"a body thrown at a wide-hit hut PASSES the projectile face "
                          + $"(x {body?.X:F2}, hit-face 4.63) — that face is empty air");
                    Check(body != null && body.X <= 4.9375f + 0.02f,
                          $"and comes to rest against the masonry "
                          + $"(x {body?.X:F2}, stand-face 4.94)");
                    Check(body != null && Mathf.Abs(body.Z) > 0.15f,
                          $"leftover speed becomes depth, not a stack on the pane "
                          + $"(z {body?.Z:F2})");
                }

                // L8: the watch garrison flies toward the post, WELL ABOVE it.
                // fromRoof is true for anyone high in the air, so the post's
                // near lip used to pin them at the face and drop them as a
                // curtain. They must sail over.
                {
                    var postDef = ScriptableObject.CreateInstance<StructureDefinitionSO>();
                    postDef.id = "post"; postDef.maxHp = 100; postDef.size = 2.5f;
                    postDef.hasHitWidth = true; postDef.hitWidth = 3.75f;
                    postDef.standWidth = 3.125f; postDef.isPlayerSide = false;
                    var post = new StructureEntity(902, postDef, 6.5f, 0f, 0f, 100);
                    // Watch standing-Y 3.75, just left of the post's stand face.
                    var flyer = new DyingUnitEntity(12, corpseDef, false, 4.40f, 3.75f, 0f,
                                                    1.8f, 1.2f, 0f)
                        { SupportY = -1f };
                    var stFly = st with
                    {
                        Structures = new List<StructureEntity> { post },
                        DyingUnits = new List<DyingUnitEntity> { flyer },
                    };
                    float maxX = 4.40f;
                    float minVx = 1.8f;
                    DyingUnitEntity body = flyer;
                    for (int i = 0; i < 45; i++)
                    {
                        stFly = BattleTick.Step(stFly, 1f / 60f, null, new System.Random(1));
                        body = stFly.DyingUnits.FirstOrDefault(d => d.Id == 12);
                        if (body == null) break;
                        maxX = Mathf.Max(maxX, body.X);
                        minVx = Mathf.Min(minVx, body.Vx);
                    }
                    Check(body != null && maxX > 5.20f,
                          $"a body flying OVER a hut is not pinned at its near face "
                          + $"(reached x {maxX:F2}, face 4.94, y {body?.Y:F2})");
                    Check(minVx > 0.5f,
                          $"and keeps travelling instead of dropping as a curtain "
                          + $"(slowest vx {minVx:F2})");
                }
            }

            // Rolling: friction bleeds speed, and rotation is locked to travel (like a log).
            CosmeticSystems.StepRoll(2f, 1f / 60f, out float nvx, out float roll);
            Check(nvx < 2f && nvx > 0f, "roll friction bleeds speed without reversing it");
            Check(roll < 0f, "rightward travel rolls the body forward (negative degrees)");
            Check(CosmeticSystems.ShouldRoll(1f), "a fast body rolls");
            Check(!CosmeticSystems.ShouldRoll(0.1f), "a slow body stops rolling and flops");

            // Flop: near-critically damped toward the nearest LYING pose, so it settles.
            float rot = 60f, rotSpeed = 0f;
            for (int i = 0; i < 240; i++)
                CosmeticSystems.StepFlop(rot, rotSpeed, 1f / 60f, out rot, out rotSpeed);
            Near(rot, 0f, 1.0f, "a body at 60 degrees flops down to lying flat");
            Check(Mathf.Abs(rotSpeed) < 1f, "and comes to rest rather than oscillating");
            float rot2 = 130f, rs2 = 0f;
            for (int i = 0; i < 240; i++)
                CosmeticSystems.StepFlop(rot2, rs2, 1f / 60f, out rot2, out rs2);
            Near(rot2, 180f, 1.0f, "a body past vertical falls the OTHER way, to 180");

            float side = 20f, sideSp = 0f;
            for (int i = 0; i < 240; i++)
                CosmeticSystems.StepFlopToSide(side, sideSp, 1f / 60f, out side, out sideSp);
            Near(side, 90f, 2f, "a grounded body flops onto its SIDE (horizontal), not upright");
            float side2 = -20f, sideSp2 = 0f;
            for (int i = 0; i < 240; i++)
                CosmeticSystems.StepFlopToSide(side2, sideSp2, 1f / 60f, out side2, out sideSp2);
            Near(side2, -90f, 2f, "a body tipping the other way lies the other side");

            // AIRBORNE: they tip, they do not flop to horizontal in the air.
            {
                var flyDef = ScriptableObject.CreateInstance<UnitDefinitionSO>();
                flyDef.id = "flyer"; flyDef.maxHp = 8; flyDef.damage = 4;
                var flyer = new DyingUnitEntity(21, flyDef, false, 0f, 4f, 0f, 0.4f, 0.2f, 70f)
                { Rotation = 8f };
                var air = new GameState
                {
                    Phase = GamePhase.Playing,
                    DyingUnits = new List<DyingUnitEntity> { flyer },
                };
                DyingUnitEntity body = flyer;
                for (int i = 0; i < 45; i++)
                {
                    air = BattleTick.Step(air, 1f / 60f, null, new System.Random(1));
                    body = air.DyingUnits.FirstOrDefault(d => d.Id == 21);
                    if (body == null) break;
                }
                Check(body != null && CosmeticSystems.RagdollAirborne(body)
                      && Mathf.Abs(body.Rotation) > 20f,
                      "a flying body actually tumbles "
                      + $"(rot {body?.Rotation:F1}, airborne {body != null && CosmeticSystems.RagdollAirborne(body)})");
                Object.DestroyImmediate(flyDef);
            }

            {
                var wDef = ScriptableObject.CreateInstance<UnitDefinitionSO>();
                wDef.id = "w"; wDef.maxHp = 8; wDef.damage = 4;
                var wreck = new WreckEntity(1, "outpost", 5f, 0f, 0f, 4f, 2f);
                var faller = new DyingUnitEntity(3, wDef, false, 5f, 2.2f, 0f, 0f, -0.4f, 0f)
                { Tumble = true };
                var dummy = new UnitEntity(9, wDef, -8f, 0f, 0f, 8, true);
                var dummyE = new UnitEntity(10, wDef, 8f, 0f, 0f, 8, false);
                var st = new GameState
                {
                    Phase = GamePhase.Playing,
                    PlayerUnits = new List<UnitEntity> { dummy },
                    EnemyUnits = new List<UnitEntity> { dummyE },
                    Wrecks = new List<WreckEntity> { wreck },
                    DyingUnits = new List<DyingUnitEntity> { faller },
                };
                DyingUnitEntity landed = faller;
                for (int i = 0; i < 90; i++)
                {
                    st = BattleTick.Step(st, 1f / 60f, null, new System.Random(1));
                    landed = st.DyingUnits.FirstOrDefault(d => d.Id == 3);
                    if (landed == null) break;
                    if (!CosmeticSystems.RagdollAirborne(landed)) break;
                }
                Check(landed != null
                      && Mathf.Abs(landed.SupportY - CosmeticSystems.WreckLidY(wreck)) < 0.05f,
                      "a falling garrison lands ON the wreck mound, not through it "
                      + $"(support {landed?.SupportY:F2}, lid {CosmeticSystems.WreckLidY(wreck):F2})");
                Object.DestroyImmediate(wDef);
            }

            Check(CosmeticSystems.RagdollExpired(5f), "a body is culled at the age limit");
            Check(!CosmeticSystems.RagdollExpired(4.9f), "and not before");

            // SINK: last stretch, dirt only, fully under before cull. A pop is
            // the same artefact as the health bar that vanished in one frame.
            Check(CosmeticSystems.RagdollSinkY(0f, 0.05f) == 0f,
                  "a fresh corpse on the dirt has not started sinking");
            Check(CosmeticSystems.RagdollSinkY(3.5f, 0.05f) == 0f,
                  "and not before the last stretch");
            Near(CosmeticSystems.RagdollSinkY(CosmeticSystems.RagdollMaxAgeSeconds, 0.05f),
                 -CosmeticSystems.RagdollSinkDepth, 1e-4f,
                 "at expiry a dirt body is fully under the ground");
            Check(CosmeticSystems.RagdollSinkY(4.8f, 2.5f) == 0f,
                  "a body on a roof does not sink into masonry");
            Check(CosmeticSystems.RagdollSinkY(4.8f, -1f) == 0f,
                  "an airborne body does not sink");
            Check(CosmeticSystems.RagdollSinkY(4.6f, 0.05f)
                  > CosmeticSystems.RagdollSinkY(4.9f, 0.05f),
                  "the sink only goes down");

            // Debris sleep: ONLY rubble sleeps, and only when actually still.
            Check(CosmeticSystems.ShouldSleep(true, true, 0f, 0f, 0f), "still grounded rubble sleeps");
            Check(!CosmeticSystems.ShouldSleep(false, true, 0f, 0f, 0f),
                  "transient spatter never sleeps — it ages out on ttl instead");
            Check(!CosmeticSystems.ShouldSleep(true, false, 0f, 0f, 0f), "airborne rubble stays awake");
            Check(!CosmeticSystems.ShouldSleep(true, true, 1f, 0f, 0f), "moving rubble stays awake");
            Check(!CosmeticSystems.ShouldSleep(true, true, 0f, 0f, 50f), "spinning rubble stays awake");
            Check(CosmeticSystems.DebrisRubbleTtl > CosmeticSystems.DebrisTtlSeconds,
                  "rubble outlives transient debris by design");
            Check(CosmeticSystems.DebrisTtlSeconds >= 7f,
                  $"transient debris lasts through the next aim "
                  + $"({CosmeticSystems.DebrisTtlSeconds:F1}s) — 2.6s vanished in the follow");

            // Scorch merging: nearby marks combine instead of stacking identical decals.
            var marks = new List<ScorchMark> { new(1, 5f, 0f), new(2, 20f, 0f) };
            Check(CosmeticSystems.FindMergeTarget(marks, 5.05f, 0f) == 0,
                  "a hit next to an existing scorch merges into it");
            Check(CosmeticSystems.FindMergeTarget(marks, 12f, 0f) == -1,
                  "a distant hit makes its own mark");
            Check(CosmeticSystems.FindMergeTarget(new List<ScorchMark>(), 0f, 0f) == -1,
                  "an empty field has nothing to merge with");
            float grown = 1f;
            for (int i = 0; i < 100; i++) grown = CosmeticSystems.GrowScorch(grown);
            Check(grown <= CosmeticSystems.ScorchMaxScale + 1e-5f,
                  "repeated merges cannot grow one enormous blot");
            Check(CosmeticSystems.ScorchDepthStretch > 1.5f,
                  "a miss mark is stretched in DEPTH — at 6 degrees a round decal is a smear");

            // Knockback: an AGE, not a displacement — collision is unaffected.
            Check(CosmeticSystems.StepKnockback(-1f, 1f / 60f) == -1f, "an inactive hop stays inactive");
            float k = CosmeticSystems.StepKnockback(0f, 1f / 60f);
            Check(k > 0f, "a triggered hop starts counting");
            Check(CosmeticSystems.StepKnockback(0.41f, 0.05f) == -1f,
                  "the hop returns to inactive when it expires, snapping back to formation");
        }

        // --- HelicopterSystem
        {
            var ud = ScriptableObject.CreateInstance<UnitDefinitionSO>();
            ud.id = "u"; ud.maxHp = 32;
            var pl = new List<UnitEntity> { new(1, ud, -8f, 0f, 0f, 32, true) };
            var en = new List<UnitEntity> { new(2, ud, 8f, 0f, 0f, 32, false) };
            var st = new List<StructureEntity>();
            var rng = new System.Random(3);

            HelicopterEntity Heli(HeliMode m, float x = 6f, int hp = HelicopterSystem.MaxHp,
                                  int bursts = HelicopterSystem.Bursts)
                => new(x, HelicopterSystem.Altitude, -HelicopterSystem.Speed, m, bursts)
                   { Hp = hp, MaxHp = HelicopterSystem.MaxHp, HoverX = 4f };

            // Shootability: the pre-battle flyby is scenery, a falling wreck is not a target.
            Check(!HelicopterSystem.IsShootable(HeliMode.Preview), "the preview flyby is not shootable");
            Check(HelicopterSystem.IsShootable(HeliMode.Hovering), "a hovering gunship is shootable");
            Check(!HelicopterSystem.IsShootable(HeliMode.Crashing), "a crashing heli is not shootable");

            // Entering settles into Hovering exactly at hoverX, not past it.
            var entering = Heli(HeliMode.Entering, x: 4.02f);   // 2.6 u/s * 1/60 = 0.043 travelled
            var r = HelicopterSystem.Step(entering, 1f / 60f, GamePhase.Playing, pl, en, st);
            Check(r.Heli.Mode == HeliMode.Hovering, "Entering becomes Hovering on arrival");
            Near(r.Heli.X, 4f, 1e-4f, "and stops exactly at the hover spot");
            Check(r.Heli.Vx == 0f, "with its approach speed cleared");

            // A battle that ends while it hovers: leave cosmetically, holding nothing open.
            var stranded = HelicopterSystem.Step(Heli(HeliMode.Hovering), 1f / 60f,
                                                 GamePhase.Victory, pl, en, st);
            Check(stranded.Heli.Mode == HeliMode.GunRun,
                  "a hovering heli leaves when the battle is already over");

            // WOUNDING changes behaviour — that is the counter-play, not just killing it.
            var hurt = HelicopterSystem.ApplyHit(Heli(HeliMode.Hovering), 10);
            Check(hurt.Mode == HeliMode.Retreating, "a wounded gunship breaks off and retreats");
            Check(hurt.Hp == HelicopterSystem.MaxHp - 10, "and carries the damage");
            Check(hurt.BurstsLeft == 0, "a retreating heli fires nothing further");
            Check(HelicopterSystem.IsWounded(hurt.Hp, hurt.MaxHp), "and reads as wounded");

            var killed = HelicopterSystem.ApplyHit(Heli(HeliMode.Hovering), 999);
            Check(killed.Mode == HeliMode.Crashing, "enough damage starts a crash");
            Check(killed.Vy > 0f, "with a brief upward lurch at the killing hit");
            Check(killed.BurstsLeft == 0,
                  "a falling heli must not hold the turn handover open");

            // The crash falls, spins, and produces its fireball at hull contact.
            var falling = killed with { Y = 0.23f, Vy = -3f };   // one tick drops ~0.051, past 0.22
            var crashed = HelicopterSystem.Step(falling, 1f / 60f, GamePhase.Playing, pl, en, st);
            Check(crashed.Heli == null && crashed.SpawnedCrashFireball,
                  "hitting the ground despawns the heli and spawns the fireball");
            var stillFalling = HelicopterSystem.Step(killed with { Y = 2f, Vy = -1f },
                                                     1f / 60f, GamePhase.Playing, pl, en, st);
            Check(stillFalling.Heli.Rotation > 0f, "a falling heli tumbles");
            Check(stillFalling.Heli.Vy < -1f, "and accelerates downward");

            // A crash still resolves after the battle ended, rather than despawning silently.
            var postBattle = HelicopterSystem.Step(killed with { Y = 0.23f, Vy = -3f },
                                                   1f / 60f, GamePhase.Victory, pl, en, st);
            Check(postBattle.SpawnedCrashFireball,
                  "a heli falling as the battle ends still explodes");

            // Hit detection is SWEPT and side-correct.
            var hover = Heli(HeliMode.Hovering, x: 6f);
            var through = new ProjectileEntity(1, 6f, 1f, 0f, 0f, -20f, 0f, 8, true)
                { PrevX = 6f, PrevY = 6f };
            Check(HelicopterSystem.IsHitBy(hover, through),
                  "a fast round through the hover disc registers (swept, not point-sampled)");
            var wide = new ProjectileEntity(2, 20f, 1f, 0f, 0f, -20f, 0f, 8, true)
                { PrevX = 20f, PrevY = 6f };
            Check(!HelicopterSystem.IsHitBy(hover, wide), "a round nowhere near it misses");
            var enemyRound = new ProjectileEntity(3, 6f, 1f, 0f, 0f, -20f, 0f, 8, false)
                { PrevX = 6f, PrevY = 6f };
            Check(!HelicopterSystem.IsHitBy(hover, enemyRound),
                  "the enemy never shoots down its own gunship");
            var ownGunner = new ProjectileEntity(4, 6f, 1f, 0f, 0f, -20f, 0f, 8, true)
                { PrevX = 6f, PrevY = 6f, IsHeliShot = true };
            Check(!HelicopterSystem.IsHitBy(hover, ownGunner),
                  "and its own door-gunner rounds cannot hit it");
            Check(!HelicopterSystem.IsHitBy(Heli(HeliMode.Preview), through),
                  "the cosmetic flyby cannot be shot down");

            // Door gunner: fires only on a gun run, and its rounds are excluded from the camera.
            var gunning = Heli(HeliMode.GunRun) with { FireCooldown = 0f };
            var shot = HelicopterSystem.TryFire(gunning, pl, 900, rng);
            Check(shot != null, "a gun-running heli with bursts left fires");
            Check(shot.IsHeliShot,
                  "gunner rounds are flagged so the volley camera ignores them");
            Check(!shot.OwnerIsPlayer, "gunner rounds belong to the enemy side");
            Check(HelicopterSystem.TryFire(Heli(HeliMode.Hovering) with { FireCooldown = 0f },
                                           pl, 901, rng) == null,
                  "a hovering heli does not fire");
            Check(HelicopterSystem.TryFire(gunning with { BurstsLeft = 0 }, pl, 902, rng) == null,
                  "an empty gun fires nothing");
            Check(HelicopterSystem.TryFire(gunning with { FireCooldown = 0.4f }, pl, 903, rng) == null,
                  "the fire interval is respected");
            var after = HelicopterSystem.ConsumeBurst(gunning);
            Check(after.BurstsLeft == HelicopterSystem.Bursts - 1, "firing consumes a burst");
            Near(after.FireCooldown, HelicopterSystem.FireInterval, 1e-5f, "and resets the cooldown");
        }

        // --- EventSystems
        {
            // Reinforcement waves: telegraph one turn EARLY, arrive on the turn, silent after.
            Check(EventSystems.ReinforcementWaveBeat(5, 5) == EventSystems.WaveTriggerBeat.Arrive,
                  "a wave arrives on its turn");
            Check(EventSystems.ReinforcementWaveBeat(5, 4) == EventSystems.WaveTriggerBeat.Telegraph,
                  "and telegraphs one turn early so the player can react");
            Check(EventSystems.ReinforcementWaveBeat(5, 3) == EventSystems.WaveTriggerBeat.None,
                  "with nothing shown two turns out");
            Check(EventSystems.ReinforcementWaveBeat(5, 6) == EventSystems.WaveTriggerBeat.None,
                  "and nothing after it has landed");

            // Wind: sign follows BASE, so a level's wind never reverses mid-battle.
            float baseWind = 2f;
            float w = baseWind;
            for (int i = 0; i < 40; i++) w = EventSystems.NextWindAccelZ(w, baseWind, false);
            Check(w > 0f, "weakening wind never flips direction");
            Near(w, baseWind * EventSystems.WindShiftMinFrac, 1e-4f, "it clamps at the band floor");
            for (int i = 0; i < 80; i++) w = EventSystems.NextWindAccelZ(w, baseWind, true);
            Near(w, baseWind * EventSystems.WindShiftMaxFrac, 1e-4f, "and at the band ceiling");

            float negBase = -2f, nw = negBase;
            for (int i = 0; i < 40; i++) nw = EventSystems.NextWindAccelZ(nw, negBase, true);
            Check(nw < 0f, "a leftward wind stays leftward however hard it gusts");
            Near(nw, negBase * EventSystems.WindShiftMaxFrac * -1f * -1f, 1e-4f,
                 "and clamps to its own band");

            Check(EventSystems.NextWindAccelZ(0f, 0f, true) == 0f,
                  "a level with no wind never gusts");

            // The banner is suppressed when the gust changed nothing.
            float atCeiling = baseWind * EventSystems.WindShiftMaxFrac;
            float unchanged = EventSystems.NextWindAccelZ(atCeiling, baseWind, true);
            Check(EventSystems.WindShiftAnnouncement(atCeiling, unchanged, true) == null,
                  "no banner when the wind was already clamped at the edge");
            Check(EventSystems.WindShiftAnnouncement(1f, 1.35f, true) != null,
                  "a real gust does announce itself");

            // Announcement timers clear their text.
            float timer = 0.1f;
            Check(!EventSystems.TickAnnouncement(ref timer, 0.2f), "an expired banner is hidden");
            Check(timer == 0f, "and its timer floors at zero rather than going negative");
            float live = 2f;
            Check(EventSystems.TickAnnouncement(ref live, 0.2f), "a live banner stays shown");

            // Boss phases.
            var lvl = ScriptableObject.CreateInstance<LevelDefinitionSO>();
            var ud2 = ScriptableObject.CreateInstance<UnitDefinitionSO>();
            ud2.id = "u"; ud2.maxHp = 32;
            lvl.enemyGroups = new List<EnemyGroup>
            {
                new() { definition = ud2, count = 2, anchorX = 6f, standingOnStructureId = "keep" },
            };
            var runtimeIds = new Dictionary<string, int> { { "keep", 100 }, { "gate", 101 } };

            // Destroyed outright counts.
            Check(EventSystems.IsTriggerDefeated("keep", runtimeIds, new HashSet<int> { 100 },
                                                 lvl, new List<UnitEntity>()),
                  "a destroyed trigger structure counts as defeated");

            // Still standing but its garrison is cleared ALSO counts — without this a player who
            // killed the defenders and left the masonry never fires the encounter.
            Check(EventSystems.IsTriggerDefeated("keep", runtimeIds, new HashSet<int>(),
                                                 lvl, new List<UnitEntity>()),
                  "a garrisoned structure with its defenders cleared also counts");
            var stillHeld = new List<UnitEntity>
            {
                new(1, ud2, 6f, 1f, 0f, 32, false) { StandingOnStructureId = 100 },
            };
            Check(!EventSystems.IsTriggerDefeated("keep", runtimeIds, new HashSet<int>(),
                                                  lvl, stillHeld),
                  "but not while any of its garrison still stands");

            // An UNgarrisoned structure only counts once actually destroyed.
            Check(!EventSystems.IsTriggerDefeated("gate", runtimeIds, new HashSet<int>(),
                                                  lvl, new List<UnitEntity>()),
                  "an ungarrisoned structure must actually be destroyed");
            Check(!EventSystems.IsTriggerDefeated("missing", runtimeIds, new HashSet<int>(),
                                                  lvl, new List<UnitEntity>()),
                  "an unknown structure id is never defeated");

            // Trigger gating.
            var trig = new BossPhaseTrigger { triggerStructureIds = new List<string> { "keep", "gate" } };
            Check(!EventSystems.ShouldTriggerBossPhase(0, trig, new HashSet<int>(), id => id == "keep"),
                  "a phase waits until EVERY trigger structure is defeated");
            Check(EventSystems.ShouldTriggerBossPhase(0, trig, new HashSet<int>(), id => true),
                  "and fires once they all are");
            Check(!EventSystems.ShouldTriggerBossPhase(0, trig, new HashSet<int> { 0 }, id => true),
                  "an already-triggered phase never fires twice");
            var empty = new BossPhaseTrigger { triggerStructureIds = new List<string>() };
            Check(!EventSystems.ShouldTriggerBossPhase(0, empty, new HashSet<int>(), id => true),
                  "an EMPTY trigger set is not vacuously true (it would fire on tick one)");
        }

        // --- Backdrop: the properties that separate a range from a row of pyramids. None of
        // these can be seen in a screenshot of the ONE biome a level happens to load, and the
        // three that are checked here are all mistakes this file's history actually shipped.
        {
            foreach (SilhouetteStyle style in System.Enum.GetValues(typeof(SilhouetteStyle)))
            {
                var plan = ArmedConflict.Render.Backdrop.Plan(style, ArmedConflict.Render.Backdrop.DesignAspect);
                Check(plan.Count > 0, null);
                float visible = ArmedConflict.Render.Backdrop.VisibleWidthAt(
                    ArmedConflict.Render.Backdrop.NearZ, ArmedConflict.Render.Backdrop.DesignAspect);
                foreach (var layer in plan)
                {
                    Check(layer.Width > ArmedConflict.Render.Backdrop.VisibleWidthAt(
                              layer.Z, ArmedConflict.Render.Backdrop.DesignAspect), null);
                    Check(layer.Profile.Length >= 8, null);
                    foreach (var p in layer.Profile) Check(p >= -0.001f && p <= 1.001f, null);
                    // Every layer must be a closed silhouette: the base is BELOW the ground
                    // plane, so a valley meets the ground instead of stopping on a ledge.
                    Check(layer.BaseY < 0f, null);
                    var mesh = ArmedConflict.Render.SilhouetteMesh.Build(layer);
                    Check(mesh.vertexCount == layer.Profile.Length * 2, null);
                }
                Check(true, $"{style}: {plan.Count} layers, all wider than frame, profiles in range");
            }

            // Depth ordering. A layer FURTHER away that is also SMALLER on screen contradicts
            // its own haze colour, which is what made the two mountain rows read as one.
            var mtn = ArmedConflict.Render.Backdrop.Plan(SilhouetteStyle.Mountains,
                                                         ArmedConflict.Render.Backdrop.DesignAspect);
            float farAng = ArmedConflict.Render.Backdrop.AngularHeight(mtn[0].Height, mtn[0].Z);
            float nearAng = ArmedConflict.Render.Backdrop.AngularHeight(mtn[1].Height, mtn[1].Z);
            Check(farAng > nearAng * 1.3f,
                  $"mountains: the far range OUT-REACHES the foothills on screen ({farAng:F3} vs {nearAng:F3})");

            // Snow is a cap on the crests that earn it, not a blanket over the range.
            var snowLayer = mtn[0];
            int snowy = 0;
            foreach (var p in snowLayer.Profile) if (p > snowLayer.SnowLine) snowy++;
            Check(snowy > 0 && snowy < snowLayer.Profile.Length / 4,
                  $"mountains: snow covers only the top crests ({snowy}/{snowLayer.Profile.Length} columns)");
            Check(ArmedConflict.Render.SilhouetteMesh.BuildSnow(mtn[1]) == null,
                  "no snow on the foothills (BuildSnow returns null when no line is set)");
        }

        // CityRuins is an authored 2.5D strip, not the 1D profile above. The profile is
        // the fallback; these meshes are what the player sees on L4 and L10.
        {
            Bounds CityBounds(string key)
            {
                var go = AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/Models/{key}.glb");
                Check(go != null, $"city backdrop {key}.glb is imported");
                var b = new Bounds();
                bool any = false;
                if (go == null) return b;
                foreach (var f in go.GetComponentsInChildren<MeshFilter>())
                {
                    if (f.sharedMesh == null) continue;
                    var mb = f.sharedMesh.bounds;
                    var world = new Bounds(f.transform.TransformPoint(mb.center),
                                           f.transform.TransformVector(mb.size));
                    if (!any) { b = world; any = true; }
                    else b.Encapsulate(world);
                }
                Check(any, $"city backdrop {key} has mesh filters");
                return b;
            }

            var far = CityBounds(ArmedConflict.Render.BackdropRuntime.CityFarModel);
            var near = CityBounds(ArmedConflict.Render.BackdropRuntime.CityNearModel);
            float farNeed = ArmedConflict.Render.Backdrop.VisibleWidthAt(
                ArmedConflict.Render.Backdrop.FarZ, ArmedConflict.Render.Backdrop.DesignAspect);
            float nearNeed = ArmedConflict.Render.Backdrop.VisibleWidthAt(
                ArmedConflict.Render.Backdrop.NearZ, ArmedConflict.Render.Backdrop.DesignAspect);
            Check(far.size.x > farNeed,
                  $"city far spans the frustum ({far.size.x:F1} > {farNeed:F1})");
            Check(near.size.x > nearNeed,
                  $"city near spans the frustum ({near.size.x:F1} > {nearNeed:F1})");
            Check(near.size.y > far.size.y,
                  $"city near is the taller skyline ({near.size.y:F1} vs far {far.size.y:F1})");
            Check(near.min.y > -1.5f && near.min.y < 1.0f,
                  $"city near sits on the ground plane (min y {near.min.y:F2})");

            // The phone draws whatever the SCENE references, not whatever is on disk.
            // Re-exporting a GLB can change its root fileID; the name stays in the
            // table and the prefab slot goes missing. That is how L4 kept the old
            // silhouette after the strip shipped.
            var scene = UnityEditor.SceneManagement.EditorSceneManager.OpenScene(
                "Assets/Scenes/Battle.unity", UnityEditor.SceneManagement.OpenSceneMode.Additive);
            try
            {
                var scenery = Object.FindFirstObjectByType<LevelScenery>();
                Check(scenery != null, "Battle.unity has a LevelScenery");
                if (scenery != null)
                {
                    var so = new UnityEditor.SerializedObject(scenery);
                    var names = so.FindProperty("modelNames");
                    var prefs = so.FindProperty("modelPrefabs");
                    bool farOk = false, nearOk = false;
                    int n = Mathf.Min(names.arraySize, prefs.arraySize);
                    for (int i = 0; i < n; i++)
                    {
                        string key = names.GetArrayElementAtIndex(i).stringValue;
                        var pref = prefs.GetArrayElementAtIndex(i).objectReferenceValue;
                        if (key == ArmedConflict.Render.BackdropRuntime.CityFarModel)
                            farOk = pref != null;
                        if (key == ArmedConflict.Render.BackdropRuntime.CityNearModel)
                            nearOk = pref != null;
                    }
                    Check(farOk && nearOk,
                          $"Battle.unity still references both city GLBs (far={farOk}, near={nearOk})");
                    bool fFar = false, fNear = false;
                    for (int i = 0; i < n; i++)
                    {
                        string key = names.GetArrayElementAtIndex(i).stringValue;
                        var pref = prefs.GetArrayElementAtIndex(i).objectReferenceValue;
                        if (key == ArmedConflict.Render.BackdropRuntime.ForestFarModel)
                            fFar = pref != null;
                        if (key == ArmedConflict.Render.BackdropRuntime.ForestNearModel)
                            fNear = pref != null;
                    }
                    Check(fFar && fNear,
                          $"Battle.unity still references both forest GLBs (far={fFar}, near={fNear})");
                    foreach (SilhouetteStyle style in System.Enum.GetValues(typeof(SilhouetteStyle)))
                    {
                        string fk = ArmedConflict.Render.BackdropRuntime.StripFar(style);
                        string nk = ArmedConflict.Render.BackdropRuntime.StripNear(style);
                        if (fk == null) continue;
                        bool a = false, b = false;
                        for (int i = 0; i < n; i++)
                        {
                            string key = names.GetArrayElementAtIndex(i).stringValue;
                            var pref = prefs.GetArrayElementAtIndex(i).objectReferenceValue;
                            if (key == fk) a = pref != null;
                            if (key == nk) b = pref != null;
                        }
                        Check(a && b, $"Battle.unity references {style} strip (far={a}, near={b})");

                        // The MID strip is optional: a style that declares one must have it
                        // wired, and a style that does not must stay on two planes. Both
                        // halves matter — the runtime deliberately does not let a missing mid
                        // drop the whole strip back to the procedural profile, so a broken
                        // reference here is silent except for one LogError on device.
                        string mk = ArmedConflict.Render.BackdropRuntime.StripMid(style);
                        bool mid = false;
                        for (int i = 0; i < n && mk != null; i++)
                            if (names.GetArrayElementAtIndex(i).stringValue == mk)
                                mid = prefs.GetArrayElementAtIndex(i).objectReferenceValue != null;
                        string qk = ArmedConflict.Render.BackdropRuntime.StripFore(style);
                        bool fore = false;
                        for (int i = 0; i < n && qk != null; i++)
                            if (names.GetArrayElementAtIndex(i).stringValue == qk)
                                fore = prefs.GetArrayElementAtIndex(i).objectReferenceValue != null;
                        Check((mk == null || mid) && (qk == null || fore),
                              $"{style} optional strips wired (mid={mk ?? "none"}:{mid}, " +
                              $"fore={qk ?? "none"}:{fore})");
                    }
                    Check(ArmedConflict.Render.BackdropRuntime.StripMid(SilhouetteStyle.Forest)
                              == ArmedConflict.Render.BackdropRuntime.ForestMidModel
                          && ArmedConflict.Render.BackdropRuntime.StripMid(SilhouetteStyle.City)
                              == null
                          && ArmedConflict.Render.BackdropRuntime.StripFore(SilhouetteStyle.Forest)
                              == null
                          && Backdrop.MidZ < Backdrop.NearZ && Backdrop.MidZ > Backdrop.FarZ
                          // ForeZ stays a safe unused slot: a later biome can opt in
                          // without the camera ending up behind the strip.
                          && Backdrop.ForeZ > 0f
                          && Backdrop.ForeZ < ArmedConflict.Game.CameraDirector.ZMin,
                          "only forest declares mid, no style declares fore, MidZ sits " +
                          $"between far and near ({Backdrop.FarZ} < {Backdrop.MidZ} < " +
                          $"{Backdrop.NearZ}), and ForeZ {Backdrop.ForeZ} is in front of " +
                          "the play plane but inside camera " +
                          $"ZMin {ArmedConflict.Game.CameraDirector.ZMin}");
                }
            }
            finally
            {
                UnityEditor.SceneManagement.EditorSceneManager.CloseScene(scene, true);
            }

            var fFarB = CityBounds(ArmedConflict.Render.BackdropRuntime.ForestFarModel);
            var fNearB = CityBounds(ArmedConflict.Render.BackdropRuntime.ForestNearModel);
            Check(fFarB.size.x > farNeed,
                  $"forest far spans the frustum ({fFarB.size.x:F1} > {farNeed:F1})");
            Check(fNearB.size.x > nearNeed,
                  $"forest near spans the frustum ({fNearB.size.x:F1} > {nearNeed:F1})");
            Check(fNearB.size.y > fFarB.size.y,
                  $"forest near is the taller tree line ({fNearB.size.y:F1} vs far {fFarB.size.y:F1})");
            Check(fNearB.min.y > -1.5f && fNearB.min.y < 1.0f,
                  $"forest near sits on the ground plane (min y {fNearB.min.y:F2})");

            foreach (var pair in new[]
                     {
                         ("mountains", ArmedConflict.Render.BackdropRuntime.MountainsFarModel,
                          ArmedConflict.Render.BackdropRuntime.MountainsNearModel, true),
                         ("winter", ArmedConflict.Render.BackdropRuntime.WinterFarModel,
                          ArmedConflict.Render.BackdropRuntime.WinterNearModel, true),
                         ("desert", ArmedConflict.Render.BackdropRuntime.DesertFarModel,
                          ArmedConflict.Render.BackdropRuntime.DesertNearModel, false),
                     })
            {
                var fb = CityBounds(pair.Item2);
                var nb = CityBounds(pair.Item3);
                Check(fb.size.x > farNeed,
                      $"{pair.Item1} far spans the frustum ({fb.size.x:F1} > {farNeed:F1})");
                Check(nb.size.x > nearNeed,
                      $"{pair.Item1} near spans the frustum ({nb.size.x:F1} > {nearNeed:F1})");
                if (pair.Item4)
                    Check(fb.size.y > nb.size.y * 1.15f,
                          $"{pair.Item1} far out-reaches the foothills "
                          + $"({fb.size.y:F1} vs {nb.size.y:F1})");
            }
        }

        // Ruin fire and smoke. The sites are a list; the check is what they DO.
        {
            int fireN = 0, smokeN = 0;
            foreach (var s in ArmedConflict.Render.RuinFx.Sites)
            {
                fireN++;
                if (s.SmokeH > 0.01f) smokeN++;
            }
            Check(fireN >= 4 && smokeN >= 3,
                  $"city ruin FX: {fireN} fires, {smokeN} plumes (need a few of each)");

            var fade = AssetDatabase.LoadAssetAtPath<Material>(
                "Assets/Materials/BackdropFadeSource.mat");
            Check(fade != null, "BackdropFadeSource.mat is what the ruin FX clones");
            foreach (var s in ArmedConflict.Render.RuinFx.Sites)
                Check(s.Z < -0.5f,
                      $"ruin fire sits IN the building (z={s.Z:F2}), not in front of the facade");

            var host = new GameObject("RuinFxHost");
            var owned = new List<Object>();
            var session = ArmedConflict.Render.RuinFx.Attach(host.transform, fade, owned);
            Check(session.Fires.Length == fireN && session.Smokes.Length == smokeN,
                  $"Attach built every site ({session.Fires.Length} fires, {session.Smokes.Length} plumes)");
            Check(session.Fires[0].Glow.localPosition.z < -0.5f,
                  $"the glow is behind the facade ({session.Fires[0].Glow.localPosition.z:F2})");
            float tongueZ = session.Fires[0].Outer.parent.localPosition.z;
            Check(tongueZ > -0.2f && tongueZ < 0.25f,
                  $"the tongue sits in the window mouth (z={tongueZ:F2})");

            session.Tick(0f);
            float flame0 = session.Fires[0].Outer.localScale.y;
            session.Tick(0.20f);
            float flame1 = session.Fires[0].Outer.localScale.y;
            Check(Mathf.Abs(flame1 - flame0) > 0.02f,
                  $"ruin tongue licks ({flame0:F2} -> {flame1:F2})");

            var smokeTex = ArmedConflict.Render.RuinFx.SmokeTex();
            var foot = smokeTex.GetPixel(smokeTex.width / 2, 2);
            var tip = smokeTex.GetPixel(smokeTex.width / 2, smokeTex.height - 2);
            Check(foot.a > 0.08f && foot.a < 0.70f && tip.a < foot.a * 0.45f,
                  $"smoke is a soft column, not a slab (foot a={foot.a:F2}, tip a={tip.a:F2})");
            Object.DestroyImmediate(smokeTex);
            Object.DestroyImmediate(host);
            foreach (var o in owned) if (o != null) Object.DestroyImmediate(o);
        }

        // --- The enemy's RAISED RIFLE must describe the round it actually fired.
        //
        // EnemyAI picks a fresh random arc per unit inside AimAt, so the only safe way to pose
        // the arms is to read the elevation back off the velocity that was really used. Drawing
        // a second angle for display would pose every enemy at an arc no round takes, and it
        // would look entirely plausible on screen — which is why this is asserted rather than
        // eyeballed.
        {
            var lv = AssetDatabase.FindAssets("t:LevelDefinitionSO")
                .Select(g => AssetDatabase.LoadAssetAtPath<LevelDefinitionSO>(
                    AssetDatabase.GUIDToAssetPath(g)))
                .Where(l => l != null).OrderBy(l => l.levelNumber).First();

            var st = LevelBuilder.BuildInitialState(lv, 1, 24, new System.Random(11));
            st = st with { Phase = GamePhase.Playing, TurnPhase = TurnPhase.Aiming };
            int before = st.Projectiles.Count;
            var fired = BattleTick.FireEnemyVolley(st, new System.Random(11));

            var prepared = BattleTick.PrepareEnemyVolley(st, new System.Random(11));
            Check(prepared.EnemyAimDegrees.Count == prepared.EnemyUnits.Count
                  && prepared.Projectiles.Count == st.Projectiles.Count,
                  "PrepareEnemyVolley poses the line WITHOUT firing "
                  + $"({prepared.EnemyAimDegrees.Count} aims, "
                  + $"{prepared.Projectiles.Count} rounds)");
            var fromPrepared = BattleTick.FireEnemyVolley(prepared, new System.Random(99));
            int sameArc = 0;
            foreach (var u in fromPrepared.EnemyUnits)
            {
                if (!prepared.EnemyAimDegrees.TryGetValue(u.Id, out float posed)) continue;
                if (!fromPrepared.EnemyAimDegrees.TryGetValue(u.Id, out float firedDeg)) continue;
                if (Mathf.Abs(posed - firedDeg) < 0.01f) sameArc++;
            }
            Check(sameArc == fromPrepared.EnemyUnits.Count,
                  "FireEnemyVolley uses the windup pose, not a second roll "
                  + $"({sameArc} of {fromPrepared.EnemyUnits.Count})");

            Check(fired.EnemyAimDegrees.Count == fired.EnemyUnits.Count,
                  $"every enemy records a launch elevation ({fired.EnemyAimDegrees.Count}" +
                  $" of {fired.EnemyUnits.Count})");

            var shots = fired.Projectiles.Skip(before).ToList();
            int matched = 0;
            foreach (var u in fired.EnemyUnits)
            {
                if (!fired.EnemyAimDegrees.TryGetValue(u.Id, out float posed)) continue;
                // The round this unit fired left from its own position.
                var shot = shots.FirstOrDefault(r => Mathf.Abs(r.X - u.X) < 1e-3f
                                                  && Mathf.Abs(r.Z - u.Z) < 1e-3f);
                if (shot == null) continue;
                float real = Mathf.Atan2(shot.Vy, Mathf.Abs(shot.Vx)) * Mathf.Rad2Deg;
                if (Mathf.Abs(real - posed) < 0.01f) matched++;
            }
            Check(matched == fired.EnemyUnits.Count,
                  $"each posed elevation equals the round that unit fired ({matched}" +
                  $" of {fired.EnemyUnits.Count})");

            // And it must not outlive the volley: units are POOLED, so a pose left set would be
            // inherited by whoever recycles the slot.
            Check(st.EnemyAimDegrees.Count == 0, "no enemy pose is held outside a volley");

            // The windup is when they RAISE. Handing over into it must pose the
            // rifles before any round exists.
            var intoWindup = BattleTick.Step(
                st with
                {
                    Phase = GamePhase.Playing,
                    TurnPhase = TurnPhase.Resolving,
                    TurnSide = TurnSide.Player,
                    TurnHandoverDelay = 0f,
                    Projectiles = new List<ProjectileEntity>(),
                },
                1f / 60f, lv, new System.Random(11));
            Check(intoWindup.TurnPhase == TurnPhase.EnemyWindup
                  && intoWindup.EnemyAimDegrees.Count == intoWindup.EnemyUnits.Count
                  && intoWindup.Projectiles.Count == 0,
                  "the windup poses every rifle and has not fired yet "
                  + $"(phase {intoWindup.TurnPhase}, "
                  + $"{intoWindup.EnemyAimDegrees.Count} aims, "
                  + $"{intoWindup.Projectiles.Count} rounds)");
        }

        // --- EVERY level must build and must have geometry for everything it places.
        //
        // This is the check that makes level navigation safe to ship. While only L1 was
        // reachable, a level whose structure GLB was never imported, or that threw while
        // building, was indistinguishable from a level nobody had visited — and the models
        // folder genuinely held two of the set. A device sweep finds this too, at about a
        // minute a level; this finds it in the same second as a typo.
        {
            var levels = AssetDatabase.FindAssets("t:LevelDefinitionSO")
                .Select(g => AssetDatabase.LoadAssetAtPath<LevelDefinitionSO>(
                    AssetDatabase.GUIDToAssetPath(g)))
                .Where(l => l != null)
                .OrderBy(l => l.levelNumber)
                .ToList();

            var haveModel = new HashSet<string>(
                AssetDatabase.FindAssets("t:GameObject", new[] { "Assets/Models" })
                    .Select(AssetDatabase.GUIDToAssetPath)
                    .Where(p => p.EndsWith(".glb") && !p.Contains("/Kenney/"))
                    .Select(LevelScenery.ModelKey));

            Check(levels.Count == 29, $"all 29 levels present ({levels.Count})");

            Check(levels.Count(l => !l.isTestLevel) == 12,
                  "the campaign is 12 levels — PRODUCT_DIRECTION Tier 0.1's funnel, "
                  + $"one beat each ({levels.Count(l => !l.isTestLevel)})");

            // Mid-ground scenery is the emptiness lever. Aiming is the empty
            // beat; backdrop strips sit at z=-30. Every campaign level owes
            // two keepColors plants behind the play plane.
            var campaignLevels = levels.Where(l => !l.isTestLevel).ToList();
            var bare = campaignLevels.Where(l =>
                l.props.Count(p => p.keepColors && p.z <= -6f) < 2)
                .Select(l => l.displayName).ToList();
            Check(bare.Count == 0,
                  bare.Count == 0
                      ? "every campaign level plants two mid-ground scenery props"
                      : $"NO MID-GROUND SCENERY: {string.Join(", ", bare)}");

            // The 2026-08-18 plant was the same three models on every level
            // (and the same model twice on L2/L7/L12). Variety is a wider
            // per-biome set, not a second copy of the tree.
            var twins = campaignLevels.Select(l =>
            {
                var keys = l.props.Where(p => p.keepColors && p.z <= -6f)
                    .Select(p => LevelScenery.ModelKey(p.modelAsset)).ToList();
                return (l.displayName, dup: keys.Count != keys.Distinct().Count());
            }).Where(t => t.dup).Select(t => t.displayName).ToList();
            Check(twins.Count == 0,
                  twins.Count == 0
                      ? "no campaign level plants the same mid-ground model twice"
                      : $"TWIN MID-GROUND: {string.Join(", ", twins)}");
            var midModels = campaignLevels
                .SelectMany(l => l.props.Where(p => p.keepColors && p.z <= -6f)
                    .Select(p => LevelScenery.ModelKey(p.modelAsset)))
                .Distinct().ToList();
            Check(midModels.Count >= 8,
                  $"campaign mid-ground uses {midModels.Count} models (need >=8, was 3)");

            // City boulevard. A flat decal at 6° is a smear, so this is a
            // world-sized kerbed slab with absoluteScale — Normalize would
            // stamp it to `scale` on the longest axis.
            var cityBare = campaignLevels.Where(l =>
                l.background != null && l.background.style == SilhouetteStyle.City
                && !l.props.Any(p => p.modelAsset != null
                    && p.modelAsset.Contains("city_road")
                    && p.keepColors && p.absoluteScale)).Select(l => l.displayName).ToList();
            Check(cityBare.Count == 0,
                  cityBare.Count == 0
                      ? "CityRuins campaign levels plant an absoluteScale road"
                      : $"NO CITY ROAD: {string.Join(", ", cityBare)}");

            // CONTIGUITY IS A CAMPAIGN RULE ONLY, as of the 2026-08-06 campaign/rig split.
            //
            // It used to be asserted across all 24 — which is what forced every test rig to be
            // renumbered whenever the campaign changed size, a standing chore and a standing bug.
            // SpikeSceneBattle now orders campaign-then-rigs, so the campaign block leads and is
            // indexed by position; a rig's number is free and indexes nothing.
            //
            // The campaign half still matters, and matters MORE than before: the orphan sweep is
            // gone (see LegacyKotlinImport), so a stale level asset can no longer be deleted for
            // us, and this check is the only thing that catches one rejoining the campaign.
            var campaign = levels.Where(l => !l.isTestLevel).OrderBy(l => l.levelNumber).ToList();
            var misnumbered = campaign.Where((l, i) => l.levelNumber != i + 1).ToList();
            Check(misnumbered.Count == 0,
                  $"campaign levelNumbers are contiguous from 1 ({campaign.Count} levels)" +
                  (misnumbered.Count == 0 ? "" : $" (first bad: {misnumbered[0].displayName} " +
                                                 $"is L{misnumbered[0].levelNumber})"));

            // Ids are the PlayerPrefs keys the star results are stored under, so a duplicate
            // silently makes two levels share a best-star record. The rigs are excluded from
            // TotalStars but still record stars, so this covers all 24.
            // --- LOADOUT ---------------------------------------------------------------------
            {
                var roster = AssetDatabase.LoadAssetAtPath<RosterDefinitionSO>(
                    "Assets/GameData/Roster.asset");
                Check(roster != null && roster.slots.Count > 0, "the roster asset exists");

                if (roster != null)
                {
                    ProgressStore.ResetAll();
                    System.Func<string, bool> unlocked = ProgressStore.IsUnitUnlocked;

                    Check(roster.slots.Any(s2 => s2.coinPrice == 0),
                          "at least one roster unit is free — a locked-out player has no squad");
                    Check(roster.slots.All(s2 => s2.unit != null && s2.pointCost >= 1),
                          "every roster slot has a unit and costs at least a point");
                    Check(roster.slots.OrderBy(s2 => s2.pointCost).First().coinPrice == 0,
                          "the CHEAPEST unit is the free one — a locked specialist is swapped " +
                          "for it, so a dear free unit would make the substitute unaffordable");

                    // WITHOUT the encounter grant, Begin still fields a legal all-rifle line
                    // (pillar 8). WITH the grant, Begin fields the authored mix.
                    var mixed = levels.First(l2 => !l2.isTestLevel
                        && Loadout.AuthoredPicks(l2).Any(p => p.Unit != null && p.Unit.id != "rifleman"));
                    ProgressStore.ResetAll();
                    var beforeGrant = Loadout.Default(mixed, roster, unlocked);
                    Check(Loadout.IsLegal(beforeGrant, mixed, roster, unlocked),
                          $"{mixed.displayName}: Default stays legal when specialists are locked " +
                          "(locked slots become riflemen)");
                    Check(beforeGrant.All(p => p.Unit != null && p.Unit.id == "rifleman"),
                          $"{mixed.displayName}: locked specialists substitute to riflemen, " +
                          "not an illegal mix");
                    EncounterUnlocks.GrantUnits(mixed);
                    var afterGrant = Loadout.Default(mixed, roster, ProgressStore.IsUnitUnlocked);
                    Check(afterGrant.Any(p => p.Unit != null && p.Unit.id != "rifleman"),
                          $"{mixed.displayName}: after the grant, Default keeps the authored specialist");

                    foreach (var lv in levels.Where(l2 => !l2.isTestLevel))
                    {
                        EncounterUnlocks.GrantUnits(lv);
                        var def = Loadout.Default(lv, roster, ProgressStore.IsUnitUnlocked);
                        var authored = Loadout.AuthoredPicks(lv);
                        Check(def.Count == authored.Count
                              && def.All(p => authored.Any(a => a.Unit == p.Unit && a.Count == p.Count)),
                              $"{lv.displayName}: after grant, Default IS the authored mix " +
                              "(not N cheapest)");
                        Check(Loadout.PointsUsed(def, roster) <= Loadout.Budget(lv),
                              $"{lv.displayName}: authored mix ({Loadout.PointsUsed(def, roster)}pt) " +
                              $"fits deployBudget {Loadout.Budget(lv)}");
                        Check(Loadout.IsLegal(def, lv, roster, unlocked),
                              $"{lv.displayName}: the DEFAULT loadout is legal — pillar 8, a " +
                              "player who taps straight through must lose nothing");
                        Check(Loadout.UnitsUsed(def) == Loadout.Slots(lv),
                              $"{lv.displayName}: the default fills every slot " +
                              $"({Loadout.UnitsUsed(def)}/{Loadout.Slots(lv)}) — this is the " +
                              "squad the level was balanced against");

                        // THE FRAMING CONTRACT. Composition rule 1 measures the player line and
                        // the aiming camera is framed on it, so no legal loadout may make the
                        // line wider than the level was authored with. Checked against the real
                        // built state, the same number LevelComposition reads.
                        var asAuthored = LevelBuilder.BuildInitialState(lv, 1, 12, new System.Random(9));
                        var chosen = Loadout.ToPlayerGroups(lv, def);
                        var asPicked = LevelBuilder.BuildInitialState(
                            lv, 1, 12, new System.Random(9), playerGroupsOverride: chosen);
                        Check(asPicked.PlayerCamHalfWidth <= asAuthored.PlayerCamHalfWidth + 0.01f,
                              $"{lv.displayName}: the default squad is no wider than the authored " +
                              $"line ({asPicked.PlayerCamHalfWidth:F2} vs " +
                              $"{asAuthored.PlayerCamHalfWidth:F2})");

                        // A squad of the DEAREST affordable units must also fit the frame — that
                        // is the widest a legal loadout can get once everything is unlocked.
                        var dearest = roster.slots.OrderByDescending(s2 => s2.pointCost).First();
                        int many = Mathf.Min(Loadout.Slots(lv),
                                             Loadout.Budget(lv) / dearest.pointCost);
                        if (many > 0)
                        {
                            var heavy = new List<Pick> { new(dearest.unit, many) };
                            var st2 = LevelBuilder.BuildInitialState(
                                lv, 1, 12, new System.Random(9),
                                playerGroupsOverride: Loadout.ToPlayerGroups(lv, heavy));
                            Check(st2.PlayerCamHalfWidth <= asAuthored.PlayerCamHalfWidth + 0.01f,
                                  $"{lv.displayName}: an all-{dearest.unit.name} squad also fits " +
                                  "the authored frame");
                        }

                        // The budget must never be so tight that the default cannot fill the
                        // level, nor so loose that slots stop being the binding constraint.
                        Check(Loadout.Budget(lv) >= Loadout.Slots(lv),
                              $"{lv.displayName}: deployBudget ({Loadout.Budget(lv)}) covers at " +
                              $"least one cheap body per slot ({Loadout.Slots(lv)})");
                    }

                    // L1–L2 stay rifle. From L3 the authored default is a specialist mix —
                    // that is the whole point of Default reading the level instead of N cheapest.
                    bool Has(int n, string id) =>
                        Loadout.AuthoredPicks(campaign.First(c => c.levelNumber == n))
                               .Any(p => p.Unit != null && p.Unit.id == id);
                    Check(!Has(1, "grenadier") && !Has(2, "grenadier"),
                          "L1 and L2 stay rifle-only — the drag is the lesson");
                    Check(Has(3, "grenadier") && Has(5, "sniper") && Has(6, "rocket_trooper"),
                          "L3 grenadier, L5 sniper, L6 rocket — the first three specialist teaches");
                    Check(Has(7, "rocket_trooper") && Has(8, "sniper") && Has(8, "grenadier"),
                          "L7 rocket, L8 combines L5's mix");
                    Check(Has(9, "shield_bearer") && Has(10, "machine_gunner")
                          && Has(11, "sniper") && Has(11, "rocket_trooper")
                          && Has(12, "rocket_trooper"),
                          "L9–L12 each carry a specialist the all-rifle default used to hide");

                    // Garrisons are level geometry and must survive any loadout.
                    var withTank = levels.First(l2 => !l2.isTestLevel
                        && l2.playerGroups.Any(g => !string.IsNullOrEmpty(g.standingOnStructureId)));
                    var kept = Loadout.ToPlayerGroups(withTank,
                        Loadout.Default(withTank, roster, unlocked));
                    Check(kept.Count(g => !string.IsNullOrEmpty(g.standingOnStructureId))
                          == withTank.playerGroups.Count(g => !string.IsNullOrEmpty(g.standingOnStructureId)),
                          "a loadout never disturbs the garrisoned groups — the tank crew is " +
                          "level geometry, not a squad pick");

                    // DISJOINT GROUND GROUPS. The mean of two flanks is the gap, and the gap
                    // is where the scenery sits. Campaign levels are one contiguous line, so
                    // the centre must not move; the parade rig is the case that used to fail.
                    var moved = levels.Where(l2 => !l2.isTestLevel).Select(lv =>
                    {
                        var ground = lv.playerGroups
                            .Where(g => string.IsNullOrEmpty(g.standingOnStructureId)).ToList();
                        if (ground.Count == 0) return (name: (string)null, d: 0f);
                        float old = ground.Sum(g => g.anchorX * g.count)
                                    / Mathf.Max(1, ground.Sum(g => g.count));
                        return (name: lv.displayName,
                                d: Mathf.Abs(Loadout.GroundAnchorX(lv) - old));
                    }).Where(t => t.name != null && t.d > 1e-4f).ToList();
                    Check(moved.Count == 0,
                          "GroundAnchorX still centres every campaign line on its authored " +
                          "mean — a disjoint-flank fix must not move a contiguous squad" +
                          (moved.Count == 0 ? "" : $" (moved: {moved[0].name} by {moved[0].d:F3})"));
                    var parade = levels.FirstOrDefault(l2 => l2.id == "level_test_natural_parade");
                    if (parade != null)
                    {
                        float ax = Loadout.GroundAnchorX(parade);
                        Near(ax, -5.6f, 0.05f,
                             "disjoint flanks pick the left scale-reference group, " +
                             "not the gap (0 is RidgeWatchtower)");
                        var paradeGroups = Loadout.ToPlayerGroups(
                            parade, Loadout.Default(parade, roster, unlocked));
                        var paradeState = LevelBuilder.BuildInitialState(
                            parade, 1, 12, new System.Random(9),
                            playerGroupsOverride: paradeGroups);
                        // The gap trap is the RIDGE at x 0.2, not the cliff the left
                        // flank was authored against. Counting any box would fail the
                        // correct placement — those two men already brush CliffOutcrop.
                        int inRidge = 0;
                        foreach (var u in paradeState.PlayerUnits)
                        {
                            if (u.StandingOnStructureId != null) continue;
                            foreach (var st in paradeState.Structures)
                            {
                                if (st.Definition == null || st.Definition.name != "RidgeWatchtower")
                                    continue;
                                float halfW = (st.Definition.hasHitWidth ? st.Definition.hitWidth
                                                                         : st.Definition.size) / 2f;
                                if (Mathf.Abs(u.X - st.X) <= halfW) inRidge++;
                            }
                        }
                        Check(inRidge == 0,
                              "a parade loadout does not spawn inside RidgeWatchtower " +
                              $"(in-ridge {inRidge}) — the output the player would notice");
                    }

                    // Legality, at the edges. GrantUnits above unlocked every campaign
                    // specialist — Reset so "locked" still means locked.
                    ProgressStore.ResetAll();
                    var one = levels.First(l2 => !l2.isTestLevel);
                    var rifle = roster.slots.OrderBy(s2 => s2.pointCost).First();
                    Check(!Loadout.IsLegal(new List<Pick>(), one, roster, unlocked),
                          "an EMPTY loadout is illegal — that is not a decision, it is no battle");
                    Check(!Loadout.IsLegal(
                              new List<Pick> { new(rifle.unit, Loadout.Slots(one) + 1) },
                              one, roster, unlocked),
                          "overfilling the slots is illegal even when the points would allow it");
                    Check(Loadout.IsLegal(
                              new List<Pick> { new(rifle.unit, 1) }, one, roster, unlocked),
                          "UNDER-filling is legal — fewer, better troops is a real choice");

                    var locked = roster.slots.FirstOrDefault(s2 => s2.coinPrice > 0);
                    if (locked != null)
                        Check(!Loadout.IsLegal(new List<Pick> { new(locked.unit, 1) },
                                               one, roster, unlocked),
                              "a locked unit cannot be fielded before it is bought");

                    ProgressStore.ResetAll();
                }
            }

            var dupIds = levels.GroupBy(l => l.id).Where(g => g.Count() > 1).ToList();
            Check(dupIds.Count == 0,
                  "every level id is unique — ids key the saved star results" +
                  (dupIds.Count == 0 ? "" : $" (duplicated: {dupIds[0].Key})"));

            // MID-BATTLE EVENTS reference level data by NAME, and a name that matches nothing
            // fails silently — the phase simply never fires and the level plays as an ordinary
            // fight. Nothing about that looks broken from the outside, which is why it is
            // asserted rather than trusted.
            foreach (var l in levels)
            {
                var structureIds = new HashSet<string>(
                    l.structures.Where(s2 => !string.IsNullOrEmpty(s2.id)).Select(s2 => s2.id));

                foreach (var b in l.bossPhases)
                {
                    Check(b.triggerStructureIds != null && b.triggerStructureIds.Count > 0,
                          $"{l.displayName}: a boss phase has trigger structures " +
                          "(an empty set never fires — ShouldTriggerBossPhase refuses it)");
                    foreach (var tid in b.triggerStructureIds ?? new List<string>())
                        Check(structureIds.Contains(tid),
                              $"{l.displayName}: boss trigger '{tid}' names a structure the level has");
                    foreach (var g in b.spawnGroups)
                        Check(g.definition != null && g.count > 0,
                              $"{l.displayName}: every boss spawn group has a unit and a count");
                }

                foreach (var w in l.reinforcementWaves)
                {
                    // Turn 1 is the player's first move, so the WHOLE lead has to fit before
                    // arrival: a 2-turn warning on a wave landing on turn 2 gets one turn, and the
                    // level would be shipping half a telegraph while passing an `>= 2` check.
                    Check(w.telegraphLeadTurns >= 1,
                          $"{l.displayName}: a reinforcement wave warns for at least one turn " +
                          $"(lead is {w.telegraphLeadTurns})");
                    Check(w.arrivesOnTurn - w.telegraphLeadTurns >= 1,
                          $"{l.displayName}: a wave arriving on turn {w.arrivesOnTurn} with a " +
                          $"{w.telegraphLeadTurns}-turn lead starts warning before turn 1");
                    Check(!string.IsNullOrEmpty(w.telegraphLabel),
                          $"{l.displayName}: a reinforcement wave carries a telegraph label " +
                          "(pillar 7: telegraph, don't blindside)");
                    // The count belongs to EventSystems.TelegraphLine, which recomputes it every
                    // turn. One authored into the label is a number that stops counting down and
                    // can silently disagree with arrivesOnTurn.
                    Check(w.telegraphLabel == null ||
                          !System.Text.RegularExpressions.Regex.IsMatch(w.telegraphLabel,
                                                                       @"\d+\s*turns?"),
                          $"{l.displayName}: the telegraph label '{w.telegraphLabel}' hardcodes a " +
                          "turn count — the countdown is composed live, not authored");
                    foreach (var g in w.spawnGroups)
                        Check(g.definition != null && g.count > 0,
                              $"{l.displayName}: every wave spawn group has a unit and a count");
                }
            }

            // EVERY AUTHORED STRING THAT REACHES TextMeshPro MUST HAVE A GLYPH FOR EVERY
            // CHARACTER IN IT.
            //
            // The failure is silent: TMP substitutes a blank box and logs nothing, so it shows up
            // on a device, mid-battle, inside a banner that fires on one level on one turn.
            //
            // ASKED OF THE FONT, NOT ASSUMED. This check was first written as "ASCII only",
            // because that is what CLAUDE.md said, and it immediately flagged 23 strings —
            // every campaign levelGoal and every test-rig name, all of which use an em dash. A
            // DEVICE SCREENSHOT then showed the em dash rendering perfectly in the loadout panel:
            // LiberationSans SDF covers Latin-1 and General Punctuation, and it is only the
            // symbols the star and coin icons were replaced over (U+2605, U+25C6) that it lacks.
            // An ASCII rule would have been 23 false positives dressed as bugs. Ask
            // TMP_Settings.defaultFontAsset what it actually has.
            {
                var font = TMP_Settings.defaultFontAsset;
                Check(font != null, "the default TMP font asset loads, so glyph coverage is " +
                                    "checkable at all (a null one would pass every check below)");
                // Proves the check can FAIL, in the same run that uses it: a character the font is
                // known not to have must be reported missing. Without this the whole block is
                // vacuous the day HasCharacter starts returning true for everything.
                Check(font != null && !font.HasCharacter('\u2605'),
                      "and reports the star glyph MISSING, which is why the HUD draws it as a sprite");

                void Glyphs(string s, string what)
                {
                    if (string.IsNullOrEmpty(s) || font == null) return;
                    int bad = -1;
                    for (int i = 0; i < s.Length && bad < 0; i++)
                        if (!char.IsWhiteSpace(s[i]) && !font.HasCharacter(s[i])) bad = i;
                    // The offending codepoint is REPORTED, not just flagged. A blank box in a
                    // banner tells you nothing about which character it was, and the whole reason
                    // this class of bug survives is that the failure carries no information.
                    Check(bad < 0,
                          bad < 0 ? $"{what} renders in the default font"
                                  : $"{what} has NO GLYPH for U+{(int)s[bad]:X4} at {bad}: \'{s}\'");
                }

                foreach (var l in levels)
                {
                    Glyphs(l.displayName, $"L{l.levelNumber} displayName");
                    Glyphs(l.levelGoal, $"L{l.levelNumber} levelGoal");
                    foreach (var b in l.bossPhases)
                        Glyphs(b.announcement, $"L{l.levelNumber} boss announcement");
                    foreach (var w in l.reinforcementWaves)
                    {
                        Glyphs(w.announcement, $"L{l.levelNumber} wave announcement");
                        Glyphs(w.telegraphLabel, $"L{l.levelNumber} telegraph label");
                    }
                }

                foreach (var u in AssetDatabase.FindAssets("t:UnitDefinitionSO")
                             .Select(AssetDatabase.GUIDToAssetPath)
                             .Select(AssetDatabase.LoadAssetAtPath<UnitDefinitionSO>)
                             .Where(u => u != null))
                    Glyphs(u.displayName, $"unit {u.name} displayName");

                // And the strings the CODE composes. Wind's came over from the Kotlin carrying a
                // wind emoji and a directional arrow; wind is not wired yet, so those three boxes
                // were waiting for whoever wires it.
                Glyphs(EventSystems.WindShiftAnnouncement(1f, 1.35f, true), "wind rising banner");
                Glyphs(EventSystems.WindShiftAnnouncement(1.35f, 1f, false), "wind falling banner");
                Glyphs(EventSystems.TelegraphLine("Armor column inbound", 2), "a composed telegraph");

                // THE VICTORY BANNERS, which were not covered until 2026-08-12 and one of them
                // was broken the whole time: "New 3★ Best!" carried U+2605 — the very codepoint
                // this block proves the font lacks two checks above — onto the one screen whose
                // job is to congratulate the player. Every tag AwardVictory can compose is listed
                // here, so a new one cannot be added without passing through this.
                Glyphs("First Clear!", "victory tag: first clear");
                Glyphs("New 3-Star Best!", "victory tag: new 3-star best");
                Glyphs("Daily Bonus!", "victory tag: daily bonus");
                foreach (int star in new[] { 25, 50, 75, 100 })
                    Glyphs(TurnFlow.MilestoneTag(star), $"victory tag: {star}-star chest");
                Glyphs(TurnFlow.StarReason(7, 10), "victory star reason");
                Glyphs(TurnFlow.StarReason(10, 10), "victory star reason, clean sweep");
                Glyphs(TurnFlow.DefeatCharge, "defeat reason: charge");
                Glyphs(TurnFlow.DefeatGarrison, "defeat reason: garrison");
                Glyphs(TurnFlow.DefeatVolley, "defeat reason: volley");
                Glyphs(TurnFlow.DefeatOverrun, "defeat reason: overrun");
                Glyphs(TurnFlow.NudgeLine(ConsumableType.TraumaKit, 0), "defeat nudge: trauma, unowned");
                Glyphs(TurnFlow.NudgeLine(ConsumableType.TraumaKit, 1), "defeat nudge: trauma, owned");
                Glyphs(TurnFlow.NudgeLine(ConsumableType.SmokeScreen, 0), "defeat nudge: smoke, unowned");
                Glyphs(TurnFlow.NudgeLine(ConsumableType.Airstrike, 0), "defeat nudge: airstrike, unowned");
                Glyphs(TurnFlow.NudgeLine(ConsumableType.Airstrike, 1), "defeat nudge: airstrike, owned");
            }

            // END TO END: a boss phase must actually put units on the field. The decision
            // function has been tested since the port; what was never true is that anything
            // CALLED it — bossPhases were read only to size the pools, so the Sovereign would
            // have stayed off the board no matter how correct the arithmetic was.
            {
                var bossLevel = levels.First(l => !l.isTestLevel && l.bossPhases.Count > 0);
                var st0 = LevelBuilder.BuildInitialState(bossLevel, 1, 12, new System.Random(3));
                int before = st0.EnemyUnits.Count;

                // Remove the trigger structures and their garrison, which is what defeating them
                // looks like to the tick, then run one step.
                var triggerIds = new HashSet<string>(bossLevel.bossPhases[0].triggerStructureIds);
                var doomed = new HashSet<int>();
                for (int i = 0; i < bossLevel.structures.Count; i++)
                    if (triggerIds.Contains(bossLevel.structures[i].id))
                        doomed.Add(LevelBuilder.StructureIdBase + i);

                var razed = st0 with
                {
                    // BuildInitialState does not set Phase — BattleRunner.LoadLevel does, right
                    // after. Without it Step takes the cosmetic-only path and no event fires,
                    // which is exactly how this check failed the first time it was written.
                    Phase = GamePhase.Playing,
                    TurnPhase = TurnPhase.Aiming,
                    Structures = st0.Structures.Where(s2 => !doomed.Contains(s2.Id)).ToList(),
                    EnemyUnits = st0.EnemyUnits
                        .Where(u => u.StandingOnStructureId == null
                                 || !doomed.Contains(u.StandingOnStructureId.Value)).ToList(),
                };
                var after = BattleTick.Step(razed, 0.016f, bossLevel, new System.Random(3));

                Check(after.TriggeredBossPhases.Count == 1,
                      $"{bossLevel.displayName}: the boss phase fires once its structure is gone");
                Check(after.EnemyUnits.Count > razed.EnemyUnits.Count,
                      "and puts its spawn groups on the field");
                Check(after.BossAnnouncementTimer > 0f,
                      "and arms the arrival hold");
                Check(string.IsNullOrEmpty(after.BossAnnouncement),
                      "the flavor banner is not raised — Sovereign-will-not-yield is withdrawn");

                // L12 is the motivating case: the citadel's captured frame is ~5 half-width
                // and the escort is four men. Recapture + arrival push-in is what makes
                // the riot shields readable. Asserted on TheCitadel, not "any boss", so a
                // compact L6 spawn cannot hide a still-wide L12.
                var citadel = levels.FirstOrDefault(l => l.id == "level_12");
                if (citadel == null) Check(false, "TheCitadel asset present for the armour zoom");
                else
                {
                    var c0 = LevelBuilder.BuildInitialState(citadel, 1, 12, new System.Random(3));
                    var citadelDoomed = new HashSet<int>();
                    for (int i = 0; i < citadel.structures.Count; i++)
                        if (citadel.structures[i].id == "citadel")
                            citadelDoomed.Add(LevelBuilder.StructureIdBase + i);

                    var cRazed = c0 with
                    {
                        Phase = GamePhase.Playing,
                        TurnPhase = TurnPhase.Resolving,
                        TurnSide = TurnSide.Player,
                        Structures = c0.Structures.Where(s2 => !citadelDoomed.Contains(s2.Id)).ToList(),
                        EnemyUnits = c0.EnemyUnits
                            .Where(u => u.StandingOnStructureId == null
                                     || !citadelDoomed.Contains(u.StandingOnStructureId.Value))
                            .ToList(),
                    };
                    var cAfter = BattleTick.Step(cRazed, 0.016f, citadel, new System.Random(3));

                    Check(cAfter.TriggeredBossPhases.Count == 1,
                          "L12: dropping the citadel fires the Sovereign");
                    Check(cAfter.EnemyCamHalfWidth < c0.EnemyCamHalfWidth - 0.8f,
                          $"L12: the leftover frame TIGHTENS once the citadel is gone " +
                          $"({c0.EnemyCamHalfWidth:F2} -> {cAfter.EnemyCamHalfWidth:F2})");
                    Check(cAfter.ArrivalCamHalfWidth > 0f
                          && cAfter.ArrivalCamHalfWidth < cAfter.EnemyCamHalfWidth
                          && cAfter.BossAnnouncementTimer > 0f,
                          $"L12: the announcement frames the ESCORT tighter still " +
                          $"(arrival {cAfter.ArrivalCamHalfWidth:F2} vs leftover " +
                          $"{cAfter.EnemyCamHalfWidth:F2})");

                    // Casualties must NOT twitch the zoom — the recapture is on the SET
                    // changing, not on membership. Kill one leftover, the captured
                    // leftover half-width stays.
                    if (cAfter.EnemyUnits.Count > 1)
                    {
                        var oneDown = cAfter with
                        {
                            EnemyUnits = cAfter.EnemyUnits.Skip(1).ToList(),
                            BossAnnouncementTimer = 0f,
                            ArrivalCamHalfWidth = 0f,
                        };
                        var afterKill = BattleTick.Step(oneDown, 0.016f, citadel,
                                                        new System.Random(3));
                        Near(afterKill.EnemyCamHalfWidth, cAfter.EnemyCamHalfWidth, 1e-4f,
                             "killing one leftover does not recapture the enemy frame");
                    }
                }

                // TELEGRAPHS. A wave arriving on turn N must warn on turn N-1 and the warning must
                // still be standing for the whole of that turn — pillar 7. Driven off the real
                // level so a wave authored with no telegraph text cannot pass.
                var waveLevel = levels.First(l => !l.isTestLevel && l.reinforcementWaves.Count > 0);
                var wave0 = waveLevel.reinforcementWaves[0];
                var ws = LevelBuilder.BuildInitialState(waveLevel, 1, 12, new System.Random(4))
                         with { Phase = GamePhase.Playing, TurnPhase = TurnPhase.Aiming };

                int lead = wave0.telegraphLeadTurns;
                var early = BattleTick.Step(ws with { TurnNumber = wave0.arrivesOnTurn - lead - 1 },
                                            0.016f, waveLevel, new System.Random(4));
                Check(string.IsNullOrEmpty(early.TelegraphText),
                      "no telegraph before the wave's lead begins — a warning that early is noise");

                // ASSERT THE STRING THE PLAYER READS, not that some telegraph is set. The count
                // is the whole point of a multi-turn lead, and it is the part that goes stale.
                var opened = BattleTick.Step(ws with { TurnNumber = wave0.arrivesOnTurn - lead },
                                             0.016f, waveLevel, new System.Random(4));
                Check(opened.TelegraphText ==
                      EventSystems.TelegraphLine(wave0.telegraphLabel, lead),
                      $"the telegraph opens at the full lead reading '{opened.TelegraphText}'");
                Check(opened.TelegraphText != null &&
                      opened.TelegraphText.Contains(lead == 1 ? "1 turn" : $"{lead} turns"),
                      "and carries the live turn count");

                var warned = BattleTick.Step(ws with { TurnNumber = wave0.arrivesOnTurn - 1 },
                                             0.016f, waveLevel, new System.Random(4));
                Check(warned.TelegraphText ==
                      EventSystems.TelegraphLine(wave0.telegraphLabel, 1),
                      "the telegraph stands on the turn before the wave arrives");
                Check(warned.TelegraphText != null && warned.TelegraphText.EndsWith("1 turn"),
                      "and has COUNTED DOWN to one turn — a number that never moves says the " +
                      "clock has stopped");
                if (lead > 1)
                    Check(warned.TelegraphText != opened.TelegraphText,
                          "so the line the player reads is not the same on both turns");
                Check(warned.EnemyUnits.Count == ws.EnemyUnits.Count,
                      "and the telegraph does not spend the wave — it must still arrive");

                var landed = BattleTick.Step(warned with { TurnNumber = wave0.arrivesOnTurn },
                                             0.016f, waveLevel, new System.Random(4));
                Check(landed.EnemyUnits.Count > ws.EnemyUnits.Count, "the wave then arrives");
                Check(string.IsNullOrEmpty(landed.TelegraphText),
                      "and the warning clears itself once it has");
                Check(string.IsNullOrEmpty(landed.BossAnnouncement),
                      "wave arrival does not flash flavor copy");

                var again = BattleTick.Step(after, 0.016f, bossLevel, new System.Random(3));
                Check(again.EnemyUnits.Count == after.EnemyUnits.Count,
                      "and does NOT fire a second time on the next tick");

                var allIds2 = after.EnemyUnits.Select(u => u.Id)
                    .Concat(after.PlayerUnits.Select(u => u.Id)).ToList();
                Check(allIds2.Distinct().Count() == allIds2.Count,
                      "spawned unit ids never collide with the units already fighting");
            }

            var missing = new SortedSet<string>();
            var unitClasses = new SortedSet<string>();
            int built = 0;
            foreach (var l in levels)
            {
                var st = LevelBuilder.BuildInitialState(l, 1, levels.Count, new System.Random(7));
                built++;
                Check(st.PlayerUnits.Count > 0, null);
                foreach (var u in st.PlayerUnits.Concat(st.EnemyUnits))
                    unitClasses.Add(LevelScenery.ModelKey(u.Definition.modelAsset));
                foreach (var s in st.Structures)
                {
                    if (!haveModel.Contains(LevelScenery.ModelKey(s.Definition.modelAsset)))
                        missing.Add(s.Definition.modelAsset);
                    if (!string.IsNullOrEmpty(s.Definition.wreckModelAsset)
                        && !haveModel.Contains(LevelScenery.ModelKey(s.Definition.wreckModelAsset)))
                        missing.Add(s.Definition.wreckModelAsset);
                }
                foreach (var p in l.props)
                    if (!haveModel.Contains(LevelScenery.ModelKey(p.modelAsset)))
                        missing.Add(p.modelAsset);
            }
            Check(built == levels.Count, $"every level builds an initial state ({built})");
            Check(missing.Count == 0,
                  missing.Count == 0
                      ? "every structure and prop the campaign places has an imported model"
                      : $"MODELS NOT IMPORTED: {string.Join(", ", missing)}");

            var wreckMissing = new SortedSet<string>();
            foreach (var path in AssetDatabase.FindAssets("t:StructureDefinitionSO")
                         .Select(AssetDatabase.GUIDToAssetPath))
            {
                var def = AssetDatabase.LoadAssetAtPath<StructureDefinitionSO>(path);
                if (def == null || def.isPlayerSide && def.hasCannon) continue;
                if (string.IsNullOrEmpty(def.wreckModelAsset))
                {
                    wreckMissing.Add(def.id + " (no wreck)");
                    continue;
                }
                var key = LevelScenery.ModelKey(def.wreckModelAsset);
                if (!haveModel.Contains(key)
                    && AssetDatabase.LoadAssetAtPath<GameObject>(
                           $"Assets/Models/{key}.glb") == null)
                    wreckMissing.Add(def.wreckModelAsset);
            }
            Check(wreckMissing.Count == 0,
                  wreckMissing.Count == 0
                      ? "every destroyable structure has an imported collapse"
                      : $"WRECKS MISSING: {string.Join(", ", wreckMissing)}");

            // OUTPUT: L8's wrecks ROTATE. Euler keys on a glTF-imported
            // QUATERNION object export location only — the hut drops 5cm
            // and still reads as a building. Seen 2026-08-16 on Timberline.
            foreach (var wreckPath in new[]
                     {
                         "Assets/Models/watch_tower_collapse.glb",
                         "Assets/Models/garrison_post_collapse.glb",
                     })
            {
                var go = AssetDatabase.LoadAssetAtPath<GameObject>(wreckPath);
                Check(go != null, $"{wreckPath} is imported");
                if (go == null) continue;
                var inst = (GameObject)PrefabUtility.InstantiatePrefab(go);
                try
                {
                    var body = inst.GetComponent<Animation>()
                               ?? inst.GetComponentInChildren<Animation>(true);
                    var clip = body == null ? null : body[WreckAnim.Collapse];
                    if (clip == null && body != null)
                        foreach (AnimationState s in body) { clip = s; break; }
                    Check(body != null && clip != null,
                          $"{wreckPath} can play collapse");
                    if (body == null || clip == null) continue;
                    var rest = inst.GetComponentsInChildren<Transform>(true)
                        .Where(t => t != inst.transform)
                        .ToDictionary(t => t, t => t.localRotation);
                    clip.wrapMode = WrapMode.ClampForever;
                    clip.time = clip.length;
                    clip.enabled = true;
                    body.Play(clip.name);
                    body.Sample();
                    float travel = rest
                        .Select(kv => Quaternion.Angle(kv.Value, kv.Key.localRotation))
                        .DefaultIfEmpty(0f)
                        .Max();
                    Check(travel > 20f,
                          $"{System.IO.Path.GetFileName(wreckPath)} ROTATES "
                          + $"({travel:F1} deg) — location-only is a standing wreck");

                    // L8 leftover was a short hut (0.92 of 1.62). Outpost
                    // bar is ~0.50 of standing. Sample the clip, not the
                    // rest pose — a grid of boxes at frame 1 is still a hut.
                    if (wreckPath.Contains("garrison_post"))
                    {
                        float Span()
                        {
                            float y0 = 1e9f, y1 = -1e9f;
                            foreach (var r in inst.GetComponentsInChildren<MeshRenderer>(true))
                            {
                                if (!r.enabled) continue;
                                y0 = Mathf.Min(y0, r.bounds.min.y);
                                y1 = Mathf.Max(y1, r.bounds.max.y);
                            }
                            return y1 - y0;
                        }
                        clip.time = 0f;
                        body.Sample();
                        float restH = Span();
                        clip.time = clip.length;
                        body.Sample();
                        float endH = Span();
                        Check(restH > 0.4f && endH < restH * 0.50f,
                              $"garrison leftover is a pile, not a hut "
                              + $"(end {endH:F2} of rest {restH:F2})");
                    }
                }
                finally
                {
                    Object.DestroyImmediate(inst);
                }
            }

            // Every unit CLASS the levels actually field needs its own rigged silhouette and a
            // prefab per side, or BattleRunner has no pool to hand it a slot from and the soldier
            // renders as nothing at all. This is the guard for that: a class added to the Kotlin
            // roster without a matching build_units_rigged.py builder fails HERE, in a second,
            // rather than as a gap in a firing line on a device.
            var noArt = unitClasses.Where(k => !RiggedUnits.Models.Contains(k)).ToList();
            Check(noArt.Count == 0,
                  noArt.Count == 0
                      ? $"every unit class the campaign fields has a rigged model ({unitClasses.Count})"
                      : $"UNIT CLASSES WITH NO RIGGED MODEL: {string.Join(", ", noArt)}");

            var noPrefab = new SortedSet<string>();
            foreach (var k in unitClasses)
                foreach (var side in new[] { "Player", "Enemy" })
                    if (AssetDatabase.LoadAssetAtPath<GameObject>(
                            $"Assets/Prefabs/{side}Unit_{k}.prefab") == null)
                        noPrefab.Add($"{side}Unit_{k}");
            Check(noPrefab.Count == 0,
                  noPrefab.Count == 0
                      ? "every fielded class has a per-side prefab"
                      : $"PREFABS MISSING (rebuild the scene): {string.Join(", ", noPrefab)}");

            // Aim split: torso lean + remaining arm pitch. Ready is
            // arms-only (the signed hold). A live aim must still SUM to
            // the drag, or the muzzle lies.
            {
                UnitAnim.SplitAim(-UnitAnim.ReadyDrop, true, out float t0, out float a0);
                Check(Mathf.Abs(t0) < 0.01f && Mathf.Abs(a0 + UnitAnim.ReadyDrop) < 0.01f,
                      "ready is arms-only — the signed hold does not slouch");
                UnitAnim.SplitAim(45f, true, out float t45, out float a45);
                Near(t45 + a45, 45f, 0.01f, "torso + arms is the drag at 45°");
                Check(t45 > 4f && t45 <= UnitAnim.TorsoMax,
                      $"45° leans the torso ({t45:F1}°, cap {UnitAnim.TorsoMax})");
                UnitAnim.SplitAim(45f, false, out float tw, out float aw);
                Check(Mathf.Abs(tw) < 0.01f && Mathf.Abs(aw - 45f) < 0.01f,
                      "a walking charger does not lean — the march already is whole-body");
                UnitAnim.SplitAim(90f, true, out float t90, out float a90);
                Near(t90 + a90, 90f, 0.01f, "torso + arms is the drag at 90°");
                Near(t90, UnitAnim.TorsoMax, 0.01f, "a high arc caps the lean so they do not fall over");
            }

            // --- WHY UnitAnim.Stand() RESTORES THE ROOT BY HAND.
            //
            // `die` drives the ROOT; nothing below it does. Legacy Animation leaves a transform
            // wherever the clip last sampled it, so stopping the death and restarting the idle
            // brings back every joint EXCEPT the root — and a recycled slot then plays a perfect
            // breathing loop while lying on its back. That shipped: restarting a level came back
            // with the whole enemy line dead on the ground.
            //
            // This asserts the SHAPE of the problem rather than the symptom, so a future clip set
            // that breaks the assumption says so here instead of on a device.
            var animPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/Prefabs/PlayerUnit_unit_rifleman.prefab");
            if (animPrefab != null)
            {
                var animation = animPrefab.GetComponentInChildren<Animation>();
                Check(animation != null, "the unit prefab carries an Animation component");
                if (animation != null)
                {
                    bool DrivesRoot(string clipName)
                    {
                        var st = animation[clipName];
                        if (st?.clip == null) return false;
                        foreach (var b in AnimationUtility.GetCurveBindings(st.clip))
                            if (string.IsNullOrEmpty(b.path)) return true;
                        return false;
                    }
                    Check(DrivesRoot(UnitAnim.Die),
                          "`die` drives the ROOT — which is why a corpse's root has to be restored");
                    Check(!DrivesRoot(UnitAnim.Idle),
                          "`idle` does NOT drive the root, so it cannot undo a death on its own");

                    bool DrivesPath(string clipName, string path)
                    {
                        var st = animation[clipName];
                        if (st?.clip == null) return false;
                        foreach (var b in AnimationUtility.GetCurveBindings(st.clip))
                            if (b.path == path) return true;
                        return false;
                    }
                    // `walk` writes the legs; `idle` does not. A march that CrossFades to idle
                    // mid-stride therefore stays mid-stride — the hold-line frozen run Rob
                    // caught on 2026-08-13. UnitAnim.RestoreStance puts the authored stance
                    // back; this pair is the reason that method has to exist.
                    Check(DrivesPath(UnitAnim.Walk, "leg-left")
                          && DrivesPath(UnitAnim.Walk, "leg-right"),
                          "`walk` drives both legs");
                    Check(!DrivesPath(UnitAnim.Idle, "leg-left")
                          && !DrivesPath(UnitAnim.Idle, "leg-right"),
                          "`idle` does NOT drive the legs, so a stopped march cannot stand itself up");

                    // OUTPUT: a stopped march stands up. The pair above is why; this is the
                    // restore. Instantiated, walked to mid-stride, stopped, LateUpdate — the
                    // left leg must be back at rest. Seen against the shape of the bug: idle
                    // Sample after CrossFade leaves the stride on the joint.
                    {
                        var go = (GameObject)PrefabUtility.InstantiatePrefab(animPrefab);
                        try
                        {
                            var ua = go.GetComponent<UnitAnim>();
                            var body = go.GetComponentInChildren<Animation>();
                            var leg = body != null ? body.transform.Find("leg-left") : null;
                            Check(ua != null && body != null && body[UnitAnim.Walk] != null
                                  && leg != null,
                                  "the rifleman prefab can play walk");
                            if (ua != null && body != null && body[UnitAnim.Walk] != null
                                && leg != null)
                            {
                                // executeMethod is not play mode: Awake never ran, so the
                                // rest-pose cache and the Animation ref are still empty.
                                ua.SendMessage("Awake", SendMessageOptions.DontRequireReceiver);
                                var rest = leg.localRotation;
                                ua.SetWalking(true);
                                // CrossFade has not blended yet; Play + Sample is the pose.
                                body.Play(UnitAnim.Walk);
                                body[UnitAnim.Walk].time = 0.166f;
                                body.Sample();
                                float stride = Quaternion.Angle(rest, leg.localRotation);
                                ua.SendMessage("LateUpdate",
                                               SendMessageOptions.DontRequireReceiver);
                                float full = Quaternion.Angle(rest, leg.localRotation);
                                ua.SetWalking(true, UnitAnim.MarchStride, UnitAnim.MarchAnimSpeed);
                                body[UnitAnim.Walk].time = 0.166f;
                                body.Sample();
                                ua.SendMessage("LateUpdate",
                                               SendMessageOptions.DontRequireReceiver);
                                float marched = Quaternion.Angle(rest, leg.localRotation);
                                ua.SetWalking(false);
                                body.Sample();
                                ua.SendMessage("LateUpdate",
                                               SendMessageOptions.DontRequireReceiver);
                                float stood = Quaternion.Angle(rest, leg.localRotation);
                                Check(stride > 10f && stood < 2f,
                                      $"a stopped march STANDS UP "
                                      + $"(leg {stood:F1} deg from rest, was {stride:F1} mid-stride)");
                                // OUTPUT: the arrive stride is smaller than Kenney's jog.
                                // A missing ApplyStride leaves marched == full and this goes red.
                                Check(full > 10f
                                      && marched < full * 0.70f
                                      && marched > full * 0.30f,
                                      $"player arrive is a MARCH, not Kenney's 60° jog "
                                      + $"(full {full:F1} deg, marched {marched:F1})");

                                // --- THE CHARGE IS A RUN, re-gaited 2026-08-24.
                                //
                                // Rob: "the leg movements are too dramatic ... make it look
                                // more like a run." The charge had been playing the clip raw.
                                // ASK THE ASSET, NOT THE COMMENT: the file said "60° hip jog"
                                // and the clip is ±60° — 120° of scissor — at 3 steps a second.
                                // That is a sprinter's amplitude on a stroller's cadence, which
                                // is exactly backwards from a run and is what read as flailing.
                                //
                                // Measured off the clip so the constants cannot drift from it,
                                // then asserted on the OUTPUT: less swing AND quicker legs.
                                body.Play(UnitAnim.Walk);
                                float lo = 999f, hi = -999f;
                                for (int i = 0; i <= 24; i++)
                                {
                                    body[UnitAnim.Walk].time =
                                        body[UnitAnim.Walk].clip.length * i / 24f;
                                    body.Sample();
                                    float a = leg.localEulerAngles.x;
                                    if (a > 180f) a -= 360f;
                                    lo = Mathf.Min(lo, a); hi = Mathf.Max(hi, a);
                                }
                                float hipSwing = (hi - lo) / 2f;
                                float cycle = body[UnitAnim.Walk].clip.length;
                                Check(Mathf.Abs(hipSwing - UnitAnim.FullSwingDegrees) < 2f
                                      && Mathf.Abs(cycle - UnitAnim.WalkCycleSeconds) < 0.02f,
                                      $"the walk clip is what UnitAnim says it is "
                                      + $"(±{hipSwing:F1} deg over {cycle:F3}s)");

                                // The feet's own carry, from the RIG: the hip joint's height is
                                // the leg, a stride is two steps of 2·L·sin(hipSwing), and the
                                // prefab scale puts it in world units. If a re-export moves the
                                // joint or rescales the man, this goes red rather than quietly
                                // regressing the gait match.
                                float legLen = leg.localPosition.y;
                                float carry = 4f * legLen
                                              * Mathf.Sin(hipSwing * Mathf.Deg2Rad)
                                              * go.transform.localScale.y;
                                Check(Mathf.Abs(carry - UnitAnim.CycleCarryUnits) < 0.03f,
                                      $"one walk cycle carries {carry:F3} world units "
                                      + $"(UnitAnim.CycleCarryUnits {UnitAnim.CycleCarryUnits:F3})");

                                // OUTPUT 1: the charge SWINGS LESS. Against the old code — the
                                // charge at stride 1 — charged == full and this is red.
                                ua.SetWalking(true, UnitAnim.ChargeStride,
                                              UnitAnim.GaitSpeed(AdvanceSystems.AdvanceSpeed,
                                                                 UnitAnim.ChargeStride));
                                body[UnitAnim.Walk].time = 0.166f;
                                body.Sample();
                                ua.SendMessage("LateUpdate",
                                               SendMessageOptions.DontRequireReceiver);
                                float charged = Quaternion.Angle(rest, leg.localRotation);
                                float chargeCadence =
                                    UnitAnim.GaitSpeed(AdvanceSystems.AdvanceSpeed,
                                                       UnitAnim.ChargeStride);
                                Check(charged < full * 0.85f && charged > marched,
                                      $"the charge is CONTAINED, and still bigger than the "
                                      + $"march (full {full:F1}, charge {charged:F1}, "
                                      + $"march {marched:F1} deg)");

                                // OUTPUT 2: and QUICKER. A run is cadence, not amplitude — drop
                                // the hipSwing without this and the charge is merely a smaller
                                // stroll. Red against the old code, which walked at speed 1.
                                Check(chargeCadence > 1.25f
                                      && chargeCadence <= UnitAnim.MaxGaitSpeed,
                                      $"the charge's legs are QUICKER than the raw clip "
                                      + $"(x{chargeCadence:F2} = {chargeCadence / cycle * 2f:F1} steps/s)");

                                // OUTPUT 3: and the cadence ANSWERS TO THE GROUND. The wire
                                // slows a charger to 35% and nothing used to tell his legs —
                                // he windmilled at full rate to crawl. Derived, so he cannot.
                                float wireCadence =
                                    UnitAnim.GaitSpeed(AdvanceSystems.AdvanceSpeed
                                                       * AdvanceSystems.WireSlowFactor,
                                                       UnitAnim.ChargeStride);
                                Check(wireCadence < chargeCadence * 0.75f,
                                      $"a WIRE-SLOWED charger slows his legs too "
                                      + $"(x{wireCadence:F2} against x{chargeCadence:F2})");
                            }
                        }
                        finally
                        {
                            Object.DestroyImmediate(go);
                        }
                    }

                    // --- THE MELEE SWING, bound 2026-08-13.
                    //
                    // Advancing squads shipped 2026-08-12 with no fight animation at all: a
                    // mutual kill was a knockback flinch and two ragdolls, so the mechanic was
                    // real and unreadable. What can go wrong is not "is the clip listed" — it is
                    // that a clip can be BOUND and move nothing, which is exactly how the port
                    // shipped `walk` in the pack for a week without anyone marching.
                    //
                    // So this measures the ARM the player watches, and carries its own control:
                    // `holding-both` is a static two-handed pose and MUST measure ~0 by the same
                    // ruler. A travel measurement that cannot report zero is not a measurement.
                    // Recorded when written: melee arm-right 161.6 degrees, hold 0.0.
                    float ArmTravel(string clipName)
                    {
                        var st = animation[clipName];
                        if (st?.clip == null) return -1f;
                        var binding = AnimationUtility.GetCurveBindings(st.clip)
                            .Where(b => b.path == "torso/arm-right"
                                        && b.propertyName.StartsWith("m_LocalRotation"))
                            .ToList();
                        if (binding.Count == 0) return 0f;
                        // PER COMPONENT. Pooling x/y/z/w into one range measures the POSE rather
                        // than the motion — the static hold is a constant quaternion whose
                        // components are 0.71 apart, and the first draft of this check read that
                        // as a 90-degree swing and failed its own control.
                        float widest = 0f;
                        foreach (var b in binding)
                        {
                            var c = AnimationUtility.GetEditorCurve(st.clip, b);
                            float lo = float.MaxValue, hi = float.MinValue;
                            foreach (var k in c.keys) { lo = Mathf.Min(lo, k.value); hi = Mathf.Max(hi, k.value); }
                            if (c.keys.Length > 0) widest = Mathf.Max(widest, hi - lo);
                        }
                        // Quaternion components, so the excursion is an angle only up to a
                        // constant — ample for "did this joint move at all", which is the question.
                        return Mathf.Rad2Deg * 2f * Mathf.Asin(Mathf.Clamp01(widest * 0.5f));
                    }

                    float swing = ArmTravel(UnitAnim.Melee), still = ArmTravel(UnitAnim.Hold);
                    Check(swing > 20f && still < 1f && DrivesRoot(UnitAnim.Melee),
                          $"the melee clip SWINGS the right arm ({swing:F1} deg) and steps the root, "
                          + $"measured against a static hold that reads {still:F1} deg");

                    // Die clip stays OFF. Flail is the limb motion on a neutral stance.
                    {
                        var go = (GameObject)PrefabUtility.InstantiatePrefab(animPrefab);
                        try
                        {
                            var ua = go.GetComponent<UnitAnim>();
                            var body = go.GetComponentInChildren<Animation>();
                            var arm = body != null ? body.transform.Find("torso/arm-right") : null;
                            Check(ua != null && body != null && arm != null,
                                  "the rifleman prefab can ragdoll");
                            if (ua != null && body != null && arm != null)
                            {
                                ua.SendMessage("Awake", SendMessageOptions.DontRequireReceiver);
                                ua.Set(UnitAnim.Die);
                                body.Sample();
                                var rest = arm.localRotation;
                                ua.SetRagdoll(7, 0.25f, true);
                                ua.SendMessage("LateUpdate",
                                               SendMessageOptions.DontRequireReceiver);
                                float flung = Quaternion.Angle(rest, arm.localRotation);
                                ua.SetRagdoll(7, 0.25f, false);
                                ua.SendMessage("LateUpdate",
                                               SendMessageOptions.DontRequireReceiver);
                                float settled = Quaternion.Angle(rest, arm.localRotation);
                                Check(flung > 3f && flung < 28f && settled < 2f,
                                      $"an airborne corpse FLAILS "
                                      + $"({flung:F1} deg) and a settled one does not "
                                      + $"({settled:F1} deg)");

                                // SLUMP: against a wall the torso folds, and the flail
                                // does not run over it. Sample die, then LateUpdate with
                                // slump=1, airborne=true — if flail won, the ARM would
                                // move and the torso would not.
                                var torso = body.transform.Find("torso");
                                Check(torso != null, "the rifleman prefab has a torso");
                                if (torso != null)
                                {
                                    body[UnitAnim.Die].normalizedTime = 1f;
                                    body.Sample();
                                    var frozenTorso = torso.localRotation;
                                    var frozenArm = arm.localRotation;
                                    ua.SetRagdoll(7, 0.25f, true, slumpToward: 1f);
                                    ua.SendMessage("LateUpdate",
                                                   SendMessageOptions.DontRequireReceiver);
                                    float folded = Quaternion.Angle(frozenTorso,
                                                                    torso.localRotation);
                                    float armTwitch = Quaternion.Angle(frozenArm,
                                                                       arm.localRotation);
                                    Check(folded > 8f && armTwitch < folded,
                                          $"a body against masonry SLUMPS the torso "
                                          + $"({folded:F1} deg) and does not twitch the "
                                          + $"arm over it ({armTwitch:F1} deg)");
                                }
                            }
                        }
                        finally
                        {
                            Object.DestroyImmediate(go);
                        }
                    }
                }
            }

            // Every rigged prefab, not just the rifleman: the swing has to reach the classes that
            // actually do the fighting. A clip list is per-prefab and `MakePrefab` runs 14 times.
            {
                var noMelee = new List<string>();
                foreach (var k in RiggedUnits.Models)
                    foreach (var side in new[] { "Player", "Enemy" })
                    {
                        var pf = AssetDatabase.LoadAssetAtPath<GameObject>(
                            $"Assets/Prefabs/{side}Unit_{k}.prefab");
                        var a = pf == null ? null : pf.GetComponentInChildren<Animation>();
                        if (a == null || a[UnitAnim.Melee]?.clip == null) noMelee.Add($"{side}Unit_{k}");
                    }
                Check(noMelee.Count == 0,
                      noMelee.Count == 0
                          ? $"all {RiggedUnits.Models.Length * 2} unit prefabs carry the melee swing"
                          : $"NO MELEE CLIP (rebuild the scene): {string.Join(", ", noMelee)}");
            }

            // The held rifle must sit BELOW the helmet. AttachGun is in model units and
            // Normalize scales by the tallest point, so a short helmet (v2's first ACH,
            // 2.64 against the old stacked hat's 2.97) lifts the hold-pose gun to the
            // crown. Assert the thing the player would notice, on the built prefab.
            {
                var pf = AssetDatabase.LoadAssetAtPath<GameObject>(
                    "Assets/Prefabs/PlayerUnit_unit_rifleman.prefab");
                Check(pf != null, "the player rifleman prefab exists to measure the held rifle");
                if (pf != null)
                {
                    var go = (GameObject)PrefabUtility.InstantiatePrefab(pf);
                    try
                    {
                        var body = go.GetComponentInChildren<Animation>();
                        var hold = body != null ? body[UnitAnim.Hold] : null;
                        var gun = go.GetComponentsInChildren<Transform>(true)
                            .FirstOrDefault(t => t.name == "gun");
                        var helm = go.GetComponentsInChildren<MeshRenderer>(true)
                            .FirstOrDefault(r => r.gameObject.name.StartsWith("accent_head"));
                        var gunR = gun != null ? gun.GetComponentInChildren<MeshRenderer>() : null;
                        Check(hold != null && gunR != null && helm != null,
                              "rifleman prefab has a hold clip, a gun mesh and a helmet");
                        if (hold != null && gunR != null && helm != null)
                        {
                            hold.enabled = true;
                            hold.weight = 1f;
                            hold.normalizedTime = 0f;
                            body.Sample();
                            float gunTop = gunR.bounds.max.y;
                            float helmTop = helm.bounds.max.y;
                            Check(gunTop < helmTop - 0.01f,
                                  $"the held rifle sits BELOW the helmet "
                                  + $"(gun top {gunTop:F3} vs helmet {helmTop:F3})");
                            // placeholder_gun's long axis is +X. Identity parenting plus
                            // Kenney's hold pointed that at the camera (world Z after the
                            // facing yaw). Downfield is world X. Assert the span the
                            // player sees, not the localEuler we meant to write.
                            var span = gunR.bounds.size;
                            Check(span.x > span.z * 1.4f,
                                  $"the held rifle lies along the field, not toward the camera "
                                  + $"(span x {span.x:F3} vs z {span.z:F3})");
                            // Unity +X is SCREEN LEFT. The player must face −X
                            // (screen right, the enemy). A check on world +X was
                            // GREEN while the phone showed every back to the
                            // outpost. Ask the facing pivot, then the muzzle
                            // along that forward.
                            var facing = go.transform.Find("facing");
                            Check(facing != null, "rifleman prefab has a facing pivot");
                            if (facing != null)
                            {
                                var faceDir = facing.TransformDirection(Vector3.forward);
                                Check(faceDir.x < -0.7f,
                                      $"the player faces screen-right / Unity −X "
                                      + $"(forward x {faceDir.x:F2})");
                                // The imported gun root's +X is not always the
                                // mesh barrel (glTFast can wrap a root). Assert
                                // the rendered mass: the bounds centre should
                                // sit on the facing side of the grip.
                                var grip = gun.position;
                                float along = Vector3.Dot(gunR.bounds.center - grip, faceDir);
                                Check(along > 0.01f,
                                      $"the rifle mesh sits the way the soldier faces "
                                      + $"(mesh along {along:F3})");
                            }
                        }
                    }
                    finally { Object.DestroyImmediate(go); }
                }
            }

            // --- AMMO TYPES. The whole feature was PRESENT AND DEAD before 2026-08-07: the
            // enum, ProjectileEntity.Ammo, GameState.SelectedAmmo, GameState.BurningEnemyIds,
            // the unlock/selection persistence and CollisionSystem.IncendiaryHitUnitIds all
            // existed, and FireVolley never set Ammo, so every round was Standard forever.
            // These checks assert the WIRING, which is the part that was missing.
            {
                var catalog = AssetDatabase.LoadAssetAtPath<AmmoCatalogSO>(
                    "Assets/GameData/AmmoCatalog.asset");
                Check(catalog != null && catalog.slots.Count == 4,
                      $"the ammo catalogue exists with four types ({catalog?.slots.Count ?? 0})");

                if (catalog != null)
                {
                    // STANDARD MUST BE THE IDENTITY. This is what makes PRODUCT_DIRECTION's "no
                    // ammo is ever REQUIRED to clear a level" checkable rather than a promise:
                    // a level cleared on Standard is a level cleared with every modifier at 1.
                    var std = AmmoModifiers.From(catalog, AmmoType.Standard);
                    Check(std.UnitDamageScale == 1f && std.StructureDamageScale == 1f
                          && std.SpreadScale == 1f && std.BurnDamage == 0,
                          "Standard ammo is the IDENTITY — it cannot change a volley");
                    Check(AmmoModifiers.From(null, AmmoType.Cluster).UnitDamageScale == 1f,
                          "a MISSING catalogue falls back to Standard rather than throwing");

                    // Only Standard is free, or a fresh player has a choice they never earned.
                    Check(catalog.slots.Count(a => a.coinPrice == 0) == 1
                          && catalog.Find(AmmoType.Standard).coinPrice == 0,
                          "Standard is the only free ammo");

                    // The burn must not one-shot the FRAILEST unit in the CURRENT roster. The
                    // old value of 6 was calibrated against an 8hp Sniper that no longer exists
                    // (HANDOVER records it); anchoring to the live roster is what stops this
                    // expiring silently the next time the roster is cut.
                    int frailest = AssetDatabase.FindAssets("t:UnitDefinitionSO")
                        .Select(AssetDatabase.GUIDToAssetPath)
                        .Select(AssetDatabase.LoadAssetAtPath<UnitDefinitionSO>)
                        .Where(u => u != null && u.maxHp > 0).Min(u => u.maxHp);
                    int burn = catalog.Find(AmmoType.Incendiary).burnDamage;
                    Check(burn > 0 && burn < frailest,
                          $"the incendiary burn ({burn}) chips rather than one-shots the " +
                          $"frailest unit in the roster ({frailest} hp)");

                    // AP is the masonry answer and pays for it against soft targets.
                    var ap = AmmoModifiers.From(catalog, AmmoType.AP);
                    Check(ap.StructureDamageScale > 1f && ap.UnitDamageScale < 1f,
                          $"AP trades unit damage ({ap.UnitDamageScale:F2}x) for structure " +
                          $"damage ({ap.StructureDamageScale:F1}x)");
                    // THE NET EFFECT PER ROUND, which is the contract and is what the first
                    // version of this got wrong. The engine does `Damage * StructureMultiplier`,
                    // so scaling Damage down for soft targets silently scaled MASONRY down too:
                    // AP's real effect was 0.6 * 2 = 1.2x, and the check that only looked at the
                    // multiplier passed anyway. Caught on device — 128 off a 165hp citadel where
                    // ~192 was intended. Assert the PRODUCT, never the factor.
                    var stdM = AmmoModifiers.From(catalog, AmmoType.Standard);
                    foreach (var (baseDmg, baseMult) in new[] { (32, 3f), (8, 0.25f), (8, 6f) })
                    {
                        float stdOut = stdM.UnitDamage(baseDmg) * stdM.StructureMultiplier(baseMult);
                        float apOut = ap.UnitDamage(baseDmg) * ap.StructureMultiplier(baseMult);
                        // Tolerance DERIVED from integer rounding, not guessed: Damage is an
                        // int, so an 8-damage round scaled by 0.6 lands on 5 rather than 4.8 and
                        // the ratio comes out 2.08x. A fixed epsilon would either fail this
                        // honestly-correct case or be too loose to catch a real 1.2x regression.
                        float tol = ap.StructureDamageScale * (0.5f / baseDmg) + 0.02f;
                        Check(Mathf.Abs(apOut / stdOut - ap.StructureDamageScale) < tol,
                              $"AP does {ap.StructureDamageScale:F1}x STRUCTURE damage per round " +
                              $"(dmg {baseDmg} x{baseMult}: {stdOut:F0} -> {apOut:F0}, " +
                              $"{apOut / stdOut:F2}x)");
                        Check(ap.UnitDamage(baseDmg) < stdM.UnitDamage(baseDmg),
                              $"...while doing LESS to men ({stdM.UnitDamage(baseDmg)} -> " +
                              $"{ap.UnitDamage(baseDmg)})");
                    }
                    // Damage is floored at 1, so a scale can never make an owned ammo do nothing.
                    Check(AmmoModifiers.From(catalog, AmmoType.AP).UnitDamage(1) >= 1,
                          "an ammo scale never rounds a round's damage down to zero");

                    var cluster = AmmoModifiers.From(catalog, AmmoType.Cluster);
                    Check(cluster.SpreadScale > 1f && cluster.UnitDamageScale < 1f,
                          $"Cluster spreads wider ({cluster.SpreadScale:F1}x) for lighter hits " +
                          $"({cluster.UnitDamageScale:F2}x)");

                    // --- THE WIRING ITSELF: a fired round must CARRY the selection.
                    // Its own lookup rather than the shell block's `withCannon`, which is
                    // declared below: a level with a cannon lets the same checks cover the
                    // SHELL, which must take the ammo like everything else.
                    var withCannonLevel = levels.FirstOrDefault(l => l.playerGroups.Count > 0 &&
                        l.structures.Any(p => p.definition != null && p.definition.isPlayerSide
                                              && p.definition.hasCannon));
                    if (withCannonLevel != null)
                    {
                        var baseState = LevelBuilder.BuildInitialState(
                            withCannonLevel, 1, levels.Count, new System.Random(7))
                            with { Phase = GamePhase.Playing, TurnPhase = TurnPhase.Aiming };

                        foreach (var type in new[] { AmmoType.Incendiary, AmmoType.AP, AmmoType.Cluster })
                        {
                            var picked = baseState with { SelectedAmmo = type };
                            var v = BattleTick.FireVolley(picked, new Vector3(6f, 6f, 0f),
                                                          new System.Random(3), catalog);
                            Check(v.Projectiles.Where(p => p.OwnerIsPlayer).All(p => p.Ammo == type),
                                  $"every round in the volley carries {type}");
                            // Including the SHELL — DYNAMISM_DESIGN is explicit that there are
                            // no special cases, and an AP shell is the bunker-buster fantasy.
                            var sh = v.Projectiles.FirstOrDefault(p => p.Type == ProjectileType.Shell);
                            Check(sh != null && sh.Ammo == type,
                                  $"the TANK SHELL also carries {type}");
                        }

                        // Standard leaves the volley exactly as it was before ammo existed.
                        var asStd = BattleTick.FireVolley(baseState, new Vector3(6f, 6f, 0f),
                                                          new System.Random(3), catalog);
                        var noCat = BattleTick.FireVolley(baseState, new Vector3(6f, 6f, 0f),
                                                          new System.Random(3), null);
                        Check(asStd.Projectiles.Where(p => p.OwnerIsPlayer).Select(p => p.Damage)
                                  .SequenceEqual(noCat.Projectiles.Where(p => p.OwnerIsPlayer)
                                  .Select(p => p.Damage)),
                              "Standard fires the identical volley to having no catalogue at all");

                        // --- THE BURN ACTUALLY LANDS. This is the half that was most likely to
                        // stay dead: CollisionSystem has populated IncendiaryHitUnitIds since
                        // the port and NOTHING consumed it, and GameState.BurningEnemyIds was
                        // declared and never written. Driven through the real Step so the
                        // handover edge is the thing under test, not a helper.
                        {
                            // ReadyToHandOver: player side, nothing in the air, no pause left.
                            var marked = baseState with
                            {
                                SelectedAmmo = AmmoType.Incendiary,
                                TurnSide = TurnSide.Player,
                                TurnPhase = TurnPhase.Resolving,
                                TurnHandoverDelay = 0f,
                                Projectiles = new List<ProjectileEntity>(),
                                BurningEnemyIds = new HashSet<int> { baseState.EnemyUnits[0].Id },
                            };
                            int hpBefore = marked.EnemyUnits[0].Hp;
                            var burned = BattleTick.Step(marked, 1f / 60f, withCannonLevel,
                                                         new System.Random(5), catalog);
                            var after = burned.EnemyUnits.FirstOrDefault(
                                u => u.Id == marked.EnemyUnits[0].Id);
                            Check(after != null && after.Hp == hpBefore - burn,
                                  $"a burning unit takes its burn on the handover " +
                                  $"({hpBefore} -> {after?.Hp})");
                            Check(burned.TurnPhase == TurnPhase.EnemyWindup,
                                  "the burn lands on the edge into the enemy windup");

                            // ONCE, not forever. The set is spent as it is applied, so one
                            // incendiary hit is one tick — a unit that kept burning every turn
                            // off a single round would make the type a win button.
                            Check(burned.BurningEnemyIds.Count == 0,
                                  "the burning set is CLEARED as it is spent, so a unit burns once");

                            // And Standard never burns, however the set got populated.
                            var noBurn = BattleTick.Step(marked with { SelectedAmmo = AmmoType.Standard },
                                                         1f / 60f, withCannonLevel,
                                                         new System.Random(5), catalog);
                            Check(noBurn.EnemyUnits.First(u => u.Id == marked.EnemyUnits[0].Id).Hp
                                  == hpBefore,
                                  "Standard ammo applies no burn");
                        }

                        // AP really does reach the round's structure multiplier.
                        var apVolley = BattleTick.FireVolley(
                            baseState with { SelectedAmmo = AmmoType.AP },
                            new Vector3(6f, 6f, 0f), new System.Random(3), catalog);
                        var apShell = apVolley.Projectiles.First(p => p.Type == ProjectileType.Shell);
                        var stdShell = asStd.Projectiles.First(p => p.Type == ProjectileType.Shell);
                        Check(apShell.StructureDamageMultiplier > stdShell.StructureDamageMultiplier,
                              $"an AP shell hits masonry harder than a standard one " +
                              $"({apShell.StructureDamageMultiplier:F1}x vs " +
                              $"{stdShell.StructureDamageMultiplier:F1}x)");
                    }
                }
            }

            // --- THE TANK SHELL. It is off-roster — built from a STRUCTURE, not a unit — so
            // nothing in the unit-facing checks above would notice it had stopped firing, which
            // THE MACHINE GUNNER'S BURST REACHES THE PLAYER'S VOLLEY — Tier 2.3, 2026-08-12.
            //
            // `projectilesPerVolley` was read by `AutoFire` and by nothing else, so the class the
            // store sells for 250 coins as "fires a burst instead of a round" fired ONE round in
            // the player's hands: a rifleman at half the damage for twice the points. Identical in
            // every measurable respect to the shield bearer, which is the exact failure Tier 2.3
            // names. Same family as the three properties the block above guards, and invisible for
            // the same reason — the debug driver did it correctly, so reading AutoFire reassured.
            //
            // Asserted through `FireVolley`, the function the DRAG calls, never off the asset.
            // Both facts in one check on purpose: a burst that fires three rounds on one jitter is
            // one round doing triple damage, which is not the thing that was bought.
            var burstUnit = AssetDatabase
                .LoadAssetAtPath<RosterDefinitionSO>("Assets/GameData/Roster.asset")?.slots
                .Select(s => s.unit)
                .FirstOrDefault(u => u != null && u.projectilesPerVolley > 1);
            Check(burstUnit != null, "some pickable class fires a burst at all (else this is untested)");
            if (burstUnit != null && levels.Count > 0)
            {
                var burstLevel = levels.First();
                var groups = Loadout.ToPlayerGroups(burstLevel, new List<Pick> { new(burstUnit, 3) })
                    .Where(g => g.definition == burstUnit).ToList();
                var bs = LevelBuilder.BuildInitialState(burstLevel, 1, levels.Count,
                             new System.Random(9), playerGroupsOverride: groups)
                         with { Phase = GamePhase.Playing, TurnPhase = TurnPhase.Aiming };
                var aim = new Vector3(6f, 6f, 0f);
                var bf = BattleTick.FireVolley(bs, aim, new System.Random(3));
                var bullets = bf.Projectiles
                    .Where(p => p.OwnerIsPlayer && p.Type != ProjectileType.Shell).ToList();
                int want = bs.PlayerUnits.Count * burstUnit.projectilesPerVolley;
                int distinctAim = bullets.Select(p => $"{p.Vx:F4}/{p.Vy:F4}").Distinct().Count();

                // AND THE SPREAD HAS TWO DEGREES OF FREEDOM. "Distinct aims" was already true of
                // the collinear version and is why it read as correct for a day: the old code drew
                // ONE jitter and added it to Vx AND Vy, so every round differed from every other
                // and every round sat on the same 45° line through the aim. Three rounds, one
                // visible streak — confirmed on a device before it was confirmed here.
                //
                // The measurable form of "fanned" is that the per-axis offsets are not equal:
                // under the old code (Vx-aimX) - (Vy-aimY) was EXACTLY 0 for every round.
                float offAxis = bullets.Count == 0 ? 0f
                    : bullets.Max(p => Mathf.Abs((p.Vx - aim.x) - (p.Vy - aim.y)));
                Check(bs.PlayerUnits.Count > 0 && bullets.Count == want
                      && distinctAim == bullets.Count && offAxis > 0.01f,
                      $"{burstUnit.name}'s burst reaches the PLAYER's volley and FANS: " +
                      $"{bs.PlayerUnits.Count} shooters x {burstUnit.projectilesPerVolley} = " +
                      $"{bullets.Count} rounds (want {want}), {distinctAim} distinct aims, " +
                      $"widest off-axis offset {offAxis:F3} (0 means every round sits on the same " +
                      "45° line, which is one streak however many rounds it is)");
            }

            // is exactly how it went missing from the port in the first place.
            var withCannon = levels.FirstOrDefault(l => l.playerGroups.Count > 0 &&
                l.structures.Any(p => p.definition != null && p.definition.isPlayerSide
                                      && p.definition.hasCannon));
            Check(withCannon != null, "at least one level fields a player cannon");
            if (withCannon != null)
            {
                var st0 = LevelBuilder.BuildInitialState(withCannon, 1, levels.Count, new System.Random(7));
                st0 = st0 with { Phase = GamePhase.Playing, TurnPhase = TurnPhase.Aiming };
                Check(st0.TankShellsRemaining > 0,
                      $"a level with a cannon starts with ammo ({st0.TankShellsRemaining})");

                var fired = BattleTick.FireVolley(st0, new Vector3(6f, 6f, 0f), new System.Random(3));
                int shellsOut = fired.Projectiles.Count(p => p.Type == ProjectileType.Shell);
                Check(shellsOut > 0, $"the player volley includes a tank shell ({shellsOut})");
                Check(fired.TankShellsRemaining == st0.TankShellsRemaining - shellsOut,
                      "firing SPENDS the shells it fired");
                // The shell is the only round with a structure multiplier worth having; a shell
                // that arrives at 1x is a rifle bullet with a big model.
                var shell = fired.Projectiles.First(p => p.Type == ProjectileType.Shell);
                Check(shell.StructureDamageMultiplier > 1f,
                      $"the shell carries its structure multiplier ({shell.StructureDamageMultiplier:F1}x)");
                Check(shell.Id >= 30000, "shell ids sit in their own band, clear of bullets and enemy fire");

                // THE SHELL MUST LAND WHERE THE VOLLEY LANDS. This is the check that was missing
                // while the shell was `aimVelocity * velocityBoost`: range goes as v^2, so a 1.12
                // boost is 1.2544x the range from a muzzle only ~2 units behind the line, and the
                // shell overshot by ~1.3 units. Measured on device before it was found — aiming
                // the infantry at one structure put the shell onto the one behind it, and since
                // the shell is the only round that can meaningfully hurt a building, the player
                // could not place the only weapon that mattered.
                //
                // Compared as LANDING POINTS from their real origins, not as velocities: the two
                // launch from different places, so equal velocity is precisely the bug.
                foreach (var aim in new[] { new Vector3(5f, 5f, 0f), new Vector3(6f, 6f, 0f),
                                            new Vector3(7f, 5f, 0f) })
                {
                    var v = BattleTick.FireVolley(
                        st0 with { TurnPhase = TurnPhase.Aiming }, aim, new System.Random(3));
                    var sh = v.Projectiles.FirstOrDefault(p => p.Type == ProjectileType.Shell);
                    if (sh == null) continue;

                    float shellX = TrajectoryPhysics.LandingPoint(
                        new Vector3(sh.X, sh.Y, sh.Z), new Vector3(sh.Vx, sh.Vy, sh.Vz)).x;

                    // The volley's own centre, from the same mean muzzle FireVolley solves to.
                    float meanX = st0.PlayerUnits.Average(u => u.X);
                    float meanY = st0.PlayerUnits.Average(u => u.Y) + BattleTick.InfantryMuzzleY;
                    float volleyX = TrajectoryPhysics.LandingPoint(
                        new Vector3(meanX, meanY, 0f), aim).x;

                    Check(Mathf.Abs(shellX - volleyX) < 0.5f,
                          $"the shell lands with the volley at aim ({aim.x},{aim.y}) — " +
                          $"shell {shellX:F2} vs volley {volleyX:F2}");
                }

                // velocityBoost is now HEADROOM, not a blind multiplier: it caps the solved speed
                // so the gun may reach further back than the drag that ordered the shot, and a
                // shell can still never be faster than that cap.
                {
                    var aim = new Vector3(6f, 6f, 0f);
                    var v = BattleTick.FireVolley(
                        st0 with { TurnPhase = TurnPhase.Aiming }, aim, new System.Random(3));
                    var sh = v.Projectiles.First(p => p.Type == ProjectileType.Shell);
                    float shellSpeed = Mathf.Sqrt(sh.Vx * sh.Vx + sh.Vy * sh.Vy);
                    float aimSpeed = Mathf.Sqrt(aim.x * aim.x + aim.y * aim.y);
                    var cannon = withCannon.structures
                        .First(p => p.definition != null && p.definition.isPlayerSide
                                    && p.definition.hasCannon).definition.cannon;
                    Check(shellSpeed <= aimSpeed * cannon.velocityBoost + 0.01f,
                          $"the shell never exceeds its velocityBoost headroom " +
                          $"({shellSpeed:F2} <= {aimSpeed * cannon.velocityBoost:F2})");
                }

                // Run the ammo down and past zero. An unclamped decrement would go negative and
                // the gun would fire forever on a level that had spent its shells.
                var s2 = fired;
                for (int i = 0; i < 12; i++)
                {
                    s2 = s2 with { TurnPhase = TurnPhase.Aiming };
                    s2 = BattleTick.FireVolley(s2, new Vector3(6f, 6f, 0f), new System.Random(3));
                }
                Check(s2.TankShellsRemaining == 0, $"ammo stops at zero ({s2.TankShellsRemaining})");

                // And the gate is honoured, so a level can field a tank with a cold gun.
                var cold = st0 with { CannonArmed = false };
                var coldFired = BattleTick.FireVolley(cold, new Vector3(6f, 6f, 0f), new System.Random(3));
                Check(coldFired.Projectiles.All(p => p.Type != ProjectileType.Shell),
                      "CannonArmed=false fires no shell");

                // The PLAYER's volley must carry each unit's own projectile type — it did not,
                // and only AutoFire did, so a rocket trooper's round was a plain bullet with no
                // splash and a 1x structure multiplier whenever a human fired it.
                var types = fired.Projectiles.Where(p => p.OwnerIsPlayer).Select(p => p.Type).Distinct().ToList();
                Check(types.Count >= 1, $"player rounds carry a projectile type ({types.Count} distinct)");

                // AUTO IGNORES THE AMMO SELECTION, and this asserts that rather than fixing it.
                //
                // Found on a device 2026-08-10, and it cost most of a session: six incendiary
                // volleys were fired with Auto and not one man ever caught fire. The state said
                // Incendiary the whole time — a probe printed `ammo=Incendiary unitsHit=1
                // incendiaryHits=0` — because AutoFire builds its own ProjectileEntity and never
                // sets `Ammo`, so every round it throws is Standard whatever is selected.
                //
                // It is DELIBERATE (CannonShells documents the identity default), and it is the
                // exact sibling of "Auto cannot test STRUCTURES": Auto is a test harness, not the
                // player. The check exists so the next person reads it here in a second instead of
                // rediscovering it against a phone.
                var autoState = st0 with { SelectedAmmo = AmmoType.Incendiary,
                                           TurnPhase = TurnPhase.Aiming };
                var autoFired = BattleTick.AutoFire(autoState);
                var autoRounds = autoFired.Projectiles.Where(p => p.OwnerIsPlayer).ToList();
                Check(autoRounds.Count > 0 && autoRounds.All(p => p.Ammo == AmmoType.Standard),
                      $"AUTO fires STANDARD rounds whatever is selected — it cannot test ammo " +
                      $"({autoRounds.Count} rounds, state says {autoFired.SelectedAmmo})");

                // The contrast, in the same breath: a real volley DOES carry it. Without this the
                // check above would still pass if ammo were broken everywhere.
                var ammoCat = AssetDatabase.LoadAssetAtPath<AmmoCatalogSO>(
                    "Assets/GameData/AmmoCatalog.asset");
                var dragFired = BattleTick.FireVolley(
                    st0 with { SelectedAmmo = AmmoType.Incendiary, TurnPhase = TurnPhase.Aiming },
                    new Vector3(6f, 6f, 0f), new System.Random(3), ammoCat);
                Check(dragFired.Projectiles.Any(p => p.OwnerIsPlayer && p.Ammo == AmmoType.Incendiary),
                      "...while a real DRAG volley carries the selected ammo");
            }
        }

        CheckConsumables();
        CheckFactions();
        CheckCosmetics();
        CheckL1RangeTrial();
        CheckL5NoTank();
        CheckL3OneSniper();
        CheckHeroStaging();
        CheckNobodyOverlaps();
        CheckNobodyStandsInAWall();
        CheckCrowdSplitKeptTheBalance();
        CheckAdvancingSquads();

        Debug.Log($"[PortSelfTest] {(failed == 0 ? "ALL PASS" : $"{failed} FAILURES")}\n{Log}");
        if (failed > 0 && Application.isBatchMode) EditorApplication.Exit(1);
    }

    /// <summary>
    /// 2026-08-20 L1 range trial. The envelope and L1 geometry have to move together — raising
    /// v without sliding the outpost makes every other level easier and this check would still
    /// pass; sliding the outpost without raising v is a 99% back-rank shot on the garrison.
    /// L1 signed 2026-08-20; L2–L12 slid the same +2 after that. Player tanks stay put.
    /// </summary>
    static void CheckL1RangeTrial()
    {
        Near(AimSystem.MaxAimMagnitude, 9.5f, 1e-4f, "L1 trial: MaxAimMagnitude is 9.5");
        Near(AimSystem.MaxRange45, 9.5f * 9.5f / TrajectoryPhysics.Gravity, 1e-4f,
             "L1 trial: flat max range is v^2/g (22.56)");
        // Same 525 px / 23.4 drag-unit full pull the scale was derived from.
        Near(23.4f * AimSystem.DragSpeedScale, AimSystem.MaxAimMagnitude, 0.02f,
             "L1 trial: a comfortable full-length drag still lands on 100%");

        var levels = AssetDatabase.FindAssets("t:LevelDefinitionSO")
            .Select(AssetDatabase.GUIDToAssetPath)
            .Select(AssetDatabase.LoadAssetAtPath<LevelDefinitionSO>)
            .Where(l => l != null && !l.isTestLevel)
            .ToList();
        var l1 = levels.FirstOrDefault(l => l.levelNumber == 1);
        var l2 = levels.FirstOrDefault(l => l.levelNumber == 2);
        var l4 = levels.FirstOrDefault(l => l.levelNumber == 4);
        Check(l1 != null && l2 != null && l4 != null, "L1/L2/L4 campaign assets load");
        if (l1 == null || l2 == null || l4 == null) return;

        var outpost = l1.structures.FirstOrDefault(s => s.id == "outpost");
        var tank = l1.structures.FirstOrDefault(s => s.id == "player_tank");
        var ground = l1.enemyGroups.FirstOrDefault(g =>
            string.IsNullOrEmpty(g.standingOnStructureId));
        Check(outpost != null && tank != null && ground != null, "L1 has tank, outpost, ground squad");
        if (outpost == null || tank == null || ground == null) return;

        Near(tank.x, -9.5f, 1e-3f, "L1 trial: player tank stays at -9.5");
        Near(outpost.x, 9f, 1e-3f, "L1 trial: outpost is at 9 (was 7)");
        Near(ground.anchorX, 6.5f, 1e-3f, "L1 trial: ground squad is at 6.5 (was 4.5)");
        Near(Mathf.Abs(outpost.x - tank.x), 18.5f, 1e-3f,
             "L1 trial: tank -> outpost is 18.5");

        // L2–L12 took the same +2 after L1 signed. Player tanks do not move.
        var l2Post = l2.structures.FirstOrDefault(s => s.id == "post");
        var l2Tank = l2.structures.FirstOrDefault(s => s.id == "player_tank");
        var l4Block = l4.structures.FirstOrDefault(s => s.id == "block");
        var l4Charge = l4.enemyGroups.FirstOrDefault(g => g.advancePerTurn > 0f);
        Check(l2Tank != null && Mathf.Abs(l2Tank.x - (-9.5f)) < 1e-3f,
              "range slide did not move the L2 player tank");
        Check(l2Post != null && Mathf.Abs(l2Post.x - 8.5f) < 1e-3f,
              "L2 post slid +2 (8.5, was 6.5)");
        Check(l4Block != null && Mathf.Abs(l4Block.x - 6.8f) < 1e-3f,
              "L4 block slid +2 (6.8, was 4.8)");
        Check(l4Charge != null && Mathf.Abs(l4Charge.advancePerTurn - 1.5f) < 1e-3f,
              "L4 shield charge steps 1.5/turn (was 1.1) so the extra street does not add turns");

        var st = LevelBuilder.BuildInitialState(l1, 1, 1, new System.Random(12345));
        var reach = BalanceAudit.ReachRule(st);
        Check(reach.Level != LevelComposition.Severity.Error,
              "L1 trial: the garrison is still reachable — " + reach.Text);
        Check(reach.Level == LevelComposition.Severity.Ok,
              "L1 trial: front rank has aim headroom — " + reach.Text);

        float frontPlayer = st.PlayerUnits.Max(u => u.X);
        float frontEnemy = st.EnemyUnits.Min(u => u.X);
        float gap = frontEnemy - frontPlayer;
        // Anchors moved +2; formation width eats ~1, so the built street is ~12.4 not 13.5.
        // Old built gap was ~10.4. Seeded, so this is a lock not a floor.
        Near(gap, 12.4f, 0.25f,
             "L1 trial: built infantry gap is ~12.4 (anchors +2, was ~10.4)");
    }

    /// <summary>
    /// L5 is the loft beat. The tank's three shells were the structure-killer and
    /// turned "fight upward" into a 3-shell errand. Rob 2026-08-21: take it off
    /// this level. Shop comes later; this level must play without it.
    /// </summary>
    static void CheckL5NoTank()
    {
        var l5 = AssetDatabase.FindAssets("t:LevelDefinitionSO")
            .Select(AssetDatabase.GUIDToAssetPath)
            .Select(AssetDatabase.LoadAssetAtPath<LevelDefinitionSO>)
            .FirstOrDefault(l => l != null && !l.isTestLevel && l.levelNumber == 5);
        Check(l5 != null, "L5 Tower Assault loads");
        if (l5 == null) return;

        Check(l5.structures.All(s => s.id != "player_tank"
                                  && (s.definition == null || !s.definition.hasCannon
                                      || !s.definition.isPlayerSide)),
              "L5 fields no player tank");
        Check(l5.playerGroups.All(g => string.IsNullOrEmpty(g.standingOnStructureId)),
              "L5's squad stands on the ground (crew folded into the line)");
        Check(l5.playerGroups.Sum(g => g.count) == 10,
              $"L5 still fields 10 bodies ({l5.playerGroups.Sum(g => g.count)})");

        var st = LevelBuilder.BuildInitialState(l5, 1, 12, new System.Random(9));
        Check(st.TankShellsRemaining == 0, "L5 starts with no shells");
        st = st with { Phase = GamePhase.Playing, TurnPhase = TurnPhase.Aiming };
        var fired = BattleTick.FireVolley(st, new Vector3(6f, 6f, 0f), new System.Random(3));
        Check(fired.Projectiles.Count(p => p.Type == ProjectileType.Shell) == 0,
              "a drag on L5 does not fire a tank shell");

        var opened = TurnFlow.StartBattle(
            LevelBuilder.BuildInitialState(l5, 1, 12, new System.Random(9)));
        Check(opened.TurnPhase == TurnPhase.TankArrive
              && opened.PlayerUnits.Any(u => u.MarchTargetX != null),
              "L5 still jogs the ground line in (TankArrive without a hull)");

        var reach = BalanceAudit.ReachRule(st);
        Check(reach.Level != LevelComposition.Severity.Error,
              "L5 remains reachable without the tank — " + reach.Text);

        // Roles: MG in the street, one sniper on the platform. The tower used to field
        // six machine-gunners; Rob read them as snipers.
        var tower = l5.enemyGroups.Where(g => g.standingOnStructureId == "tow_top").ToList();
        var front = l5.enemyGroups.FirstOrDefault(g =>
            string.IsNullOrEmpty(g.standingOnStructureId));
        Check(tower.Count == 1 && tower[0].count == 1
              && tower[0].definition != null && tower[0].definition.id == "enemy_sniper",
              "L5's tower fields one sniper");
        Check(front != null && front.definition != null
              && front.definition.id == "enemy_machine_gunner" && front.count == 3,
              "L5's machine gunners stand in the street, not on the roof");
        Check(l5.enemyGroups.All(g =>
                  g.standingOnStructureId != "tow_top"
                  || (g.definition != null && !g.definition.id.Contains("machine_gunner"))),
              "L5 puts no machine gunner on the tower");
    }

    static void CheckL3OneSniper()
    {
        var l3 = AssetDatabase.FindAssets("t:LevelDefinitionSO")
            .Select(AssetDatabase.GUIDToAssetPath)
            .Select(AssetDatabase.LoadAssetAtPath<LevelDefinitionSO>)
            .FirstOrDefault(l => l != null && !l.isTestLevel && l.levelNumber == 3);
        Check(l3 != null, "L3 Watchpost Ridge loads");
        if (l3 == null) return;
        var onTower = l3.enemyGroups.Where(g => g.standingOnStructureId == "tower").ToList();
        int n = onTower.Sum(g => g.count);
        Check(n == 1, $"L3's tower fields one sniper (was 3, got {n})");
    }

    /// <summary>
    /// CROWD + HERO staging — Tier 2.2. The reference composition is a mass of interchangeable
    /// crowd plus a SMALL number of large heroes standing apart at the front, and the port had
    /// only the first half: every hero group in the campaign was authored ONTO a structure, in
    /// counts of four and five.
    ///
    /// That is invisible to every other check. `LevelComposition` measures spans and reach and
    /// passed all twelve levels while four of them packed five 1.9x bodies into a deck row, and
    /// `FormationFor` dispatches on the garrison branch FIRST — so `Formation.Heroes`, the whole
    /// "stands apart, individually" path, was reached by exactly one reinforcement wave in the
    /// entire game and nothing said so.
    ///
    /// Asserted on the BUILT state, per the standing rule — the positions a player would see,
    /// not the fields an author typed. A hero that is gridded in reads as elite crowd, and the
    /// measurable form of "gridded in" is: it stands at deck height, or its nearest crowd body is
    /// a crowd spacing away.
    ///
    /// NOT asserted: that a hero stands forward in Z. That was the intended third property and
    /// measuring killed it — L12's deck garrison sits at z 0.80 against the hero's 0.34, because
    /// a deck z offset and a staging z offset are different things. It would have asserted a
    /// belief.
    ///
    /// The hero COUNT is part of the condition, not a separate check: with no heroes authored
    /// anywhere this whole function is vacuously true, which is the empty-purse trap.
    /// </summary>
    static void CheckHeroStaging()
    {
        // 2.5x the crowd's own column spacing. A hero packed back into a garrison measures at
        // MountedColumnSpacing (0.187) and a hero gridded into a ground cluster at the packed
        // cluster spacing (0.189) — both an order below this, so the floor is coarse on purpose.
        const float ClearanceFactor = 2.5f;
        float floor = Formation.DefaultColumnSpacing * ClearanceFactor;

        var levels = AssetDatabase.FindAssets("t:LevelDefinitionSO")
            .Select(AssetDatabase.GUIDToAssetPath)
            .Select(AssetDatabase.LoadAssetAtPath<LevelDefinitionSO>)
            .Where(l => l != null && !l.isTestLevel)
            .OrderBy(l => l.levelNumber)
            .ToList();

        int heroes = 0, onDeck = 0, crowded = 0, biggestGroup = 0;
        float tightest = float.MaxValue;
        string worst = "none";

        foreach (var level in levels)
        {
            GameState state;
            try { state = LevelBuilder.BuildInitialState(level, 1, 1, new System.Random(12345)); }
            catch { continue; }   // a level that does not build is LevelComposition's finding

            var heroUnits = state.EnemyUnits
                .Where(u => u.Definition != null && u.Definition.renderScale > 1.01f).ToList();
            if (heroUnits.Count == 0) continue;
            heroes += heroUnits.Count;
            biggestGroup = Mathf.Max(biggestGroup, heroUnits.Count);

            var crowd = state.EnemyUnits
                .Where(u => u.Definition != null && u.Definition.renderScale <= 1.01f).ToList();

            foreach (var h in heroUnits)
            {
                if (h.Y > 0.01f || h.StandingOnStructureId != null) onDeck++;
                float nearest = crowd.Select(c => Mathf.Abs(c.X - h.X)).DefaultIfEmpty(99f).Min();
                if (nearest < tightest) { tightest = nearest; worst = $"L{level.levelNumber}"; }
                if (nearest < floor) crowded++;
            }
        }

        Check(heroes > 0 && onDeck == 0 && crowded == 0 && biggestGroup <= 2,
              $"heroes stand APART on the ground, never gridded into the crowd — {heroes} across " +
              $"the campaign, biggest group {biggestGroup} (max 2), {onDeck} on a deck, {crowded} " +
              $"inside the {floor:F2} clearance floor (tightest {tightest:F2} on {worst}, crowd " +
              $"spacing {Formation.DefaultColumnSpacing:F2})");
    }


    /// <summary>
    /// NO TWO MEN ON A SIDE STAND IN THE SAME PLACE. A deck is one piece of ground, and
    /// `FormationFor` used to lay out each authored GROUP on it separately — so two groups
    /// garrisoned on the same structure were each centred on that structure and stood INSIDE one
    /// another. L11 shipped with three riflemen and three machine gunners occupying an identical
    /// three spots, dx 0.000 dz 0.000; L6 and L12 were partial versions of the same thing.
    ///
    /// Nothing could see it. `LevelComposition` reads span and reach, both of which a doubled-up
    /// garrison satisfies perfectly — a row of six that is really three men twice over measures
    /// exactly like a row of three, and the rules have no opinion about how many bodies are in a
    /// spot. The units are also individually correct: right deck, right height, right rank.
    ///
    /// CHEBYSHEV, not Euclidean, and that is the whole subtlety. Two men one RANK apart are not
    /// overlapping however close they are in x — that is what a second rank IS. An earlier
    /// version of this compared x-RANGES per group and reported L11 as still broken after the fix
    /// had landed, because a back rank legitimately spans the same x as the front one. The
    /// detector was wrong, not the code.
    /// </summary>
    /// <summary>
    /// The crowd split (Tier 2.2 part four) doubled the BODIES on every garrison and must have
    /// moved nothing else. These are ABSOLUTES, measured off the pre-split data and pasted here,
    /// not re-derived from the crowd definitions — deriving them would assert the split against
    /// itself and pass however wrong the factors were. Every one of these twelve rows was read
    /// off a build of the ORIGINAL level data, and the split build reproduced all three columns
    /// exactly.
    ///
    /// So this goes red if anyone edits a crowd variant's stats, changes a garrison count, or
    /// adds a class to CrowdSplit.Factors whose HP or damage does not divide exactly.
    ///
    /// It also pins the frailest crowd body ABOVE the incendiary burn, which is the constraint
    /// that picked the factors: the first version of the table split the sniper x2 and the
    /// grenadier x3, landing both on exactly 8 hp, and the burn stopped chipping and started
    /// one-shotting. That was caught by the roster-frailty check, and this makes it explicit
    /// rather than incidental.
    /// </summary>
    static void CheckCrowdSplitKeptTheBalance()
    {
        // level -> units before, hp, volley damage, structure damage
        var expected = new (int level, int unitsBefore, int hp, int volleyDmg, float structDmg)[]
        {
            (1,   9, 288,  72, 18f), (2,  11, 352,  88, 22f), (3,   9, 272,  84, 26f),
            (4,  17, 616, 132, 33f), (5,   8, 264,  88, 27f), (6,  16, 616, 148, 37f),
            (7,  11, 392,  82, 52f), (8,  13, 400, 124, 46f), (9,  15, 536, 116, 29f),
            (10, 13, 448, 120, 30f), (11, 10, 352,  80, 89f), (12, 18, 680, 164, 41f),
        };

        var levels = AssetDatabase.FindAssets("t:LevelDefinitionSO")
            .Select(AssetDatabase.GUIDToAssetPath)
            .Select(AssetDatabase.LoadAssetAtPath<LevelDefinitionSO>)
            .Where(l => l != null && !l.isTestLevel)
            .ToDictionary(l => l.levelNumber);

        int checkedLevels = 0, bodiesBefore = 0, bodiesAfter = 0;
        string worst = null;
        foreach (var e in expected)
        {
            if (!levels.TryGetValue(e.level, out var lv)) continue;
            var st = LevelBuilder.BuildInitialState(lv, 1, 1, new System.Random(12345));
            int hp = st.EnemyUnits.Sum(u => u.Hp);
            int dmg = st.EnemyUnits.Sum(u => u.Definition.damage * u.Definition.projectilesPerVolley);
            float sdmg = st.EnemyUnits.Sum(u => u.Definition.damage
                                              * u.Definition.projectilesPerVolley
                                              * u.Definition.structureDamageMultiplier);
            checkedLevels++;
            bodiesBefore += e.unitsBefore;
            bodiesAfter += st.EnemyUnits.Count;
            if (worst == null && (hp != e.hp || dmg != e.volleyDmg
                                  || Mathf.Abs(sdmg - e.structDmg) > 0.01f))
                worst = $"L{e.level} hp {hp} (was {e.hp}), volley {dmg} (was {e.volleyDmg}), " +
                        $"structure {sdmg:F1} (was {e.structDmg:F1})";
        }

        Check(checkedLevels == expected.Length && worst == null,
              $"the crowd split moved BODIES and nothing else — {checkedLevels}/{expected.Length} " +
              $"levels, {bodiesBefore} bodies -> {bodiesAfter}, every level's HP, volley damage " +
              $"and structure damage unchanged" + (worst == null ? "" : $" [{worst}]"));

        // THE PROJECTILE POOL IS SHARED PER TYPE AND OVERFLOWS SILENTLY — BattleRunner's draw
        // loop skips any round past the end of the pool, so it flies and damages while drawing
        // nothing. The crowd split more than doubled the enemy's rounds in the air (L12: ~23 ->
        // 51), so this measures the real peak across the campaign against the real constant
        // rather than against a copy of it.
        {
            int worstCount = 0;
            string worstWhere = "none";
            foreach (var lv in AssetDatabase.FindAssets("t:LevelDefinitionSO")
                         .Select(AssetDatabase.GUIDToAssetPath)
                         .Select(AssetDatabase.LoadAssetAtPath<LevelDefinitionSO>)
                         .Where(l => l != null && !l.isTestLevel))
            {
                var st = LevelBuilder.BuildInitialState(lv, 1, 1, new System.Random(12345));
                foreach (var g in st.EnemyUnits.GroupBy(u => u.Definition.projectileType))
                {
                    int n = g.Sum(u => u.Definition.projectilesPerVolley);
                    if (n > worstCount) { worstCount = n; worstWhere = $"L{lv.levelNumber} {g.Key}"; }
                }
            }
            Check(worstCount < BattleRunner.ProjectilePoolSize,
                  $"no volley can outrun the projectile pool — worst is {worstWhere} at " +
                  $"{worstCount} rounds against a pool of {BattleRunner.ProjectilePoolSize}");
        }

        // Every crowd body must survive an incendiary tick, or the burn is a wipe.
        const int burn = 8;
        var frailest = AssetDatabase.FindAssets("t:UnitDefinitionSO")
            .Select(AssetDatabase.GUIDToAssetPath)
            .Select(AssetDatabase.LoadAssetAtPath<UnitDefinitionSO>)
            .Where(u => u != null && u.name.EndsWith("Crowd"))
            .OrderBy(u => u.maxHp).FirstOrDefault();
        Check(frailest != null && frailest.maxHp > burn,
              $"the frailest crowd body outlives one incendiary tick — " +
              $"{(frailest == null ? "no crowd variants found" : $"{frailest.name} {frailest.maxHp} hp vs burn {burn}")}");
    }

    static void CheckNobodyOverlaps()
    {
        // A body is ~0.21 wide in legacy units. Half of that is the floor: closer than half a
        // body on BOTH axes at once is one man standing in another, not a tight formation.
        float body = 0.21f * UnitGeometry.LegacyScaleRatio;
        float floor = body * 0.5f;

        var levels = AssetDatabase.FindAssets("t:LevelDefinitionSO")
            .Select(AssetDatabase.GUIDToAssetPath)
            .Select(AssetDatabase.LoadAssetAtPath<LevelDefinitionSO>)
            .Where(l => l != null && !l.isTestLevel)
            .OrderBy(l => l.levelNumber)
            .ToList();

        int pairs = 0, measured = 0;
        float tightest = float.MaxValue;
        string worst = "none";

        foreach (var level in levels)
        {
            GameState state;
            try { state = LevelBuilder.BuildInitialState(level, 1, 1, new System.Random(12345)); }
            catch { continue; }

            var all = state.PlayerUnits.Concat(state.EnemyUnits).ToList();
            for (int i = 0; i < all.Count; i++)
                for (int j = i + 1; j < all.Count; j++)
                {
                    if (all[i].IsPlayerSide != all[j].IsPlayerSide) continue;
                    measured++;
                    float d = Mathf.Max(Mathf.Abs(all[i].X - all[j].X),
                                        Mathf.Abs(all[i].Z - all[j].Z));
                    if (d < tightest) { tightest = d; worst = $"L{level.levelNumber}"; }
                    if (d < floor) pairs++;
                }
        }

        // measured > 0 is part of the condition: over an empty campaign this is vacuously true,
        // which is the same empty-purse trap the hero check guards against.
        Check(measured > 0 && pairs == 0,
              $"no two units on a side stand in the same place — {pairs} co-located pairs over " +
              $"{measured} same-side pairs, tightest {tightest:F3} on {worst} " +
              $"(floor {floor:F3}, body {body:F3})");
    }


    /// <summary>
    /// RULE 8 — no ground unit stands inside a structure's collision box.
    ///
    /// THE RULE ITSELF LIVES IN `LevelComposition.CollisionBoxRule`, with the full write-up of
    /// why it exists. It was implemented here first, which was the wrong place: rules 1-7 render
    /// live in `LevelDefinitionInspector` beside the level being edited, and rule 8 alone could
    /// only be found by failing this suite — an author saw seven rules where there are eight.
    /// Moved 2026-08-12 so the author meets it where they author.
    ///
    /// The suite still asserts it, because a level that ships a unit nobody can hit is a bug and
    /// not a style note. It DELEGATES rather than re-measuring: two implementations of one rule
    /// is the "second source of truth" failure this project has already paid for, and it is why
    /// rule 7 is called out of `BalanceAudit` rather than copied into both.
    /// </summary>
    static void CheckNobodyStandsInAWall()
    {
        var levels = AssetDatabase.FindAssets("t:LevelDefinitionSO")
            .Select(AssetDatabase.GUIDToAssetPath)
            .Select(AssetDatabase.LoadAssetAtPath<LevelDefinitionSO>)
            .Where(l => l != null && !l.isTestLevel)
            .OrderBy(l => l.levelNumber)
            .ToList();

        int measured = 0;
        var offenders = new List<string>();
        var advisories = new List<string>();

        foreach (var level in levels)
        {
            GameState state;
            try { state = LevelBuilder.BuildInitialState(level, 1, 1, new System.Random(12345)); }
            catch { continue; }   // a level that does not build is LevelComposition's finding
            measured++;

            var f = LevelComposition.CollisionBoxRule(level, state);
            // ONLY AN ERROR FAILS THE SUITE. The rule gained a Warn severity on 2026-08-12 with
            // advancing squads: a charger that starts inside masonry and walks clear on its
            // SECOND march is hittable, just not yet, and that is a pacing judgement rather than
            // an unkillable body. Failing the suite on it would make the one severity that means
            // "this level cannot be played" indistinguishable from a note about tempo.
            if (f.Level == LevelComposition.Severity.Error)
                offenders.Add($"L{level.levelNumber} {f.Text}");
            else if (f.Level == LevelComposition.Severity.Warn)
                advisories.Add($"L{level.levelNumber} {f.Text}");
        }

        // measured > 0 is part of the condition: over an empty campaign this is vacuously true,
        // which is the empty-purse trap.
        Check(measured > 0 && offenders.Count == 0,
              $"no ground unit stands inside a structure's collision box — {measured} campaign " +
              $"level(s) measured, {offenders.Count} offending, {advisories.Count} advisory" +
              (offenders.Count == 0 ? "" : $": {string.Join("; ", offenders)}"));

        // Surfaced, never asserted: a warning a level may bend still has to be READABLE, or the
        // next session rediscovers it by hand.
        foreach (var a in advisories) Debug.Log($"[PortSelfTest] rule 8 advisory — {a}");
    }

    /// <summary>
    /// ADVANCING SQUADS AND MELEE — the eighth dead system, ported 2026-08-12.
    ///
    /// EVERY ASSERTION HERE IS AN OUTPUT. `AdvanceRemaining` and `SkirmishEntity` were both
    /// DECLARED and never written for the whole life of this port, and a check on either field's
    /// presence would have passed against that. So these ask what the player would see: did the
    /// body MOVE, did it STOP at the line, did a soldier DIE for letting it arrive, and does
    /// killing the attacker first SAVE him.
    ///
    /// All four were seen RED against the unwired tick before being trusted, with the failing
    /// numbers recorded in HANDOVER.md.
    /// </summary>
    static void CheckAdvancingSquads()
    {
        // A REAL CAMPAIGN LEVEL THAT AUTHORS AN ADVANCE, not a synthetic one: the mechanic is
        // only worth anything if the shipped data drives it, and four levels do (L4, L8, L9, L12).
        var level = AssetDatabase.FindAssets("t:LevelDefinitionSO")
            .Select(g => AssetDatabase.LoadAssetAtPath<LevelDefinitionSO>(
                AssetDatabase.GUIDToAssetPath(g)))
            .Where(l => l != null && !l.isTestLevel
                        && l.enemyGroups.Any(gr => gr.advancePerTurn > 0f))
            .OrderBy(l => l.levelNumber).FirstOrDefault();
        if (level == null) { Check(false, "a campaign level that authors an advance"); return; }

        var random = new System.Random(9);
        var start = LevelBuilder.BuildInitialState(level, 1, 12, random)
            with { Phase = GamePhase.Playing };

        Check(start.EnemyUnits.Any(u => u.AdvancePerTurn > 0f),
              $"L{level.levelNumber} fields an advancing squad " +
              $"({start.EnemyUnits.Count(u => u.AdvancePerTurn > 0f)} bodies)");

        // PURE MELEE NEVER VOLLEYS. L4 is the first advancing campaign level and its
        // chargers are shield bearers; if this ran on a shooter-only advance it would
        // pass against code that still fires melee. Count the OUTPUT: rounds in the air.
        {
            int melee = start.EnemyUnits.Count(BattleTick.IsPureMelee);
            int shooters = start.EnemyUnits.Count - melee;
            int roundsBefore = start.Projectiles.Count;
            var volley = BattleTick.FireEnemyVolley(start, new System.Random(9));
            int shots = volley.Projectiles.Count - roundsBefore;
            Check(melee > 0 && shots == shooters,
                  $"melee does not volley — {melee} shield bearers silent, " +
                  $"{shots} rounds from {shooters} shooters");
        }

        // --- 1. the march: it MOVES, it spends its budget, and it HOLDS short of the line ------
        //
        // Driven through the same edge the game uses — Resolving with an empty volley hands over
        // to EnemyWindup, which is where the budget is banked. Asserting on a hand-set
        // AdvanceRemaining would test the marcher and skip the banking, and the banking is the
        // half that was missing.
        var handover = start with { TurnSide = TurnSide.Player, TurnPhase = TurnPhase.Resolving,
                                    TurnHandoverDelay = 0f };
        var windup = BattleTick.Step(handover, 1f / 60f, level, random);

        float budget = windup.EnemyUnits.Where(u => u.AdvancePerTurn > 0f)
                             .Select(u => u.AdvanceRemaining).DefaultIfEmpty(0f).Max();
        Check(windup.TurnPhase == TurnPhase.EnemyWindup && budget > 0f,
              $"the handover into the windup BANKS an advance budget ({budget:F2})");

        var before = windup;
        var marching = windup;
        for (int i = 0; i < 120 && AdvanceSystems.Marching(marching.EnemyUnits); i++)
            marching = BattleTick.Step(marching, 1f / 60f, level, random);

        int id = before.EnemyUnits.First(u => u.AdvancePerTurn > 0f).Id;
        float fromX = before.EnemyUnits.First(u => u.Id == id).X;
        float toX = marching.EnemyUnits.First(u => u.Id == id).X;
        float frontline = marching.PlayerUnits.Where(u => u.StandingOnStructureId == null)
                                  .Max(u => u.X);

        Check(toX < fromX - 0.05f
              && toX >= frontline + AdvanceSystems.AdvanceStopGap - 0.01f
              && marching.EnemyUnits.All(u => u.AdvanceRemaining == 0f),
              $"the squad WALKS toward the line and stops clear of it " +
              $"(x {fromX:F2} -> {toX:F2}, front {frontline:F2}, " +
              $"hold {frontline + AdvanceSystems.AdvanceStopGap:F2})");

        // --- 2. arrival costs a soldier, and takes the attacker with it -----------------------
        //
        // Walked to the line rather than teleported there: MeleeRange is a claim condition, and a
        // body placed AT the line by hand would prove the claim works on a state the march can
        // never produce. Budget is topped up until it arrives.
        var closing = windup;
        for (int i = 0; i < 600 && closing.Skirmishes.Count == 0; i++)
        {
            if (!AdvanceSystems.Marching(closing.EnemyUnits))
                closing = closing with { EnemyUnits = AdvanceSystems.BankBudget(closing.EnemyUnits) };
            closing = BattleTick.Step(closing, 1f / 60f, level, random);
        }
        Check(closing.Skirmishes.Count > 0,
              $"a charger that reaches the line LOCKS ONTO a soldier " +
              $"({closing.Skirmishes.Count} fight(s))");

        // NOTHING BELOW CAN RUN WITHOUT A FIGHT, and it must go RED rather than THROW: against
        // the unwired tick this list is empty, and indexing it aborted the whole suite — every
        // check after this one silently stopped running. A check that explodes is not a check
        // that failed; it takes its neighbours with it.
        if (closing.Skirmishes.Count == 0)
        {
            Check(false, "the fight is a MUTUAL KILL — NO FIGHT EVER STARTED");
            Check(false, "killing the attacker mid-scuffle SPARES the soldier — NO FIGHT");
            return;
        }

        int playersBefore = closing.PlayerUnits.Count;
        int enemiesBefore = closing.EnemyUnits.Count;
        int victimId = closing.Skirmishes[0].VictimId;
        int attackerId = closing.Skirmishes[0].AttackerId;

        var fought = closing;
        for (int i = 0; i < 240 && fought.Skirmishes.Count > 0; i++)
            fought = BattleTick.Step(fought, 1f / 60f, level, random);

        Check(fought.PlayerUnits.All(u => u.Id != victimId)
              && fought.EnemyUnits.All(u => u.Id != attackerId)
              && fought.PlayerUnits.Count < playersBefore
              && fought.EnemyUnits.Count < enemiesBefore,
              $"the fight is a MUTUAL KILL — both bodies fall " +
              $"(players {playersBefore} -> {fought.PlayerUnits.Count}, " +
              $"enemies {enemiesBefore} -> {fought.EnemyUnits.Count})");

        // --- 3. the counter-play: kill the attacker and the soldier lives --------------------
        //
        // The whole point of the mechanic is that the advance can be ANSWERED. Same locked pair,
        // attacker removed mid-scuffle the way a volley would remove it.
        var spared = closing with
        {
            EnemyUnits = closing.EnemyUnits.Where(u => u.Id != attackerId).ToList(),
        };
        for (int i = 0; i < 240 && spared.Skirmishes.Count > 0; i++)
            spared = BattleTick.Step(spared, 1f / 60f, level, random);

        Check(spared.PlayerUnits.Any(u => u.Id == victimId) && spared.Skirmishes.Count == 0,
              "killing the attacker mid-scuffle SPARES the soldier — the fight ends");

        // --- 3b. THE TANK CREW IS REACHABLE, and the ground line is not a shield ---------------
        //
        // Rob found this on the first device build: "the player standing on the tank never gets
        // touched by the assault force." The crew stands 0.60 up on the vehicle and the old rule
        // exempted anyone standing on anything, so once the ground line was dead the chargers had
        // nobody they were allowed to touch and the battle could not be lost to melee at all.
        //
        // PUT THE WORLD IN THE STATE WHERE IT COULD FAIL: every ground unit removed, so the crew
        // is the ONLY thing left. Against the old predicate the squad stands there forever.
        {
            var crewOnly = windup with
            {
                PlayerUnits = windup.PlayerUnits.Where(u => u.StandingOnStructureId != null).ToList(),
            };
            Check(crewOnly.PlayerUnits.Count > 0 && crewOnly.PlayerUnits.All(u => u.Y > 0f),
                  $"the tank crew is garrisoned, off the ground " +
                  $"({crewOnly.PlayerUnits.Count} at y {crewOnly.PlayerUnits.Max(u => u.Y):F2})");

            int crewBefore = crewOnly.PlayerUnits.Count;
            for (int i = 0; i < 900 && crewOnly.PlayerUnits.Count == crewBefore; i++)
            {
                if (!AdvanceSystems.Marching(crewOnly.EnemyUnits)
                    && crewOnly.Skirmishes.Count == 0)
                    crewOnly = crewOnly with
                        { EnemyUnits = AdvanceSystems.BankBudget(crewOnly.EnemyUnits) };
                crewOnly = BattleTick.Step(crewOnly, 1f / 60f, level, random);
            }

            Check(crewOnly.PlayerUnits.Count < crewBefore,
                  $"an assault that has run out of GROUND targets comes for the TANK CREW " +
                  $"({crewBefore} -> {crewOnly.PlayerUnits.Count})");
        }

        // A GARRISON ON A REAL STRUCTURE STAYS OUT OF REACH — the other half of the same rule, and
        // the half that stops this from becoming "melee hits everything". Measured decks: the tank
        // is 0.60, every enemy structure is 1.40 or higher.
        Check(AdvanceSystems.Reachable(start.PlayerUnits.First(u => u.StandingOnStructureId != null))
              && !AdvanceSystems.Reachable(
                     start.PlayerUnits.First(u => u.StandingOnStructureId != null)
                     with { Y = 1.40f }),
              $"reach is a HEIGHT, not a flag — a 0.60 tank deck is reachable and a 1.40 deck is " +
              $"not (threshold {AdvanceSystems.MeleeReachHeight:F2})");

        // --- 3c. THE CAMERA GOES WHERE THE FIGHT IS ------------------------------------------
        //
        // Rob, first device build: the melee "happens off camera and it's weird". The windup
        // anchor was a fixed per-level value on the ENEMY side, while the march and the fight
        // happen at the PLAYER's line — `PhaseHalfWidth`'s whole marcher branch was ported and
        // fed `0f, false` from a literal.
        //
        // ASSERTED AS AN OUTPUT: where the camera actually ends up after a second of marching,
        // measured against the enemy anchor it used to sit on and the marchers it should now be
        // following. Asserting that the arguments are non-literal would be an input check.
        {
            var riding = windup;
            for (int i = 0; i < 60 && AdvanceSystems.Marching(riding.EnemyUnits); i++)
                riding = BattleTick.Step(riding, 1f / 60f, level, random);

            float marcherMean = riding.EnemyUnits.Where(u => u.AdvancePerTurn > 0f)
                                      .Select(u => u.X).Average();
            float cam = riding.CameraFollowX ?? riding.EnemyCamXAnchor;
            Check(Mathf.Abs(cam - marcherMean) < Mathf.Abs(cam - riding.EnemyCamXAnchor),
                  $"the windup camera RIDES THE MARCH rather than holding the enemy anchor " +
                  $"(cam {cam:F2}, marchers {marcherMean:F2}, enemy anchor " +
                  $"{riding.EnemyCamXAnchor:F2})");
        }

        // And it HOLDS on the fight once one starts: an engaged attacker has spent its budget and
        // is no longer a marcher, so a target built from marchers alone snaps back to the shooter
        // line ~1s before the mutual kill it was waiting for.
        Check(Mathf.Approximately(
                  CameraDirector.EnemyWindupAnchorX(
                      new List<float>(), new List<float> { -6f, -5.8f },
                      new List<float> { 7f, 8f }, new List<float> { 7f, 8f, -6f }, 9f),
                  -5.9f),
              "with the march over and a fight running, the camera holds on the SKIRMISH line");

        // --- 3d. A FIGHT KEEPS THE CAMERA EVEN WITH A VOLLEY IN THE AIR ----------------------
        //
        // Rob, second device build: "when the actual melee attack takes place, the camera should
        // stay on that until it's complete." Holding it inside the windup branch was not enough —
        // a skirmish SPANS phases (the handover gate waits for it), so a fight still running when
        // the windup ended handed the frame to the volley chase, which is by definition somewhere
        // else on the field.
        //
        // PUT IT IN THE STATE WHERE IT COULD FAIL: a live fight AND a live volley, in Resolving,
        // which is exactly the frame the player lost. The rounds are placed on the far side of the
        // field so the two targets cannot be confused for one another.
        {
            var mid = closing;
            var attackerNow = mid.EnemyUnits.First(u => u.Id == mid.Skirmishes[0].AttackerId);
            var far = mid.Projectiles.ToList();
            var contested = mid with
            {
                TurnPhase = TurnPhase.Resolving,
                TurnSide = TurnSide.Player,
                CameraFollowX = attackerNow.X,
            };
            // LONG ENOUGH FOR THE SPRING TO ACTUALLY GO SOMEWHERE. The first draft of this check
            // seeded the camera ON the fight and stepped ONE tick, so it passed against the very
            // regression it was written for — a camera that has not had time to move is not
            // evidence of a camera that stayed. 40 ticks is two thirds of a second, plenty of
            // travel toward the enemy anchor ~9 units away, and short of SkirmishDuration so the
            // fight is still running at the end.
            for (int i = 0; i < 40 && contested.Skirmishes.Count > 0; i++)
                contested = BattleTick.Step(contested, 1f / 60f, level, random);

            float fightX = contested.Skirmishes
                .Select(sk => contested.EnemyUnits.FirstOrDefault(u => u.Id == sk.AttackerId)?.X)
                .Where(x => x.HasValue).Select(x => x.Value).DefaultIfEmpty(attackerNow.X).Average();
            float camNow = contested.CameraFollowX ?? 0f;

            // ASSERTED AS CONTAINMENT, NOT PROXIMITY. The camera deliberately does NOT sit on the
            // fight — it sits at the midpoint of the whole engagement so the player's force is in
            // shot too, which on L4 is 1.54 from the fight and would fail a distance test while
            // being exactly right. The frame is recovered from CameraFollowZ through TargetZ's own
            // inverse rather than re-derived, so this cannot drift from the camera the game uses.
            float shownHalfWidth = (contested.CameraFollowZ ?? 0f) * CameraDirector.ZHalfFovTan
                                   - CameraDirector.FramePad;
            float playerFrontX = contested.PlayerUnits.Max(u => u.X);
            float playerRearX = contested.PlayerUnits.Min(u => u.X);

            Check(contested.Skirmishes.Count > 0
                  && Mathf.Abs(camNow - fightX) <= shownHalfWidth
                  && Mathf.Abs(camNow - playerRearX) <= shownHalfWidth
                  && Mathf.Abs(camNow - playerFrontX) <= shownHalfWidth,
                  $"a running fight KEEPS the camera in Resolving AND frames the whole engagement " +
                  $"— the fight and the player's force, rear rank included (cam {camNow:F2} " +
                  $"±{shownHalfWidth:F2}, fight {fightX:F2}, player line {playerRearX:F2}.." +
                  $"{playerFrontX:F2}, {contested.Skirmishes.Count} fight(s))");
        }

        // THE FRAME ITSELF, asked directly: a fight at the line must not crop the TANK CREW out of
        // the picture. That is the exact shot Rob asked for — "so the player can see what's
        // happening to their force" — and it is what framing the fighters alone got wrong.
        {
            var force = start.PlayerUnits.Select(u => u.X).ToList();
            float fightAt = force.Max() + AdvanceSystems.AdvanceStopGap;
            float half = CameraDirector.AssaultFrame(
                new List<float>(), new List<float> { fightAt, force.Max() }, force, out float at);

            Check(force.All(x => Mathf.Abs(x - at) <= half) && Mathf.Abs(fightAt - at) <= half,
                  $"the assault frame holds the FIGHT and the WHOLE player force including the " +
                  $"rear rank (centre {at:F2} ±{half:F2}, force {force.Min():F2}..{force.Max():F2}, " +
                  $"fight {fightAt:F2})");

            // AND IT HOLDS THEM NO WIDER THAN THAT. Containment alone is a one-way check — it is
            // satisfied by any frame big enough, which is how the contact shot sat at ±4.00 on an
            // engagement that wanted ±2.36 until 2026-08-21 without a single test going red. Rob:
            // "we're zooming out way too much here." The air is now exactly the spring margin, so
            // the ceiling is stated in terms of the union rather than as another constant to drift.
            var span = force.Concat(new[] { fightAt }).ToList();
            float need = CameraFraming.HalfWidth((span.Min() + span.Max()) / 2f, span);
            Check(half <= need + CameraDirector.ContactSpringMargin + 0.01f,
                  $"...and no wider — the contact frame is the engagement plus the spring margin, "
                  + $"not a pulled-back plaza (±{half:F2} against {need:F2} needed + "
                  + $"{CameraDirector.ContactSpringMargin:F2} air)");
        }

        // --- 3e. THE CAMERA HOLDS AFTER THE KILL, and the volley waits for it -----------------
        //
        // Rob, fourth device build: "we still are in a hurry to zoom back to the main force. we
        // need to show the melee assault the whole time and pause so it registers with the
        // player." Releasing on the tick the skirmish list emptied played the payoff — the two
        // bodies falling — as the camera was already leaving.
        //
        // MEASURED HALF A SECOND AFTER THE LAST PAIR FELL, which is the window that was broken:
        // the fight is over, its participants are gone from the unit lists, and the camera must
        // still be looking at where it happened.
        {
            var ending = closing;
            for (int i = 0; i < 240 && ending.Skirmishes.Count > 0; i++)
                ending = BattleTick.Step(ending, 1f / 60f, level, random);

            float heldAt = ending.MeleeHoldAnchorX;
            Check(ending.Skirmishes.Count == 0 && ending.MeleeHold > 0f,
                  $"the hold ARMS when the fight ends ({ending.MeleeHold:F2}s on the clock)");

            for (int i = 0; i < 30; i++)
                ending = BattleTick.Step(ending, 1f / 60f, level, random);

            float camAfter = ending.CameraFollowX ?? 0f;
            Check(ending.MeleeHold > 0f && Mathf.Abs(camAfter - heldAt) < 1.0f,
                  $"half a second after the last pair falls the camera is STILL THERE " +
                  $"(cam {camAfter:F2}, fight was at {heldAt:F2}, {ending.MeleeHold:F2}s left)");

            // And nothing shoots into the pause. The windup is the renderer's clock, so this
            // asserts the predicate the renderer gates on rather than the clock itself.
            Check(ending.MeleeHold > 0f,
                  "...and the enemy volley is still held off while it runs");
        }

        // THE LAST KILL HOLDS THE CAMERA. The cosmetic-over path used to spring to the
        // survivors the same tick Phase became Victory, so the killing blow played as the
        // camera was already leaving. Rob, 2026-08-13.
        {
            var def = ScriptableObject.CreateInstance<UnitDefinitionSO>();
            def.id = "holdcam"; def.maxHp = 16; def.damage = 8;
            var survivor = new UnitEntity(1, def, -7.2f, 0f, 0f, 16, true);
            var held = new GameState
            {
                Phase = GamePhase.Victory,
                VictoryCamHold = CameraDirector.VictoryCamHoldSeconds,
                CameraFollowX = 5.2f,
                CameraFollowZ = 11f,
                PlayerUnits = new List<UnitEntity> { survivor },
            };
            float killCam = held.CameraFollowX.Value;
            for (int i = 0; i < 60; i++)
                held = BattleTick.Step(held, 1f / 60f, null, new System.Random(1));
            Check(held.VictoryCamHold > 0.8f
                  && Mathf.Abs(held.CameraFollowX.Value - killCam) < 0.35f,
                  $"victory hold keeps the camera on the last kill at 1s "
                  + $"(cam {held.CameraFollowX:F2}, was {killCam:F2}, "
                  + $"{held.VictoryCamHold:F2}s left)");
            for (int i = 0; i < 180; i++)
                held = BattleTick.Step(held, 1f / 60f, null, new System.Random(1));
            Check(held.VictoryCamHold <= 0f && held.CameraFollowX.Value < 0f,
                  $"then it pans back to the survivors "
                  + $"(cam {held.CameraFollowX:F2}, hold {held.VictoryCamHold:F2})");
        }

        // --- 4. the turn cannot hand over while a fight is running ---------------------------
        //
        // TurnFlow.EvaluateVolley already took a skirmish count before anything could ever build
        // one; this is the first time it is asked with a real fight in progress.
        Check(TurnFlow.EvaluateVolley(0, 1, 0f, TurnSide.Enemy, 0, 1) == TurnFlow.VolleyGate.Busy
              && TurnFlow.EvaluateVolley(0, 0, 0f, TurnSide.Enemy, 0, 0)
                 == TurnFlow.VolleyGate.ReadyToHandOver,
              "a running skirmish HOLDS the turn open, and its end releases it");
    }

}
