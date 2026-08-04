using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using ArmedConflict.Data;

namespace ArmedConflict.Game
{
    /// <summary>
    /// First slice of the GameViewModel port: turning a LevelDefinition into the initial
    /// GameState. Everything else in the tick operates on what this produces, so it goes first.
    ///
    /// Corresponds to buildUnits / buildInitialState / formationFor / standingYFor in
    /// GameViewModel.kt.
    /// </summary>
    public static class LevelBuilder
    {
        // Structure runtime ids start at 100, player units at 1, enemy units at 1000 — the
        // bands keep ids globally unique, which the tick relies on for hit tracking.
        public const int StructureIdBase = 100;
        public const int PlayerUnitIdBase = 1;
        public const int EnemyUnitIdBase = 1000;

        public const bool HeliEnabled = false;      // camera-load problem, not a sequencing TODO
        public const float HeliAltitude = 3.0f;
        public const float HeliPreviewSpeed = 3.0f;
        public const float ReinforcementEnterDistance = 4.0f;
        public const float CameraZMargin = 1.2f;
        public const float CameraZHalfFovTan = 0.45f;

        /// <summary>
        /// Deck height for a garrison standing on a structure.
        ///
        /// The authored placement.y MUST go through the definition's own worldScale, exactly as
        /// the structure entity's y does — a stack's y values (0.8, 1.6) are multiples of the
        /// supporting tier's height, so they scale with it. That multiply was missed when
        /// STRUCTURE_SCALE landed, and UNSTACKED structures hid it completely because their y is
        /// 0 and 0 * k == 0. Only stacked tiers were wrong, and badly: a mid tier's row stood at
        /// 2.8 against a real deck of 4.0, embedded in the masonry below, drawing nothing —
        /// which reads exactly like a parapet swallowing the row and was mistaken for that.
        ///
        /// deckY, not size: the deck is where the model's roof actually is, and several are not
        /// at `size`. Falls back to size where they agree.
        /// </summary>
        public static float StandingYFor(StructurePlacement p)
            => p.y * p.definition.worldScale
             + (p.definition.hasDeckY ? p.definition.deckY : p.definition.size);

        /// <summary>
        /// Formation for one group. Three cases, and which one applies is the whole
        /// crowd-vs-hero presentation decision:
        ///  - garrison on a structure -> compressed deck row, clamped onto the deck
        ///  - hero-scale class -> stands apart individually, not gridded with the crowd
        ///  - everyone else -> loose clusters, never one rigid line
        /// </summary>
        static List<Vector2> FormationFor(EnemyGroup group,
                                          IReadOnlyDictionary<string, StructurePlacement> byLevelId,
                                          System.Random random)
        {
            float renderScale = group.definition != null ? group.definition.renderScale : 1f;
            float columnSpacing = Formation.DefaultColumnSpacing * renderScale;

            StructurePlacement placement = null;
            if (!string.IsNullOrEmpty(group.standingOnStructureId))
                byLevelId.TryGetValue(group.standingOnStructureId, out placement);

            if (placement != null)
            {
                // Anchor at the GROUP's x so a garrison can sit off-centre on its ledge, but pass
                // the STRUCTURE's x as deckCenterX so the row can be clamped onto the deck. An
                // older comment claimed levels always set both the same, "so this is equivalent";
                // that was wrong, and six garrisons stood in mid-air off the tier edge as a result.
                return Formation.Mounted(
                    count: group.count,
                    anchorX: group.anchorX,
                    width: placement.hasStandWidth ? placement.standWidth
                                                   : placement.definition.standWidth,
                    // deckStandZOffset puts the row on the ledge the structure actually leaves
                    // free — stepped fortress tiers each expose a different z.
                    anchorZ: group.anchorZ + placement.definition.deckStandZOffset,
                    // Garrisons pack tighter than ground formations, still scaled by renderScale
                    // so a hero-scale defender takes proportionally more of the ledge.
                    columnSpacing: Formation.MountedColumnSpacing * renderScale,
                    deckCenterX: placement.x);
            }

            if (!Mathf.Approximately(renderScale, 1f))
            {
                // 1.3x, not the wider spread tried first: level data built before hero-scale
                // units existed packs 4-5 groups within a ~3-unit span, and a wider hero spread
                // visibly swallowed neighbouring groups' territory.
                return Formation.Heroes(group.count, group.anchorX, group.anchorZ,
                                        columnSpacing * 1.3f, random);
            }

            return Formation.Clustered(group.count, group.anchorX, group.anchorZ,
                                       columnSpacing, Formation.DefaultRowSpacing, random);
        }

        /// <summary>Port of buildUnits.</summary>
        public static List<UnitEntity> BuildUnits(
            LevelDefinitionSO level,
            IReadOnlyList<EnemyGroup> groups,
            bool isPlayerSide,
            int startId,
            System.Random random = null,
            System.Func<UnitDefinitionSO, UnitDefinitionSO> tierResolver = null)
        {
            random ??= new System.Random();
            var byLevelId = new Dictionary<string, StructurePlacement>();
            var runtimeIdByLevelId = new Dictionary<string, int>();
            for (int i = 0; i < level.structures.Count; i++)
            {
                var p = level.structures[i];
                if (string.IsNullOrEmpty(p.id)) continue;
                byLevelId[p.id] = p;
                runtimeIdByLevelId[p.id] = StructureIdBase + i;
            }

            var outp = new List<UnitEntity>();
            int nextId = startId;

            foreach (var group in groups)
            {
                int? structureId = null;
                float? standingY = null;
                if (!string.IsNullOrEmpty(group.standingOnStructureId))
                {
                    if (runtimeIdByLevelId.TryGetValue(group.standingOnStructureId, out var rid))
                        structureId = rid;
                    if (byLevelId.TryGetValue(group.standingOnStructureId, out var pl))
                        standingY = StandingYFor(pl);
                }

                // Player units resolve through their upgrade tier; enemies always use base stats.
                var definition = isPlayerSide && tierResolver != null
                    ? tierResolver(group.definition)
                    : group.definition;

                foreach (var spot in FormationFor(group, byLevelId, random))
                {
                    outp.Add(new UnitEntity(
                        Id: nextId++,
                        Definition: definition,
                        X: spot.x,
                        Y: standingY ?? 0f,
                        Z: spot.y,
                        Hp: definition != null ? definition.maxHp : 1,
                        IsPlayerSide: isPlayerSide)
                    {
                        StandingOnStructureId = structureId,
                        AdvancePerTurn = isPlayerSide ? 0f : group.advancePerTurn,
                    });
                }
            }
            return outp;
        }

        /// <summary>Port of buildInitialState.</summary>
        public static GameState BuildInitialState(
            LevelDefinitionSO level,
            int battleId,
            int totalLevels,
            System.Random random = null,
            System.Func<UnitDefinitionSO, UnitDefinitionSO> tierResolver = null)
        {
            random ??= new System.Random();

            var structures = new List<StructureEntity>();
            var runtimeIdByLevelId = new Dictionary<string, int>();
            for (int i = 0; i < level.structures.Count; i++)
                if (!string.IsNullOrEmpty(level.structures[i].id))
                    runtimeIdByLevelId[level.structures[i].id] = StructureIdBase + i;

            for (int i = 0; i < level.structures.Count; i++)
            {
                var p = level.structures[i];
                int? collapseWith = null, restsOn = null;
                if (!string.IsNullOrEmpty(p.collapseWith)
                    && runtimeIdByLevelId.TryGetValue(p.collapseWith, out var cw)) collapseWith = cw;
                if (!string.IsNullOrEmpty(p.restsOn)
                    && runtimeIdByLevelId.TryGetValue(p.restsOn, out var ro)) restsOn = ro;

                // hpScale is the PLACEMENT's multiplier; MaxHp must carry it, or damage fractions
                // are taken against the wrong denominator and the structure never sheds.
                int hp = Mathf.Max((int)(p.definition.maxHp * p.hpScale), 1);

                structures.Add(new StructureEntity(
                    Id: StructureIdBase + i,
                    Definition: p.definition,
                    X: p.x,
                    // placement.y scales with the structure; size/2 centres the box on it.
                    Y: p.y * p.definition.worldScale + p.definition.size / 2f,
                    Z: p.z,
                    Hp: hp)
                {
                    MaxHp = hp,
                    CollapseWith = collapseWith,
                    RestsOnId = restsOn,
                });
            }

            var playerUnits = BuildUnits(level, level.playerGroups, true, PlayerUnitIdBase, random, tierResolver);
            var enemyUnits = BuildUnits(level, level.enemyGroups, false, EnemyUnitIdBase, random, tierResolver);

            // STABLE per-level anchors: the mean x of the INITIAL roster, computed once and never
            // recomputed as units die. A live mean would drift and reintroduce per-tick camera
            // jitter — the exact class of bug the camera architecture exists to prevent.
            float playerCamXAnchor = playerUnits.Count > 0 ? playerUnits.Average(u => u.X) : -6f;

            var enemyAnchorXs = enemyUnits.Select(u => u.X)
                .Concat(structures.Where(s => !s.Definition.isPlayerSide).Select(s => s.X))
                .ToList();
            float enemyCamXAnchor = enemyUnits.Count > 0 && enemyAnchorXs.Count > 0
                ? enemyAnchorXs.Average() : 6f;

            float staticCamZ = 19f;
            if (level.staticCamera)
            {
                var allXs = new List<float>();
                allXs.AddRange(playerUnits.Select(u => u.X));
                allXs.AddRange(enemyUnits.Select(u => u.X));
                allXs.AddRange(StructureEdges(structures));
                allXs.Add(playerCamXAnchor - ReinforcementEnterDistance);

                if (HeliEnabled && level.heliChance > 0f)
                {
                    var enemyEdges = StructureEdges(structures.Where(s => !s.Definition.isPlayerSide));
                    var heliXs = enemyUnits.Select(u => u.X).Concat(enemyEdges).ToList();
                    if (heliXs.Count > 0) allXs.Add(heliXs.Max() + 3f);
                }

                float minX = allXs.Count > 0 ? allXs.Min() : playerCamXAnchor - 3f;
                float maxX = allXs.Count > 0 ? allXs.Max() : enemyCamXAnchor + 3f;
                staticCamZ = ((maxX - minX) / 2f + CameraZMargin) / CameraZHalfFovTan;
            }

            HelicopterEntity heli = null;
            if (HeliEnabled && level.heliChance > 0f)
            {
                var xs = enemyUnits.Select(u => u.X)
                    .Concat(structures.Where(s => !s.Definition.isPlayerSide).Select(s => s.X))
                    .ToList();
                heli = new HelicopterEntity(
                    X: xs.Count > 0 ? xs.Max() + 4f : 12f,
                    Y: HeliAltitude,
                    Vx: -HeliPreviewSpeed,
                    Mode: HeliMode.Preview,
                    BurstsLeft: 0);
            }

            int tankShells = structures
                .Where(s => s.Definition.isPlayerSide && s.Definition.hasCannon)
                .Sum(s => s.Definition.cannon.ammoPerBattle);

            return new GameState
            {
                BattleId = battleId,
                LevelId = level.id,
                LevelDisplayName = level.displayName,
                LevelGoal = level.levelGoal,
                LevelNumber = level.levelNumber,
                TotalLevels = totalLevels,
                InitialPlayerCount = playerUnits.Count,
                PlayerUnits = playerUnits,
                EnemyUnits = enemyUnits,
                Structures = structures,
                Background = level.background,
                Phase = GamePhase.Preview,
                Props = level.props,
                Helicopter = heli,
                TankShellsRemaining = tankShells,
                PlayerCamXAnchor = playerCamXAnchor,
                EnemyCamXAnchor = enemyCamXAnchor,
                StaticCamera = level.staticCamera,
                StaticCamZ = staticCamZ,
                WindAccelZ = level.windAccelZ,
            };
        }

        static IEnumerable<float> StructureEdges(IEnumerable<StructureEntity> structs)
        {
            foreach (var s in structs)
            {
                float halfW = (s.Definition.hasHitWidth ? s.Definition.hitWidth
                                                        : s.Definition.size) / 2f;
                yield return s.X - halfW;
                yield return s.X + halfW;
            }
        }
    }
}
