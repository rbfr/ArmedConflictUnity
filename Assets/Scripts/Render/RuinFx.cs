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
                public Transform Quad;
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
            MaterialPropertyBlock props;

            public void Restart() => BornAt = Time.time;

            public void Tick(float time)
            {
                if (Host != null) SitOnPile();
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
                    float phase = CosmeticSystems.FlamePhase(sm.Id + 90);
                    float wiggle = Mathf.Sin(time * SmokeHz * Mathf.PI * 2f + phase);
                    float lean = Mathf.Sin(time * SmokeLeanHz * Mathf.PI * 2f + phase * 1.3f);
                    float h = sm.H * fade * (1f + 0.08f * wiggle);
                    float w = sm.W * fade * (1f + 0.10f * wiggle);
                    sm.Quad.localScale = new Vector3(w, h, 1f);
                    sm.Quad.localPosition = new Vector3(sm.X + lean * sm.H * 0.05f, sm.Y + h * 0.45f, sm.Z);
                }
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
            /// GarrisonPost's leftover mass sits on the origin and ate the
            /// old feet-of-the-wreck tongues. Sample the masonry (not the
            /// FX) and nestle fire IN the live pile — follows the collapse
            /// down. Proud of the camera-facing lip is a row of candles
            /// in the street; the leftover origin swallows them.
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
                // +Z is toward the camera (GameSpace). Pull the tongue INTO
                // the front band so rubble frames it, the way the city
                // facade frames a window fire. Do not go deep: the leftover
                // cube will swallow anything near the origin.
                float depth = Mathf.Max(0.01f, z1 - z0);
                float z = z1 - Mathf.Clamp(depth * 0.18f, 0.06f, 0.16f);
                float y = Mathf.Max(0.06f, y0 + (y1 - y0) * 0.08f);
                float mid = (x0 + x1) * 0.5f;
                float half = Mathf.Max(0.10f, (x1 - x0) * 0.20f);
                // Stagger so two wrecks do not draw a picket of six.
                var xs = Fires.Length >= 3
                    ? new[] { mid - half, mid + half * 0.15f, mid + half }
                    : new[] { mid - half, mid + half };
                var ys = Fires.Length >= 3
                    ? new[] { y, y + 0.05f, y - 0.02f }
                    : new[] { y, y + 0.03f };
                var zs = Fires.Length >= 3
                    ? new[] { z + 0.03f, z - 0.05f, z + 0.01f }
                    : new[] { z, z - 0.03f };
                int n = Mathf.Min(Fires.Length, xs.Length);
                for (int i = 0; i < n; i++)
                {
                    var root = Fires[i].Outer;
                    if (root == null) continue;
                    root = root.parent;
                    if (root == null) continue;
                    root.localPosition = new Vector3(xs[i], ys[i], zs[i]);
                }
                if (Smokes.Length > 0)
                {
                    var sm = Smokes[0];
                    sm.X = mid;
                    sm.Y = y;
                    // Smoke rises from inside the pile, not in front of it.
                    sm.Z = z0 + depth * 0.40f;
                    Smokes[0] = sm;
                }
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
                var plume = QuadMesh.Create($"RuinSmoke_{site.Id}", cityNear, smokeMat);
                plume.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
                plume.transform.localPosition = new Vector3(site.X, smokeY, smokeZ);
                smokes.Add(new Session.Smoke
                {
                    Quad = plume.transform, H = site.SmokeH, W = site.SmokeW,
                    X = site.X, Y = smokeY, Z = smokeZ, Id = site.Id,
                });
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
            var fireMat = new Material(fadeSource) { color = new Color(0.72f, 0.42f, 0.22f, 0.62f) };
            var smokeMat = new Material(fadeSource) { color = new Color(0.85f, 0.82f, 0.78f, 0.70f) };
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
        /// Two tongues and a plume in the wreck pile. Same art as the city strip
        /// (L4), sized against the wreck's world scale so a 2.5x outpost does
        /// not get a city-block fire. Z is toward the camera — the 6° trap is
        /// the same as the street: fire at the feet with +z reads as a square
        /// on the dirt if it is too far forward.
        /// </summary>
        public static Session AttachWreck(Transform wreck, Kit kit, int seed)
        {
            var session = new Session { FadeSeconds = 0.40f };
            if (wreck == null || kit.Fire == null) return session;

            float s = Mathf.Max(wreck.lossyScale.x, 0.01f);
            // Half the first pass. Six 0.78 tongues on the lip read as a
            // foreground row; these nestle in the rubble.
            float flameH = 0.40f / s;
            float flameW = 0.26f / s;
            float smokeH = 1.70f / s;
            float smokeW = 0.70f / s;
            float y = 0.12f / s;
            float z = 0.12f / s;
            float spread = 0.28f / s;

            var fires = new List<Session.Fire>();
            var smokes = new List<Session.Smoke>();
            var xs = new[] { -spread, 0f, spread };
            for (int i = 0; i < xs.Length; i++)
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

            int smokeId = 900 + seed * 3 + 2;
            var plume = QuadMesh.Create($"WreckSmoke_{smokeId}", wreck, kit.Smoke);
            plume.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
            plume.transform.localPosition = new Vector3(0.04f / s, y, z * 0.5f);
            smokes.Add(new Session.Smoke
            {
                Quad = plume.transform, H = smokeH, W = smokeW,
                X = 0.04f / s, Y = y, Z = z * 0.5f, Id = smokeId,
            });

            session.Fires = fires.ToArray();
            session.Smokes = smokes.ToArray();
            session.Host = wreck;
            var driver = wreck.gameObject.AddComponent<RuinFxDriver>();
            driver.Session = session;
            return session;
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
            const int Size = 48;
            var tex = new Texture2D(Size, Size, TextureFormat.RGBA32, false)
            { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            var ash = new Color(0.42f, 0.38f, 0.34f);
            for (int y = 0; y < Size; y++)
            {
                float t = (y + 0.5f) / Size;
                float half = 0.26f + 0.34f * t;
                float dens = 0.55f * Mathf.Pow(1f - t, 0.95f);
                float drift = 0.10f * t * Mathf.Sin(t * 7.3f);
                for (int x = 0; x < Size; x++)
                {
                    float dx = Mathf.Abs((x + 0.5f) / Size - 0.5f - drift) / half;
                    float edge = dx >= 1f ? 0f : dx < 0.45f ? 1f
                        : 1f - (dx - 0.45f) / 0.55f;
                    tex.SetPixel(x, y, new Color(ash.r, ash.g, ash.b, dens * edge));
                }
            }
            tex.Apply();
            return tex;
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
