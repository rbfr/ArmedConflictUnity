using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using ArmedConflict.Data;

namespace ArmedConflict.Game
{
    /// <summary>
    /// Eighth and final slice of the GameViewModel port: the mid-battle events layer — boss
    /// phases, reinforcement waves and wind shifts.
    ///
    /// These are what stop a battle being a static exchange of volleys, and each one is pure
    /// turn arithmetic or a pure clamp, deliberately kept free of state mutation so it can be
    /// reasoned about (and tested) on its own.
    /// </summary>
    public static class EventSystems
    {
        public const float BossAnnouncementSeconds = 2.5f;
        public const int BossWaveIdBase = 4000;
        public const int ReinforcementWaveIdBase = 5000;

        // ---- boss phases ----------------------------------------------------------------

        /// <summary>
        /// Whether a trigger structure counts as DEFEATED.
        ///
        /// Two ways, and the second is the one that matters: destroyed outright, OR — if it was
        /// garrisoned at level start — no longer holding any of its garrison. Without the second
        /// clause a player who cleared the defenders off a boss structure but left the masonry
        /// standing would never trigger the phase, and a full playtest could clear the level with
        /// both boss structures intact and the encounter never firing.
        /// </summary>
        public static bool IsTriggerDefeated(string levelStructureId,
                                             IReadOnlyDictionary<string, int> runtimeIdByLevelId,
                                             ICollection<int> destroyedEver,
                                             LevelDefinitionSO level,
                                             IReadOnlyList<UnitEntity> enemyUnits)
        {
            if (!runtimeIdByLevelId.TryGetValue(levelStructureId, out int runtimeId)) return false;
            if (destroyedEver.Contains(runtimeId)) return true;

            bool wasGarrisoned = level.enemyGroups.Any(g => g.standingOnStructureId == levelStructureId);
            if (!wasGarrisoned) return false;
            return !enemyUnits.Any(u => u.StandingOnStructureId == runtimeId);
        }

        /// <summary>
        /// Whether phase `index` should fire now. Requires a NON-EMPTY trigger set whose every
        /// member is defeated — an empty set would otherwise be vacuously true and fire the
        /// phase on the first tick of the battle.
        /// </summary>
        public static bool ShouldTriggerBossPhase(int index,
                                                  BossPhaseTrigger trigger,
                                                  ICollection<int> alreadyTriggered,
                                                  System.Func<string, bool> isDefeated)
        {
            if (alreadyTriggered.Contains(index)) return false;
            if (trigger.triggerStructureIds == null || trigger.triggerStructureIds.Count == 0) return false;
            return trigger.triggerStructureIds.All(id => isDefeated(id));
        }

        /// <summary>
        /// TELEGRAPH, DON'T BLINDSIDE — pillar 7, which until 2026-09-04 the biggest arrival in
        /// the game was the only thing exempt from. A four-man reinforcement squad is not allowed
        /// to opt out of a warning (see `ReinforcementWaveBeat`); a 260 hp Sovereign and a heavy
        /// escort arrived with none.
        ///
        /// A boss cannot borrow the waves' countdown. A wave has `arrivesOnTurn`, so "2 turns"
        /// is a fact; a boss fires when a STRUCTURE FALLS, and the player owns that clock. What
        /// can honestly be warned is PROXIMITY TO THE TRIGGER: once the structure gating the
        /// phase is nearly down, the arrival is imminent in the only sense that exists here. So
        /// this is a health threshold and the line carries NO number.
        ///
        /// <paramref name="triggerHealthFraction"/> is the fraction of the phase's REMAINING
        /// gate — the trigger that is furthest from dying, since the phase waits for the LAST of
        /// them. At 0 the gate is already down and the phase fires this tick anyway, so a
        /// warning would arrive with the thing it was warning about; that is not a telegraph and
        /// is excluded rather than clamped.
        /// </summary>
        public const float DefaultBossTelegraphFraction = 0.5f;

        public static bool ShouldTelegraphBossPhase(int index,
                                                    BossPhaseTrigger trigger,
                                                    ICollection<int> alreadyTriggered,
                                                    float triggerHealthFraction)
        {
            if (alreadyTriggered.Contains(index)) return false;
            if (trigger == null || string.IsNullOrEmpty(trigger.telegraphLabel)) return false;
            if (trigger.triggerStructureIds == null || trigger.triggerStructureIds.Count == 0)
                return false;
            float at = trigger.telegraphAtHealthFraction <= 0f
                ? DefaultBossTelegraphFraction
                : trigger.telegraphAtHealthFraction;
            return triggerHealthFraction > 0f && triggerHealthFraction <= at;
        }

        // ---- reinforcement waves --------------------------------------------------------

        /// <summary>
        /// ARRIVE and TELEGRAPH are mutually exclusive for a single wave, since arrivesOnTurn is
        /// one fixed turn number. The telegraph runs for the `leadTurns` turns BEFORE arrival so
        /// the player can react — a wave that appeared with no warning would read as the game
        /// cheating.
        ///
        /// The lead is per wave rather than fixed at one, because how long a warning is worth
        /// depends on what is coming: a four-man squad is a turn's problem, and a column that
        /// changes what you should be shooting at needs long enough to change your mind and still
        /// have a volley left to act on it (`DYNAMISM_DESIGN.md` Phase B: "armor column inbound —
        /// 2 turns"). A lead below 1 is meaningless and is clamped, not honoured: pillar 7 is
        /// "telegraph, don't blindside", and a wave is not allowed to opt out of it.
        /// </summary>
        public enum WaveTriggerBeat { None, Telegraph, Arrive }

        public const int DefaultTelegraphLeadTurns = 1;

        public static WaveTriggerBeat ReinforcementWaveBeat(int arrivesOnTurn, int turnNumber,
                                                            int leadTurns = DefaultTelegraphLeadTurns)
        {
            if (arrivesOnTurn == turnNumber) return WaveTriggerBeat.Arrive;
            int away = arrivesOnTurn - turnNumber;
            if (away >= 1 && away <= Mathf.Max(1, leadTurns)) return WaveTriggerBeat.Telegraph;
            return WaveTriggerBeat.None;
        }

        /// <summary>
        /// The telegraph line for a wave `turnsAway` turns out — the authored LABEL plus a live
        /// countdown.
        ///
        /// The count is composed here rather than authored into the label because a multi-turn
        /// lead makes it change every turn: a label reading "inbound - 2 turns" for the whole
        /// warning is worse than no number at all, since it tells the player the clock has
        /// stopped. Authoring the number also puts it a copy-paste away from disagreeing with
        /// `arrivesOnTurn`, which nothing would catch.
        ///
        /// ASCII ONLY, deliberately — the default TMP font asset has no glyph for an em dash and
        /// renders one as a silent missing-glyph box. The one telegraph the game shipped with had
        /// exactly that bug (`Heavy support inbound — 1 turn`).
        /// </summary>
        public static string TelegraphLine(string label, int turnsAway)
        {
            if (string.IsNullOrEmpty(label)) return null;
            if (turnsAway < 1) return label;
            return $"{label} - {turnsAway} {(turnsAway == 1 ? "turn" : "turns")}";
        }

        // ---- wind ------------------------------------------------------------------------

        public const float WindShiftChance = 0.35f;
        public const float WindShiftStep = 0.35f;
        public const float WindShiftMinFrac = 0.4f;
        public const float WindShiftMaxFrac = 1.8f;
        public const float WindAnnouncementSeconds = 2.5f;

        /// <summary>
        /// Gusts the current wind by one step, clamped to a band around the level's BASE wind.
        ///
        /// The sign always follows the base, so a level's wind never reverses direction
        /// mid-battle — it only strengthens or weakens. A reversal would invalidate every shot
        /// the player had already learned to compensate for, which is the difference between a
        /// mechanic and a punishment.
        ///
        /// A level with no wind (base 0) never gusts at all.
        /// </summary>
        public static float NextWindAccelZ(float current, float baseWind, bool gustUp,
                                           float step = WindShiftStep,
                                           float minFrac = WindShiftMinFrac,
                                           float maxFrac = WindShiftMaxFrac)
        {
            if (Mathf.Approximately(baseWind, 0f)) return current;
            float delta = gustUp ? step : -step;
            float minMag = Mathf.Abs(baseWind) * minFrac;
            float maxMag = Mathf.Abs(baseWind) * maxFrac;
            float newMag = Mathf.Clamp(Mathf.Abs(current) + delta, minMag, maxMag);
            return Mathf.Sign(baseWind) * newMag;
        }

        /// <summary>
        /// The banner for a gust, or null when the wind did not actually move — already clamped
        /// at the edge of its band. Announcing a change that did not happen trains the player to
        /// ignore the banner.
        ///
        /// ASCII, like every other string that reaches TMP. The Kotlin wrote these with a wind
        /// emoji and a directional arrow; the default font asset has a glyph for none of them and
        /// would have drawn three missing-glyph boxes the day wind was wired up.
        /// </summary>
        public static string WindShiftAnnouncement(float before, float after, bool gustUp)
        {
            if (Mathf.Approximately(before, after)) return null;
            return gustUp ? "Wind rising >>" : "Wind falling <<";
        }

        // ---- announcement timers ---------------------------------------------------------

        /// <summary>
        /// Runs an announcement timer down, clearing the text when it expires. Returns whether
        /// the banner should still be shown.
        /// </summary>
        public static bool TickAnnouncement(ref float timer, float dt)
        {
            timer = Mathf.Max(timer - dt, 0f);
            return timer > 0f;
        }
    }
}
