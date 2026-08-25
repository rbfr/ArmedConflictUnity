using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using ArmedConflict.Data;

namespace ArmedConflict.Game
{
    /// <summary>One unit type and how many of it the player is fielding.</summary>
    public readonly struct Pick
    {
        public readonly UnitDefinitionSO Unit;
        public readonly int Count;
        public Pick(UnitDefinitionSO unit, int count) { Unit = unit; Count = count; }
    }

    /// <summary>
    /// The pre-battle squad: which ground troops the player brings, within the level's slots and
    /// points. See RosterDefinitionSO for why those two are separate.
    ///
    /// GARRISONED PLAYER GROUPS ARE NEVER TOUCHED. The tank crew stands on a structure at a fixed
    /// anchor and is part of the level's geometry; replacing it would move a garrison rather than
    /// pick a squad, and it counts against the locked 7-30 scale either way.
    ///
    /// Engine-independent and pure — every function here is a value in, a value out, so the whole
    /// thing is testable without a scene.
    /// </summary>
    public static class Loadout
    {
        /// <summary>
        /// How many ground troops the level was AUTHORED with. This is the slot count, and it is
        /// deliberately read from the level rather than configured: composition rule 1 measures
        /// the player line's width, the aiming camera is framed on it, and the level was measured
        /// with exactly this many bodies.
        /// </summary>
        public static int Slots(LevelDefinitionSO level)
            => level.playerGroups
                .Where(g => string.IsNullOrEmpty(g.standingOnStructureId))
                .Sum(g => g.count);

        /// <summary>
        /// The x the authored ground squad is centred on.
        ///
        /// A single contiguous line is the count-weighted mean of its groups — that is the
        /// centre ToPlayerGroups tiles around, and every campaign level is one of those.
        /// Two DISJOINT flanks must not be averaged: the mean lands in the gap, and the
        /// gap is where this game puts the scenery. LevelNaturalParadeTest's scale-reference
        /// groups at ±5.6 averaged to 0, dead centre of RidgeWatchtower's box.
        ///
        /// The test is the OUTPUT, not a gap heuristic. Clustering by spacing would have
        /// to be wide enough to keep TowerAssault's three authored ranks as one line
        /// (edge gap ~3.8 spacings) and still refuse a 11-unit parade split; the next
        /// authored line that sat a little looser would silently pick a new centre. If
        /// the mean sits inside a structure's collision box — the same box CollisionSystem
        /// uses — it is the gap trap, and the largest authored flank is the line.
        /// Those flanks may brush scenery; that is where they were placed.
        /// </summary>
        public static float GroundAnchorX(LevelDefinitionSO level)
        {
            var ground = level.playerGroups
                .Where(g => string.IsNullOrEmpty(g.standingOnStructureId)).ToList();
            if (ground.Count == 0) return -7f;
            int bodies = Mathf.Max(1, ground.Sum(g => g.count));
            float mean = ground.Sum(g => g.anchorX * g.count) / bodies;
            if (!IsGapTrap(level, ground, mean)) return mean;

            // The authored flanks are the answer — they may sit next to scenery
            // (the parade's scale-reference groups brush the cliff and the bunker).
            // Filtering them by the same box test throws both away and returns the
            // mean we just rejected.
            EnemyGroup pick = ground[0];
            foreach (var g in ground)
            {
                if (g.count > pick.count
                    || (g.count == pick.count && g.anchorX < pick.anchorX))
                    pick = g;
            }
            return pick.anchorX;
        }

        /// <summary>
        /// The mean of two flanks landed on the scenery, not on a line.
        ///
        /// Primary: inside an enemy collision box — EXACTLY CollisionSystem's box
        /// (hitWidth when set, else size; no worldScale).
        /// Fallback: closer to an enemy structure's anchor than to any ground group.
        /// The fallback does not need a resolved definition, so a missing hitWidth
        /// cannot hide the trap the way a box-only test can.
        /// </summary>
        static bool IsGapTrap(LevelDefinitionSO level, List<EnemyGroup> ground, float x)
        {
            float toGroup = float.PositiveInfinity;
            foreach (var g in ground)
                toGroup = Mathf.Min(toGroup, Mathf.Abs(x - g.anchorX));

            float toEnemy = float.PositiveInfinity;
            if (level.structures != null)
            {
                foreach (var s in level.structures)
                {
                    if (s == null) continue;
                    var def = s.definition;
                    if (def != null && def.isPlayerSide) continue;
                    if (def != null)
                    {
                        float halfW = (def.hasHitWidth ? def.hitWidth : def.size) / 2f;
                        if (Mathf.Abs(x - s.x) <= halfW) return true;
                    }
                    toEnemy = Mathf.Min(toEnemy, Mathf.Abs(x - s.x));
                }
            }
            return toEnemy < toGroup;
        }

        public static int PointsUsed(IReadOnlyList<Pick> picks, RosterDefinitionSO roster)
            => picks.Sum(p => p.Count * PointCost(p.Unit, roster));

        public static int UnitsUsed(IReadOnlyList<Pick> picks) => picks.Sum(p => p.Count);

        public static int PointCost(UnitDefinitionSO unit, RosterDefinitionSO roster)
            => roster.slots.FirstOrDefault(s => s.unit == unit)?.pointCost ?? 1;

        /// <summary>
        /// A loadout is legal when it fills no more than the slots and spends no more than the
        /// budget, and every unit in it is actually unlocked.
        ///
        /// UNDER-filling is legal on purpose. Fielding six good troops instead of eight ordinary
        /// ones is a real choice, and forbidding it would turn the budget back into a quantity
        /// rule. Fielding NOTHING is not — that is an empty battle, not a decision.
        /// </summary>
        public static bool IsLegal(IReadOnlyList<Pick> picks, LevelDefinitionSO level,
                                   RosterDefinitionSO roster, System.Func<string, bool> isUnlocked)
        {
            if (picks.Count == 0 || UnitsUsed(picks) <= 0) return false;
            if (UnitsUsed(picks) > Slots(level)) return false;
            if (PointsUsed(picks, roster) > Budget(level)) return false;
            return picks.All(p => p.Unit != null && isUnlocked(p.Unit.id));
        }

        /// <summary>
        /// The point budget. Falls back to the slot count when a level does not set one, which
        /// makes the fallback exactly "one cheap body per slot" — today's behaviour.
        /// </summary>
        public static int Budget(LevelDefinitionSO level)
            => level.deployBudget > 0 ? level.deployBudget : Slots(level);

        /// <summary>
        /// The authored ground mix, tank crew excluded. One pick per unit type, counts merged.
        ///
        /// This is the squad Begin is supposed to field — the mix the level was written around,
        /// not "N cheapest." Garrisoned groups stay out: they are geometry, not a pick.
        /// </summary>
        public static List<Pick> AuthoredPicks(LevelDefinitionSO level)
        {
            var picks = new List<Pick>();
            if (level?.playerGroups == null) return picks;
            foreach (var g in level.playerGroups)
            {
                if (g?.definition == null || g.count <= 0) continue;
                if (!string.IsNullOrEmpty(g.standingOnStructureId)) continue;
                int i = picks.FindIndex(p => p.Unit == g.definition);
                if (i >= 0) picks[i] = new Pick(g.definition, picks[i].Count + g.count);
                else picks.Add(new Pick(g.definition, g.count));
            }
            return picks;
        }

        /// <summary>
        /// The squad a player gets for pressing BEGIN without opening the picker: the level's
        /// authored ground mix. A locked specialist is swapped for the cheapest unlocked unit
        /// so Begin stays legal if the grant-on-encounter path did not run (pillar 8).
        ///
        /// After <see cref="EncounterUnlocks.GrantUnits"/> the substitution is a no-op and
        /// Begin fields exactly what the level authored.
        /// </summary>
        public static List<Pick> Default(LevelDefinitionSO level, RosterDefinitionSO roster,
                                         System.Func<string, bool> isUnlocked)
        {
            var cheapest = roster.slots
                .Where(s => s.unit != null && isUnlocked(s.unit.id))
                .OrderBy(s => s.pointCost)
                .FirstOrDefault();

            var authored = AuthoredPicks(level);
            if (authored.Count == 0)
            {
                if (cheapest == null) return new List<Pick>();
                int slots = Slots(level);
                int affordable = Mathf.Min(slots, Budget(level) / Mathf.Max(1, cheapest.pointCost));
                return affordable <= 0
                    ? new List<Pick>()
                    : new List<Pick> { new(cheapest.unit, affordable) };
            }

            var picks = new List<Pick>();
            int substitute = 0;
            foreach (var p in authored)
            {
                if (p.Unit != null && isUnlocked(p.Unit.id)) picks.Add(p);
                else substitute += p.Count;
            }
            if (substitute > 0 && cheapest != null)
            {
                int i = picks.FindIndex(p => p.Unit == cheapest.unit);
                if (i >= 0) picks[i] = new Pick(cheapest.unit, picks[i].Count + substitute);
                else picks.Insert(0, new Pick(cheapest.unit, substitute));
            }
            return picks;
        }

        /// <summary>
        /// Turns picks into the level's player groups: the untouched garrison groups, plus one
        /// ground group per pick.
        ///
        /// The ground groups are TILED so the whole squad occupies exactly the width the authored
        /// squad did, whatever the split. Placing every pick on the same anchor would stack them
        /// on top of each other; giving each a fixed spacing would make a three-type loadout wider
        /// than a one-type loadout, and rule 1 would start failing on the player's choices rather
        /// than on the level. Width is a property of the SLOT COUNT here, not of the picks.
        /// </summary>
        public static List<EnemyGroup> ToPlayerGroups(LevelDefinitionSO level,
                                                      IReadOnlyList<Pick> picks)
        {
            var groups = level.playerGroups
                .Where(g => !string.IsNullOrEmpty(g.standingOnStructureId))
                .ToList();

            int total = UnitsUsed(picks);
            if (total <= 0) return groups;

            // The level may pack its line tighter — see LevelDefinitionSO.playerSpacingScale.
            // Applied to the TILING here and to the intra-group cluster in LevelBuilder, because
            // both contribute to how wide the finished line is.
            float spacing = Formation.DefaultColumnSpacing
                          * Mathf.Max(level.playerSpacingScale, 0.05f);
            float centre = GroundAnchorX(level);
            float fullWidth = (Slots(level) - 1) * spacing;
            float cursor = centre - fullWidth / 2f;

            foreach (var p in picks)
            {
                if (p.Unit == null || p.Count <= 0) continue;
                float groupWidth = (p.Count - 1) * spacing;
                groups.Add(new EnemyGroup
                {
                    definition = p.Unit,
                    count = p.Count,
                    anchorX = cursor + groupWidth / 2f,
                    anchorZ = 0f,
                });
                cursor += p.Count * spacing;
            }
            return groups;
        }
    }
}
