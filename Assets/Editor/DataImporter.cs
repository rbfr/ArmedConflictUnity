using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using ArmedConflict.Data;

/// <summary>
/// Builds ScriptableObject assets from the JSON emitted by tools/export_kotlin_data.py.
///
/// Run:  -batchmode -quit -executeMethod DataImporter.Import
///       (reads Assets/../data.json unless -dataJson &lt;path&gt; is given)
///
/// Re-runnable: the Android build is still the shipping build and its data keeps moving, so this
/// overwrites existing assets in place rather than minting new ones. GUIDs are preserved, which
/// is what keeps scene and asset references from breaking on a re-import.
/// </summary>
public static class DataImporter
{
    const string Root = "Assets/GameData";

    static readonly Dictionary<string, UnitDefinitionSO> Units = new();
    static readonly Dictionary<string, StructureDefinitionSO> Structures = new();
    static readonly Dictionary<string, BackgroundDefinitionSO> Backgrounds = new();
    static readonly Dictionary<string, LevelDefinitionSO> Levels = new();

    public static void Import()
    {
        string path = ArgOr("-dataJson", "data.json");
        if (!File.Exists(path)) { Fail($"data.json not found at {path}"); return; }

        var root = MiniJson.Parse(File.ReadAllText(path)) as Dictionary<string, object>;
        if (root == null) { Fail("data.json did not parse as an object"); return; }

        var unparsed = root.GetList("unparsed");
        if (unparsed.Count > 0)
        {
            // The exporter records anything it could not parse. Importing a partial data set
            // silently is how a level quietly disappears, so refuse.
            Fail($"exporter reported {unparsed.Count} unparsed definitions — fix the export first");
            return;
        }

        foreach (var d in new[] { "Units", "Structures", "Backgrounds", "Levels", "Stages" })
            Directory.CreateDirectory($"{Root}/{d}");

        // Deliberately NOT wrapped in StartAssetEditing/StopAssetEditing: that batches (and
        // therefore DEFERS) asset creation, so an asset referenced by another asset created in
        // the same batch has no persistent id yet and serialises as `{fileID: 0}`. Every unit
        // reference in every level came out null that way — a silent, total data loss that
        // still "imported successfully".
        // Order matters: levels reference units/structures/backgrounds.
        foreach (var kv in root.GetDict("backgrounds")) BuildBackground(kv.Key, kv.Value.AsDict());
        foreach (var kv in root.GetDict("units")) BuildUnit(kv.Key, kv.Value.AsDict());
        foreach (var kv in root.GetDict("structures")) BuildStructure(kv.Key, kv.Value.AsDict());
        AssetDatabase.SaveAssets();
        foreach (var kv in root.GetDict("levels")) BuildLevel(kv.Key, kv.Value.AsDict());
        AssetDatabase.SaveAssets();
        foreach (var kv in root.GetDict("stages")) BuildStage(kv.Key, kv.Value.AsDict());
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        // The level ORDER is load bearing: the debug switcher does jumpToLevel(levelNumber),
        // which is only correct while levelNumber == index + 1. Read it BEFORE the sandboxes are
        // generated, because they take their numbers from it.
        var order = root.GetList("levelOrder");
        var names = order.Select(o => RefName(o.AsDict())).ToList();

        BuildSandboxLevels(names);
        AssetDatabase.SaveAssets();
        var absent = names.Where(n => n != null && !Levels.ContainsKey(n)).ToList();

        // DELETE ORPHANS. The importer creates and updates, and used to do nothing else — so a
        // level removed from the Kotlin left its .asset behind, and SpikeSceneBattle collects
        // EVERY LevelDefinitionSO it can find and orders them by levelNumber. A deleted level
        // therefore rejoined the campaign silently, at whatever number it used to hold. Found
        // when the campaign was cut to seven biome levels on 2026-08-05 and six assets were left
        // stranded. The Kotlin is the source of truth in BOTH directions: what it no longer
        // declares must not survive here.
        foreach (var orphan in AssetDatabase.FindAssets("t:LevelDefinitionSO", new[] { $"{Root}/Levels" })
                     .Select(AssetDatabase.GUIDToAssetPath)
                     .Where(p => !Levels.ContainsKey(Path.GetFileNameWithoutExtension(p)))
                     .ToList())
        {
            Debug.Log($"[DataImport] removing orphaned level asset {Path.GetFileName(orphan)}");
            AssetDatabase.DeleteAsset(orphan);
        }

        Debug.Log($"[DataImport] units={Units.Count} structures={Structures.Count} " +
                  $"backgrounds={Backgrounds.Count} levels={Levels.Count} " +
                  $"orderedLevels={names.Count} notImported={absent.Count}");
        if (absent.Count > 0)
            Debug.LogWarning("[DataImport] in levelOrder but NOT imported (generated by Kotlin " +
                             $"helper functions, need their generator ported): {string.Join(", ", absent)}");

        foreach (var lv in Levels.Values.Where(l => !l.isTestLevel).OrderBy(l => l.levelNumber))
            Debug.Log($"[DataImport] L{lv.levelNumber} {lv.displayName}: " +
                      $"{lv.enemyGroups.Sum(g => g.count)} enemies, {lv.structures.Count} structures");
    }

    // ------------------------------------------------------------------ builders

    static void BuildUnit(string name, Dictionary<string, object> v)
    {
        var flat = Flatten(v, n => Units.TryGetValue(n, out var u) ? Capture(u) : null);
        var so = Load<UnitDefinitionSO>($"{Root}/Units/{name}.asset");
        so.id = flat.Str("id"); so.displayName = flat.Str("displayName");
        so.modelAsset = flat.Str("modelAsset"); so.gunModelAsset = flat.Str("gunModelAsset");
        so.maxHp = flat.Int("maxHp"); so.damage = flat.Int("damage");
        so.projectileType = flat.Enum("projectileType", ProjectileType.Bullet);
        so.bulletVariant = flat.Enum("bulletVariant", BulletVariant.Standard);
        so.projectilesPerVolley = flat.Int("projectilesPerVolley", 1);
        so.splashRadius = flat.F("splashRadius");
        so.structureDamageMultiplier = flat.F("structureDamageMultiplier", 1f);
        so.meleeDamage = flat.Int("meleeDamage");
        so.renderScale = flat.F("renderScale", 1f);
        EditorUtility.SetDirty(so);
        Units[name] = so;
    }

    static void BuildStructure(string name, Dictionary<string, object> v)
    {
        bool scaled = HasMethod(v, "scaled");
        var flat = Flatten(v, n => Structures.TryGetValue(n, out var s) ? Capture(s) : null);
        var so = Load<StructureDefinitionSO>($"{Root}/Structures/{name}.asset");

        so.id = flat.Str("id"); so.displayName = flat.Str("displayName");
        so.modelAsset = flat.Str("modelAsset");
        so.maxHp = flat.Int("maxHp");
        so.size = flat.F("size");
        so.isPlayerSide = flat.B("isPlayerSide");
        so.modelAbsoluteScale = flat.B("modelAbsoluteScale");
        so.modelScaleUnits = flat.F("modelScaleUnits", 1f);
        so.standWidth = flat.F("standWidth", 0.6f);
        so.deckStandZOffset = flat.F("deckStandZOffset");
        so.worldScale = flat.F("worldScale", 1f);
        so.hasHitWidth = flat.Has("hitWidth"); so.hitWidth = flat.F("hitWidth", -1f);
        so.hasDeckY = flat.Has("deckY"); so.deckY = flat.F("deckY", -1f);

        so.cannon = null; so.hasCannon = false;
        if (flat.Raw("cannon") is Dictionary<string, object> cd)
        {
            var c = Flatten(cd, _ => null);
            so.cannon = new CannonSpec
            {
                damage = c.Int("damage"),
                splashRadius = c.F("splashRadius"),
                structureDamageMultiplier = c.F("structureDamageMultiplier", 1f),
                muzzleOffsetX = c.F("muzzleOffsetX"),
                muzzleOffsetY = c.F("muzzleOffsetY"),
                velocityBoost = c.F("velocityBoost", 1.12f),
                ammoPerBattle = c.Int("ammoPerBattle", 2),
            };
            so.hasCannon = true;
        }

        so.flagMount = null; so.hasFlagMount = false;
        if (flat.Raw("flagMount") is Dictionary<string, object> fd)
        {
            var f = Flatten(fd, _ => null);
            var pos = f.Positional();
            so.flagMount = new FlagMount
            {
                offsetX = f.Has("offsetX") ? f.F("offsetX") : Num(pos.ElementAtOrDefault(0)),
                offsetY = f.Has("offsetY") ? f.F("offsetY") : Num(pos.ElementAtOrDefault(1)),
                model = f.Str("model") ?? "models/flag.glb",
                scale = f.F("scale", 1f),
            };
            so.hasFlagMount = true;
        }

        so.damageChunks = new List<DamageChunk>();
        foreach (var ch in flat.List("damageChunks"))
        {
            var c = Flatten(ch.AsDict(), _ => null);
            var p = c.Positional();
            so.damageChunks.Add(new DamageChunk
            {
                offsetX = Num(p.ElementAtOrDefault(0)), offsetY = Num(p.ElementAtOrDefault(1)),
                offsetZ = Num(p.ElementAtOrDefault(2)), sizeX = Num(p.ElementAtOrDefault(3)),
                sizeY = Num(p.ElementAtOrDefault(4)), sizeZ = Num(p.ElementAtOrDefault(5)),
                pieces = c.Int("pieces", 1),
            });
        }

        if (scaled) ApplyScaled(so, StructureDefinitionSO.StructureScale);
        EditorUtility.SetDirty(so);
        Structures[name] = so;
    }

    /// <summary>
    /// Port of `StructureDefinition.scaled()`. Every length moves TOGETHER — and note
    /// flagMount.scale gets k as well. That multiplication was missed when STRUCTURE_SCALE
    /// landed, and the omission hid precisely because the OFFSETS were scaled: flags kept
    /// riding the right spot on a model that had grown 2.5x around them, so nothing looked
    /// broken, it just looked like small flags.
    /// </summary>
    static void ApplyScaled(StructureDefinitionSO s, float k)
    {
        s.maxHp = (int)(s.maxHp * k);            // STRUCTURE_HP_SCALE == STRUCTURE_SCALE
        s.size *= k;
        if (s.hasHitWidth) s.hitWidth *= k;
        s.standWidth *= k;
        s.deckStandZOffset *= k;
        s.modelScaleUnits *= k;
        s.worldScale *= k;
        if (s.hasDeckY) s.deckY *= k;
        if (s.hasFlagMount)
        {
            s.flagMount.offsetX *= k;
            s.flagMount.offsetY *= k;
            s.flagMount.scale *= k;
        }
        if (s.hasCannon)
        {
            s.cannon.muzzleOffsetX *= k;
            s.cannon.muzzleOffsetY *= k;
        }
        foreach (var c in s.damageChunks)
        {
            c.offsetX *= k; c.offsetY *= k; c.offsetZ *= k;
            c.sizeX *= k; c.sizeY *= k; c.sizeZ *= k;
        }
    }

    static void BuildBackground(string name, Dictionary<string, object> v)
    {
        var flat = Flatten(v, n => Backgrounds.TryGetValue(n, out var b) ? Capture(b) : null);
        var so = Load<BackgroundDefinitionSO>($"{Root}/Backgrounds/{name}.asset");
        so.skyTop = flat.Col("skyTop"); so.skyHorizon = flat.Col("skyHorizon");
        so.groundColor = flat.Col("groundColor"); so.groundNear = flat.Col("groundNear");
        so.horizonAccent = flat.Col("horizonAccent");
        so.silhouetteFar = flat.Col("silhouetteFar"); so.silhouetteNear = flat.Col("silhouetteNear");
        so.style = flat.Enum("style", SilhouetteStyle.Mountains);
        so.snowfall = flat.B("snowfall");
        EditorUtility.SetDirty(so);
        Backgrounds[name] = so;
    }

    static void BuildLevel(string name, Dictionary<string, object> v)
    {
        var flat = Flatten(v, _ => null);
        var so = Load<LevelDefinitionSO>($"{Root}/Levels/{name}.asset");
        so.id = flat.Str("id"); so.displayName = flat.Str("displayName");
        so.levelNumber = flat.Int("levelNumber", 1);
        so.levelGoal = flat.Str("levelGoal") ?? "Destroy all enemy units";
        so.heliChance = flat.F("heliChance");
        so.levelBase = flat.Int("levelBase", 60);
        so.deployBudget = flat.Int("deployBudget");
        so.windAccelZ = flat.F("windAccelZ");
        so.staticCamera = flat.B("staticCamera");
        so.isTestLevel = flat.B("isTestLevel");
        so.background = Resolve(flat.Raw("background"), Backgrounds);

        so.enemyGroups = flat.List("enemyGroups").Select(Group).ToList();
        so.playerGroups = flat.List("playerGroups").Select(Group).ToList();
        so.structures = flat.List("structures").Select(o => Placement(o, name)).ToList();
        so.props = flat.List("props").Select(Prop).ToList();

        EditorUtility.SetDirty(so);
        Levels[name] = so;
    }

    // The roster/grouping sandboxes (L21-L28) are GENERATED by a Kotlin helper rather than
    // declared as data, so the exporter cannot see them. Porting the generator is the only way
    // to get them, and it is small. Kept faithful to rosterSandbox()/sandboxGroups().
    static readonly string[] PlayerCycle =
        { "Rifleman", "Rifleman", "MachineGunner", "Rifleman", "Grenadier" };
    static readonly string[] EnemyCycle =
        { "EnemyRifleman", "EnemyRifleman", "EnemyMachineGunner", "EnemyRifleman", "EnemyGrenadier" };

    /// <summary>
    /// The roster/grouping sandboxes, whose Kotlin generator (`rosterSandbox`) the exporter cannot
    /// parse — so they are rebuilt here from the same parameters.
    ///
    /// The NUMBERS are taken from the level's position in `levelOrder`, never hardcoded. They used
    /// to be literals in the spec table below, which made this a SECOND source of truth for level
    /// numbering: cutting the campaign to seven biome levels renumbered them in the Kotlin and
    /// they silently kept their old 21-28 here, breaking `levelNumber == index + 1` and with it
    /// the level switcher. The composition is duplicated because it has to be; the ordering is not.
    /// </summary>
    static void BuildSandboxLevels(List<string> order)
    {
        // (assetName, label, playerCount, enemyCount, playerSquads, enemySquads)
        var specs = new (string name, string label, int pc, int ec, int ps, int es)[]
        {
            ("LevelRosterSmall",     "Roster S v S",       6,  6, 2, 2),
            ("LevelRosterMedium",    "Roster M v M",      14, 14, 3, 3),
            ("LevelRosterLarge",     "Roster L v L",      26, 26, 5, 5),
            ("LevelRosterSmallVsLg", "Roster S v L",       6, 26, 2, 5),
            ("LevelRosterLargeVsSm", "Roster L v S",      26,  6, 5, 2),
            ("LevelGroupingOne",     "Grouping 1 squad",  14, 14, 1, 1),
            ("LevelGroupingTwo",     "Grouping 2 squads", 14, 14, 2, 2),
            ("LevelGroupingSeven",   "Grouping 7 squads", 14, 14, 7, 7),
        };

        foreach (var s in specs)
        {
            int n = order.IndexOf(s.name) + 1;
            if (n == 0)
            {
                Debug.LogWarning($"[DataImport] {s.name} is not in levelOrder — sandbox skipped");
                continue;
            }
            var so = Load<LevelDefinitionSO>($"{Root}/Levels/{s.name}.asset");
            so.id = $"level_test_roster_{n}";
            so.displayName = $"TEST — {s.label}";
            so.levelNumber = n;
            so.levelGoal = $"Sandbox: {s.pc} v {s.ec}, {s.ps} v {s.es} squads";
            so.isTestLevel = true;
            so.levelBase = 0;
            // Winter: flat bright ground reads massed units best.
            so.background = Backgrounds.GetValueOrDefault("Winter");
            // No enemy structures ON PURPOSE — a dominant structure would drive the
            // scout/resolve framing and mask the thing being measured.
            so.structures = new List<StructurePlacement>
            {
                new()
                {
                    id = "player_tank",
                    definition = Structures.GetValueOrDefault("PlayerTank"),
                    x = -10.5f, y = 0f, z = 0f, hpScale = 1f, standWidth = -1f,
                },
            };
            so.playerGroups = SandboxGroups(s.pc, s.ps, -7.5f, PlayerCycle);
            so.enemyGroups = SandboxGroups(s.ec, s.es, 6.5f, EnemyCycle);
            so.props = new List<PropPlacement>();
            EditorUtility.SetDirty(so);
            Levels[s.name] = so;
        }
        Debug.Log($"[DataImport] generated {specs.Length} sandbox levels from the ported rosterSandbox()");
    }

    /// <summary>
    /// Port of sandboxGroups(): splits count into squads whose sizes differ by at most one,
    /// anchored around centerX at 1.7 spacing.
    /// </summary>
    static List<EnemyGroup> SandboxGroups(int count, int squads, float centerX, string[] cycle)
    {
        const float squadSpacing = 1.7f;
        int n = Mathf.Clamp(squads, 1, count);
        var outp = new List<EnemyGroup>();
        for (int i = 0; i < n; i++)
        {
            int size = count / n + (i < count % n ? 1 : 0);
            outp.Add(new EnemyGroup
            {
                definition = Units.GetValueOrDefault(cycle[i % cycle.Length]),
                count = size,
                anchorX = centerX + (i - (n - 1) / 2f) * squadSpacing,
            });
        }
        return outp;
    }

    static void BuildStage(string name, Dictionary<string, object> v)
    {
        var flat = Flatten(v, _ => null);
        var so = Load<StageDefinitionSO>($"{Root}/Stages/{name}.asset");
        so.id = flat.Str("id"); so.displayName = flat.Str("displayName");
        so.tagline = flat.Str("tagline");
        so.starsToUnlock = flat.Int("starsToUnlock");
        so.unlockRewardId = flat.Str("unlockRewardId");
        so.completionCoinBonus = flat.Int("completionCoinBonus");
        so.levels = flat.List("levels")
                        .Select(o => Resolve(o, Levels))
                        .Where(l => l != null).ToList();
        EditorUtility.SetDirty(so);
    }

    static EnemyGroup Group(object o)
    {
        var f = Flatten(o.AsDict(), _ => null);
        var p = f.Positional();
        return new EnemyGroup
        {
            definition = Resolve(f.Has("definition") ? f.Raw("definition") : p.ElementAtOrDefault(0), Units),
            count = f.Int("count"),
            anchorX = f.F("anchorX"),
            anchorZ = f.F("anchorZ"),
            standingOnStructureId = f.Str("standingOnStructureId"),
            advancePerTurn = f.F("advancePerTurn"),
        };
    }

    static StructurePlacement Placement(object o, string levelName)
    {
        var f = Flatten(o.AsDict(), _ => null);
        var p = f.Positional();
        var defExpr = f.Has("definition") ? f.Raw("definition") : p.ElementAtOrDefault(1);
        return new StructurePlacement
        {
            id = f.Has("id") ? f.Str("id") : p.ElementAtOrDefault(0) as string,
            definition = ResolveStructure(defExpr, levelName),
            x = f.F("x"), y = f.F("y"), z = f.F("z"),
            collapseWith = f.Str("collapseWith"),
            restsOn = f.Str("restsOn"),
            hasStandWidth = f.Has("standWidth"),
            standWidth = f.F("standWidth", -1f),
            hpScale = f.F("hpScale", 1f),
        };
    }

    /// <summary>
    /// Resolves a placement's structure, MINTING A LEVEL-LOCAL VARIANT when the level overrode
    /// it. L6 does exactly this: `PlayerTank.let { tank -> tank.copy(cannon =
    /// tank.cannon?.copy(ammoPerBattle = 6)) }` — six shells instead of three, because 637 HP of
    /// masonry against ~80 damage per volley states a strategy the player cannot execute.
    /// Resolving straight to the shared asset would drop that and quietly re-break the level.
    /// Anything this does not understand is a hard failure, never a silent fallback.
    /// </summary>
    static StructureDefinitionSO ResolveStructure(object expr, string levelName)
    {
        var d = expr.AsDict();
        var baseAsset = Resolve(expr, Structures);
        if (d == null || baseAsset == null) return baseAsset;

        var overrides = CollectOverrides(d);
        if (overrides.Count == 0) return baseAsset;

        string variantPath = $"{Root}/Structures/{baseAsset.name}__{levelName}.asset";
        var v = AssetDatabase.LoadAssetAtPath<StructureDefinitionSO>(variantPath);
        if (v == null)
        {
            v = ScriptableObject.CreateInstance<StructureDefinitionSO>();
            AssetDatabase.CreateAsset(v, variantPath);
        }
        EditorUtility.CopySerialized(baseAsset, v);

        foreach (var kv in overrides)
        {
            switch (kv.Key)
            {
                case "cannon":
                    var cd = kv.Value.AsDict();
                    var args = (cd?.GetValueOrDefault("__args")).AsDict();
                    if (args == null) Fail($"{levelName}: cannon override not understood");
                    else if (v.hasCannon)
                    {
                        if (args.ContainsKey("ammoPerBattle"))
                            v.cannon.ammoPerBattle = (int)Num(args["ammoPerBattle"]);
                        if (args.ContainsKey("damage"))
                            v.cannon.damage = (int)Num(args["damage"]);
                    }
                    break;
                case "maxHp": v.maxHp = (int)Num(kv.Value); break;
                case "size": v.size = Num(kv.Value); break;
                default:
                    Fail($"{levelName}: unhandled structure override '{kv.Key}' — add it rather " +
                         "than letting it drop silently");
                    break;
            }
        }
        EditorUtility.SetDirty(v);
        Debug.Log($"[DataImport] {levelName}: level-local {baseAsset.name} " +
                  $"({string.Join(",", overrides.Keys)}) -> {System.IO.Path.GetFileName(variantPath)}");
        return v;
    }

    /// <summary>Pulls the override arguments out of a .copy(...) / .let{ x -> x.copy(...) } chain.</summary>
    static Dictionary<string, object> CollectOverrides(Dictionary<string, object> d)
    {
        var outp = new Dictionary<string, object>();
        int guard = 0;
        while (d != null && guard++ < 16)
        {
            if (d.GetValueOrDefault("__method") as string == "let")
            {
                d = d.GetValueOrDefault("__body").AsDict();
                continue;
            }
            if (d.TryGetValue("__ctor", out var c) && (c as string ?? "").EndsWith(".copy"))
            {
                foreach (var kv in d)
                    if (!kv.Key.StartsWith("__")) outp[kv.Key] = kv.Value;
                break;
            }
            if (d.GetValueOrDefault("__method") as string == "copy"
                && d.GetValueOrDefault("__args").AsDict() is { } a)
            {
                foreach (var kv in a) if (!kv.Key.StartsWith("__")) outp[kv.Key] = kv.Value;
            }
            d = d.GetValueOrDefault("__on").AsDict();
        }
        return outp;
    }

    static PropPlacement Prop(object o)
    {
        var f = Flatten(o.AsDict(), _ => null);
        var p = f.Positional();
        return new PropPlacement
        {
            modelAsset = f.Has("modelAsset") ? f.Str("modelAsset") : p.ElementAtOrDefault(0) as string,
            x = f.F("x"), z = f.F("z"), scale = f.F("scale", 1f),
            slowsAdvance = f.B("slowsAdvance"), halfWidth = f.F("halfWidth", 1f),
        };
    }

    // ------------------------------------------------------------------ reference resolution

    /// <summary>
    /// Flattens `.copy(...)` / `.let { x -> x.copy(...) }` chains into a single argument map:
    /// walk to the base, capture its fields, then lay each override on top.
    /// This is what preserves L6's level-local tank (6 shells instead of 3, against 637 HP of
    /// masonry) — dropping it would be a balance change disguised as an import detail.
    /// </summary>
    static Flat Flatten(Dictionary<string, object> v, Func<string, Dictionary<string, object>> lookup)
    {
        var overrides = new List<Dictionary<string, object>>();
        int guard = 0;
        while (v != null && guard++ < 16)
        {
            if (v.TryGetValue("__method", out var m))
            {
                string meth = m as string;
                if (meth == "let" && v.TryGetValue("__body", out var body))
                {
                    v = body.AsDict();
                    continue;
                }
                if (v.TryGetValue("__args", out var a) && a is Dictionary<string, object> ad)
                    overrides.Insert(0, ad);
                v = v.GetValueOrDefault("__on").AsDict();
                continue;
            }
            if (v.TryGetValue("__ctor", out var c))
            {
                string ctor = c as string;
                string method = DerivingSuffix(ctor);
                if (method != null)
                {
                    // `Rifleman.copy(displayName = "Enemy Rifleman")` — the copy ARGS are the
                    // override, and the receiver name is the base to layer them onto. Taking the
                    // args alone would produce a unit with a name and nothing else.
                    //
                    // ANY deriving call takes this shape, not only copy: the exporter's ident
                    // reader swallows the dot, so `FortressTierUnscaled.scaled()` also arrives as
                    // a ctor NAMED for the call. Matching only ".copy" left FortressTier with no
                    // base at all, and five levels place it.
                    overrides.Insert(0, v);
                    var baseName = Short(ctor.Substring(0, ctor.Length - method.Length - 1));
                    var baseFields = lookup?.Invoke(baseName);
                    if (baseFields != null) overrides.Insert(0, baseFields);
                    break;
                }
                overrides.Insert(0, v);
                break;
            }
            if (v.TryGetValue("__ref", out var r) && lookup != null)
            {
                var baseFields = lookup(Short(r as string));
                if (baseFields != null) overrides.Insert(0, baseFields);
                break;
            }
            break;
        }

        var merged = new Dictionary<string, object>();
        foreach (var o in overrides)
            foreach (var kv in o)
                if (!kv.Key.StartsWith("__") || kv.Key == "__positional")
                    merged[kv.Key] = kv.Value;
        return new Flat(merged);
    }

    /// <summary>Member calls that derive one definition from another — see Flatten.</summary>
    static readonly string[] DerivingMethods = { "copy", "scaled" };

    /// <summary>The deriving call a ctor NAME encodes, or null. "Foo.scaled" -> "scaled".</summary>
    static string DerivingSuffix(string ctor)
    {
        if (ctor == null) return null;
        foreach (var m in DerivingMethods)
            if (ctor.EndsWith("." + m)) return m;
        return null;
    }

    /// <summary>
    /// True if `name` appears anywhere in the chain — as a real `__method` node, OR folded into a
    /// ctor name. Both forms occur for the same Kotlin: `X.copy(...).scaled()` gives a __method,
    /// while a bare `X.scaled()` gives only the ctor name. Checking one form made the structure
    /// scale silently depend on which shape the source happened to take.
    /// </summary>
    static bool HasMethod(Dictionary<string, object> v, string name)
    {
        int guard = 0;
        while (v != null && guard++ < 16)
        {
            if (v.GetValueOrDefault("__method") as string == name) return true;
            if (DerivingSuffix(v.GetValueOrDefault("__ctor") as string) == name) return true;
            v = v.GetValueOrDefault("__on").AsDict();
        }
        return false;
    }

    static T Resolve<T>(object o, Dictionary<string, T> table) where T : ScriptableObject
    {
        var name = RefName(o.AsDict());
        if (name != null && table.TryGetValue(name, out var hit)) return hit;
        return null;
    }

    static string RefName(Dictionary<string, object> d)
    {
        if (d == null) return null;
        int guard = 0;
        while (d != null && guard++ < 16)
        {
            if (d.TryGetValue("__ref", out var r)) return Short(r as string);
            d = d.GetValueOrDefault("__on").AsDict();
        }
        return null;
    }

    static string Short(string dotted)
        => dotted == null ? null : dotted.Substring(dotted.LastIndexOf('.') + 1);

    /// <summary>Reads an already-built asset back into an argument map, so `.copy()` can layer on it.</summary>
    static Dictionary<string, object> Capture(UnitDefinitionSO u) => new()
    {
        ["id"] = u.id, ["displayName"] = u.displayName, ["modelAsset"] = u.modelAsset,
        ["gunModelAsset"] = u.gunModelAsset, ["maxHp"] = (double)u.maxHp,
        ["damage"] = (double)u.damage, ["projectileType"] = Ref(u.projectileType.ToString()),
        ["bulletVariant"] = Ref(u.bulletVariant.ToString()),
        ["projectilesPerVolley"] = (double)u.projectilesPerVolley,
        ["splashRadius"] = (double)u.splashRadius,
        ["structureDamageMultiplier"] = (double)u.structureDamageMultiplier,
        ["meleeDamage"] = (double)u.meleeDamage, ["renderScale"] = (double)u.renderScale,
    };

    /// <summary>
    /// Reads a built structure back as an argument map so a derived one can layer on it.
    ///
    /// The OPTIONAL fields have to come across too, and used not to. Every field below the
    /// worldScale line was missing, so any `.copy()`/`.scaled()` that did not restate a field
    /// silently lost it — a structure derived from a base would come back with no hit width, no
    /// deck, no flag and NO DAMAGE CHUNKS, which means it also could not shed geometry when hit.
    /// It stayed hidden because the wide and small fortress tiers restate all of theirs, and the
    /// one val that restates nothing (`FortressTier = FortressTierUnscaled.scaled()`) was being
    /// dropped by the exporter before it ever got here.
    ///
    /// hitWidth and deckY are written ONLY when the base actually has them: their presence is the
    /// signal (`flat.Has`), so an unconditional -1 would read as "measured, and it is -1".
    /// </summary>
    static Dictionary<string, object> Capture(StructureDefinitionSO s)
    {
        var d = new Dictionary<string, object>
        {
            ["id"] = s.id, ["displayName"] = s.displayName, ["modelAsset"] = s.modelAsset,
            ["maxHp"] = (double)s.maxHp, ["size"] = (double)s.size,
            ["isPlayerSide"] = s.isPlayerSide, ["modelAbsoluteScale"] = s.modelAbsoluteScale,
            ["modelScaleUnits"] = (double)s.modelScaleUnits,
            ["standWidth"] = (double)s.standWidth,
            ["deckStandZOffset"] = (double)s.deckStandZOffset,
            ["worldScale"] = (double)s.worldScale,
        };
        if (s.hasHitWidth) d["hitWidth"] = (double)s.hitWidth;
        if (s.hasDeckY) d["deckY"] = (double)s.deckY;
        if (s.hasCannon && s.cannon != null)
            d["cannon"] = new Dictionary<string, object>
            {
                ["damage"] = (double)s.cannon.damage,
                ["splashRadius"] = (double)s.cannon.splashRadius,
                ["structureDamageMultiplier"] = (double)s.cannon.structureDamageMultiplier,
                ["muzzleOffsetX"] = (double)s.cannon.muzzleOffsetX,
                ["muzzleOffsetY"] = (double)s.cannon.muzzleOffsetY,
                ["velocityBoost"] = (double)s.cannon.velocityBoost,
                ["ammoPerBattle"] = (double)s.cannon.ammoPerBattle,
            };
        if (s.hasFlagMount && s.flagMount != null)
            d["flagMount"] = new Dictionary<string, object>
            {
                ["offsetX"] = (double)s.flagMount.offsetX,
                ["offsetY"] = (double)s.flagMount.offsetY,
                ["model"] = s.flagMount.model,
                ["scale"] = (double)s.flagMount.scale,
            };
        if (s.damageChunks != null && s.damageChunks.Count > 0)
            d["damageChunks"] = s.damageChunks.Select(c => (object)new Dictionary<string, object>
            {
                // The chunk reader takes offsets and sizes POSITIONALLY, in the Kotlin's own
                // parameter order, so a captured chunk has to be handed back the same way.
                ["__positional"] = new List<object>
                {
                    (double)c.offsetX, (double)c.offsetY, (double)c.offsetZ,
                    (double)c.sizeX, (double)c.sizeY, (double)c.sizeZ,
                },
                ["pieces"] = (double)c.pieces,
            }).ToList();
        return d;
    }

    static Dictionary<string, object> Capture(BackgroundDefinitionSO b) => new();

    static Dictionary<string, object> Ref(string name) => new() { ["__ref"] = name };

    static float Num(object o) => o is double d ? (float)d : o is long l ? l : 0f;

    static T Load<T>(string path) where T : ScriptableObject
    {
        var existing = AssetDatabase.LoadAssetAtPath<T>(path);
        if (existing != null) return existing;                  // preserve GUID on re-import
        var created = ScriptableObject.CreateInstance<T>();
        AssetDatabase.CreateAsset(created, path);
        return created;
    }

    static string ArgOr(string flag, string fallback)
    {
        var a = Environment.GetCommandLineArgs();
        for (int i = 0; i < a.Length - 1; i++) if (a[i] == flag) return a[i + 1];
        return fallback;
    }

    static void Fail(string msg)
    {
        Debug.LogError($"[DataImport] {msg}");
        if (Application.isBatchMode) EditorApplication.Exit(1);
    }

    // ------------------------------------------------------------------ tiny typed accessor

    class Flat
    {
        readonly Dictionary<string, object> d;
        public Flat(Dictionary<string, object> d) => this.d = d;

        public bool Has(string k) => d.ContainsKey(k) && d[k] != null;
        public object Raw(string k) => d.GetValueOrDefault(k);
        public string Str(string k) => d.GetValueOrDefault(k) as string;
        public float F(string k, float dflt = 0f) => Has(k) ? Num(d[k]) : dflt;
        public int Int(string k, int dflt = 0) => Has(k) ? (int)Math.Round(Num(d[k])) : dflt;
        public bool B(string k, bool dflt = false) => Has(k) && d[k] is bool b ? b : dflt;
        public List<object> Positional() => (d.GetValueOrDefault("__positional") as List<object>) ?? new();
        public List<object> List(string k) => (d.GetValueOrDefault(k) as List<object>) ?? new();

        public TEnum Enum<TEnum>(string k, TEnum dflt) where TEnum : struct
        {
            var name = Short((d.GetValueOrDefault(k).AsDict())?.GetValueOrDefault("__ref") as string);
            return name != null && System.Enum.TryParse<TEnum>(name, out var e) ? e : dflt;
        }

        /// <summary>
        /// Compose colours arrive as 0xAARRGGBB longs inside a Color(...) constructor.
        ///
        /// The argument list may be under EITHER key: the exporter writes "__args" for a
        /// constructor whose arguments are all positional, and "__positional" only when they are
        /// mixed with named ones. Reading just one of them imported every background as pure
        /// black — silently, because the asset still existed and the count still looked right.
        /// Verify CONTENT, not just counts.
        /// </summary>
        public Color Col(string k)
        {
            if (!Has(k)) return Color.magenta;
            var raw = d[k];
            if (raw.AsDict() is { } dd)
            {
                var args = dd.GetValueOrDefault("__args") ?? dd.GetValueOrDefault("__positional");
                if (args is List<object> pl && pl.Count > 0) raw = pl[0];
            }
            // Read the DOUBLE straight to long. Going through Num()'s float loses the low bits
            // of a 32-bit ARGB value — a float's 24-bit mantissa cannot hold 0xFF4A90D9, and the
            // damage lands on the low byte, so every colour came back with blue = 0.
            long v = raw is double dv ? (long)dv : (long)Num(raw);
            return new Color(((v >> 16) & 0xFF) / 255f, ((v >> 8) & 0xFF) / 255f,
                             (v & 0xFF) / 255f, ((v >> 24) & 0xFF) / 255f);
        }
    }
}

static class JsonExt
{
    public static Dictionary<string, object> AsDict(this object o) => o as Dictionary<string, object>;
    public static Dictionary<string, object> GetDict(this Dictionary<string, object> d, string k)
        => d.GetValueOrDefault(k) as Dictionary<string, object> ?? new();
    public static List<object> GetList(this Dictionary<string, object> d, string k)
        => d.GetValueOrDefault(k) as List<object> ?? new();
}
