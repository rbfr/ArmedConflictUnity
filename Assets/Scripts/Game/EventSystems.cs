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

        // ---- reinforcement waves --------------------------------------------------------

        /// <summary>
        /// ARRIVE and TELEGRAPH are mutually exclusive for a single wave, since arrivesOnTurn is
        /// one fixed turn number. The telegraph lands one turn EARLY so the player can react —
        /// a wave that appeared with no warning would read as the game cheating.
        /// </summary>
        public enum WaveTriggerBeat { None, Telegraph, Arrive }

        public static WaveTriggerBeat ReinforcementWaveBeat(int arrivesOnTurn, int turnNumber)
        {
            if (arrivesOnTurn == turnNumber) return WaveTriggerBeat.Arrive;
            if (arrivesOnTurn == turnNumber + 1) return WaveTriggerBeat.Telegraph;
            return WaveTriggerBeat.None;
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
        /// </summary>
        public static string WindShiftAnnouncement(float before, float after, bool gustUp)
        {
            if (Mathf.Approximately(before, after)) return null;
            return gustUp ? "🌬️ Wind rising →" : "🌬️ Wind falling ←";
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
