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
        public string telegraphText;
    }

    /// <summary>
    /// Port of LevelDefinition.kt.
    ///
    /// THE SIX COMPOSITION RULES are derived, not taste — read them before authoring a level:
    ///  1. the Aiming camera frames the PLAYER LINE ONLY — keep it ~6 wide
    ///  2. scout/resolve framing is set by the enemy cluster INCLUDING structure edges — under ~11
    ///  3. one dominant structure per level, plus at most two small supports
    ///  4. 14-18 units of separation, measured TANK -> DOMINANT STRUCTURE
    ///  5. garrison the MAJORITY of the enemy roster on structures — otherwise the unit-kill win
    ///     condition resolves before the structures matter and their HP is irrelevant
    ///     (measured: L3 won in three volleys with its structures at 238/340)
    ///  6. test levels are isTestLevel, in no stage, excluded from star totals
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
    }
}
