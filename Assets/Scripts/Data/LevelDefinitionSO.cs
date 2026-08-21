using System;
using System.Collections.Generic;
using UnityEngine;

namespace ArmedConflict.Data
{
    [Serializable]
    public class EnemyGroup
    {
        public UnitDefinitionSO definition;
        public int count;
        public float anchorX;
        public float anchorZ = 0f;
        public string standingOnStructureId;
        public float advancePerTurn = 0f;
    }

    [Serializable]
    public class StructurePlacement
    {
        public string id;
        public StructureDefinitionSO definition;
        public float x, y, z;
        public string collapseWith;
        public string restsOn;
        public float standWidth = -1f;
        public bool hasStandWidth = false;

        /// <summary>
        /// A level can multiply a structure's HP. Damage fractions must be taken against the
        /// PLACEMENT's max HP, not the definition's — reading the definition made a 4x rig wall
        /// compute a negative damage fraction and never shed anything however hard it was hit.
        /// </summary>
        public float hpScale = 1f;
    }

    [Serializable]
    public class PropPlacement
    {
        public string modelAsset;
        public float x;
        public float z = 0f;
        public float scale = 1f;
        public bool slowsAdvance = false;
        public float halfWidth = 1f;
        /// <summary>
        /// Keep the GLB's authored colours. Default false: sandbags and
        /// wire take the player structure paint. A wreck or a cactus
        /// painted olive is a different object.
        /// </summary>
        public bool keepColors = false;
        /// <summary>
        /// Skip Normalize. The GLB is already in world units; `scale` is
        /// a uniform multiplier (1 = authored size). A road's longest
        /// axis is its length — Normalize would turn scale 1 into a
        /// postage stamp.
        /// </summary>
        public bool absoluteScale = false;
    }

    [Serializable]
    public class BossPhaseTrigger
    {
        public List<string> triggerStructureIds = new();
        public List<EnemyGroup> spawnGroups = new();
        public string announcement;
    }

    [Serializable]
    public class ReinforcementWave
    {
        public int arrivesOnTurn;
        public List<EnemyGroup> spawnGroups = new();
        public string announcement;

        /// <summary>
        /// WHAT is coming, with no count in it — "Heavy support inbound". The countdown is
        /// composed per turn by <see cref="Game.EventSystems.TelegraphLine"/>, so it cannot go
        /// stale against <see cref="arrivesOnTurn"/> and it keeps ticking down over a lead
        /// longer than one turn.
        /// </summary>
        public string telegraphLabel;

        /// <summary>
        /// How many turns the warning runs for before the wave lands. 1 is a heads-up; 2 is a
        /// clock the player can actually spend a volley against, and is what the "armor column
        /// inbound" beat wants. Must leave room before turn 1 — checked by PortSelfTest.
        /// </summary>
        public int telegraphLeadTurns = 1;
    }

    /// <summary>
    /// Port of LevelDefinition.kt.
    ///
    /// THE SEVEN COMPOSITION RULES are derived, not taste — read them before authoring a level:
    ///  1. the Aiming camera frames the PLAYER LINE ONLY — keep it ~6 wide
    ///  2. scout/resolve framing is set by the enemy cluster INCLUDING structure edges — under ~11
    ///  3. one dominant structure per level, plus at most two small supports
    ///  4. 14-20 units of separation, measured TANK -> DOMINANT STRUCTURE
    ///     (20 while L1 trials 18.5; was 14-18 at v=9)
    ///  5. garrison the MAJORITY of the enemy roster on structures — otherwise the unit-kill win
    ///     condition resolves before the structures matter and their HP is irrelevant
    ///     (measured: L3 won in three volleys with its structures at 238/340)
    ///  6. test levels are isTestLevel, in no stage, excluded from star totals
    ///  7. every enemy UNIT must be REACHABLE — max range is v^2/g (22.56 at v=9.5) and HEIGHT spends
    ///     it twice, so a garrison lifted onto a tall structure at full separation can be
    ///     unwinnable while passing rules 1-6. Checked by BalanceAudit.ReachRule
    ///
    /// Test levels must be RENUMBERED whenever the campaign grows: the debug switcher does
    /// jumpToLevel(levelNumber), which is only correct while levelNumber == index + 1.
    /// </summary>
    [CreateAssetMenu(menuName = "ArmedConflict/Level", fileName = "Level")]
    public class LevelDefinitionSO : ScriptableObject
    {
        public string id;
        public string displayName;
        public int levelNumber = 1;
        public string levelGoal = "Destroy all enemy units";

        public List<EnemyGroup> enemyGroups = new();
        public List<StructurePlacement> structures = new();
        public List<EnemyGroup> playerGroups = new();
        public BackgroundDefinitionSO background;

        public float heliChance = 0f;
        public List<PropPlacement> props = new();
        public int levelBase = 60;
        public int deployBudget = 0;
        public List<BossPhaseTrigger> bossPhases = new();
        public float windAccelZ = 0f;
        public List<ReinforcementWave> reinforcementWaves = new();
        public bool staticCamera = false;
        public bool isTestLevel = false;

        /// <summary>
        /// WHY this level is shaped the way it is — the beat it teaches, which composition rule it
        /// is deliberately bending, what a playtest changed and what it broke.
        ///
        /// It exists because the Kotlin this data came from carried a great deal of that
        /// reasoning in comments, and moving authoring into Unity on 2026-08-06 would otherwise
        /// have stranded all of it in a repo nobody opens. A number with no reason attached is a
        /// number the next person will "clean up".
        ///
        /// Prose, not a spec. The rules themselves live in LEVEL_AUTHORING.md.
        /// </summary>
        [TextArea(3, 12)] public string designNotes = "";
    }
}
