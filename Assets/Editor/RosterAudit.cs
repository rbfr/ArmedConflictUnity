using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using ArmedConflict.Data;
using ArmedConflict.Game;

/// <summary>
/// TIER 2.3 — "keep the roster MECHANIC-DISTINCT" (PRODUCT_DIRECTION.md: "six pickable is fine;
/// no class sprawl that plays the same").
///
///     -batchmode -quit -executeMethod RosterAudit.Report
///
/// WHY THIS IS NOT A READ OF THE DATA SHEET. Six classes differ on paper — the .asset files carry
/// seven distinguishing fields between them. The product question is not what they DECLARE, it is
/// what the player gets when they fire, and those are two different things in this build: a field
/// only differentiates a class if some live code path reads it. `bulletVariant` is read nowhere,
/// `meleeDamage` is read nowhere, and `projectilesPerVolley` is read by the DEBUG driver only.
/// A class whose entire signature sits in those fields is, in the player's hands, a reskin.
///
/// So this audit FIRES A REAL VOLLEY per class through `BattleTick.FireVolley` — the same
/// function the drag calls — and measures what comes out. It is the "assert the OUTPUT, not the
/// input" rule (see HANDOVER) applied to the roster: a class is distinct when its VOLLEY is
/// distinct, and no amount of distinct authoring counts for anything if the volley is the same.
///
/// It reports three things:
///   1. DELIVERED vs DECLARED — any field the .asset sets that the fired volley does not carry.
///   2. IDENTICAL PAIRS — two classes whose measured volley profile matches exactly.
///   3. DOMINATION — a class the free Rifleman beats on every measured axis per point spent.
///      Costing more for strictly less is a class that no informed player ever picks, which is
///      the same product failure as a duplicate wearing a different name.
/// </summary>
public static class RosterAudit
{
    /// <summary>The aim used for every measured volley. Any fixed aim does — the comparison is
    /// between classes firing the SAME drag, never between a volley and an absolute number.</summary>
    static readonly Vector3 Aim = new(6f, 6f, 0f);

    /// <summary>What one class actually delivers, per shooter, for one drag.</summary>
    struct Profile
    {
        public string Name;
        public int PointCost, CoinPrice;
        public int Hp;                      // per body
        public int DeclaredShots;           // projectilesPerVolley, as authored
        public float Rounds;                // rounds the volley REALLY produced, per shooter
        public float UnitDamage;            // damage delivered to bodies, per shooter
        public float StructureDamage;       // damage delivered to buildings, per shooter
        public float Splash;
        public ProjectileType Type;
        public int DeclaredMelee;
        public float DamageTaken;           // armour: fraction of an incoming round it really takes

        /// <summary>What the class is worth as a body under fire. Armour is a HP multiplier in
        /// everything but name, so a class that halves incoming damage is compared on 2x its
        /// pool — otherwise the audit rates the shield bearer on the one number its mechanic
        /// exists to change.</summary>
        public float EffectiveHp => Hp / Mathf.Max(DamageTaken, 0.01f);

        /// <summary>Everything the player can actually feel, as one comparable key. Deliberately
        /// EXCLUDES the projectile type, which only picks a prefab: two classes that differ by
        /// nothing but the mesh of the round are not mechanically distinct.</summary>
        public string Signature =>
            $"hp{EffectiveHp:F0} rounds{Rounds:F2} dmg{UnitDamage:F2} " +
            $"struct{StructureDamage:F2} splash{Splash:F2}";
    }

    public static void Report()
    {
        var roster = AssetDatabase.LoadAssetAtPath<RosterDefinitionSO>("Assets/GameData/Roster.asset");
        if (roster == null) { Debug.LogError("[Roster] no Roster.asset"); EditorApplication.Exit(1); return; }

        // L1 is the measuring bench on purpose: it is the level every other level is balanced
        // against, and the profile is normalised PER SHOOTER, so the level only has to seat a
        // squad — it does not have to be representative.
        var level = AssetDatabase.FindAssets("t:LevelDefinitionSO")
            .Select(AssetDatabase.GUIDToAssetPath)
            .Select(AssetDatabase.LoadAssetAtPath<LevelDefinitionSO>)
            .Where(l => l != null && !l.isTestLevel)
            .OrderBy(l => l.levelNumber)
            .FirstOrDefault();
        if (level == null) { Debug.LogError("[Roster] no campaign level"); EditorApplication.Exit(1); return; }

        Debug.Log($"[Roster] measuring {roster.slots.Count} pickable classes on " +
                  $"L{level.levelNumber} {level.displayName}, one real volley each.");

        var profiles = new List<Profile>();
        foreach (var slot in roster.slots.Where(s => s.unit != null))
        {
            var p = Measure(level, slot);
            if (p.HasValue) profiles.Add(p.Value);
        }

        int errors = 0, warns = 0;

        // --- 1. DELIVERED vs DECLARED ---------------------------------------------------------
        foreach (var p in profiles)
        {
            Debug.Log($"[Roster] {p.Name,-14} {p.PointCost}pt {p.CoinPrice,4}c | " +
                      $"{p.Hp,2}hp{(p.DamageTaken < 1f ? $" x{1f / p.DamageTaken:F1} armour = {p.EffectiveHp:F0}" : "")} | " +
                      $"{p.Rounds:F2} rounds x {p.UnitDamage / Mathf.Max(p.Rounds, 1f):F1} " +
                      $"= {p.UnitDamage:F1} dmg | {p.StructureDamage:F1} vs masonry | " +
                      $"splash {p.Splash:F2} | {p.Type}");

            if (p.DeclaredShots > 1 && p.Rounds < p.DeclaredShots)
                Errors(ref errors,
                    $"[Roster] {p.Name} DECLARES {p.DeclaredShots} rounds a volley and the player's " +
                    $"volley fires {p.Rounds:F2}. `projectilesPerVolley` is read by AutoFire and by " +
                    "nothing else, so the burst exists only under the debug driver.");

            // MELEE LANDED ON 2026-08-12 AND THIS NUMBER IS STILL DEAD — which is not what the
            // handover predicted, so it is worth being exact about. `meleeDamage` is read in
            // exactly one place (`AdvanceSystems.Claim`) and read as a FLAG: "does this class
            // fight hand-to-hand". The fight it starts is a MUTUAL KILL, not a damage roll, so
            // the 12 is never arithmetic on either side — that is the ported design, and the
            // reference build never used the number either.
            //
            // And it can only ever fire on the ENEMY copy: a skirmish is claimed BY an advancing
            // attacker, `LevelBuilder` pins every PLAYER unit's AdvancePerTurn to 0, and the
            // locked turn structure has the player firing from a fixed line with no counter-
            // charge. The player's shield bearer keeps ARMOUR as its distinctness, which is why
            // that was given in the first place.
            if (p.DeclaredMelee > 0)
                Warns(ref warns,
                    $"[Roster] {p.Name} declares {p.DeclaredMelee} melee damage and the number is " +
                    "still DEAD DATA now that melee ships (2026-08-12). `meleeDamage` is read only " +
                    "as a FLAG — \"this class fights hand-to-hand\" — and a skirmish is a MUTUAL " +
                    "KILL rather than a damage roll, so no melee number is ever arithmetic. It " +
                    "also only reaches the ENEMY copy: skirmishes are claimed by ADVANCING " +
                    "attackers and every PLAYER unit's AdvancePerTurn is pinned to 0. The " +
                    "player's copy earns its distinctness from ARMOUR, not from this field.");
        }

        // `bulletVariant` is a three-value enum on every unit asset and on the projectile record,
        // and it is written by the importer and read by no one. Reported once rather than per
        // class, because it is one dead field, not six findings.
        Warns(ref warns,
            "[Roster] `bulletVariant` (Standard/MachineGun/Sniper) is authored on every unit and " +
            "read by no runtime code — it distinguishes nothing, visually or mechanically.");

        // --- 2. IDENTICAL PAIRS ---------------------------------------------------------------
        foreach (var pair in profiles.SelectMany((a, i) => profiles.Skip(i + 1).Select(b => (a, b)))
                                     .Where(t => t.a.Signature == t.b.Signature))
            Errors(ref errors,
                $"[Roster] {pair.a.Name} and {pair.b.Name} PLAY IDENTICALLY — same volley on every " +
                $"measured axis ({pair.a.Signature}). Tier 2.3 is precisely the rule against this.");

        // --- 3. DOMINATION BY THE FREE UNIT ---------------------------------------------------
        // THERE ARE TWO EXCHANGE RATES AND A CLASS ONLY DIES IF IT LOSES BOTH.
        // Slots cap bodies, points cap quality, and which one binds moves through the campaign
        // (see section 5): L1-L2 run at 1.00 points per slot, where a premium pick is paid for in
        // BODIES and per-point is the honest lens; from L3 the budget outruns the slots and the
        // line is full whatever you pick, so per-SLOT is. Judging on per-point alone marks every
        // premium class as a bad buy and would have condemned the machine gunner right after its
        // burst was restored — a metric that indicts the thing you just fixed is the wrong metric.
        var baseline = profiles.FirstOrDefault(p => p.CoinPrice == 0);
        if (!string.IsNullOrEmpty(baseline.Name))
            foreach (var p in profiles.Where(p => p.Name != baseline.Name))
            {
                bool WorseAt(float scale, float bScale) =>
                    p.UnitDamage / scale <= baseline.UnitDamage / bScale
                 && p.StructureDamage / scale <= baseline.StructureDamage / bScale
                 && p.EffectiveHp / scale <= baseline.EffectiveHp / bScale
                 && p.Splash <= baseline.Splash;

                bool perPoint = WorseAt(p.PointCost, baseline.PointCost);
                bool perSlot = WorseAt(1f, 1f);
                if (perPoint && perSlot)
                    Errors(ref errors,
                        $"[Roster] {p.Name} ({p.CoinPrice} coins, {p.PointCost}pt) is STRICTLY WORSE " +
                        $"than the free {baseline.Name} on BOTH exchange rates — {p.UnitDamage:F0} " +
                        $"damage vs {baseline.UnitDamage:F0}, {p.StructureDamage:F0} masonry vs " +
                        $"{baseline.StructureDamage:F0}, {p.EffectiveHp:F0} effective hp vs " +
                        $"{baseline.EffectiveHp:F0}, no splash. " +
                        "Nothing is bought by picking it, at any point in the campaign.");
                else if (perPoint)
                    Warns(ref warns,
                        $"[Roster] {p.Name} is worse than the free {baseline.Name} PER POINT, so it " +
                        "is a losing pick on L1-L2 where the budget is at parity with the slots. " +
                        "It earns its place from L3 on, where points are slack and only slots bind.");
            }

        // --- 4. THE STORE'S OWN CLAIMS --------------------------------------------------------
        // The one-liner is the promise the player pays against, so it is part of the audit: a
        // class whose mechanic is missing sells that mechanic on the purchase screen regardless.
        foreach (var slot in roster.slots.Where(s => s.unit != null && !string.IsNullOrEmpty(s.oneLiner)))
        {
            var p = profiles.FirstOrDefault(x => x.Name == slot.unit.name);
            bool claimsBurst = slot.oneLiner.ToLowerInvariant().Contains("burst");
            bool claimsMelee = slot.oneLiner.ToLowerInvariant().Contains("hand to hand")
                            || slot.oneLiner.ToLowerInvariant().Contains("walks forward");
            bool claimsArmour = slot.oneLiner.ToLowerInvariant().Contains("half damage");
            if (claimsArmour && p.DamageTaken >= 1f)
                Errors(ref errors, $"[Roster] the STORE sells {slot.unit.name} as \"{slot.oneLiner}\" " +
                                   "and it takes full damage.");
            if (claimsBurst && p.Rounds <= 1f)
                Errors(ref errors, $"[Roster] the STORE sells {slot.unit.name} for {slot.coinPrice} " +
                                   $"coins as \"{slot.oneLiner}\" and it fires {p.Rounds:F0} round.");
            if (claimsMelee && p.DeclaredMelee > 0)
                Errors(ref errors, $"[Roster] the STORE sells {slot.unit.name} for {slot.coinPrice} " +
                                   $"coins as \"{slot.oneLiner}\" and melee does not exist in this build.");
        }

        // --- 5. IS THE CHOICE EVEN EXPRESSIBLE? ------------------------------------------------
        // Distinctness is worthless if the budget cannot buy it. Slots cap BODIES and points cap
        // QUALITY, so points-per-slot is the exchange rate between the two: at 1.0 the only
        // squad that fills the line is all-Rifleman, and every premium class costs a body. That
        // is a legitimate design (it is the trade the loadout is built on) but it has to be
        // deliberate, and on a level at exactly 1.0 the picker is decoration.
        var campaign = AssetDatabase.FindAssets("t:LevelDefinitionSO")
            .Select(AssetDatabase.GUIDToAssetPath)
            .Select(AssetDatabase.LoadAssetAtPath<LevelDefinitionSO>)
            .Where(l => l != null && !l.isTestLevel)
            .OrderBy(l => l.levelNumber);
        foreach (var l in campaign)
        {
            int slots = Loadout.Slots(l), budget = Loadout.Budget(l);
            float rate = budget / Mathf.Max(1f, slots);
            string note = rate <= 1f
                ? " — AT PARITY: only the free 1-point class fills the line, so every premium pick " +
                  "is paid for in bodies"
                : $" — {budget - slots} spare points, enough for {budget - slots} upgrades at +1pt";
            Debug.Log($"[Roster] L{l.levelNumber} {l.displayName}: {slots} slots / {budget} points " +
                      $"= {rate:F2} points per slot{note}");
        }

        Debug.Log($"[Roster] done — {errors} errors, {warns} warnings over {profiles.Count} classes. " +
                  "An error here is a class the player cannot tell from another one, or cannot " +
                  "benefit from picking.");
    }

    static void Errors(ref int n, string msg) { n++; Debug.LogError(msg); }
    static void Warns(ref int n, string msg) { n++; Debug.LogWarning(msg); }

    /// <summary>
    /// Fields an all-of-one-class squad, fires ONE volley through the player's own path, and
    /// divides by the number of shooters. The tank shell is excluded: it fires every volley for
    /// free whatever the squad is, so counting it would credit every class with it equally —
    /// the same control-shot mistake that hid the airstrike for two sessions.
    /// </summary>
    static Profile? Measure(LevelDefinitionSO level, RosterSlot slot)
    {
        int count = Mathf.Min(Loadout.Slots(level),
                              Loadout.Budget(level) / Mathf.Max(slot.pointCost, 1));
        if (count <= 0) return null;

        var picks = new List<Pick> { new(slot.unit, count) };

        // THE GARRISON IS DROPPED FROM THE MEASUREMENT, and this is the whole reason the first
        // run of this audit reported a 4-damage machine gunner as doing 5.3. `ToPlayerGroups`
        // keeps the level's garrisoned player groups — the tank crew — because the loadout is
        // forbidden to touch them, so every class's volley silently carries the same two
        // riflemen. Averaging over all shooters then measures the garrison as much as the class.
        var groups = Loadout.ToPlayerGroups(level, picks)
            .Where(g => g.definition == slot.unit && string.IsNullOrEmpty(g.standingOnStructureId))
            .ToList();
        if (groups.Count == 0) return null;

        // Fixed seed for the formation, and a fixed seed for the volley's per-shooter jitter:
        // jitter must not make two runs of the audit disagree about a class.
        var s = LevelBuilder.BuildInitialState(level, 1, 12, new System.Random(9),
                    playerGroupsOverride: groups)
                with { Phase = GamePhase.Playing, TurnPhase = TurnPhase.Aiming };
        if (s.PlayerUnits.Count == 0 || s.EnemyUnits.Count == 0) return null;

        int shooters = s.PlayerUnits.Count;
        var fired = BattleTick.FireVolley(s, Aim, new System.Random(3));
        var rounds = fired.Projectiles
            .Where(p => p.OwnerIsPlayer && p.Type != ProjectileType.Shell).ToList();

        return new Profile
        {
            Name = slot.unit.name,
            PointCost = slot.pointCost,
            CoinPrice = slot.coinPrice,
            Hp = slot.unit.maxHp,
            DamageTaken = slot.unit.damageTakenMultiplier,
            DeclaredShots = Mathf.Max(slot.unit.projectilesPerVolley, 1),
            DeclaredMelee = slot.unit.meleeDamage,
            Rounds = rounds.Count / (float)shooters,
            UnitDamage = rounds.Sum(p => p.Damage) / (float)shooters,
            StructureDamage = rounds.Sum(p => p.Damage * p.StructureDamageMultiplier) / shooters,
            Splash = rounds.Count > 0 ? rounds.Max(p => p.SplashRadius) : 0f,
            Type = rounds.Count > 0 ? rounds[0].Type : ProjectileType.Bullet,
        };
    }
}
