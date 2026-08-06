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

        /// <summary>The x the authored ground squad is centred on.</summary>
        public static float GroundAnchorX(LevelDefinitionSO level)
        {
            var ground = level.playerGroups
                .Where(g => string.IsNullOrEmpty(g.standingOnStructureId)).ToList();
            if (ground.Count == 0) return -7f;
            return ground.Sum(g => g.anchorX * g.count) / Mathf.Max(1, ground.Sum(g => g.count));
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
        /// The squad a player gets for pressing BEGIN without opening the picker: every slot
        /// filled with the cheapest unlocked unit.
        ///
        /// Pillar 8, "default paths cost nothing" — this reproduces exactly what every level
        /// fielded before the loadout existed, so a player who never touches the screen loses
        /// nothing and the levels stay balanced as authored.
        /// </summary>
        public static List<Pick> Default(LevelDefinitionSO level, RosterDefinitionSO roster,
                                         System.Func<string, bool> isUnlocked)
        {
            var cheapest = roster.slots
                .Where(s => s.unit != null && isUnlocked(s.unit.id))
                .OrderBy(s => s.pointCost)
                .FirstOrDefault();
            if (cheapest == null) return new List<Pick>();

            int slots = Slots(level);
            int affordable = Mathf.Min(slots, Budget(level) / Mathf.Max(1, cheapest.pointCost));
            return affordable <= 0
                ? new List<Pick>()
                : new List<Pick> { new(cheapest.unit, affordable) };
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

            const float spacing = Formation.DefaultColumnSpacing;
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
