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
    }
}
