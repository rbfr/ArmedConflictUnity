using ArmedConflict.Data;

namespace ArmedConflict.Game
{
    /// <summary>
    /// Variety the player gets without opening the shop: the level's authored specialists
    /// unlock when they reach it, and AP / Incendiary arrive after the fights that teach
    /// them. Shopping is then "more of a thing I already fired," not a gate on the toolbox.
    /// </summary>
    public static class EncounterUnlocks
    {
        /// <summary>
        /// Unlocks every ground unit the level authors. Rifleman is already free. Safe to
        /// call more than once — <see cref="ProgressStore.UnlockUnit"/> is idempotent.
        /// </summary>
        public static void GrantUnits(LevelDefinitionSO level)
        {
            if (level == null) return;
            foreach (var p in Loadout.AuthoredPicks(level))
            {
                if (p.Unit == null || string.IsNullOrEmpty(p.Unit.id)) continue;
                if (p.Unit.id == "rifleman") continue;
                ProgressStore.UnlockUnit(p.Unit.id);
            }
        }

        /// <summary>
        /// AP after the L2 structure teach, Incendiary after the L4 charge. Called from
        /// victory so the next battle's HUD already has the chip.
        /// </summary>
        public static AmmoType? GrantAmmoAfterClear(LevelDefinitionSO level)
        {
            if (level == null || level.isTestLevel) return null;
            if (level.levelNumber == 2) return OfferAmmo(AmmoType.AP);
            if (level.levelNumber == 4) return OfferAmmo(AmmoType.Incendiary);
            return null;
        }

        /// <summary>
        /// Same gifts when the player *reaches* the next fight, so a skip-ahead still
        /// finds the toolbox and a lost victory-grant cannot leave L3 on Standard only.
        /// </summary>
        public static void GrantAmmoForLevel(LevelDefinitionSO level)
        {
            if (level == null || level.isTestLevel) return;
            if (level.levelNumber >= 3) OfferAmmo(AmmoType.AP);
            if (level.levelNumber >= 5) OfferAmmo(AmmoType.Incendiary);
        }

        /// <summary>
        /// Unlock, then pre-select only if the player has never tapped an ammo chip.
        /// Two gifts in a row may replace each other; a player choice is never overwritten.
        /// </summary>
        static AmmoType? OfferAmmo(AmmoType type)
        {
            bool first = !ProgressStore.IsAmmoUnlocked(type);
            ProgressStore.UnlockAmmo(type);
            if (!ProgressStore.AmmoPickedByPlayer())
                ProgressStore.SetSelectedAmmo(type);
            return first ? type : (AmmoType?)null;
        }
    }
}
