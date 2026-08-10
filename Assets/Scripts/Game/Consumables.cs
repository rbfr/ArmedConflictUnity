using System.Collections.Generic;
using System.Linq;
using ArmedConflict.Data;

namespace ArmedConflict.Game
{
    /// <summary>
    /// The single-use battle items — `PROGRESSION_DESIGN.md` Phase 2, extended by
    /// `DYNAMISM_DESIGN.md` Phase C. Bought with coins, capped at TWO equipped per battle, and
    /// triggered from the battle HUD on the player's own turn.
    ///
    /// Each one reuses a system that already exists rather than adding a combat pipeline:
    /// Airstrike is a synthetic splash round (the off-roster pattern the tank shell already uses),
    /// Early Reinforcements is the relief squad fired on demand, Trauma Kit is a clamped heal, and
    /// Smoke Screen doubles EnemyAI's existing jitter-radius knob for exactly one volley.
    ///
    /// **This is a static catalog, not a ScriptableObject, which is a deliberate difference from
    /// ammo.** `AmmoCatalogSO` is an asset because ammo arrived through the Kotlin data import and
    /// is wired into the scene; consumables were never exported, so an SO would buy a scene rebuild
    /// and a new serialized field in exchange for nothing. The prices and descriptions are still
    /// data in a Definition class, which is what the convention actually asks for.
    ///
    /// **OVERWATCH FLARE IS ABSENT ON PURPOSE.** `ConsumableType` still declares it and
    /// `EnemyAI.AdvanceBudget` is ported and correct, but nothing in this port ever advances an
    /// enemy — `UnitEntity.AdvanceRemaining` is read once, in `GameState.IsVisuallyIdle`, and
    /// written nowhere. An item that halves an advance that never happens is a 200-coin button the player
    /// cannot feel, which is the same call this project already made about wind: do not ship the
    /// telegraph until the thing itself does something. Adding it here is a ONE-LINE change the day
    /// advancing squads are ported, and `PortSelfTest` asserts it stays out until then.
    /// </summary>
    public static class Consumables
    {
        public const int MaxEquippedPerBattle = 2;

        public static readonly IReadOnlyList<ConsumableDefinition> All = new[]
        {
            new ConsumableDefinition
            {
                Type = ConsumableType.Airstrike,
                DisplayName = "Airstrike",
                ShortName = "Airstrike",
                CoinPrice = 250,
                Description = "One free splash round on your next volley",
                IsArmed = true,
            },
            new ConsumableDefinition
            {
                Type = ConsumableType.EarlyReinforcements,
                DisplayName = "Early Reinforcements",
                ShortName = "Reinforce",
                CoinPrice = 200,
                Description = "Call the relief squad now, on demand",
                IsArmed = false,
            },
            new ConsumableDefinition
            {
                Type = ConsumableType.TraumaKit,
                DisplayName = "Trauma Kit",
                ShortName = "Trauma Kit",
                CoinPrice = 150,
                Description = "Heal the front rank",
                IsArmed = false,
            },
            new ConsumableDefinition
            {
                Type = ConsumableType.SmokeScreen,
                DisplayName = "Smoke Screen",
                ShortName = "Smoke",
                CoinPrice = 200,
                Description = "Their next volley fires blind — much less accurate",
                IsArmed = true,
            },
        };

        public static ConsumableDefinition For(ConsumableType type)
            => All.FirstOrDefault(c => c.Type == type);

        /// <summary>How many of `type` this battle is carrying. Absent means none.</summary>
        public static int Equipped(GameState s, ConsumableType type)
            => s.LoadedConsumables != null && s.LoadedConsumables.TryGetValue(type, out int n) ? n : 0;

        /// <summary>Total items equipped, which is what the cap-of-two is measured against.</summary>
        public static int TotalEquipped(IReadOnlyDictionary<ConsumableType, int> loaded)
        {
            if (loaded == null) return 0;
            int total = 0;
            foreach (var kv in loaded) total += kv.Value;
            return total;
        }

        /// <summary>
        /// The equipped map with one of `type` removed. Returns a NEW dictionary — GameState's
        /// collections are shared between states and must never be mutated in place.
        /// </summary>
        public static IReadOnlyDictionary<ConsumableType, int> Decrement(
            IReadOnlyDictionary<ConsumableType, int> loaded, ConsumableType type)
        {
            var next = new Dictionary<ConsumableType, int>();
            if (loaded != null) foreach (var kv in loaded) next[kv.Key] = kv.Value;
            next.TryGetValue(type, out int n);
            if (n <= 1) next.Remove(type); else next[type] = n - 1;
            return next;
        }
    }
}
