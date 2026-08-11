namespace ArmedConflict.Data
{
    /// <summary>
    /// Level -> stage -> faction. A free function rather than a field on the level, because a
    /// level's stage is already recorded once — in the stage's own <c>levels</c> list — and a
    /// second copy on the level is a copy that can disagree.
    /// </summary>
    public static class Factions
    {
        /// <summary>
        /// The faction whose uniform this level's enemy wears, or null for "the default red".
        /// Null is the ordinary answer for a TEST RIG, which belongs to no stage by rule 6.
        /// </summary>
        public static FactionDefinitionSO For(LevelDefinitionSO level, StageDefinitionSO[] stages)
        {
            if (level == null || stages == null) return null;
            foreach (var stage in stages)
            {
                if (stage == null || stage.faction == null) continue;
                foreach (var l in stage.levels)
                    if (l == level) return stage.faction;
            }
            return null;
        }
    }
}
