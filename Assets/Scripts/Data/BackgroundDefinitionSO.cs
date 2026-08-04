using UnityEngine;

namespace ArmedConflict.Data
{
    /// <summary>
    /// Port of BackgroundDefinition.kt — the ONE data file that imported Compose. Its colours
    /// were androidx Color; here they are UnityEngine.Color, which is the whole of the change.
    ///
    /// groundNear also feeds the contact-shadow tone at runtime (shadow = ground scaled toward
    /// black), so it reads as shade on bright biomes and stays proportional on dark ones.
    /// </summary>
    [CreateAssetMenu(menuName = "ArmedConflict/Background", fileName = "Background")]
    public class BackgroundDefinitionSO : ScriptableObject
    {
        public Color skyTop;
        public Color skyHorizon;
        public Color groundColor;
        public Color groundNear;
        public Color horizonAccent;
        public Color silhouetteFar;
        public Color silhouetteNear;
        public SilhouetteStyle style;
        public bool snowfall = false;
    }
}
