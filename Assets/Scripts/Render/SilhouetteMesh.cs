using System.Collections.Generic;
using UnityEngine;

namespace ArmedConflict.Render
{
    /// <summary>
    /// Turns a Backdrop.Layer's sampled profile into geometry. Runtime-safe (no UnityEditor), so
    /// the same code can bake a scene today and build a backdrop per level when level switching
    /// lands.
    ///
    /// Winding is CCW as seen from +Z. The camera views from +Z, so an unrotated quad shows it
    /// the BACK face and is culled away entirely — the same trap the sky quad's 180° turn works
    /// around. A strip built the other way round renders as nothing at all, which reads as "the
    /// mesh failed to build" rather than as a winding error.
    /// </summary>
    public static class SilhouetteMesh
    {
        /// <summary>The silhouette itself: a strip from BaseY up to the profile.</summary>
        public static Mesh Build(Backdrop.Layer layer)
        {
            int cols = layer.Profile.Length;
            var verts = new Vector3[cols * 2];
            var tris = new List<int>((cols - 1) * 6);

            for (int i = 0; i < cols; i++)
            {
                float x = -layer.Width / 2f + layer.Width * i / (cols - 1);
                verts[i * 2] = new Vector3(x, layer.BaseY, 0f);
                verts[i * 2 + 1] = new Vector3(x, TopAt(layer, i), 0f);
            }
            for (int i = 0; i < cols - 1; i++)
            {
                int b0 = i * 2, t0 = i * 2 + 1, b1 = (i + 1) * 2, t1 = (i + 1) * 2 + 1;
                tris.Add(b0); tris.Add(b1); tris.Add(t0);
                tris.Add(b1); tris.Add(t1); tris.Add(t0);
            }
            return Finish(layer.Name, verts, tris);
        }

        /// <summary>
        /// Snow, as the SAME silhouette clipped to everything above a snowline — so a cap follows
        /// the crest it sits on instead of being a second triangle stuck on the tip. The line is
        /// jittered per column, which is what stops it reading as a ruler laid across the range,
        /// and any crest that never reaches it simply contributes no geometry: bare knolls fall
        /// out of the maths rather than needing a rule of their own.
        /// </summary>
        public static Mesh BuildSnow(Backdrop.Layer layer)
        {
            if (layer.SnowLine < 0f) return null;

            int cols = layer.Profile.Length;
            var verts = new List<Vector3>();
            var tris = new List<int>();
            int runStart = -1;

            for (int i = 0; i <= cols; i++)
            {
                bool covered = i < cols && TopAt(layer, i) > SnowYAt(layer, i);

                if (covered && runStart < 0) runStart = i;
                if (covered || runStart < 0) continue;

                // Close the run [runStart, i-1]: one strip between the snowline and the crest.
                int first = verts.Count;
                for (int j = runStart; j < i; j++)
                {
                    float x = -layer.Width / 2f + layer.Width * j / (cols - 1);
                    verts.Add(new Vector3(x, SnowYAt(layer, j), 0f));
                    verts.Add(new Vector3(x, TopAt(layer, j), 0f));
                }
                for (int j = 0; j < i - runStart - 1; j++)
                {
                    int b0 = first + j * 2, t0 = b0 + 1, b1 = b0 + 2, t1 = b0 + 3;
                    tris.Add(b0); tris.Add(b1); tris.Add(t0);
                    tris.Add(b1); tris.Add(t1); tris.Add(t0);
                }
                runStart = -1;
            }
            if (tris.Count == 0) return null;
            return Finish(layer.Name + "Snow", verts.ToArray(), tris);
        }

        /// <summary>The ember windows on a ruined block: one unlit quad each.</summary>
        public static Mesh BuildWindows(Backdrop.Layer layer)
        {
            if (layer.Windows.Length == 0) return null;
            var verts = new List<Vector3>();
            var tris = new List<int>();
            foreach (var w in layer.Windows)
            {
                int v = verts.Count;
                verts.Add(new Vector3(w.x, w.y, 0f));
                verts.Add(new Vector3(w.x + w.z, w.y, 0f));
                verts.Add(new Vector3(w.x, w.y + w.w, 0f));
                verts.Add(new Vector3(w.x + w.z, w.y + w.w, 0f));
                tris.Add(v); tris.Add(v + 1); tris.Add(v + 2);
                tris.Add(v + 1); tris.Add(v + 3); tris.Add(v + 2);
            }
            return Finish(layer.Name + "Windows", verts.ToArray(), tris);
        }

        static float TopAt(Backdrop.Layer layer, int i)
            => layer.BaseY + layer.Height * layer.Profile[i];

        static float SnowYAt(Backdrop.Layer layer, int i)
            => layer.BaseY + layer.Height *
               (layer.SnowEdge != null ? layer.SnowEdge[i] : layer.SnowLine);

        static Mesh Finish(string name, Vector3[] verts, List<int> tris)
        {
            var mesh = new Mesh { name = name };
            mesh.SetVertices(verts);
            mesh.SetTriangles(tris, 0);
            // UVs run 0..1 over the layer's own bounds so a gradient material (the sea) can be
            // sampled vertically without a second mesh format.
            var bounds = new Bounds(verts[0], Vector3.zero);
            foreach (var v in verts) bounds.Encapsulate(v);
            var uv = new Vector2[verts.Length];
            for (int i = 0; i < verts.Length; i++)
                uv[i] = new Vector2(
                    Mathf.InverseLerp(bounds.min.x, bounds.max.x, verts[i].x),
                    Mathf.InverseLerp(bounds.min.y, bounds.max.y, verts[i].y));
            mesh.SetUVs(0, uv);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
