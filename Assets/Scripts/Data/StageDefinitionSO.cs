using System.Collections.Generic;
using UnityEngine;

namespace ArmedConflict.Data
{
    /// <summary>Port of StageDefinition.kt — themed level groups with star gates.</summary>
    [CreateAssetMenu(menuName = "ArmedConflict/Stage", fileName = "Stage")]
    public class StageDefinitionSO : ScriptableObject
    {
        public string id;
        public string displayName;
        public string tagline;
        public List<LevelDefinitionSO> levels = new();
        public int starsToUnlock;
        public string unlockRewardId;
        public int completionCoinBonus = 0;

        /// <summary>
        /// Who the enemy IS on this stage — Tier 2.1. Optional: a stage with no faction fields
        /// the enemy's default red, which is exactly the pre-faction behaviour and is also what
        /// every test rig gets, since a rig belongs to no stage.
        /// </summary>
        public FactionDefinitionSO faction;
    }
}
