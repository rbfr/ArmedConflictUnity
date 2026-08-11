using System.Collections.Generic;
using UnityEngine;

namespace ArmedConflict.Render
{
    /// <summary>
    /// Dresses a pooled army in a faction's colours — Tier 2.1.
    ///
    /// It is a free function rather than a lump of BattleRunner because the property worth
    /// checking is the one only a REPAINT can have: a slot painted for stage 2 and then handed to
    /// stage 1 must come back red. That is the standing pool rule ("what must be reset on a switch
    /// is everything that reads a slot's PREVIOUS occupant"), and it is unreachable from a test if
    /// the code lives behind a MonoBehaviour's private methods.
    ///
    /// The classification runs ONCE and the apply runs per level switch, so the per-switch cost is
    /// a list walk and a material assignment — no GetComponentsInChildren while a level is loading.
    /// </summary>
    public static class FactionPaint
    {
        /// <summary>
        /// Sorts every renderer under these roots into "wears the uniform" and "wears the gear",
        /// by the material the art pipeline TONED it with.
        ///
        /// Matching on the material rather than on the `skin*`/`trim*`/`accent*` mesh-name prefixes
        /// is deliberate: that convention belongs to RiggedUnits.Tone, and a second copy of it here
        /// is a copy that can disagree with the first. All this end needs to know is which of the
        /// two side-materials a mesh is currently wearing.
        /// </summary>
        public static void Classify(IEnumerable<GameObject> roots, Material uniform, Material gear,
                                    List<Renderer> intoUniform, List<Renderer> intoGear)
        {
            if (uniform == null && gear == null) return;
            foreach (var root in roots)
            {
                if (root == null) continue;
                foreach (var r in root.GetComponentsInChildren<Renderer>(true))
                {
                    var m = r.sharedMaterial;
                    if (m == null) continue;
                    if (uniform != null && m == uniform) intoUniform.Add(r);
                    else if (gear != null && m == gear) intoGear.Add(r);
                }
            }
        }

        /// <summary>
        /// Paints the classified renderers. A null material leaves its half alone rather than
        /// clearing it — an untinted soldier is a data gap; an untextured one is a bug you can see
        /// from across the room.
        /// </summary>
        public static void Apply(IReadOnlyList<Renderer> uniformRenderers,
                                 IReadOnlyList<Renderer> gearRenderers,
                                 Material uniform, Material gear)
        {
            if (uniform != null)
                for (int i = 0; i < uniformRenderers.Count; i++)
                    uniformRenderers[i].sharedMaterial = uniform;
            if (gear != null)
                for (int i = 0; i < gearRenderers.Count; i++)
                    gearRenderers[i].sharedMaterial = gear;
        }

        /// <summary>
        /// A clone of a build-time material in a faction's tone.
        ///
        /// CLONED, never tinted in place: the source is a project ASSET, so writing to it inside
        /// the editor edits the .mat on disk and every faction ends up sharing whichever colour was
        /// applied last — green in the editor, and wrong on every subsequent build.
        /// </summary>
        public static Material Recolour(Material source, Color c) =>
            source == null ? null : new Material(source) { color = c };
    }
}
