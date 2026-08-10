using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using ArmedConflict.Data;

namespace ArmedConflict.Game
{
    /// <summary>
    /// What the single-use battle items actually DO — `PROGRESSION_DESIGN.md` Phase 2 and
    /// `DYNAMISM_DESIGN.md` Phase C. See `Consumables` for the catalog and for why Overwatch Flare
    /// is deliberately absent.
    ///
    /// Every function here is a STATE IN, STATE OUT value function with no side effects, which is
    /// what lets `PortSelfTest` fire each item and read the result without a scene. In particular
    /// **nothing here touches `ProgressStore`**: the permanent inventory spend is `BattleRunner`'s,
    /// because a `PlayerPrefs` write in a pure tick function would run on every test call and
    /// quietly drain the editor's own inventory.
    ///
    /// The two SHAPES, and the difference matters:
    /// - **Instant** (Trauma Kit, Early Reinforcements) resolve on the tap. Tap time IS use time,
    ///   so they decrement here.
    /// - **Armed** (Airstrike, Smoke Screen) set a flag and are consumed later, by the volley that
    ///   fires them. They must NOT decrement at arm time — the HUD button is gated on the equipped
    ///   count, and spending at arm time made the button vanish the instant it was tapped, with no
    ///   ARMED state to see and no way to change your mind. That was found on a device, not in a
    ///   test suite, and it is the one piece of this design that cost something.
    /// </summary>
    public static class ConsumableActions
    {
        /// <summary>
        /// Trauma Kit heals this much on each man of the front rank, clamped to his max HP.
        ///
        /// It is a fixed amount rather than a fraction on purpose: a fraction heals the toughest
        /// unit most, and the front rank is where the cheap bodies stand.
        /// </summary>
        public const int TraumaKitHeal = 12;
        /// <summary>How many men the kit reaches. The FRONT rank — the ones being shot at.</summary>
        public const int TraumaKitFrontRank = 4;

        /// <summary>
        /// The relief squad is this fraction of the roster the battle STARTED with, rounded up, so
        /// it scales with the level rather than being a flat number that is trivial on a big level
        /// and decisive on a small one.
        /// </summary>
        public const float ReinforcementSizeFraction = 0.25f;
        /// <summary>
        /// Reinforcement ids get their own band, well clear of player (1..~40) and enemy (1000+),
        /// because ragdoll and hit tracking key off raw ids and a collision there is silent.
        /// </summary>
        public const int ReinforcementIdBase = 500;

        /// <summary>
        /// Whether the player may trigger an item at all: his own turn, aiming, battle live.
        /// Shared by every use path AND by the HUD, so a button can never offer something the
        /// action would then refuse.
        /// </summary>
        public static bool CanUse(GameState s)
            => s != null && s.Phase == GamePhase.Playing
                         && s.TurnSide == TurnSide.Player
                         && s.TurnPhase == TurnPhase.Aiming;

        /// <summary>
        /// Trauma Kit: heals the front rank — the ground units standing CLOSEST TO THE ENEMY.
        ///
        /// Garrisoned units are excluded, the same "front" definition the relief squad uses. A man
        /// on a structure is not in the rank taking the volley, and healing him instead of the men
        /// who are is the wrong read every time.
        /// </summary>
        public static GameState UseTraumaKit(GameState s)
        {
            if (!CanUse(s)) return s;
            if (Consumables.Equipped(s, ConsumableType.TraumaKit) <= 0) return s;

            var frontIds = new HashSet<int>(
                s.PlayerUnits.Where(u => u.StandingOnStructureId == null)
                             .OrderByDescending(u => u.X)
                             .Take(TraumaKitFrontRank)
                             .Select(u => u.Id));

            var healed = s.PlayerUnits.Select(u =>
                frontIds.Contains(u.Id)
                    ? u with { Hp = Mathf.Min(u.Hp + TraumaKitHeal,
                                              u.Definition != null ? u.Definition.maxHp : u.Hp) }
                    : u).ToList();

            return s with
            {
                PlayerUnits = healed,
                LoadedConsumables = Consumables.Decrement(s.LoadedConsumables,
                                                          ConsumableType.TraumaKit),
            };
        }

        /// <summary>
        /// Early Reinforcements: calls the relief squad on demand.
        ///
        /// It shares `ReinforcementsSent` with the automatic low-roster trigger, so the relief
        /// squad is still ONE per battle whichever path fires it. Buying the item does not buy a
        /// second squad; it buys the squad NOW, before the losses that would have summoned it.
        /// </summary>
        public static GameState UseEarlyReinforcements(GameState s)
        {
            if (!CanUse(s)) return s;
            if (s.ReinforcementsSent) return s;
            if (Consumables.Equipped(s, ConsumableType.EarlyReinforcements) <= 0) return s;

            var squad = BuildReinforcementSquad(s);
            if (squad.Count == 0) return s;

            return s with
            {
                PlayerUnits = s.PlayerUnits.Concat(squad).ToList(),
                InitialPlayerCount = s.InitialPlayerCount + squad.Count,
                ReinforcementsSent = true,
                PlayerMarchInProgress = true,
                LoadedConsumables = Consumables.Decrement(s.LoadedConsumables,
                                                          ConsumableType.EarlyReinforcements),
            };
        }

        /// <summary>
        /// The relief squad itself: `ReinforcementSizeFraction` of the opening roster, formed up
        /// just ahead of the current front and entering as one BLOCK from off the player's edge,
        /// which is what `MarchTargetX` then walks them in from.
        ///
        /// The whole grid is shifted by one offset rather than each man being placed at the edge
        /// individually — a squad that enters in formation and holds it reads as a unit arriving.
        /// Spawned individually they read as stragglers.
        ///
        /// **They are built from the player's own commonest ground unit**, not from a hardcoded
        /// Rifleman as the Kotlin does. `BattleTick` has no asset table to look one up in, and
        /// giving it one would mean a new serialized reference and a scene rebuild for a squad the
        /// player already told us the shape of at the loadout screen. Relief arriving as more of
        /// what you brought is also the better read.
        /// </summary>
        public static List<UnitEntity> BuildReinforcementSquad(GameState s)
        {
            var ground = s.PlayerUnits.Where(u => u.StandingOnStructureId == null).ToList();
            if (s.PlayerUnits.Count == 0) return new List<UnitEntity>();

            int size = Mathf.CeilToInt(s.InitialPlayerCount * ReinforcementSizeFraction);
            if (size <= 0) return new List<UnitEntity>();

            // The commonest ground definition, falling back to whatever is left standing — a
            // garrison-only survivor still gets relief.
            var definition = ground
                .Where(u => u.Definition != null)
                .GroupBy(u => u.Definition)
                .OrderByDescending(g => g.Count())
                .Select(g => g.Key)
                .FirstOrDefault()
                ?? s.PlayerUnits.Select(u => u.Definition).FirstOrDefault(d => d != null);

            float rearX = s.PlayerUnits.Select(u => u.X)
                .Concat(s.Structures.Where(st => st.Definition != null
                                                 && st.Definition.isPlayerSide).Select(st => st.X))
                .DefaultIfEmpty(-8f).Min();
            float frontX = ground.Select(u => u.X).DefaultIfEmpty(rearX).Max();

            var slots = Formation.Grid(size, anchorX: frontX + 0.8f);
            float entryShift = (frontX + 0.8f) - (rearX - LevelBuilder.ReinforcementEnterDistance);

            var squad = new List<UnitEntity>(size);
            for (int i = 0; i < slots.Count; i++)
            {
                squad.Add(new UnitEntity(
                    Id: ReinforcementIdBase + i,
                    Definition: definition,
                    X: slots[i].x - entryShift,
                    Y: 0f,
                    Z: slots[i].y,
                    Hp: definition != null ? definition.maxHp : 1,
                    IsPlayerSide: true)
                {
                    MarchTargetX = slots[i].x,
                });
            }
            return squad;
        }

        /// <summary>
        /// Arms or disarms one of the two armed items. A toggle, never a spend — see the class
        /// comment. Arming is refused when none are equipped, but DISarming always works: an item
        /// already armed can always be put away.
        /// </summary>
        public static GameState ToggleArmed(GameState s, ConsumableType type)
        {
            if (!CanUse(s)) return s;
            switch (type)
            {
                case ConsumableType.Airstrike:
                    if (!s.AirstrikeArmed && Consumables.Equipped(s, type) <= 0) return s;
                    return s with { AirstrikeArmed = !s.AirstrikeArmed };
                case ConsumableType.SmokeScreen:
                    if (!s.SmokeScreenArmed && Consumables.Equipped(s, type) <= 0) return s;
                    return s with { SmokeScreenArmed = !s.SmokeScreenArmed };
                default:
                    return s;
            }
        }

        /// <summary>
        /// Equips this battle's selection — the loadout picker's BEGIN.
        ///
        /// **Over the cap equips NOTHING rather than a truncated pick.** Choosing which two of
        /// three to keep is the player's decision, and silently dropping one is the game taking it.
        /// The picker refuses the third tap anyway; this is the backstop for any other caller.
        /// </summary>
        public static IReadOnlyDictionary<ConsumableType, int> Equip(
            IReadOnlyDictionary<ConsumableType, int> selection)
        {
            if (selection == null) return new Dictionary<ConsumableType, int>();
            if (Consumables.TotalEquipped(selection) > Consumables.MaxEquippedPerBattle)
                return new Dictionary<ConsumableType, int>();
            var copy = new Dictionary<ConsumableType, int>();
            foreach (var kv in selection) if (kv.Value > 0) copy[kv.Key] = kv.Value;
            return copy;
        }
    }
}
