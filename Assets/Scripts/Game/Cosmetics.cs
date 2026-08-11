using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using ArmedConflict.Data;

namespace ArmedConflict.Game
{
    /// <summary>
    /// The player's camo sets — `DYNAMISM_DESIGN.md` Phase D4, PRODUCT_DIRECTION Tier 2.4.
    ///
    /// **Pure vanity, and that is a hard rule, not a current state.** A cosmetic never touches a
    /// stat, a price, a level or a hitbox. It is the one coin sink that can be added without
    /// re-balancing anything, which is exactly why the design keeps it: the alternative sinks all
    /// buy power, and a player who spends coins on power has to be balanced against.
    ///
    /// **It repaints the PLAYER's units only, through the same path a faction repaints the
    /// enemy's** (`Render.FactionPaint`). Uniform and gear; skin and the per-class trim are shared
    /// across both armies and stay put. The player's STRUCTURES — the tank most of all — keep their
    /// fixed palette, matching the choice already made for enemy factions: a structure's colour
    /// carries building TYPE, which is more information than a camo tint would add.
    ///
    /// **A COSMETIC MUST NEVER READ AS THE ENEMY.** Two systems now paint two armies, and the one
    /// way this feature can damage the game rather than merely look dull is a camo set that lands
    /// near a faction's uniform — Urban Grey beside Ironclad Legion's steel blue-grey is the near
    /// miss, and it is why Urban is warmed toward brown rather than left neutral. `PortSelfTest`
    /// measures every camo against every faction and against the enemy default.
    ///
    /// A static catalog rather than a ScriptableObject, for the same reason as
    /// <see cref="Consumables"/>: cosmetics never came through the data import, so an asset would
    /// buy a scene rebuild and a serialized field in exchange for nothing.
    /// </summary>
    public static class Cosmetics
    {
        /// <summary>
        /// The set every player owns and starts in. Free, never stored in the unlock set, and the
        /// fallback for a selection that is somehow not owned — mirroring Standard ammo exactly.
        ///
        /// Its colours are the ones the player prefabs are BUILT with, so selecting Olive is a
        /// repaint back to the build-time materials rather than to a clone that merely matches.
        /// </summary>
        public const CosmeticSet Default = CosmeticSet.Olive;

        public class Camo
        {
            public CosmeticSet Set;
            public string DisplayName;
            public int CoinPrice;
            /// <summary>Null for Olive, which wears the prefabs' own materials.</summary>
            public Color? UniformColor;
            public Color? GearColor;
        }

        public static readonly IReadOnlyList<Camo> All = new[]
        {
            new Camo
            {
                Set = CosmeticSet.Olive,
                DisplayName = "Olive Drab",
                CoinPrice = 0,
                UniformColor = null,
                GearColor = null,
            },
            new Camo
            {
                Set = CosmeticSet.Desert,
                DisplayName = "Desert Tan",
                CoinPrice = 300,
                UniformColor = new Color(0.66f, 0.56f, 0.36f),
                GearColor = new Color(0.34f, 0.28f, 0.18f),
            },
            new Camo
            {
                // Boxed in from three sides, and the numbers are why it is a LIGHT warm grey.
                // Cooler and it approaches Ironclad Legion's steel blue-grey — two grey armies on
                // stage 2. Darker and it approaches the player's own Olive, so the 350 coins buy
                // a set nobody can see you wearing (measured: 0.159, barely over the floor).
                // Warmer and it approaches Desert Tan. Light and neutral-warm is the gap.
                Set = CosmeticSet.Urban,
                DisplayName = "Urban Grey",
                CoinPrice = 350,
                UniformColor = new Color(0.52f, 0.49f, 0.45f),
                GearColor = new Color(0.26f, 0.25f, 0.23f),
            },
            new Camo
            {
                // Not white. A pure white uniform vanishes into the Frostline biome's snow, which
                // is the one ground this set will actually be worn on — it is a pale blue-grey
                // with enough value left to hold an edge against it.
                Set = CosmeticSet.Arctic,
                DisplayName = "Arctic White",
                CoinPrice = 400,
                UniformColor = new Color(0.80f, 0.83f, 0.86f),
                GearColor = new Color(0.42f, 0.45f, 0.50f),
            },
        };

        public static Camo For(CosmeticSet set) => All.FirstOrDefault(c => c.Set == set);

        /// <summary>
        /// RIGS TEST SUPPLY: wear any set without owning it, for one session.
        ///
        /// The same bargain the consumable test supply strikes, and for the same reason — the
        /// release build is not debuggable, `run-as` cannot seed PlayerPrefs, and the test protocol
        /// is uninstall/reinstall, so confirming a 400-coin camo on the only build worth measuring
        /// would cost a 400-coin re-earn on every build.
        ///
        /// **It writes NOTHING.** No purchase, no unlock, no stored selection — turn RIGS off and
        /// the real wardrobe is exactly as it was. That session-only blast radius is what makes
        /// reusing RIGS acceptable rather than a second hidden switch.
        /// </summary>
        public static CosmeticSet? TestOverride;

        /// <summary>The set in force, test supply included.</summary>
        public static CosmeticSet SelectedSet()
            => TestOverride ?? ProgressStore.SelectedCosmetic();

        /// <summary>
        /// What the player is wearing right now. Read UNCACHED at every use — the Kotlin build's
        /// one real cosmetic bug was a keyless `remember` that evaluated once, at the first
        /// composition of a screen that never unmounts, so a set bought and selected on the loadout
        /// screen never reached the battle and the feature looked entirely broken.
        /// </summary>
        public static Camo Selected() => For(SelectedSet()) ?? For(Default);
    }
}
