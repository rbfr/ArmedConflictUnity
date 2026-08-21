using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using ArmedConflict.Data;

namespace ArmedConflict.Game
{
    /// <summary>
    /// Port of ProgressStore.kt — persistent campaign progress: best stars per level, coins,
    /// unlocks, inventory.
    ///
    /// SharedPreferences becomes PlayerPrefs, the like-for-like replacement at this scale (a
    /// handful of small values, written once per victory). ONE real difference: PlayerPrefs has
    /// no string-set type, so sets are stored as a delimited string. The Kotlin's two
    /// getStringSet footguns — never write back a mutated view, never hand out the live
    /// platform-backed set — cannot occur here, because every read parses a fresh collection.
    ///
    /// Key names match the Kotlin. Nothing migrates between the two apps, but matching keys mean
    /// a save inspected side by side reads the same.
    /// </summary>
    public static class ProgressStore
    {
        /// <summary>ASCII unit separator — cannot occur in a level id, unit id or enum name.</summary>
        const char SetSep = '\u001F';

        const string KeyCoins = "coins";
        const string KeyUnlockedUnits = "unlocked_units";
        const string KeyUnlockedAmmo = "unlocked_ammo";
        const string KeySelectedAmmo = "selected_ammo";
        const string KeyAmmoPlayerPicked = "ammo_player_picked";
        const string KeyUnlockedCosmetics = "unlocked_cosmetics";
        const string KeySelectedCosmetic = "selected_cosmetic";
        const string KeyClaimedMilestones = "claimed_milestones";
        const string KeyLastWinDate = "last_win_date";

        /// <summary>
        /// Every level, for TotalStars(). Assigned once at startup from the imported assets —
        /// the Kotlin read a compile-time object, which Unity cannot.
        /// </summary>
        public static IReadOnlyList<LevelDefinitionSO> AllLevels { get; set; }
            = new List<LevelDefinitionSO>();

        // ---- stars ----------------------------------------------------------------------

        public static int BestStars(string levelId) => PlayerPrefs.GetInt($"stars_{levelId}", 0);

        /// <summary>Records a run's star result, keeping the best. True if it set a new best.</summary>
        public static bool RecordStars(string levelId, int stars)
        {
            if (stars <= BestStars(levelId)) return false;
            PlayerPrefs.SetInt($"stars_{levelId}", stars);
            PlayerPrefs.Save();
            return true;
        }

        // ---- fail streak ----------------------------------------------------------------

        public static int FailStreak(string levelId) => PlayerPrefs.GetInt($"fails_{levelId}", 0);

        /// <summary>Increments the consecutive-defeat count for this level. Returns the new streak.</summary>
        public static int RecordDefeat(string levelId)
        {
            int n = FailStreak(levelId) + 1;
            PlayerPrefs.SetInt($"fails_{levelId}", n);
            PlayerPrefs.Save();
            return n;
        }

        /// <summary>A win on the level ends the pattern. Next loss is a miss again, not a streak.</summary>
        public static void ClearFailStreak(string levelId)
        {
            PlayerPrefs.DeleteKey($"fails_{levelId}");
            PlayerPrefs.Save();
        }

        /// <summary>Dev-only TEST levels are excluded, so playing one can never unlock a stage.</summary>
        public static int TotalStars()
            => AllLevels.Where(l => l != null && !l.isTestLevel).Sum(l => BestStars(l.id));

        public static bool IsUnlocked(StageDefinitionSO stage) => TotalStars() >= stage.starsToUnlock;

        // ---- coins ----------------------------------------------------------------------

        public static int Coins() => PlayerPrefs.GetInt(KeyCoins, 0);

        public static void AddCoins(int amount)
        {
            if (amount == 0) return;
            PlayerPrefs.SetInt(KeyCoins, Coins() + amount);
            PlayerPrefs.Save();
        }

        public static bool SpendCoins(int amount)
        {
            int balance = Coins();
            if (amount > balance) return false;
            PlayerPrefs.SetInt(KeyCoins, balance - amount);
            PlayerPrefs.Save();
            return true;
        }

        // ---- units ----------------------------------------------------------------------

        public static ISet<string> UnlockedUnitIds() => ReadSet(KeyUnlockedUnits);

        /// <summary>Rifleman is always unlocked and is never stored.</summary>
        public static bool IsUnitUnlocked(string unitId)
            => unitId == "rifleman" || UnlockedUnitIds().Contains(unitId);

        public static void UnlockUnit(string unitId) => AddToSet(KeyUnlockedUnits, unitId);

        public static int TierOf(string unitId) => PlayerPrefs.GetInt($"tier_{unitId}", 0);

        public static void SetTier(string unitId, int tier)
        {
            PlayerPrefs.SetInt($"tier_{unitId}", tier);
            PlayerPrefs.Save();
        }

        // ---- consumables ----------------------------------------------------------------

        public static int OwnedConsumables(ConsumableType type)
            => PlayerPrefs.GetInt($"consumable_{type}", 0);

        public static void AddConsumable(ConsumableType type, int count)
        {
            if (count == 0) return;
            PlayerPrefs.SetInt($"consumable_{type}", OwnedConsumables(type) + count);
            PlayerPrefs.Save();
        }

        public static bool SpendConsumable(ConsumableType type)
        {
            int owned = OwnedConsumables(type);
            if (owned <= 0) return false;
            PlayerPrefs.SetInt($"consumable_{type}", owned - 1);
            PlayerPrefs.Save();
            return true;
        }

        // ---- ammo -----------------------------------------------------------------------

        public static ISet<string> UnlockedAmmo() => ReadSet(KeyUnlockedAmmo);

        public static bool IsAmmoUnlocked(AmmoType type)
            => type == AmmoType.Standard || UnlockedAmmo().Contains(type.ToString());

        public static void UnlockAmmo(AmmoType type) => AddToSet(KeyUnlockedAmmo, type.ToString());

        /// <summary>Falls back to Standard when the stored value is absent, unknown, or locked.</summary>
        public static AmmoType SelectedAmmo()
        {
            string stored = PlayerPrefs.GetString(KeySelectedAmmo, "");
            if (string.IsNullOrEmpty(stored)) return AmmoType.Standard;
            if (!System.Enum.TryParse(stored, out AmmoType type)) return AmmoType.Standard;
            return IsAmmoUnlocked(type) ? type : AmmoType.Standard;
        }

        public static void SetSelectedAmmo(AmmoType type)
        {
            PlayerPrefs.SetString(KeySelectedAmmo, type.ToString());
            PlayerPrefs.Save();
        }

        /// <summary>
        /// True once the player has tapped an ammo chip. Encounter grants pre-select only
        /// while this is false, so a gift never overwrites a choice.
        /// </summary>
        public static bool AmmoPickedByPlayer() => PlayerPrefs.GetInt(KeyAmmoPlayerPicked, 0) == 1;

        public static void MarkAmmoPickedByPlayer()
        {
            PlayerPrefs.SetInt(KeyAmmoPlayerPicked, 1);
            PlayerPrefs.Save();
        }

        // ---- cosmetics ------------------------------------------------------------------

        public static ISet<string> UnlockedCosmetics() => ReadSet(KeyUnlockedCosmetics);

        public static bool IsCosmeticUnlocked(CosmeticSet set)
            => set == CosmeticSet.Olive || UnlockedCosmetics().Contains(set.ToString());

        public static void UnlockCosmetic(CosmeticSet set)
            => AddToSet(KeyUnlockedCosmetics, set.ToString());

        /// <summary>
        /// The inverse. Nothing in the game takes a cosmetic away — it exists so a CHECK can put
        /// the store into the state its failure needs and then put it back.
        ///
        /// Without it the vanity check was unfalsifiable twice over: comparing a battle under
        /// Olive with one under a set the player does not own is comparing Olive with Olive,
        /// because <see cref="SelectedCosmetic"/> validates on read. It passed against a build
        /// where the camo really did buff damage 50%.
        /// </summary>
        public static void LockCosmetic(CosmeticSet set)
            => RemoveFromSet(KeyUnlockedCosmetics, set.ToString());

        public static CosmeticSet SelectedCosmetic()
        {
            string stored = PlayerPrefs.GetString(KeySelectedCosmetic, "");
            if (string.IsNullOrEmpty(stored)) return CosmeticSet.Olive;
            if (!System.Enum.TryParse(stored, out CosmeticSet set)) return CosmeticSet.Olive;
            return IsCosmeticUnlocked(set) ? set : CosmeticSet.Olive;
        }

        public static void SetSelectedCosmetic(CosmeticSet set)
        {
            PlayerPrefs.SetString(KeySelectedCosmetic, set.ToString());
            PlayerPrefs.Save();
        }

        // ---- milestones / daily ---------------------------------------------------------

        public static ISet<string> ClaimedMilestones() => ReadSet(KeyClaimedMilestones);

        public static bool ClaimMilestone(int milestoneStar)
        {
            string m = milestoneStar.ToString();
            if (ClaimedMilestones().Contains(m)) return false;
            AddToSet(KeyClaimedMilestones, m);
            return true;
        }

        public static bool IsDailyBonusAvailable()
            => PlayerPrefs.GetString(KeyLastWinDate, "") != Today();

        public static void RecordDailyWin()
        {
            PlayerPrefs.SetString(KeyLastWinDate, Today());
            PlayerPrefs.Save();
        }

        /// <summary>
        /// ISO date in LOCAL time, matching Kotlin's java.time.LocalDate.now(). Deliberately not
        /// UtcNow: a player near midnight would otherwise see the daily flip at the wrong hour.
        /// </summary>
        static string Today() => System.DateTime.Now.ToString("yyyy-MM-dd");

        // ---- set storage ----------------------------------------------------------------

        static ISet<string> ReadSet(string key)
        {
            var set = new HashSet<string>();
            string raw = PlayerPrefs.GetString(key, "");
            if (string.IsNullOrEmpty(raw)) return set;
            foreach (var part in raw.Split(SetSep))
                if (part.Length > 0) set.Add(part);
            return set;
        }

        static void AddToSet(string key, string value)
        {
            var set = ReadSet(key);
            if (!set.Add(value)) return;
            PlayerPrefs.SetString(key, string.Join(SetSep.ToString(), set));
            PlayerPrefs.Save();
        }

        static void RemoveFromSet(string key, string value)
        {
            var set = ReadSet(key);
            if (!set.Remove(value)) return;
            PlayerPrefs.SetString(key, string.Join(SetSep.ToString(), set));
            PlayerPrefs.Save();
        }

        /// <summary>Test hook — wipes every key this store owns.</summary>
        public static void ResetAll()
        {
            foreach (var l in AllLevels)
            {
                if (l == null) continue;
                PlayerPrefs.DeleteKey($"stars_{l.id}");
                PlayerPrefs.DeleteKey($"fails_{l.id}");
            }
            foreach (var k in new[]
            {
                KeyCoins, KeyUnlockedUnits, KeyUnlockedAmmo, KeySelectedAmmo, KeyAmmoPlayerPicked,
                KeyUnlockedCosmetics, KeySelectedCosmetic, KeyClaimedMilestones, KeyLastWinDate,
            }) PlayerPrefs.DeleteKey(k);
            foreach (ConsumableType t in System.Enum.GetValues(typeof(ConsumableType)))
                PlayerPrefs.DeleteKey($"consumable_{t}");
            PlayerPrefs.Save();
        }
    }
}
