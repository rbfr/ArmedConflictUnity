using System.Collections.Generic;
using UnityEngine;
using ArmedConflict.Game;

namespace ArmedConflict.Render
{
    /// <summary>
    /// Burned-out city: a warm glow INSIDE the shell, a small incendiary
    /// tongue in the opening, smoke rising out. Positive local z put sprites
    /// in the street (orange squares). The tongue sits just behind the facade
    /// so the masonry frames it; the glow sits deeper so holes look occupied.
    /// </summary>
    public static class RuinFx
    {
        // Local to CityNear. Z is negative (into the block). Y is glow centre.
        // X matches the current gutted / sheared shells in build_backdrop_city.py.
        public static readonly Site[] Sites =
        {
            new Site(-18.2f, 2.4f, -1.7f, 2.4f, 1.8f,  8.5f, 2.8f, 701),
            new Site(-12.6f, 3.1f, -1.9f, 2.8f, 2.0f, 10.0f, 3.0f, 702),
            new Site( -7.4f, 2.2f, -1.6f, 2.2f, 1.6f,  9.5f, 2.5f, 703),
            new Site(  2.6f, 2.8f, -1.8f, 2.6f, 1.9f,  8.0f, 2.8f, 704),
            new Site(  7.8f, 2.3f, -1.6f, 2.3f, 1.7f,  7.8f, 2.6f, 705),
            new Site( 15.2f, 2.5f, -1.8f, 2.5f, 1.8f,  9.2f, 2.9f, 706),
        };

        public const float SmokeHz = 0.21f;
        public const float SmokeLeanHz = 0.13f;
        /// <summary>Puff cycle. ~6s to rise and fade; a second puff is 180° out of phase so the column never holds still.</summary>
        public const float SmokeRiseHz = 0.17f;
        /// <summary>Travel as a multiple of authored H. The old quad sat at a fixed height and read as a cap.</summary>
        public const float SmokeRise = 1.75f;
        public const float GlowHz = 0.16f;
        public const float GlowAlphaMid = 0.58f;
        public const float GlowAlphaSwing = 0.14f;

        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        public readonly struct Site
        {
            public readonly float X, Y, Z, FireH, FireW, SmokeH, SmokeW;
            public readonly int Id;
            public Site(float x, float y, float z, float fireH, float fireW,
                        float smokeH, float smokeW, int id)
            {
                X = x; Y = y; Z = z; FireH = fireH; FireW = fireW;
                SmokeH = smokeH; SmokeW = smokeW; Id = id;
            }
        }

        public sealed class Session
        {
            public struct Fire
            {
                public Transform Glow, Outer, Inner;
                public MeshRenderer GlowRenderer;
                public float GlowH, GlowW, FlameH, FlameW;
                public int Id;
            }
            public struct Smoke
            {
                public Transform Quad, Quad2, Quad3;
                public MeshRenderer Rend, Rend2, Rend3;
                public float H, W, X, Y, Z;
                public int Id;
            }

            public Fire[] Fires = System.Array.Empty<Fire>();
            public Smoke[] Smokes = System.Array.Empty<Smoke>();
            /// <summary>0 = full from the first frame (city). Wrecks fade in so
            /// fire does not pop on the intact hut for the first collapse frames.</summary>
            public float FadeSeconds;
            public float BornAt = -1f;
            /// <summary>Wreck root. When set, tongues sit on the live pile
            /// (front, toward camera) so a leftover block cannot swallow them.
            /// City sessions leave this null.</summary>
            public Transform Host;
            /// <summary>
            /// A burned-out hull (L13 apron), not a collapse pancake. SitOnPile
            /// uses the AABB top so a leftover block cannot hide the tongue —
            /// on an airliner that top is the tail, and the fire floats.
            /// Hull sits on the fuselage from mesh verts, once.
            /// </summary>
            public bool Hull;
            bool hullPosed;
            MaterialPropertyBlock props;

            public void Restart() => BornAt = Time.time;

            public void Tick(float time)
            {
                if (Host != null)
                {
                    if (Hull) SitInHull();
                    else SitOnPile();
                }
                if (BornAt < 0f) BornAt = time;
                float fade = FadeSeconds <= 1e-4f
                    ? 1f
                    : Mathf.Clamp01((time - BornAt) / FadeSeconds);
                props ??= new MaterialPropertyBlock();
                for (int i = 0; i < Fires.Length; i++)
                {
                    var f = Fires[i];
                    float phase = CosmeticSystems.FlamePhase(f.Id);
                    if (f.Glow != null)
                    {
                        float s = Mathf.Sin(time * GlowHz * Mathf.PI * 2f + phase);
                        f.Glow.localScale = new Vector3(
                            f.GlowW * (1f + 0.05f * s), f.GlowH * (1f + 0.05f * s), 1f);
                        float a = Mathf.Clamp01(GlowAlphaMid + GlowAlphaSwing * s);
                        props.SetColor(BaseColorId, new Color(1f, 1f, 1f, a));
                        f.GlowRenderer.SetPropertyBlock(props);
                    }
                    PoseTongue(f.Outer, CosmeticSystems.FlameScale(time, phase, false),
                               f.FlameW * fade, f.FlameH * fade, 1f);
                    PoseTongue(f.Inner, CosmeticSystems.FlameScale(time, phase, true),
                               f.FlameW * fade, f.FlameH * fade, FlameRig.InnerScale);
                }
                for (int i = 0; i < Smokes.Length; i++)
                {
                    var sm = Smokes[i];
                    PosePuff(sm.Quad, sm.Rend, sm, time, fade, 0f, 0f);
                    if (sm.Quad2 != null)
                        PosePuff(sm.Quad2, sm.Rend2, sm, time, fade, 1f / 3f, 0.012f);
                    if (sm.Quad3 != null)
                        PosePuff(sm.Quad3, sm.Rend3, sm, time, fade, 2f / 3f, -0.012f);
                }
            }

            void PosePuff(Transform t, MeshRenderer r, Smoke sm, float time, float fade,
                          float cycleOff, float zBias)
            {
                if (t == null) return;
                float phase = CosmeticSystems.FlamePhase(sm.Id + 90);
                float cycle = Mathf.Repeat(time * SmokeRiseHz + phase * 0.159f + cycleOff, 1f);
                float wiggle = Mathf.Sin(time * SmokeHz * Mathf.PI * 2f + phase + cycleOff * 4f);
                float lean = Mathf.Sin(time * SmokeLeanHz * Mathf.PI * 2f + phase * 1.3f + cycleOff);
                float rise = cycle * sm.H * SmokeRise * fade;
                // Cloud, not a pillar: size stays near-even so a stack of
                // puffs reads as one billow, not dark feet under pale tops.
                float puff = sm.W * (0.92f + 0.28f * cycle);
                float w = puff * (1f + 0.14f * wiggle);
                float h = puff * 0.90f * (1f - 0.10f * wiggle);
                float a = Mathf.Sin(cycle * Mathf.PI) * fade;
                t.localScale = new Vector3(w, h, 1f);
                t.localRotation = Quaternion.Euler(0f, 180f, wiggle * 11f);
                t.localPosition = new Vector3(
                    sm.X + lean * (0.05f + rise * 0.07f),
                    sm.Y + rise + h * 0.20f,
                    sm.Z + zBias);
                if (r == null) return;
                var c = r.sharedMaterial != null ? r.sharedMaterial.color : Color.white;
                props.SetColor(BaseColorId, new Color(c.r, c.g, c.b, c.a * a));
                r.SetPropertyBlock(props);
            }

            static bool IsFx(Transform t, Transform host)
            {
                while (t != null && t != host)
                {
                    if (t.name.StartsWith("WreckFire") || t.name.StartsWith("WreckSmoke"))
                        return true;
                    t = t.parent;
                }
                return false;
            }

            /// <summary>
            /// Fire on TOP of the live pile, proud of the camera lip.
            /// Nested in the rubble (the first pass) is invisible at 6° —
            /// the hangar's pancake hid every tongue. Proud at the FEET is
            /// a row of candles in the street. Proud at the TOP is the
            /// destruction read: tongues licking out of the heap, smoke
            /// clearing the roofline. Follows the collapse down.
            /// </summary>
            void SitOnPile()
            {
                float x0 = 1e9f, y0 = 1e9f, z0 = 1e9f;
                float x1 = -1e9f, y1 = -1e9f, z1 = -1e9f;
                bool any = false;
                var rends = Host.GetComponentsInChildren<MeshRenderer>(true);
                for (int i = 0; i < rends.Length; i++)
                {
                    var r = rends[i];
                    if (!r.enabled || IsFx(r.transform, Host)) continue;
                    var b = r.bounds;
                    for (int c = 0; c < 8; c++)
                    {
                        var w = new Vector3(
                            (c & 1) == 0 ? b.min.x : b.max.x,
                            (c & 2) == 0 ? b.min.y : b.max.y,
                            (c & 4) == 0 ? b.min.z : b.max.z);
                        var p = Host.InverseTransformPoint(w);
                        if (!any)
                        {
                            x0 = x1 = p.x; y0 = y1 = p.y; z0 = z1 = p.z;
                            any = true;
                        }
                        else
                        {
                            if (p.x < x0) x0 = p.x; if (p.x > x1) x1 = p.x;
                            if (p.y < y0) y0 = p.y; if (p.y > y1) y1 = p.y;
                            if (p.z < z0) z0 = p.z; if (p.z > z1) z1 = p.z;
                        }
                    }
                }
                if (!any) return;
                float worldS = Mathf.Max(Host.lossyScale.x, 0.01f);
                float pileH = Mathf.Max(0.12f, y1 - y0);
                float pileWorld = pileH * worldS;
                // World sizes, then /scale so a 2.5x hangar is not a candle.
                float flameH = Mathf.Clamp(Mathf.Max(0.95f, pileWorld * 0.55f), 0.95f, 1.90f) / worldS;
                float flameW = Mathf.Clamp(Mathf.Max(0.50f, pileWorld * 0.28f), 0.50f, 1.05f) / worldS;
                float smokeH = Mathf.Clamp(Mathf.Max(4.2f, pileWorld * 2.6f), 4.2f, 8.5f) / worldS;
                float smokeW = Mathf.Clamp(Mathf.Max(1.8f, pileWorld * 1.05f), 1.8f, 3.6f) / worldS;
                // +Z toward camera. A few centimetres proud of the lip so
                // a leftover block cannot cover the tongue.
                float z = z1 + 0.14f / worldS;
                float yTop = y1 - 0.04f / worldS;
                if (yTop < y0 + 0.08f) yTop = y0 + 0.08f;
                float mid = (x0 + x1) * 0.5f;
                float half = Mathf.Max(0.12f, (x1 - x0) * 0.22f);
                var xs = Fires.Length >= 3
                    ? new[] { mid - half, mid + half * 0.10f, mid + half }
                    : new[] { mid - half, mid + half };
                var ys = Fires.Length >= 3
                    ? new[] { yTop, yTop - 0.04f / worldS, yTop - 0.02f / worldS }
                    : new[] { yTop, yTop - 0.03f / worldS };
                var zs = Fires.Length >= 3
                    ? new[] { z, z + 0.03f / worldS, z - 0.02f / worldS }
                    : new[] { z, z + 0.02f / worldS };
                int n = Mathf.Min(Fires.Length, xs.Length);
                for (int i = 0; i < n; i++)
                {
                    var f = Fires[i];
                    f.FlameH = flameH;
                    f.FlameW = flameW;
                    Fires[i] = f;
                    var root = f.Outer;
                    if (root == null) continue;
                    root = root.parent;
                    if (root == null) continue;
                    root.localPosition = new Vector3(xs[i], ys[i], zs[i]);
                }
                var smokeXs = Smokes.Length >= 3
                    ? new[] { mid - half * 0.85f, mid + 0.04f / worldS, mid + half * 0.90f }
                    : new[] { mid };
                var smokeScale = Smokes.Length >= 3
                    ? new[] { 1.00f, 1.22f, 0.88f }
                    : new[] { 1f };
                int sn = Mathf.Min(Smokes.Length, smokeXs.Length);
                for (int i = 0; i < sn; i++)
                {
                    var sm = Smokes[i];
                    sm.X = smokeXs[i];
                    sm.Y = yTop;
                    sm.Z = z - 0.04f / worldS;
                    sm.H = smokeH * smokeScale[i];
                    sm.W = smokeW * (0.90f + 0.12f * i);
                    Smokes[i] = sm;
                }
            }

            /// <summary>
            /// Tongues ON the fuselage. Vertex percentiles ignore the tail
            /// spike and the wing tips that inflate the AABB.
            /// </summary>
            void SitInHull()
            {
                if (hullPosed || Host == null) return;
                var xs = new List<float>(512);
                var ys = new List<float>(512);
                var zs = new List<float>(512);
                var filters = Host.GetComponentsInChildren<MeshFilter>(true);
                for (int i = 0; i < filters.Length; i++)
                {
                    var mf = filters[i];
                    if (mf.sharedMesh == null || IsFx(mf.transform, Host)) continue;
                    var verts = mf.sharedMesh.vertices;
                    int step = Mathf.Max(1, verts.Length / 500);
                    for (int v = 0; v < verts.Length; v += step)
                    {
                        var p = Host.InverseTransformPoint(mf.transform.TransformPoint(verts[v]));
                        xs.Add(p.x); ys.Add(p.y); zs.Add(p.z);
                    }
                }
                if (ys.Count < 8) return;
                float yBelly = Percentile(ys, 0.08f);
                float yRoof = Percentile(ys, 0.72f);
                float zLip = Percentile(zs, 0.62f);
                float worldS = Mathf.Max(Host.lossyScale.x, 0.01f);
                float hullWorld = Mathf.Max(0.20f, (yRoof - yBelly) * worldS);
                float flameH = Mathf.Clamp(Mathf.Max(0.70f, hullWorld * 1.10f), 0.70f, 1.15f) / worldS;
                float flameW = Mathf.Clamp(Mathf.Max(0.38f, hullWorld * 0.55f), 0.38f, 0.72f) / worldS;
                float smokeH = Mathf.Clamp(Mathf.Max(3.4f, hullWorld * 6.5f), 3.4f, 6.8f) / worldS;
                float smokeW = Mathf.Clamp(Mathf.Max(1.4f, hullWorld * 2.4f), 1.4f, 2.8f) / worldS;
                float z = zLip + 0.06f / worldS;
                float y = yRoof;
                // Two halves of the snapped hull. A single mid-span row sat
                // on the nose and left the tail cold.
                SplitHalves(xs, out float a0, out float a1, out float b0, out float b1);
                float da = Mathf.Max(0.04f, (a1 - a0) * 0.18f);
                float db = Mathf.Max(0.04f, (b1 - b0) * 0.18f);
                float am = (a0 + a1) * 0.5f;
                float bm = (b0 + b1) * 0.5f;
                var fxs = Fires.Length >= 4
                    ? new[] { am - da, am + da, bm - db, bm + db }
                    : new[] { am, bm };
                int n = Mathf.Min(Fires.Length, fxs.Length);
                for (int i = 0; i < n; i++)
                {
                    var f = Fires[i];
                    f.FlameH = flameH;
                    f.FlameW = flameW;
                    Fires[i] = f;
                    var root = f.Outer;
                    if (root == null) continue;
                    root = root.parent;
                    if (root == null) continue;
                    root.localPosition = new Vector3(fxs[i], y, z);
                }
                var smokeXs = Smokes.Length >= 4
                    ? new[] { am - da * 0.4f, am + da * 0.5f, bm - db * 0.5f, bm + db * 0.4f }
                    : new[] { am, bm };
                int sn = Mathf.Min(Smokes.Length, smokeXs.Length);
                for (int i = 0; i < sn; i++)
                {
                    var sm = Smokes[i];
                    sm.X = smokeXs[i];
                    sm.Y = y;
                    sm.Z = z - 0.03f / worldS;
                    sm.H = smokeH * (i % 2 == 1 ? 1.12f : 0.92f);
                    sm.W = smokeW * (0.90f + 0.08f * (i % 2));
                    Smokes[i] = sm;
                }
                hullPosed = true;
            }

            static void SplitHalves(List<float> xs, out float a0, out float a1, out float b0, out float b1)
            {
                var s = new List<float>(xs);
                s.Sort();
                int gapAt = s.Count / 2;
                float best = -1f;
                for (int i = 1; i < s.Count; i++)
                {
                    float g = s[i] - s[i - 1];
                    if (g > best) { best = g; gapAt = i; }
                }
                float span = s[s.Count - 1] - s[0];
                if (best < span * 0.12f)
                    gapAt = s.Count / 2;
                a0 = s[0]; a1 = s[Mathf.Max(0, gapAt - 1)];
                b0 = s[gapAt]; b1 = s[s.Count - 1];
            }

            static float Percentile(List<float> v, float p)
            {
                v.Sort();
                float i = (v.Count - 1) * Mathf.Clamp01(p);
                int lo = (int)i;
                int hi = Mathf.Min(lo + 1, v.Count - 1);
                return Mathf.Lerp(v[lo], v[hi], i - lo);
            }

            static void PoseTongue(Transform t, Vector2 flicker, float w, float h, float tongue)
            {
                float hh = h * tongue * flicker.y;
                t.localScale = new Vector3(w * tongue * flicker.x, hh, 1f);
                t.localPosition = new Vector3(0f, hh * 0.5f, t.localPosition.z);
            }
        }

        static List<Site> CollectMarks(Transform cityNear)
        {
            var list = new List<Site>();
            int id = 800;
            foreach (var t in cityNear.GetComponentsInChildren<Transform>(true))
            {
                if (t == cityNear || !t.name.StartsWith("fx_fire")) continue;
                var r = t.GetComponent<MeshRenderer>();
                if (r != null) r.enabled = false;
                var p = cityNear.InverseTransformPoint(t.position);
                // Marker is at the opening. Nudge it TOWARD the camera so the
                // tongue sits in the window mouth, not inside the wall volume
                // (where it is invisible) and not out in the street (y≈0, +z).
                float z = Mathf.Clamp(p.z + 0.40f, -0.05f, 0.18f);
                list.Add(new Site(p.x, p.y, z, 2.2f, 1.6f, 7.5f, 2.4f, id++));
            }
            if (list.Count > 12)
            {
                var thin = new List<Site>();
                for (int i = 0; i < 12; i++)
                    thin.Add(list[i * (list.Count - 1) / 11]);
                return thin;
            }
            return list;
        }

        public static Session Attach(Transform cityNear, Material fadeSource, List<Object> owned)
        {
            var session = new Session();
            if (cityNear == null || fadeSource == null) return session;

            var glowTex = GlowTex();
            var fireTex = FireTex();
            var smokeTex = SmokeTex();
            var glowMat = new Material(fadeSource) { color = Color.white };
            var fireMat = new Material(fadeSource) { color = Color.white };
            var smokeMat = new Material(fadeSource) { color = Color.white };
            glowMat.mainTexture = glowTex;
            fireMat.mainTexture = fireTex;
            smokeMat.mainTexture = smokeTex;
            glowMat.SetTexture("_BaseMap", glowTex);
            fireMat.SetTexture("_BaseMap", fireTex);
            smokeMat.SetTexture("_BaseMap", smokeTex);
            owned.Add(glowTex);
            owned.Add(fireTex);
            owned.Add(smokeTex);
            owned.Add(glowMat);
            owned.Add(fireMat);
            owned.Add(smokeMat);

            var fires = new List<Session.Fire>();
            var smokes = new List<Session.Smoke>();
            var marks = CollectMarks(cityNear);
            IEnumerable<Site> sites = marks.Count > 0 ? marks : Sites;
            foreach (var site in sites)
            {
                var glow = QuadMesh.Create($"RuinGlow_{site.Id}", cityNear, glowMat);
                glow.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
                glow.transform.localPosition = new Vector3(site.X, site.Y, Mathf.Min(site.Z, -1.2f));
                glow.transform.localScale = new Vector3(site.FireW, site.FireH, 1f);

                // Tongue in the WINDOW MOUTH: same x/y as the opening, z just
                // proud of the facade. Buried (z≪0) is invisible; y≈0 +z is the street.
                const float FlameH = 1.45f;
                const float FlameW = 0.95f;
                var root = new GameObject($"RuinFire_{site.Id}");
                root.transform.SetParent(cityNear, false);
                float flameY = site.Y > 2.2f ? 0.75f : site.Y;
                float flameZ = Mathf.Clamp(site.Z + 1.40f, -0.05f, 0.18f);
                root.transform.localPosition = new Vector3(site.X, flameY, flameZ);
                root.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
                var outer = QuadMesh.Create("outer", root.transform, fireMat);
                var inner = QuadMesh.Create("inner", root.transform, fireMat);
                inner.transform.localPosition = new Vector3(0f, 0f, -0.008f);

                fires.Add(new Session.Fire
                {
                    Glow = glow.transform,
                    GlowRenderer = glow.GetComponent<MeshRenderer>(),
                    Outer = outer.transform, Inner = inner.transform,
                    GlowH = site.FireH, GlowW = site.FireW,
                    FlameH = FlameH, FlameW = FlameW, Id = site.Id,
                });

                if (site.SmokeH <= 0.01f) continue;
                // Smoke rises from the rubble IN the block, then clears the roofline.
                float smokeY = 0.6f;
                float smokeZ = site.Z * 0.45f;
                smokes.Add(MakeSmoke($"RuinSmoke_{site.Id}", cityNear, smokeMat,
                    site.SmokeH, site.SmokeW, site.X, smokeY, smokeZ, site.Id));
            }

            session.Fires = fires.ToArray();
            session.Smokes = smokes.ToArray();
            session.Tick(0f);

            var driver = cityNear.gameObject.AddComponent<RuinFxDriver>();
            driver.Session = session;
            return session;
        }

        public readonly struct Kit
        {
            public readonly Material Fire, Smoke;
            public Kit(Material fire, Material smoke) { Fire = fire; Smoke = smoke; }
        }

        /// <summary>
        /// Shared fire/smoke materials for every wreck this level. Built once so
        /// two outposts do not mint two texture sets.
        /// </summary>
        public static Kit MakeKit(Material fadeSource, List<Object> owned)
        {
            if (fadeSource == null) return default;
            var fireTex = FireTex();
            var smokeTex = SmokeTex();
            // Ember, not a hero VFX. City tongues stay white — this kit
            // is wrecks only, so a mute here cannot flatten the strip.
            var fireMat = new Material(fadeSource) { color = new Color(0.95f, 0.48f, 0.16f, 0.90f) };
            var smokeMat = new Material(fadeSource) { color = new Color(0.58f, 0.55f, 0.52f, 0.62f) };
            fireMat.mainTexture = fireTex;
            smokeMat.mainTexture = smokeTex;
            fireMat.SetTexture("_BaseMap", fireTex);
            smokeMat.SetTexture("_BaseMap", smokeTex);
            owned.Add(fireTex);
            owned.Add(smokeTex);
            owned.Add(fireMat);
            owned.Add(smokeMat);
            return new Kit(fireMat, smokeMat);
        }

        /// <summary>
        /// Three tongues and three plumes on the wreck. SitOnPile moves them
        /// onto the live heap each tick (top, camera-proud) so a hangar
        /// pancake cannot bury them. Sizes are world-constant floors, then
        /// divided by lossyScale — a 2.5x hangar used to get a 0.40 candle.
        /// </summary>
        public static Session AttachWreck(Transform wreck, Kit kit, int seed,
                                         bool hull = false)
        {
            var session = new Session { FadeSeconds = hull ? 0f : 0.40f, Hull = hull };
            if (wreck == null || kit.Fire == null) return session;

            float s = Mathf.Max(wreck.lossyScale.x, 0.01f);
            float flameH = 0.95f / s;
            float flameW = 0.50f / s;
            float smokeH = 4.2f / s;
            float smokeW = 1.8f / s;
            float y = 0.40f / s;
            float z = 0.20f / s;
            float spread = 0.40f / s;

            var fires = new List<Session.Fire>();
            var smokes = new List<Session.Smoke>();
            int nFx = hull ? 4 : 3;
            var xs = hull
                ? new[] { -spread, -spread * 0.35f, spread * 0.35f, spread }
                : new[] { -spread, 0f, spread };
            for (int i = 0; i < nFx; i++)
            {
                int id = 900 + seed * 3 + i;
                var root = new GameObject($"WreckFire_{id}");
                root.transform.SetParent(wreck, false);
                root.transform.localPosition = new Vector3(xs[i], y, z);
                root.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
                var outer = QuadMesh.Create("outer", root.transform, kit.Fire);
                var inner = QuadMesh.Create("inner", root.transform, kit.Fire);
                inner.transform.localPosition = new Vector3(0f, 0f, -0.008f);
                fires.Add(new Session.Fire
                {
                    Outer = outer.transform, Inner = inner.transform,
                    FlameH = flameH, FlameW = flameW, Id = id,
                });
            }

            for (int i = 0; i < nFx; i++)
            {
                int smokeId = 960 + seed * 3 + i;
                smokes.Add(MakeSmoke($"WreckSmoke_{smokeId}", wreck, kit.Smoke,
                    smokeH, smokeW, xs[i], y, z, smokeId));
            }

            session.Fires = fires.ToArray();
            session.Smokes = smokes.ToArray();
            session.Host = wreck;
            var driver = wreck.gameObject.AddComponent<RuinFxDriver>();
            driver.Session = session;
            return session;
        }

        static Session.Smoke MakeSmoke(string name, Transform parent, Material mat,
                                       float h, float w, float x, float y, float z, int id)
        {
            GameObject Puff(string n)
            {
                var go = QuadMesh.Create(n, parent, mat);
                go.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
                return go;
            }
            var a = Puff(name);
            var b = Puff(name + "_b");
            var c = Puff(name + "_c");
            return new Session.Smoke
            {
                Quad = a.transform, Quad2 = b.transform, Quad3 = c.transform,
                Rend = a.GetComponent<MeshRenderer>(),
                Rend2 = b.GetComponent<MeshRenderer>(),
                Rend3 = c.GetComponent<MeshRenderer>(),
                H = h, W = w, X = x, Y = y, Z = z, Id = id,
            };
        }

        /// <summary>The incendiary tongue. Same formula as the soldier flame so
        /// a zoomed-in pocket is fire, not a square.</summary>
        public static Texture2D FireTex()
        {
            const int Size = 64;
            var tex = new Texture2D(Size, Size, TextureFormat.RGBA32, false)
            { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            var tip = new Color(0.96f, 0.28f, 0.06f);
            var core = new Color(1f, 0.90f, 0.42f);
            for (int y = 0; y < Size; y++)
            {
                float t = (y + 0.5f) / Size;
                float lean = 0.11f * t * t;
                float neck = 0.34f + 0.66f * Mathf.Min(1f, t / 0.20f);
                float taper = Mathf.Pow(Mathf.Max(0f, 1f - t), 0.85f);
                float w = 0.50f * neck * taper;
                for (int x = 0; x < Size; x++)
                {
                    float dx = (x + 0.5f) / Size - 0.5f - lean;
                    float a = 0f;
                    if (w > 1e-4f)
                    {
                        float e = Mathf.Abs(dx) / w;
                        a = e >= 1f ? 0f : e < 0.62f ? 1f : 1f - (e - 0.62f) / 0.38f;
                    }
                    a *= Mathf.Clamp01((1f - t) / 0.34f);
                    a *= Mathf.Clamp01(0.55f + t / 0.12f);
                    float hot = Mathf.Clamp01((1f - t) * (1f - t));
                    var c = Color.Lerp(tip, core, hot);
                    tex.SetPixel(x, y, new Color(c.r, c.g, c.b, a));
                }
            }
            tex.Apply();
            return tex;
        }

        /// <summary>Soft interior firelight. Falloff is the shape — a hard quad
        /// behind a hole still reads as a window pane.</summary>
        public static Texture2D GlowTex()
        {
            const int Size = 48;
            var tex = new Texture2D(Size, Size, TextureFormat.RGBA32, false)
            { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            var hot = new Color(1.00f, 0.72f, 0.22f);
            var ember = new Color(0.85f, 0.28f, 0.05f);
            for (int y = 0; y < Size; y++)
            for (int x = 0; x < Size; x++)
            {
                float dx = (x + 0.5f) / Size - 0.5f;
                float dy = (y + 0.5f) / Size - 0.5f;
                float d = Mathf.Sqrt(dx * dx + dy * dy * 0.85f) * 2f;
                float a = 1f - Threshold(0.08f, 1f, d);
                float core = 1f - Threshold(0.00f, 0.50f, d);
                var c = Color.Lerp(ember, hot, core);
                tex.SetPixel(x, y, new Color(c.r, c.g, c.b, a * (0.40f + 0.60f * core)));
            }
            tex.Apply();
            return tex;
        }

        static float Threshold(float edge0, float edge1, float x)
        {
            float t = Mathf.Clamp01((x - edge0) / (edge1 - edge0));
            return t * t * (3f - 2f * t);
        }

        public static Texture2D SmokeTex()
        {
            const int Size = 64;
            var tex = new Texture2D(Size, Size, TextureFormat.RGBA32, false)
            { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            var ash = new Color(0.62f, 0.60f, 0.57f);
            for (int y = 0; y < Size; y++)
            for (int x = 0; x < Size; x++)
            {
                float u = (x + 0.5f) / Size;
                float v = (y + 0.5f) / Size;
                // Overlapping blobs, no vertical density bias — a column
                // texture was opaque at the foot, so every puff read as a
                // dark slab with a pale top.
                float a = Blob(u, v, 0.50f, 0.50f, 0.44f, 0.42f);
                a = Mathf.Max(a, Blob(u, v, 0.36f, 0.56f, 0.28f, 0.26f) * 0.82f);
                a = Mathf.Max(a, Blob(u, v, 0.64f, 0.42f, 0.26f, 0.28f) * 0.78f);
                a = Mathf.Max(a, Blob(u, v, 0.48f, 0.34f, 0.24f, 0.22f) * 0.70f);
                tex.SetPixel(x, y, new Color(ash.r, ash.g, ash.b, a * 0.55f));
            }
            tex.Apply();
            return tex;
        }

        static float Blob(float x, float y, float cx, float cy, float rx, float ry)
        {
            float u = (x - cx) / rx;
            float v = (y - cy) / ry;
            float d = Mathf.Sqrt(u * u + v * v);
            if (d >= 1f) return 0f;
            return 1f - Threshold(0.12f, 1f, d);
        }
    }

    public sealed class RuinFxDriver : MonoBehaviour
    {
        public RuinFx.Session Session;
        void OnEnable()
        {
            if (Session != null) Session.Restart();
        }
        void LateUpdate()
        {
            if (Session != null) Session.Tick(Time.time);
        }
    }
}
