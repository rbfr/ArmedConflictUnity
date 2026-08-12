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

        /// <summary>
        /// The fewest survivors that earns <paramref name="stars"/>.
        ///
        /// Deliberately derived by asking StarsFor rather than by re-deriving the thresholds:
        /// two copies of "0.75 and 0.4" drift, and this one is shown to the player as a promise
        /// ("keep 11 alive for 3★"). A promise the award code disagrees with is worse than no
        /// promise. Rosters are 7-30 units, so the scan is free.
        /// </summary>
        public static int SurvivorsFor(int stars, int initialCount)
        {
            for (int s = 0; s <= initialCount; s++)
                if (StarsFor(s, initialCount) >= stars) return s;
            return initialCount;
        }

        /// <summary>
        /// The one-line reason for a star result, in units the player can say out loud.
        ///
        /// PRODUCT_DIRECTION 0.5 asks for the reason on EVERY victory, and this costs nothing to
        /// honour: the star rule is pure roster survival, so the reason is always a count and the
        /// next threshold is always reachable by keeping N more alive. Nothing here is random or
        /// hidden, which is what "opaque or RNG-gated 3★ is a bug" is asking for.
        /// </summary>
        public static string StarReason(int survivors, int initialCount)
        {
            if (initialCount <= 0) return "";

            int stars = StarsFor(survivors, initialCount);
            if (stars >= 3) return $"Kept {survivors} of {initialCount} — a clean sweep";

            int need = SurvivorsFor(stars + 1, initialCount);
            int lost = initialCount - survivors;
            // "3 stars", not "3★". The default TMP font asset is built over ASCII, so U+2605
            // renders as a missing-glyph box — confirmed on the rendered card, not guessed. The
            // stars on the panel are drawn sprites for the same reason.
            return $"Lost {lost} of {initialCount} — keep {need} alive for {stars + 1} stars";
        }

        /// <summary>
        /// The banner for a star-milestone chest. Spelled out rather than drawn with U+2605,
        /// which the default TMP font lacks — see the note on BonusTag below.
        /// </summary>
        public static string MilestoneTag(int milestoneStar) => $"{milestoneStar}-Star Chest!";

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
        /// ONE BANNER, AND THE RAREST THING WINS IT. The tag is overwritten rather than appended,
        /// matching the Kotlin, so the order below is the priority order: a star-milestone chest
        /// (four of them in the whole campaign) outranks the daily bonus, which outranks a new
        /// 3-star best, which outranks a first clear. Each is rarer than the one under it.
        ///
        /// The MILESTONE used to set no tag at all — `CheckMilestones` added its coins to the
        /// total and said nothing, so a 150-600 coin chest arrived as a bigger number with no
        /// explanation. That is exactly what PRODUCT_DIRECTION 0.3 means by "victory screen is a
        /// feature, not a silent Next modal": the payout the player cannot account for teaches
        /// them nothing about why replaying pays.
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
                // NO STAR GLYPH. This read "New 3★ Best!" until 2026-08-12 and U+2605 is one of
                // the two symbols LiberationSans SDF does not have — PortSelfTest asserts its
                // absence to prove the glyph check can fail — so the banner drew a box on the
                // one screen that congratulates the player. The HUD's stars are sprites for this
                // reason; a composed string has no such escape and must spell the word.
                BonusTag = payout.FirstClear ? "First Clear!"
                         : payout.First3Star ? "New 3-Star Best!"
                         : null,
            };

            int daily = EconomyStore.GrantDailyBonusIfAvailable();
            if (daily > 0)
            {
                award.Coins += daily;
                award.BonusTag = "Daily Bonus!";
            }

            foreach (var m in EconomyStore.CheckMilestones())
            {
                award.Coins += m.Coins;
                award.BonusTag = MilestoneTag(m.MilestoneStar);
            }

            return award;
        }

        /// <summary>Defeat pays a consolation so a loss still feels like income.</summary>
        public static int AwardDefeat(LevelDefinitionSO level) => EconomyStore.GrantDefeatPayout(level);
    }
}
