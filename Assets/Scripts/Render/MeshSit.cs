using System.Collections.Generic;
using UnityEngine;

namespace ArmedConflict.Render
{
    /// <summary>
    /// Where a mark or a flame actually sits on a mesh. Renderer AABBs lie:
    /// a rotated collapse slab, a tail fin, a porch, any of them inflate the
    /// box and the stamp floats in front of (or above) the thing the player
    /// is looking at. Vertices and triangles do not.
    /// </summary>
    public static class MeshSit
    {
        static readonly List<Vector3> VertBuf = new List<Vector3>(256);
        static readonly List<int> TriBuf = new List<int>(768);

        public static bool IsFx(Transform t, Transform host)
        {
            while (t != null && t != host)
            {
                var n = t.name;
                if (n.StartsWith("WreckFire") || n.StartsWith("WreckSmoke")
                    || n.StartsWith("RuinFire") || n.StartsWith("RuinSmoke")
                    || n.StartsWith("RuinGlow"))
                    return true;
                t = t.parent;
            }
            return false;
        }

        /// <summary>
        /// Host-local vertex samples. Caps at ~500 so a hangar collapse is
        /// cheap to re-sit every tick while the clip is still falling.
        /// </summary>
        public static void SampleLocal(Transform host, List<float> xs, List<float> ys, List<float> zs)
        {
            xs.Clear(); ys.Clear(); zs.Clear();
            if (host == null) return;
            var filters = host.GetComponentsInChildren<MeshFilter>(true);
            for (int i = 0; i < filters.Length; i++)
            {
                var mf = filters[i];
                if (mf.sharedMesh == null || IsFx(mf.transform, host)) continue;
                var rend = mf.GetComponent<MeshRenderer>();
                if (rend != null && !rend.enabled) continue;
                var verts = mf.sharedMesh.vertices;
                if (verts == null || verts.Length == 0) continue;
                int step = Mathf.Max(1, verts.Length / 500);
                for (int v = 0; v < verts.Length; v += step)
                {
                    var p = host.InverseTransformPoint(mf.transform.TransformPoint(verts[v]));
                    xs.Add(p.x); ys.Add(p.y); zs.Add(p.z);
                }
            }
        }

        public static float Percentile(List<float> v, float p)
        {
            if (v == null || v.Count == 0) return 0f;
            v.Sort();
            float i = (v.Count - 1) * Mathf.Clamp01(p);
            int lo = (int)i;
            int hi = Mathf.Min(lo + 1, v.Count - 1);
            return Mathf.Lerp(v[lo], v[hi], i - lo);
        }

        /// <summary>
        /// Camera-facing surface at a world X/Y. Picks the frontmost triangle
        /// that covers the point (max Unity Z), so a porch two metres forward
        /// cannot steal a hit that landed on the wall beside it.
        /// </summary>
        public static bool TryFrontZ(Transform host, float worldX, float worldY, out float worldZ)
        {
            worldZ = 0f;
            if (host == null) return false;
            float best = float.NegativeInfinity;
            bool any = false;
            float nearZ = 0f, nearD = float.PositiveInfinity;
            var filters = host.GetComponentsInChildren<MeshFilter>(true);
            for (int i = 0; i < filters.Length; i++)
            {
                var mf = filters[i];
                var mesh = mf.sharedMesh;
                if (mesh == null || IsFx(mf.transform, host)) continue;
                var rend = mf.GetComponent<MeshRenderer>();
                if (rend != null && !rend.enabled) continue;
                var b = rend != null ? rend.bounds : new Bounds();
                if (rend != null)
                {
                    // Cheap reject: if this piece cannot contain the hit in X/Y,
                    // it is not the wall we stamped.
                    const float pad = 0.08f;
                    if (worldX < b.min.x - pad || worldX > b.max.x + pad
                        || worldY < b.min.y - pad || worldY > b.max.y + pad)
                        continue;
                }
                mesh.GetVertices(VertBuf);
                if (VertBuf.Count == 0) continue;
                var l2w = mf.transform.localToWorldMatrix;
                int sub = mesh.subMeshCount;
                for (int s = 0; s < sub; s++)
                {
                    mesh.GetTriangles(TriBuf, s);
                    for (int t = 0; t < TriBuf.Count; t += 3)
                    {
                        var a = l2w.MultiplyPoint3x4(VertBuf[TriBuf[t]]);
                        var c = l2w.MultiplyPoint3x4(VertBuf[TriBuf[t + 1]]);
                        var d = l2w.MultiplyPoint3x4(VertBuf[TriBuf[t + 2]]);
                        if (HitXY(a, c, d, worldX, worldY, out float z) && z > best)
                        {
                            best = z;
                            any = true;
                        }
                    }
                }
                if (!any)
                {
                    for (int v = 0; v < VertBuf.Count; v++)
                    {
                        var p = l2w.MultiplyPoint3x4(VertBuf[v]);
                        float dx = p.x - worldX, dy = p.y - worldY;
                        float dd = dx * dx + dy * dy;
                        if (dd < nearD)
                        {
                            nearD = dd;
                            nearZ = p.z;
                        }
                    }
                }
            }
            if (any)
            {
                worldZ = best;
                return true;
            }
            // Collision box is wider than the mesh (rule 8). Snap only if the
            // nearest vert is still on the building, not a metre of empty air.
            if (nearD < 0.45f * 0.45f)
            {
                worldZ = nearZ;
                return true;
            }
            return false;
        }

        static bool HitXY(Vector3 a, Vector3 b, Vector3 c, float x, float y, out float z)
        {
            float denom = (b.y - c.y) * (a.x - c.x) + (c.x - b.x) * (a.y - c.y);
            if (Mathf.Abs(denom) < 1e-8f) { z = 0f; return false; }
            float wa = ((b.y - c.y) * (x - c.x) + (c.x - b.x) * (y - c.y)) / denom;
            float wb = ((c.y - a.y) * (x - c.x) + (a.x - c.x) * (y - c.y)) / denom;
            float wc = 1f - wa - wb;
            const float eps = -1e-4f;
            if (wa < eps || wb < eps || wc < eps) { z = 0f; return false; }
            z = wa * a.z + wb * b.z + wc * c.z;
            return true;
        }
    }
}
