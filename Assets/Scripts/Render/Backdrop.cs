using System.Collections.Generic;
using UnityEngine;
using ArmedConflict.Data;

namespace ArmedConflict.Render
{
    /// <summary>
    /// The backdrop's DESIGN, engine-side but MonoBehaviour-free: for a biome style it returns a
    /// list of layers, each already reduced to a sampled height profile in world units. The
    /// editor/scene side only turns those numbers into meshes, materials and GameObjects.
    ///
    /// The first version drew each layer as a row of INDEPENDENT isosceles triangles, which is
    /// what made the mountains read as pyramids: identical symmetric wedges, and — because
    /// neighbouring wedges only overlap part-way up — a dead-flat plateau where their slopes
    /// crossed, so the range sat on a featureless wall. A ridge is ONE continuous silhouette;
    /// modelling it as a sampled profile gives asymmetric crests, subsidiary shoulders, and
    /// valleys that run all the way down to the ground line, for the same triangle count.
    ///
    /// Profiles are normalised to [0,1] AFTER sampling (see Normalize), so the tallest crest is
    /// exactly `Height` and the lowest valley is exactly `BaseY`. That removes all guesswork
    /// about the raw range of a noise function, and it is why `BaseY` sits BELOW the ground
    /// plane: the bottom slice of every layer is occluded by the 3D ground at its own depth, so
    /// valleys meet the ground instead of stopping on a visible ledge.
    /// </summary>
    public static class Backdrop
    {
        // NEGATIVE z. The camera sits at +camZ looking toward -Z, so positive z is BEHIND the
        // viewer — the first attempt put the whole backdrop at the camera's back and rendered
        // nothing at all.
        public const float SkyZ = -120f;
        public const float FarZ = -45f;
        public const float MidZ = -38f;
        public const float NearZ = -30f;

        /// <summary>
        /// FOREGROUND — the only backdrop layer at POSITIVE z, i.e. between the camera and the
        /// play plane at z = 0. It must sit inside <see cref="Game.CameraDirector.ZMin"/> (5.5)
        /// or a tight frame would put the camera BEHIND it and it would vanish or clip through.
        /// 3 leaves margin under that floor at every framing the director can choose.
        /// </summary>
        public const float ForeZ = 3f;

        /// <summary>Roughly the gameplay camera's z, so a layer's DISTANCE is |z| + this.</summary>
        public const float CameraZ = 11f;

        /// <summary>
        /// The test device, and the aspect every layer's width is solved against. The build is
        /// PORTRAIT-LOCKED, so this is a narrow range, and the 1.35x width margin covers the
        /// spread. Do NOT read `Screen` when baking a scene: batchmode reports a placeholder
        /// desktop resolution, and a landscape aspect makes every layer ~3x too wide — which
        /// puts a third as many crests on screen and brings the giant-wedge look straight back.
        /// </summary>
        public const float DesignAspect = 1080f / 2404f;

        /// <summary>Which of the biome's two silhouette colours a layer wears.</summary>
        public enum Tint { Far, Mid, Near, Accent }

        public sealed class Layer
        {
            public string Name;
            public float Z;
            public float BaseY;
            public float Height;
            public float Width;
            public Tint Colour;
            /// <summary>Sampled 0..1 height fractions, evenly spaced across Width.</summary>
            public float[] Profile;
            /// <summary>Fraction of Height above which snow lies. Negative = no snow.</summary>
            public float SnowLine = -1f;
            /// <summary>
            /// Per-column snowline, as a fraction of Height — the wandering the eye expects of a
            /// real snowline. A single flat value reads as a ruler laid across the range, and
            /// jittering it with a plain sine (the first attempt) is worse: the regular period
            /// turns the whole boundary into a scalloped wave, which reads as surf.
            /// </summary>
            public float[] SnowEdge;
            /// <summary>True = a flat band filled with a Far→Near vertical gradient (the sea).</summary>
            public bool Gradient;
            /// <summary>Lit windows, in layer-local units: (x, y, width, height).</summary>
            public Vector4[] Windows = System.Array.Empty<Vector4>();
            /// <summary>Colour for those decals. Null = the city's lit-window orange.</summary>
            public Color? DecalColour;
            /// <summary>Sun disc centre, layer-local. Radius &lt;= 0 means the layer has no sun.
            /// It is a LAYER property rather than its own layer so it inherits the layer's depth
            /// and therefore parallaxes with the water it is sitting over.</summary>
            public Vector2 SunAt;
            public float SunRadius = -1f;
        }

        /// <summary>
        /// Visible width at depth z. A layer has to be sized against the FRUSTUM AT ITS OWN
        /// DEPTH, not authored in absolute units: at z = -90 the visible width is about 90
        /// units, so a 460-wide range with 7 peaks put roughly ONE peak on screen and a mountain
        /// range read as a single giant wedge. VFOV is 90 (tan(half) = 1), so the vertical
        /// half-extent at distance d is exactly d and the horizontal is d * aspect.
        /// </summary>
        public static float VisibleWidthAt(float z, float aspect)
            => 2f * (Mathf.Abs(z) + CameraZ) * aspect;

        /// <summary>Screen-height fraction a layer of this height occupies at this depth.</summary>
        public static float AngularHeight(float height, float z)
            => height / (2f * (Mathf.Abs(z) + CameraZ));

        // 1.35x the visible width so a range runs off both edges rather than ending mid-frame,
        // and so a camera pan during a volley never reaches the end of one.
        const float WidthMargin = 1.35f;

        // Terrain reads fine at ~0.2 world units per column. City and forest need far more:
        // both are built from silhouettes with VERTICAL sides, and a column is the finest edge
        // the strip can express, so a coarse sampling bevels every wall and trunk.
        const int TerrainColumns = 384;
        const int EdgedColumns = 896;

        public static List<Layer> Plan(SilhouetteStyle style, float aspect) => style switch
        {
            SilhouetteStyle.Mountains => Mountains(aspect, snowLine: 0.82f),
            // Winter is the same range in an ice palette, with the snowline dropped well down the
            // face — the palette alone reads as "cold rock", not as snow.
            SilhouetteStyle.Winter => Mountains(aspect, snowLine: 0.58f),
            SilhouetteStyle.Desert => Dunes(aspect),
            SilhouetteStyle.Forest => Forest(aspect),
            SilhouetteStyle.City => City(aspect),
            SilhouetteStyle.Ocean => Ocean(aspect),
            _ => Mountains(aspect, snowLine: 0.62f),
        };

        // --- Mountains -------------------------------------------------------------------
        //
        // The near row is FOOTHILLS, not a second range. The first version gave the two rows
        // almost the same angular height (0.39 far vs 0.32 near), so the pale haze layer and the
        // dark near layer cut across each other at the same scale and neither read as being
        // behind the other — the depth cue in the colour was contradicted by the depth cue in
        // the size. Near is now a little over HALF the far range's angular height, which is what
        // lets the pale peaks read as distance rather than as translucent glass.
        static List<Layer> Mountains(float aspect, float snowLine)
        {
            var far = Ridge("RidgeFar", FarZ, aspect, height: 17f, cycles: 10f, floor: 0.30f,
                            octaves: 4, seed: 401);
            far.Colour = Tint.Far;
            SnowCap(far, snowLine, cycles: 10f, seed: 401);

            // Fewer, broader, calmer than the range behind it. At four octaves the foothills grew
            // as much fine detail as the mountains and the two layers read as one busy texture —
            // detail belongs to the layer the eye is meant to look at.
            var near = Ridge("RidgeNear", NearZ, aspect, height: 8f, cycles: 5.5f, floor: 0.34f,
                             octaves: 3, seed: 523);
            near.Colour = Tint.Near;
            // No snow on the foothills: a cap on a hill a third the height of the range behind
            // it says the two are the same size, which is the error the heights just fixed.

            return new List<Layer> { far, near };
        }

        static Layer Ridge(string name, float z, float aspect, float height, float cycles,
                           float floor, int octaves, int seed)
        {
            float width = VisibleWidthAt(z, aspect) * WidthMargin;
            var profile = new float[TerrainColumns];
            for (int i = 0; i < profile.Length; i++)
            {
                float x = (float)i / (profile.Length - 1);
                profile[i] = RidgedFbm(x * cycles, seed, octaves);
            }
            // A range has a CONTINUOUS BODY that its peaks rise out of. Normalising to a floor of
            // 0 instead let the valleys drop to nothing, and the two layers then read as two
            // separate groups of peaks standing in different parts of the frame rather than as
            // one range behind another — the depth ordering was correct and invisible.
            Normalize(profile, floor);
            return new Layer
            {
                Name = name, Z = z, Width = width, Height = height,
                // A tenth of the range is parked under the ground plane, so the body closes
                // against the ground instead of ending on a visible ledge.
                BaseY = -height * 0.10f,
                Profile = profile,
            };
        }

        /// <summary>
        /// Puts snow on a ridge, as a wandering line rather than a level. The line is high on
        /// purpose: with the range normalised to a floor, a snowline at 0.62 of the layer height
        /// covered most of the visible rock and read as a bank of cloud sitting on the range —
        /// snow has to be a CAP on the few crests that earn it, or it stops meaning altitude.
        /// </summary>
        static void SnowCap(Layer layer, float snowLine, float cycles, int seed)
        {
            layer.SnowLine = snowLine;
            layer.SnowEdge = new float[layer.Profile.Length];
            for (int i = 0; i < layer.SnowEdge.Length; i++)
            {
                float x = (float)i / (layer.SnowEdge.Length - 1) * cycles;
                layer.SnowEdge[i] = snowLine + 0.05f * Fbm(x * 1.7f, seed + 77, octaves: 2);
            }
        }

        // --- Desert ----------------------------------------------------------------------
        // Dunes are the same machinery with smooth (non-ridged) noise and a DOMAIN WARP: each
        // sample is taken at x + k*h(x), which drags every crest downwind and leaves a long
        // windward slope against a short lee face. Symmetric humps read as hills, not sand.
        static List<Layer> Dunes(float aspect)
        {
            // Long backs and real amplitude. The first pass ran 6 units of height against a 0.42
            // floor, which left 3.5 units of actual relief at 21 px each — a texture on the
            // horizon rather than a landform, and against a sand-coloured sky it disappeared.
            var far = Dune("DuneFar", FarZ, aspect, height: 9f, cycles: 3.5f, seed: 601);
            far.Colour = Tint.Far;
            var near = Dune("DuneNear", NearZ, aspect, height: 6f, cycles: 2.2f, seed: 733);
            near.Colour = Tint.Near;
            return new List<Layer> { far, near };
        }

        static Layer Dune(string name, float z, float aspect, float height, float cycles, int seed)
        {
            float width = VisibleWidthAt(z, aspect) * WidthMargin;
            var profile = new float[TerrainColumns];
            for (int i = 0; i < profile.Length; i++)
            {
                float x = (float)i / (profile.Length - 1) * cycles;
                float warp = Fbm(x, seed, octaves: 2);
                profile[i] = Fbm(x + 0.35f * warp, seed, octaves: 3);
            }
            // Sand is continuous — a dune field never shows a gap down to the horizon.
            Normalize(profile, floor: 0.30f);
            return new Layer
            {
                Name = name, Z = z, Width = width, Height = height,
                BaseY = -height * 0.18f, Profile = profile,
            };
        }

        // --- Forest ----------------------------------------------------------------------
        // A canopy is the MAXIMUM over the trees standing in it, which is what makes a treeline
        // continuous at the base and jagged along the top with no gaps to manage. Each pine is
        // two tents — a wide shoulder and a narrow spire — so the profile steps rather than
        // running straight from tip to ground.
        //
        // A distant ridge, then two rows of trees growing TOWARD the viewer. The ridge is not
        // decoration: with only treelines the three rows sat on the same ground line at nearly
        // the same screen height and fused into one green band, so the layering bought nothing.
        // A forested country needs something for the far woods to stand ON.
        static List<Layer> Forest(float aspect)
        {
            // THE TREES MUST CARRY THE MASS. An earlier pass made the hills TALLER than the
            // treeline (15 units against 11) to stop the ridge hiding behind the woods, and won
            // that argument at the cost of the biome: a big pale ridge dominated the silhouette
            // and the whole thing read as GREEN MOUNTAINS with a spiky fringe, on the one
            // campaign level that uses it. Judge this layer set by which band owns the skyline.
            //
            // Ordered by ANGULAR height (height / |z|), which is what the eye actually compares:
            //   hills 0.22  <  mid trees 0.30  <  near trees 0.42
            // The hills now sit UNDER the crowns and show through the gaps between them, which is
            // what a wooded ridge looks like — they are a backdrop mass, not the subject. That is
            // the opposite of the Mountains plan, where the far range is meant to out-reach the
            // near foothills, and the difference is the point of having two plans.
            var hills = Ridge("ForestHills", FarZ, aspect, height: 15f, cycles: 3.5f, floor: 0.40f,
                              octaves: 3, seed: 89);
            hills.Colour = Tint.Far;

            // MANY NARROW trees, not a few wide ones. Nine crowns spanning the frame makes each
            // one an eighth of the screen across, and a triangle that wide is a hill however it
            // is shaded — which is the other half of why this read as mountains. The opposite
            // failure is on record too (crowns so narrow the row read as GRASS), so the crown
            // SHAPE below is left alone; only the count and the heights move.
            var mid = Treeline("TreesMid", MidZ, aspect, height: 11.5f, trees: 26, seed: 211,
                                 crownScale: 1.35f, floor: 0.62f);
            mid.Colour = Tint.Mid;
            var near = Treeline("TreesNear", NearZ, aspect, height: 12.5f, trees: 17, seed: 307,
                                  crownScale: 1.4f, floor: 0.50f);
            near.Colour = Tint.Near;
            return new List<Layer> { hills, mid, near };
        }

        /// <param name="crownScale">Widens every crown. A conifer silhouette at this distance is
        /// roughly half as wide as it is tall; at 1.0 with a high tree count the spire comes out
        /// nearer a fifth, and a row of those is REEDS. Widen the crown rather than thinning the
        /// row — dropping the count instead just walks back toward the wide-triangle "hills"
        /// failure at the other end.</param>
        /// <param name="floor">Understorey: the fraction of Height that is solid canopy mass
        /// under the crowns. This is what makes a WOOD rather than a row of separate trees — at
        /// 0.35 the sky came down between every pair of crowns and the band read as a fringe.</param>
        static Layer Treeline(string name, float z, float aspect, float height, int trees, int seed,
                              float crownScale = 1f, float floor = 0.35f)
        {
            float width = VisibleWidthAt(z, aspect) * WidthMargin;
            float pitch = 1f / trees;
            var profile = new float[EdgedColumns];

            for (int t = 0; t < trees; t++)
            {
                // Jitter along the row, but never past a neighbour: a treeline that crosses
                // itself reads as one clump and one hole.
                float cx = (t + 0.5f) * pitch + (Hash01(seed + t * 13) - 0.5f) * pitch * 0.55f;
                // Crowns OVERLAP their neighbours (halfW ≈ a whole pitch) and vary by more than
                // 2x in height. Narrow crowns of near-equal height, evenly spaced, are exactly
                // the recipe for a saw blade — which is what "reads as grass" actually meant.
                float h = 0.42f + Hash01(seed + t * 7) * 0.58f;
                float halfW = pitch * (0.85f + Hash01(seed + t * 5) * 0.35f) * crownScale;
                for (int i = 0; i < profile.Length; i++)
                {
                    float x = (float)i / (profile.Length - 1);
                    float d = Mathf.Abs(x - cx);
                    // A pine is a broad skirt under a narrower spire. The first pass had the
                    // skirt at 0.66 of the height and 0.46 of the width, which is a needle with
                    // barely any shoulder — a row of them read as GRASS, not as a treeline.
                    float skirt = h * 0.80f * Mathf.Clamp01(1f - d / halfW);
                    float spire = h * Mathf.Clamp01(1f - d / (halfW * 0.62f));
                    profile[i] = Mathf.Max(profile[i], Mathf.Max(skirt, spire));
                }
            }
            // Woods seen from outside show a solid dark band beneath the crowns — the trunks and
            // the trees behind them. Without it every gap between two trees cut to the horizon
            // and the row read as a comb.
            // ...and the top of that mass WANDERS. A constant floor is a ruler laid across the
            // frame — the same failure a flat snowline has, and worse here because it runs the
            // full width. Rolling it turns the band into canopy the crowns rise out of.
            for (int i = 0; i < profile.Length; i++)
            {
                float x = (float)i / (profile.Length - 1);
                float wander = Mathf.PerlinNoise(x * 4.5f, seed * 0.017f);
                // Clamped: the wander multiplies up to 1.22x, so a floor above ~0.82 would push
                // the profile past 1 and break the [0,1] contract every consumer relies on.
                profile[i] = Mathf.Max(profile[i], Mathf.Min(1f, floor * (0.78f + 0.44f * wander)));
            }

            return new Layer
            {
                Name = name, Z = z, Width = width, Height = height,
                // Trees stand ON the ground, so the row's base is only just under it — sinking
                // a treeline the way a ridge is sunk would saw the trunks off.
                BaseY = -height * 0.06f,
                Profile = profile,
            };
        }

        // --- City ------------------------------------------------------------------------
        // FALLBACK / TEST CONTRACT. The live CityRuins skyline is the authored GLB pair
        // (backdrop_city_far / _near) placed by BackdropRuntime. This profile still has to
        // produce a legal Plan — PortSelfTest walks every style — and it is what draws if
        // those meshes are missing. Do not delete it to "use the GLB"; the GLB does not
        // go through Plan.
        //
        // Burned-out blocks: the same max-of-elements profile, but each element is a rectangle
        // whose roofline is bitten into three ragged steps. Sparse ember windows go on the near
        // row only — on the far row they would be single lit pixels in the haze.
        static List<Layer> City(float aspect)
        {
            var far = Blocks("CityFar", FarZ, aspect, height: 13f, blocks: 20, seed: 811, windows: false);
            far.Colour = Tint.Far;
            var near = Blocks("CityNear", NearZ, aspect, height: 16f, blocks: 12, seed: 907, windows: true);
            near.Colour = Tint.Near;
            return new List<Layer> { far, near };
        }

        static Layer Blocks(string name, float z, float aspect, float height, int blocks, int seed, bool windows)
        {
            float width = VisibleWidthAt(z, aspect) * WidthMargin;
            float pitch = 1f / blocks;
            var profile = new float[EdgedColumns];
            var lit = new List<Vector4>();

            for (int b = 0; b < blocks; b++)
            {
                float cx = (b + 0.5f) * pitch + (Hash01(seed + b * 13) - 0.5f) * pitch * 0.3f;
                // Heights are skewed LOW so a few towers stand over a lot of low blocks. A flat
                // 0.42..1.0 spread gave every block much the same height, and evenly spaced
                // same-height blocks with a sky gap between each read as a PICKET FENCE.
                float h = 0.30f + 0.70f * Mathf.Pow(Hash01(seed + b * 7), 1.9f);
                // Widths vary by nearly 3x, so neighbours sometimes merge into one mass instead
                // of every block standing alone.
                float halfW = pitch * (0.24f + Hash01(seed + b * 5) * 0.44f);

                for (int i = 0; i < profile.Length; i++)
                {
                    float x = (float)i / (profile.Length - 1);
                    float d = x - cx;
                    if (Mathf.Abs(d) > halfW) continue;
                    // Three steps across the roof, each bitten down by its own amount.
                    int step = Mathf.Clamp((int)((d + halfW) / (2f * halfW) * 3f), 0, 2);
                    float nick = 0.06f + 0.22f * Hash01(seed + b * 31 + step * 7);
                    profile[i] = Mathf.Max(profile[i], h * (1f - nick));
                }

                if (!windows) continue;
                for (int wi = 0; wi < 12; wi++)
                {
                    if (Hash01(seed + b * 101 + wi * 17) > 0.24f) continue;  // most rooms are dead
                    float wx = (cx - halfW * 0.6f + (wi % 3) * halfW * 0.6f) * width - width / 2f;
                    float wy = (h * 0.72f - (wi / 3) * h * 0.17f) * height;
                    lit.Add(new Vector4(wx, wy, halfW * width * 0.28f, height * 0.022f));
                }
            }
            // A shelled district is not a row of towers on bare ground: the low rubble line is
            // what makes the standing blocks read as what is LEFT of something.
            for (int i = 0; i < profile.Length; i++) profile[i] = Mathf.Max(profile[i], 0.12f);

            return new Layer
            {
                Name = name, Z = z, Width = width, Height = height,
                BaseY = -height * 0.05f, Profile = profile, Windows = lit.ToArray(),
            };
        }

        // --- Ocean -----------------------------------------------------------------------
        // The sea takes the DISTANCE band the silhouettes occupy in every other biome: the
        // troops stand on the sand painted below it and are never "in" the water. Deep at the
        // sea horizon, brightening toward the shallows, so it is one gradient band rather than
        // a profile.
        // --- Ocean ---------------------------------------------------------------------
        //
        // Ported from the Filament build's drawOcean, which is a PAINTED 2D scene: sea gradient,
        // a sun with a radial glow, a scattered sun-glitter path, drifting ripple rows, and a
        // scalloped foam line where the surf laps the sand. The port had only the gradient band,
        // which is why the biome read as three flat stripes.
        //
        // What carries over and what does not: the sun, the glitter and the surf are all shape,
        // so they port. The RIPPLE DRIFT does not need porting at all — the Filament version
        // scrolls each row by a hand-tuned fraction of the world's pixels-per-unit, and here the
        // layers sit at real depths, so a camera pan parallaxes them for free.
        static List<Layer> Ocean(float aspect)
        {
            float seaW = VisibleWidthAt(FarZ, aspect) * WidthMargin;
            const float SeaTop = 7.5f;      // BaseY + Height, i.e. the sea horizon in world units

            var flat = new float[8];
            for (int i = 0; i < flat.Length; i++) flat[i] = 1f;

            var sea = new Layer
            {
                Name = "Sea", Z = FarZ, Width = seaW,
                Height = 9f, BaseY = SeaTop - 9f, Profile = flat, Colour = Tint.Far,
                Gradient = true,
                // The sun sits just ABOVE the sea horizon and RIGHT of centre, as it does in the
                // Filament build (0.76 of the frame). Unity's +X is screen LEFT here — the camera
                // looks down -Z from +Z — so right of centre is NEGATIVE x.
                //
                // OFFSET FROM THE WORLD ORIGIN, NOT FROM THE FRAME. The backdrop is world-fixed
                // and the camera is not: at Aiming it sits over the PLAYER LINE, around game
                // x -9.5, i.e. Unity +9.5. Placed at a frame-relative-looking -0.20 of the sea
                // width the sun ended up 92% of a half-frame right of that centre and was cut in
                // half by the screen edge on device — while looking perfectly placed in the
                // preview, which renders from x = 0. Judge any fixed backdrop feature at the
                // camera position the PLAYER sees, and leave it room to travel: this is a real
                // parallax pan, so the sun crosses the frame as the volley camera moves.
                SunAt = new Vector2(-seaW * 0.06f, SeaTop + 3.4f),
                SunRadius = seaW * 0.10f,
            };

            // Sun-glitter. Deliberately NOT a continuous wash (which read as a spotlight beam)
            // and NOT an evenly spaced ladder (which read as a dotted line): sparse glints that
            // fade, shorten and drift off the centreline as they come toward the shore. They also
            // cluster in the top of the water — a trail running all the way down to the beach was
            // the dotted-line failure.
            var glints = new List<Vector4>();
            float sunX = -seaW * 0.06f;
            for (int d = 0; d < 7; d++)
            {
                float t = (d + 0.2f + Hash01(d * 17 + 5) * 0.6f) / 7f;
                float y = SeaTop - 0.5f - 4.2f * t;
                float halfLen = seaW * (0.006f + 0.016f * t) * (0.6f + 0.8f * Hash01(d * 23 + 11));
                float xOff = (Hash01(d * 31 + 3) - 0.5f) * 2f * seaW * 0.02f * (0.3f + t);
                glints.Add(new Vector4(sunX + xOff - halfLen, y, halfLen * 2f, 0.18f));
            }
            // The Filament build also draws three rows of drifting RIPPLE lines across the
            // whole width. Not ported: a ripple is a wavy LINE and this decal mechanism draws
            // rectangles, so away from the sun they read as debris floating on the water rather
            // than as surface texture. It needs a strip mesh, like the silhouettes have.
            sea.Windows = glints.ToArray();
            sea.DecalColour = new Color(0.88f, 0.95f, 1f, 0.22f);

            // Surf: a scalloped foam band at the water's edge, in the biome's horizonAccent (white
            // for Ocean). Its whole job is to stop the shoreline being ruler-straight, so the
            // scallops vary in depth rather than tiling — an even repeat reads as a zip fastener.
            // BaseY only just under the ground line. Sunk the way a ridge is sunk (-1.9 of a 2.8
            // band) the ground plane occludes all but the tallest scallops and the surf comes out
            // as one straight white rule — which is the very thing it exists to prevent.
            float surfW = VisibleWidthAt(NearZ, aspect) * WidthMargin;
            var surf = new float[TerrainColumns];
            for (int i = 0; i < surf.Length; i++)
            {
                float x = (float)i / (surf.Length - 1);
                float a = Mathf.Sin(x * 26f);
                float b = Mathf.Sin(x * 9.3f + 1.7f);
                surf[i] = Mathf.Clamp01(0.52f + 0.30f * a * (0.6f + 0.4f * b));
            }

            return new List<Layer>
            {
                sea,
                new Layer
                {
                    Name = "Surf", Z = NearZ, Width = surfW,
                    Height = 2.0f, BaseY = -0.4f, Profile = surf, Colour = Tint.Accent,
                },
            };
        }

        // --- Sampling helpers ------------------------------------------------------------

        /// <summary>
        /// Rescales in place so the lowest sample is `floor` and the highest is 1. Noise functions
        /// do not have a dependable output range once they are summed, warped or ridged, and a
        /// hand-tuned divisor silently changes meaning the moment an octave count does — the
        /// range this actually produced is the only honest normaliser.
        /// </summary>
        static void Normalize(float[] samples, float floor = 0f)
        {
            float lo = float.MaxValue, hi = float.MinValue;
            foreach (var s in samples) { if (s < lo) lo = s; if (s > hi) hi = s; }
            float span = hi - lo;
            if (span < 1e-5f) { for (int i = 0; i < samples.Length; i++) samples[i] = 0.5f; return; }
            for (int i = 0; i < samples.Length; i++)
                samples[i] = floor + (1f - floor) * (samples[i] - lo) / span;
        }

        /// <summary>
        /// Ridged fBm. `1 - |noise|` puts a sharp crease where plain noise has a smooth zero
        /// crossing, which is exactly a mountain crest, and squaring sharpens it further.
        ///
        /// The textbook version also weights each octave by the one above it, which collects all
        /// the detail onto the high ground. That is right for a heightmap seen from above and
        /// wrong for a SILHOUETTE: it produced needles — single-column spires with nothing
        /// supporting them — because the weighting starves every shoulder that would have given
        /// a crest a base. Plain summation keeps the crease and keeps the mass under it.
        /// </summary>
        static float RidgedFbm(float x, int seed, int octaves)
        {
            float sum = 0f, amp = 0.5f, freq = 1f;
            for (int o = 0; o < octaves; o++)
            {
                float n = 1f - Mathf.Abs(ValueNoise(x * freq, seed + o * 131));
                sum += n * n * amp;
                freq *= 2.13f;   // not exactly 2: integer lacunarity re-aligns every octave's
                amp *= 0.52f;    // lattice on the same points and the range visibly repeats.
            }
            return sum;
        }

        static float Fbm(float x, int seed, int octaves)
        {
            float sum = 0f, amp = 0.5f, freq = 1f;
            for (int o = 0; o < octaves; o++)
            {
                sum += ValueNoise(x * freq, seed + o * 131) * amp;
                freq *= 2.13f;
                amp *= 0.5f;
            }
            return sum;
        }

        static float ValueNoise(float x, int seed)
        {
            int i = Mathf.FloorToInt(x);
            float f = x - i;
            float u = f * f * (3f - 2f * f);
            return Mathf.Lerp(Hash(i, seed), Hash(i + 1, seed), u) * 2f - 1f;
        }

        /// <summary>Deterministic from the seed, so a level looks the same every launch — a
        /// backdrop that reshuffles between runs reads as a rendering bug.</summary>
        static float Hash(int n, int seed)
        {
            unchecked
            {
                int h = n * 374761393 + seed * 668265263;
                h = (h ^ (h >> 13)) * 1274126177;
                return ((h ^ (h >> 16)) & 0x7fffffff) / (float)0x7fffffff;
            }
        }

        static float Hash01(int n) => Hash(n, 0);
    }
}
