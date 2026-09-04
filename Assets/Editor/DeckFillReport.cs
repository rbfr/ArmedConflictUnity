using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using ArmedConflict.Data;
using ArmedConflict.Game;

/// <summary>
/// Measures how much of each garrisoned deck its garrison actually occupies, and sweeps what a
/// hypothetical structure shrink would do to that. Written to put numbers on the Tier 2.2 crowd
/// question in UNIT_VARIETY_DESIGN.md — it MEASURES, it never edits an asset.
///
///     -batchmode -quit -executeMethod DeckFillReport.Run
/// </summary>
public static class DeckFillReport
{
    // Formation owns the body width now, so the layout, the overlap check and this report
    // cannot disagree about the size of a man.
    static readonly float Body = Formation.BodyWidth;

    static readonly float[] Sweep = { 1f, 0.80f, 0.6234f, 0.50f, 0.40f, 0.30f };

    struct Deck
    {
        public int Level; public string Name; public float Width; public int Count; public float Pitch;
        public float AnchorX, AnchorZ, CenterX;
    }

    public static void Run()
    {
        var decks = Collect();

        Debug.Log($"[DeckFill] body {Body:F3}, mounted pitch {Formation.MountedColumnSpacing:F3}");
        Debug.Log("[DeckFill] --- as authored today ---");
        Debug.Log("[DeckFill] level | structure | deck | garrison | span | fill% | seats/rank");
        foreach (var d in decks)
        {
            var (span, fill, ranks) = Measure(d, 1f);
            Debug.Log($"[DeckFill] L{d.Level,-2} | {d.Name,-20} | {d.Width:F2} | {d.Count,2} " +
                      $"| {span:F2} | {fill,5:F0}% | {SeatsPerRank(d),3}   ({ranks} rank(s))");
        }

        // The decisive number: how WIDE a deck would have to be for this garrison to fill it,
        // against how wide the structure actually is. The reference game runs ~15 per rank, and
        // our decks already SEAT about that — so a deck that reads as empty is a roster gap, not
        // a geometry one, and shrinking the building is shrinking the wrong term.
        Debug.Log("[DeckFill] --- what a FILLED deck would require ---");
        foreach (var d in decks)
        {
            float need = Measure(d, 1f).span / 0.75f;      // 75% fill = a full rank with air
            Debug.Log($"[DeckFill] L{d.Level,-2} {d.Name,-20} deck {d.Width:F2} -> needs " +
                      $"{need:F2} (x{need / d.Width:F2}) for {d.Count} men, " +
                      $"or {Mathf.RoundToInt(d.Width * 0.75f / (d.Pitch)) * 2,3} men at this width");
        }

        Debug.Log("[DeckFill] --- sweep: fill% per structure scale factor ---");
        Debug.Log("[DeckFill] " + "level/structure".PadRight(26) +
                  string.Join("", Sweep.Select(s => $"x{s:F2}".PadLeft(8))));
        foreach (var d in decks)
            Debug.Log($"[DeckFill] {("L" + d.Level + " " + d.Name),-26}" +
                      string.Join("", Sweep.Select(s => $"{Measure(d, s).fill:F0}%".PadLeft(8))));

        foreach (var s in Sweep)
        {
            var fills = decks.Select(d => Measure(d, s).fill).ToList();
            int rankChanges = decks.Count(d => Measure(d, s).ranks != Measure(d, 1f).ranks);
            Debug.Log($"[DeckFill] x{s:F2}: fill median {Median(fills):F0}% " +
                      $"(min {fills.Min():F0}%, max {fills.Max():F0}%), " +
                      $"{rankChanges} deck(s) change rank count, " +
                      $"widest deck {decks.Max(d => d.Width) * s:F2}");
        }
    }

    static (float span, float fill, int ranks) Measure(Deck d, float scale)
    {
        float width = d.Width * scale;
        var laid = Formation.Mounted(d.Count, d.AnchorX, width, d.AnchorZ, d.Pitch, d.CenterX);
        float span = laid.Max(p => p.x) - laid.Min(p => p.x) + Body * (d.Pitch / Formation.MountedColumnSpacing);
        int ranks = laid.Select(p => Mathf.Round(p.y * 1000f)).Distinct().Count();
        return (span, 100f * span / width, ranks);
    }

    /// <summary>What the deck seats in ONE rank at the reference-derived pitch, bodies touching.</summary>
    static int SeatsPerRank(Deck d) => Mathf.FloorToInt(d.Width / d.Pitch) + 1;

    static float Median(List<float> v)
    {
        var s = v.OrderBy(x => x).ToList();
        return s.Count == 0 ? 0f : s[s.Count / 2];
    }

    static List<Deck> Collect()
    {
        var outp = new List<Deck>();
        var levels = AssetDatabase.FindAssets("t:LevelDefinitionSO")
            .Select(AssetDatabase.GUIDToAssetPath)
            .Select(AssetDatabase.LoadAssetAtPath<LevelDefinitionSO>)
            .Where(l => l != null && !l.isTestLevel)
            .OrderBy(l => l.levelNumber);

        foreach (var level in levels)
        {
            var byId = new Dictionary<string, StructurePlacement>();
            foreach (var s in level.structures)
                if (s.definition != null && !string.IsNullOrEmpty(s.id)) byId[s.id] = s;

            foreach (var deck in level.enemyGroups
                         .Where(g => !string.IsNullOrEmpty(g.standingOnStructureId) && g.count > 0)
                         .GroupBy(g => g.standingOnStructureId))
            {
                if (!byId.TryGetValue(deck.Key, out var p) || p.definition == null) continue;
                float weight = deck.Sum(g => (float)g.count);
                outp.Add(new Deck
                {
                    Level = level.levelNumber,
                    Name = p.definition.name,
                    Width = p.hasStandWidth ? p.standWidth : p.definition.standWidth,
                    Count = deck.Sum(g => g.count),
                    Pitch = Formation.MountedColumnSpacing *
                            deck.Max(g => g.definition != null ? g.definition.renderScale : 1f),
                    AnchorX = deck.Sum(g => g.anchorX * g.count) / weight,
                    AnchorZ = deck.Sum(g => g.anchorZ * g.count) / weight + p.definition.deckStandZOffset,
                    CenterX = p.x,
                });
            }
        }
        return outp;
    }
}
