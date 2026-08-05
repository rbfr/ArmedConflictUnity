using System.Collections.Generic;
using UnityEngine;
using ArmedConflict.Data;

namespace ArmedConflict.Render
{
    /// <summary>
    /// Assembles the biome backdrop <see cref="Backdrop.Plan"/> describes into GameObjects: sky
    /// gradient, silhouette layers, snow caps, lit windows, horizon accent.
    ///
    /// This is the RUNTIME half, and it replaces the editor-only BackdropBuilder that baked one
    /// biome into Battle.unity as assets. Baking is what made a second level unreachable: eleven
    /// of the 29 levels are Winter and three are CityRuins, so a baked backdrop is the wrong
    /// picture for four levels in five.
    ///
    /// Materials, Textures and Meshes created here are NOT reclaimed when the GameObject holding
    /// them is destroyed — Unity only collects assets, not runtime instances. Every one is
    /// recorded in `owned` so the caller can destroy them on a level change; skip that and
    /// walking the campaign leaks a backdrop's worth per level.
    ///
    /// Nothing here calls Shader.Find. The shader arrives through `unlitSource`, a material ASSET
    /// the scene already references, so the build cannot strip it out from under us.
    /// </summary>
    public static class BackdropRuntime
    {
        public static void Build(BackgroundDefinitionSO bg, float aspect, Transform parent,
                                 Material unlitSource, Material fadeSource, List<Object> owned)
        {
            // Note a DESTROYED asset also trips this — Unity's fake null. That is deliberate:
            // silently drawing from a freed BackgroundDefinition is worse than drawing nothing.
            if (bg == null) return;

            MakeSky(bg, parent, unlitSource, owned);

            foreach (var layer in Backdrop.Plan(bg.style, aspect))
                MakeLayer(bg, layer, parent, unlitSource, fadeSource, owned);

            // The thin bright line where sky meets ground — cheap, and it is what makes the
            // horizon read as a horizon rather than as two flat bands meeting.
            var accent = QuadMesh.Create("HorizonAccent", parent,
                                         Unlit(unlitSource, bg.horizonAccent, owned));
            accent.transform.localPosition = new Vector3(0f, 0.02f, Backdrop.NearZ + 1f);
            // The quad faces -Z; the camera views from +Z, so an unrotated one shows the camera
            // its BACK face and is culled away entirely.
            accent.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
            accent.transform.localScale = new Vector3(400f, 0.35f, 1f);
        }

        static void MakeLayer(BackgroundDefinitionSO bg, Backdrop.Layer layer, Transform parent,
                              Material unlitSource, Material fadeSource, List<Object> owned)
        {
            Color colour = layer.Colour switch
            {
                Backdrop.Tint.Far => bg.silhouetteFar,
                Backdrop.Tint.Near => bg.silhouetteNear,
                // Accent is the biome's horizonAccent — white for Ocean, where it is the foam.
                Backdrop.Tint.Accent => bg.horizonAccent,
                _ => Color.Lerp(bg.silhouetteFar, bg.silhouetteNear, 0.5f),
            };

            var go = Attach(parent, layer.Name, layer.Z, SilhouetteMesh.Build(layer),
                            layer.Gradient
                                ? Gradient(unlitSource, bg.silhouetteFar, bg.silhouetteNear, owned)
                                : Unlit(unlitSource, colour, owned),
                            owned);

            // Snow sits a hair in front of its own ridge. Same z would leave the two coplanar and
            // z-fighting decides per pixel which one wins — on a mobile GPU that flickers as the
            // camera pans, which reads as the caps strobing.
            var snow = SilhouetteMesh.BuildSnow(layer);
            if (snow != null)
                Attach(go.transform, layer.Name + "Snow", 0.02f, snow,
                       Unlit(unlitSource, Color.Lerp(colour, Color.white, 0.7f), owned), owned);

            var windows = SilhouetteMesh.BuildWindows(layer);
            if (windows != null)
                Attach(go.transform, layer.Name + "Windows", 0.02f, windows,
                       // The city's lit-window orange unless the layer names its own — the sea's
                       // glitter uses the same decal mechanism in a pale blue-white.
                       layer.DecalColour is Color dc
                           ? UnlitFade(fadeSource, dc, owned)
                           : Unlit(unlitSource, new Color(0.88f, 0.48f, 0.18f), owned),
                       owned);

            if (layer.SunRadius > 0f) MakeSun(layer, go.transform, fadeSource, owned);
        }

        /// <summary>
        /// The sun: a bright core inside a soft radial glow, on one quad with a generated
        /// texture. A flat disc has no business in a sky — the glow is what seats it in the haze,
        /// and it is also what carries the sun's colour onto the water below it.
        ///
        /// Drawn BEHIND its own layer's surface (negative local z) so the sea it sits over
        /// occludes its lower half, which is what puts it ON the horizon rather than in front of
        /// the water.
        /// </summary>
        static void MakeSun(Backdrop.Layer layer, Transform parent, Material fadeSource,
                            List<Object> owned)
        {
            const int Size = 64;
            var tex = new Texture2D(Size, Size, TextureFormat.RGBA32, false)
            { wrapMode = TextureWrapMode.Clamp };
            for (int y = 0; y < Size; y++)
            for (int x = 0; x < Size; x++)
            {
                float dx = (x + 0.5f) / Size - 0.5f, dy = (y + 0.5f) / Size - 0.5f;
                float d = Mathf.Sqrt(dx * dx + dy * dy) * 2f;              // 0 centre -> 1 edge
                float glow = 1f - Threshold(0.10f, 1f, d);
                float core = 1f - Threshold(0.26f, 0.34f, d);
                tex.SetPixel(x, y, new Color(1f, 0.97f, 0.88f,
                                             Mathf.Clamp01(glow * 0.55f + core)));
            }
            tex.Apply();

            var mat = new Material(fadeSource) { color = Color.white, mainTexture = tex };
            owned.Add(tex);
            owned.Add(mat);

            var go = QuadMesh.Create(layer.Name + "Sun", parent, mat);
            // NEGATIVE local z: further from the camera than the sea surface, so the water cuts
            // the disc off at the horizon instead of the sun floating in front of it.
            go.transform.localPosition = new Vector3(layer.SunAt.x, layer.SunAt.y, -0.03f);
            go.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
            go.transform.localScale = Vector3.one * layer.SunRadius * 2f;
        }

        /// <summary>
        /// GLSL's smoothstep: 0 below edge0, 1 above edge1, smoothly ramped between.
        ///
        /// NOT `Mathf.SmoothStep`, which despite the name is a smoothed LERP BETWEEN its first two
        /// arguments — `Mathf.SmoothStep(0.26f, 0.34f, d)` returns a value in [0.26, 0.34] for
        /// every d, so `1 - that` never falls below 0.66. That is a near-constant alpha over the
        /// whole quad, which is exactly what drew the sun as a cream RECTANGLE with a brighter
        /// blob in it. The transparency was correct the whole time; the falloff was not.
        /// </summary>
        static float Threshold(float edge0, float edge1, float x)
        {
            float t = Mathf.Clamp01((x - edge0) / (edge1 - edge0));
            return t * t * (3f - 2f * t);
        }

        static GameObject Attach(Transform parent, string name, float z, Mesh mesh, Material mat,
                                 List<Object> owned)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = new Vector3(0f, 0f, z);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = mat;
            owned.Add(mesh);
            return go;
        }

        static void MakeSky(BackgroundDefinitionSO bg, Transform parent, Material unlitSource,
                            List<Object> owned)
        {
            // A vertical gradient rather than a flat colour: the Filament build gradients
            // skyTop -> skyHorizon, and a flat fill loses the depth that gives.
            //
            // The quad is sized to the VISIBLE BAND at its own depth, and both halves of that
            // matter. Too short (the original 200 tall at y=40) and its top edge sits inside the
            // frustum, so the camera's clear colour shows above the sky as a hard horizontal
            // seam. Too tall and the ramp — which spans the quad, not the frame — is stretched
            // until only its bottom third is on screen and the sky reads as one flat wash. At
            // z=-120 with the camera tilted 14° up the visible band is roughly y ∈ [-97, 165],
            // so 280 tall centred at 35 covers it and spends the whole ramp inside the frame.
            var quad = QuadMesh.Create("Sky", parent,
                                       Gradient(unlitSource, bg.skyTop, bg.skyHorizon, owned));
            quad.transform.localPosition = new Vector3(0f, 35f, Backdrop.SkyZ);
            quad.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);   // face the camera
            quad.transform.localScale = new Vector3(900f, 280f, 1f);
        }

        /// <summary>
        /// Alpha-blended variant, cloned from a TRANSPARENT material ASSET.
        ///
        /// `unlitSource` is opaque and a copy of it ignores the colour's alpha entirely — which
        /// drew the sun's soft glow as a solid cream RECTANGLE in the sky and the sea glitter as
        /// hard white bars. Flipping `_Surface` and the blend modes on the copy at RUNTIME does
        /// not reliably fix it either: the shader variant is chosen when the material asset is
        /// authored, so the quad kept its visible boundary. Both callers now clone a material
        /// asset that was authored transparent, which is what Blast and Scorch already do.
        /// </summary>
        public static Material UnlitFade(Material source, Color c, List<Object> owned)
        {
            var m = new Material(source) { color = c, mainTexture = null };
            owned.Add(m);
            return m;
        }

        public static Material Unlit(Material source, Color c, List<Object> owned)
        {
            var m = new Material(source) { color = c, mainTexture = null };
            owned.Add(m);
            return m;
        }

        /// <summary>A vertical top→bottom ramp. The quad is turned 180° to face the camera, which
        /// mirrors U but leaves V alone, so `top` genuinely is the top.</summary>
        public static Material Gradient(Material source, Color top, Color bottom, List<Object> owned)
        {
            var tex = new Texture2D(1, 64)
            { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            for (int y = 0; y < 64; y++) tex.SetPixel(0, y, Color.Lerp(bottom, top, y / 63f));
            tex.Apply();

            var m = new Material(source) { color = Color.white, mainTexture = tex };
            owned.Add(tex);
            owned.Add(m);
            return m;
        }
    }
}
