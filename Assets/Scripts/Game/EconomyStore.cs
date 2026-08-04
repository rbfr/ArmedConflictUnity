using System.Collections.Generic;
using UnityEngine;
using ArmedConflict.Data;

namespace ArmedConflict.Game
{
    /// <summary>
    /// Port of Roster.kt's RosterEntry — the loadout picker's per-unit cost and upgrade path.
    /// </summary>
    public class RosterEntry
    {
        public UnitDefinitionSO Unit;
        public int PointCost;
        public int CoinPrice;
        public TierAxis TierAxis = TierAxis.HP;
        public IReadOnlyList<int> TierCosts = new[] { 150, 350, 700 };
    }

    public class AmmoDefinition
    {
        public AmmoType Type;
        public int CoinPrice;
    }

    public class ConsumableDefinition
    {
        public ConsumableType Type;
        public int CoinPrice;
    }

    public class CosmeticDefinition
    {
        public CosmeticSet Set;
        public int CoinPrice;
    }

    /// <summary>
    /// Port of EconomyStore.kt — the single grant/spend API for the rest of the app.
    /// ProgressStore is only its storage.
    /// </summary>
    public static class EconomyStore
    {
        public readonly struct VictoryPayout
        {
            public readonly int Coins;
            public readonly bool FirstClear;
            public readonly bool First3Star;

            public VictoryPayout(int coins, bool firstClear, bool first3Star)
            {
                Coins = coins;
                FirstClear = firstClear;
                First3Star = first3Star;
            }
        }

        public static int Balance() => ProgressStore.Coins();
        public static bool Spend(int amount) => ProgressStore.SpendCoins(amount);

        /// <summary>
        /// Call once on the Victory transition, BEFORE ProgressStore.RecordStars overwrites the
        /// previous best — previousBestStars is what decides the one-time first-clear and
        /// first-3-star bonuses. Calling it after would make every clear look like a repeat and
        /// silently stop paying them.
        /// </summary>
        public static VictoryPayout GrantVictoryPayout(LevelDefinitionSO level,
                                                       int starsEarned, int previousBestStars)
        {
            float starMultiplier = starsEarned switch
            {
                3 => 2.0f,
                2 => 1.5f,
                _ => 1.0f,
            };

            int coins = (int)(level.levelBase * starMultiplier);
            bool firstClear = previousBestStars == 0;
            bool first3Star = starsEarned == 3 && previousBestStars < 3;
            if (firstClear) coins += level.levelBase;
            if (first3Star) coins += (int)(level.levelBase * 1.5f);

            ProgressStore.AddCoins(coins);
            return new VictoryPayout(coins, firstClear, first3Star);
        }

        /// <summary>Defeat consolation — always pays, no bonuses, so a loss still feels like income.</summary>
        public static int GrantDefeatPayout(LevelDefinitionSO level)
        {
            int coins = (int)(level.levelBase * 0.15f);
            ProgressStore.AddCoins(coins);
            return coins;
        }

        /// <summary>One-time bonus for clearing a stage with no unit left to reward.</summary>
        public static int GrantStageCompletionBonus(int amount)
        {
            ProgressStore.AddCoins(amount);
            return amount;
        }

        public static bool PurchaseUnit(RosterEntry entry)
        {
            if (ProgressStore.IsUnitUnlocked(entry.Unit.id)) return false;
            if (!Spend(entry.CoinPrice)) return false;
            ProgressStore.UnlockUnit(entry.Unit.id);
            return true;
        }

        public static bool PurchaseTier(RosterEntry entry)
        {
            int currentTier = ProgressStore.TierOf(entry.Unit.id);
            if (currentTier >= entry.TierCosts.Count) return false;
            if (!Spend(entry.TierCosts[currentTier])) return false;
            ProgressStore.SetTier(entry.Unit.id, currentTier + 1);
            return true;
        }

        /// <summary>Unlock-once ammo purchase — free forever after.</summary>
        public static bool PurchaseAmmo(AmmoDefinition def)
        {
            if (ProgressStore.IsAmmoUnlocked(def.Type)) return false;
            if (!Spend(def.CoinPrice)) return false;
            ProgressStore.UnlockAmmo(def.Type);
            return true;
        }

        /// <summary>Unlock-once cosmetic purchase — free forever after.</summary>
        public static bool PurchaseCosmetic(CosmeticDefinition def)
        {
            if (ProgressStore.IsCosmeticUnlocked(def.Set)) return false;
            if (!Spend(def.CoinPrice)) return false;
            ProgressStore.UnlockCosmetic(def.Set);
            return true;
        }

        /// <summary>Consumables are inventory, not an unlock — repeat purchases stack.</summary>
        public static bool PurchaseConsumable(ConsumableDefinition def)
        {
            if (!Spend(def.CoinPrice)) return false;
            ProgressStore.AddConsumable(def.Type, 1);
            return true;
        }

        public readonly struct MilestoneReward
        {
            public readonly int MilestoneStar;
            public readonly int Coins;

            public MilestoneReward(int milestoneStar, int coins)
            {
                MilestoneStar = milestoneStar;
                Coins = coins;
            }
        }

        static readonly Dictionary<int, int> MilestoneCoins = new()
        {
            { 25, 150 }, { 50, 300 }, { 75, 450 }, { 100, 600 },
        };

        /// <summary>
        /// Star-milestone chests at 25/50/75/100 total campaign stars — turns grinding into a
        /// secondary progress bar. Call AFTER RecordStars has updated the total.
        ///
        /// Returns EVERY milestone crossed by this call, not just the highest: a single victory
        /// can vault past two thresholds, and paying only one would quietly swallow the other.
        /// ProgressStore.ClaimMilestone is the idempotency guard — it returns false for an
        /// already-claimed milestone, so coins are granted exactly once.
        /// </summary>
        public static List<MilestoneReward> CheckMilestones()
        {
            int totalStars = ProgressStore.TotalStars();
            var rewards = new List<MilestoneReward>();
            foreach (var kv in MilestoneCoins)
            {
                if (totalStars < kv.Key) continue;
                if (!ProgressStore.ClaimMilestone(kv.Key)) continue;
                ProgressStore.AddCoins(kv.Value);
                rewards.Add(new MilestoneReward(kv.Key, kv.Value));
            }
            return rewards;
        }

        /// <summary>
        /// Daily first-win bonus — a flat grant on the first victory of each calendar day.
        /// Deliberately not an energy system, which would punish the replay the star economy
        /// exists to encourage.
        /// </summary>
        public static int GrantDailyBonusIfAvailable()
        {
            if (!ProgressStore.IsDailyBonusAvailable()) return 0;
            const int bonus = 50;
            ProgressStore.AddCoins(bonus);
            ProgressStore.RecordDailyWin();
            return bonus;
        }
    }
}
