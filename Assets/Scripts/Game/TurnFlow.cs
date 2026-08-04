using System.Collections.Generic;
using UnityEngine;
using ArmedConflict.Data;

namespace ArmedConflict.Game
{
    /// <summary>
    /// Fourth slice of the GameViewModel port: phase transitions, volley completion, and the
    /// win/loss resolution.
    ///
    /// STRICT ALTERNATING TURNS is a locked design decision — a side's whole roster fires as one
    /// volley, and the handover waits for that volley to finish rather than for a timer. The
    /// gating conditions here are what make that true.
    /// </summary>
    public static class TurnFlow
    {
        public const float EnemyWindupSeconds = 1.5f;

        /// <summary>
        /// A beat after the last round lands, before the turn changes hands. Without it the
        /// handover treads on the impact the player is still reading.
        /// </summary>
        public const float PostVolleyPauseSeconds = 1.6f;

        /// <summary>What the volley is currently waiting on, if anything.</summary>
        public enum VolleyGate
        {
            /// <summary>Rounds still in the air, or a door gunner / melee still resolving.</summary>
            Busy,
            /// <summary>The volley just emptied this tick — start the post-volley pause.</summary>
            JustLanded,
            /// <summary>Pause running down.</summary>
            Pausing,
            /// <summary>Clear to hand over.</summary>
            ReadyToHandOver,
        }

        /// <summary>
        /// Decides what the volley is waiting on.
        ///
        /// The gunnerBusy and skirmish conditions come FIRST and block everything: a helicopter
        /// door gunner mid-burst, or a melee skirmish still playing out, must finish before the
        /// turn can change hands — otherwise the handover cuts them off mid-action.
        /// </summary>
        public static VolleyGate EvaluateVolley(
            int projectilesNow, int projectilesBefore,
            float handoverDelay,
            TurnSide turnSide, int heliBurstsLeft, int skirmishCount)
        {
            bool gunnerBusy = turnSide == TurnSide.Enemy && heliBurstsLeft > 0;
            if (gunnerBusy || skirmishCount > 0) return VolleyGate.Busy;
            if (projectilesNow > 0) return VolleyGate.Busy;
            if (projectilesBefore > 0) return VolleyGate.JustLanded;
            if (handoverDelay > 0f) return VolleyGate.Pausing;
            return VolleyGate.ReadyToHandOver;
        }

        /// <summary>
        /// Win/loss. The outpost — and every other structure — is a DAMAGE OBJECTIVE, not a win
        /// condition: victory is purely about wiping out the enemy roster. This is also why the
        /// campaign composition rules insist most of the enemy roster be garrisoned on
        /// structures; otherwise the unit-kill condition resolves before the structures matter
        /// and their HP is irrelevant (measured: one level won in three volleys with its
        /// structures still at 238/340).
        /// </summary>
        public static GamePhase ResolvePhase(int playerUnitCount, int enemyUnitCount)
        {
            if (playerUnitCount == 0) return GamePhase.Defeat;
            if (enemyUnitCount == 0) return GamePhase.Victory;
            return GamePhase.Playing;
        }

        /// <summary>
        /// Stars from the fraction of the STARTING roster still alive. Thresholds are
        /// deliberately readable — "lose a quarter, lose half" — because the replay loop is
        /// chasing a cleaner win, not decoding a formula.
        /// </summary>
        public static int StarsFor(int survivors, int initialCount)
        {
            if (initialCount <= 0) return 1;
            float fraction = (float)survivors / initialCount;
            if (fraction >= 0.75f) return 3;
            if (fraction >= 0.4f) return 2;
            return 1;
        }

        public class VictoryAward
        {
            public int Stars;
            public int Coins;
            public string BonusTag;
        }

        /// <summary>
        /// Runs the victory award once, on the Playing -> Victory edge.
        ///
        /// ORDER IS LOAD BEARING: previous best is read and the payout granted BEFORE
        /// RecordStars overwrites it, because previousBestStars is what decides the one-time
        /// first-clear and first-3-star bonuses. Recording first would make every clear look
        /// like a repeat and silently stop paying them.
        ///
        /// The daily bonus overwrites the tag rather than appending, matching the Kotlin — only
        /// one banner is shown, and "Daily Bonus!" is the one the player has not seen before.
        /// </summary>
        public static VictoryAward AwardVictory(LevelDefinitionSO level, int survivors, int initialCount)
        {
            int stars = StarsFor(survivors, initialCount);
            int previousBest = ProgressStore.BestStars(level.id);

            var payout = EconomyStore.GrantVictoryPayout(level, stars, previousBest);
            ProgressStore.RecordStars(level.id, stars);

            var award = new VictoryAward
            {
                Stars = stars,
                Coins = payout.Coins,
                BonusTag = payout.FirstClear ? "First Clear!"
                         : payout.First3Star ? "New 3★ Best!"
                         : null,
            };

            int daily = EconomyStore.GrantDailyBonusIfAvailable();
            if (daily > 0)
            {
                award.Coins += daily;
                award.BonusTag = "Daily Bonus!";
            }

            foreach (var m in EconomyStore.CheckMilestones()) award.Coins += m.Coins;

            return award;
        }

        /// <summary>Defeat pays a consolation so a loss still feels like income.</summary>
        public static int AwardDefeat(LevelDefinitionSO level) => EconomyStore.GrantDefeatPayout(level);
    }
}
