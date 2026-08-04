using System;
using System.Collections.Generic;
using UnityEngine;
using ArmedConflict.Data;

namespace ArmedConflict.Game
{
    /// <summary>
    /// Port of Formation.kt. Lays units out compactly so a 7-30 roster fits a small battlefield
    /// footprint rather than spreading across one wide line.
    ///
    /// The spacing constants are DERIVED, and which ones derive is itself a decision:
    /// column (x) spacing tracks UnitGeometry.UnitScaleUnits, row (z) spacing deliberately does
    /// NOT. z is a depth/parallax cue, not a size cue — tying them together once produced a
    /// visibly exaggerated depth read, where a shot landing on a back-row target appeared to hit
    /// noticeably nearer the viewer than the target itself.
    /// </summary>
    public static class Formation
    {
        const int DefaultColumns = 5;

        public const float DefaultColumnSpacing = 0.49f * UnitGeometry.LegacyScaleRatio;
        public const float DefaultRowSpacing = 0.38f;      // NOT derived — see class comment

        const int ClusterMinCount = 4;
        // Clumps read as clumps only when the space BETWEEN them clearly exceeds the space
        // between bodies WITHIN one. The first attempt had this backwards — clusters sat 1.7
        // spacings apart while each was a full spacing wide, so three "clusters" of three read
        // as one slightly-uneven line of nine. The width comes out of the clumps instead.
        const float ClusterPackFactor = 0.62f;
        const float ClusterGapFactor = 2.2f;
        const float JitterXFactor = 0.22f;

        public const float MountedColumnSpacing = 0.30f * UnitGeometry.LegacyScaleRatio;
        const float MountedMinSpacing = 0.22f * UnitGeometry.LegacyScaleRatio;
        const int MountedMinCountForTwoRanks = 5;

        /// <summary>Grid of (x, z) offsets for count units centred on anchorX/anchorZ.</summary>
        public static List<Vector2> Grid(int count, float anchorX, float anchorZ = 0f,
                                         int columns = DefaultColumns,
                                         float columnSpacing = DefaultColumnSpacing,
                                         float rowSpacing = DefaultRowSpacing)
        {
            var result = new List<Vector2>();
            if (count <= 0) return result;
            int rows = (count + columns - 1) / columns;
            for (int i = 0; i < count; i++)
            {
                int row = i / columns;
                int unitsInRow = Mathf.Min(columns, count - row * columns);
                int col = i % columns;
                result.Add(new Vector2(
                    anchorX + (col - (unitsInRow - 1) / 2f) * columnSpacing,
                    anchorZ + (row - (rows - 1) / 2f) * rowSpacing));
            }
            return result;
        }

        /// <summary>
        /// Breaks a roster into 2-3 loose sub-clusters with their own jitter — the
        /// "small clusters in different positions, not one line" look. A perfectly uniform grid
        /// reads as a spreadsheet even when it wraps into more than one row, because row spacing
        /// is a subtle depth cue rather than real lateral separation.
        /// </summary>
        public static List<Vector2> Clustered(int count, float anchorX, float anchorZ = 0f,
                                              float columnSpacing = DefaultColumnSpacing,
                                              float rowSpacing = DefaultRowSpacing,
                                              System.Random random = null)
        {
            random ??= new System.Random();
            var outp = new List<Vector2>();
            if (count <= 0) return outp;

            if (count < ClusterMinCount)
            {
                foreach (var p in Grid(count, anchorX, anchorZ, count, columnSpacing, rowSpacing))
                    outp.Add(Jitter(p, columnSpacing, rowSpacing, random));
                return outp;
            }

            int clusterCount = count > 7 ? 3 : 2;
            var sizes = new int[clusterCount];
            for (int i = 0; i < clusterCount; i++)
                sizes[i] = count / clusterCount + (i < count % clusterCount ? 1 : 0);

            float clumpSpacing = columnSpacing * ClusterPackFactor;
            int clumpColumns = Mathf.Min(2, Max(sizes));
            float clumpWidth = clumpSpacing * (clumpColumns - 1);
            float gapWidth = clumpSpacing * ClusterGapFactor;
            float centerStep = clumpWidth + gapWidth;

            // z stays a subtle stagger deliberately: a depth cue, never what makes the clumps read.
            float[] zOffsets = clusterCount == 2
                ? new[] { -0.9f, 0.9f }
                : new[] { -0.9f, 1.1f, -0.9f };

            for (int i = 0; i < clusterCount; i++)
            {
                float xOff = (i - (clusterCount - 1) / 2f) * centerStep;
                // Anchor jitter scales to the GAP, not the spacing, so it can shift a clump
                // noticeably without ever eating enough gap to merge two clumps back together.
                float cx = anchorX + xOff + ((float)random.NextDouble() - 0.5f) * gapWidth * 0.36f;
                float cz = anchorZ + zOffsets[i] * rowSpacing
                         + ((float)random.NextDouble() - 0.5f) * rowSpacing * 0.6f;
                foreach (var p in Grid(sizes[i], cx, cz, Mathf.Min(2, sizes[i]), clumpSpacing, rowSpacing))
                    outp.Add(Jitter(p, clumpSpacing, rowSpacing, random));
            }
            return outp;
        }

        /// <summary>
        /// Individual placement for hero-scale units — spread in their own loose line instead of
        /// packed into the crowd's slots. The crowd masses in dense rows; a couple of named
        /// heroes stand apart, individually. `spacing` already carries the renderScale multiplier,
        /// so a bigger hero automatically gets more personal room.
        /// </summary>
        public static List<Vector2> Heroes(int count, float anchorX, float anchorZ = 0f,
                                           float spacing = DefaultColumnSpacing * 2.2f,
                                           System.Random random = null)
        {
            random ??= new System.Random();
            var outp = new List<Vector2>();
            for (int i = 0; i < count; i++)
            {
                float x = anchorX + (i - (count - 1) / 2f) * spacing
                        + ((float)random.NextDouble() - 0.5f) * spacing * 0.25f;
                float z = anchorZ + ((float)random.NextDouble() - 0.5f) * spacing * 0.3f;
                outp.Add(new Vector2(x, z));
            }
            return outp;
        }

        /// <summary>
        /// Layout for a garrison standing ON a structure: compressed to fit the deck `width`
        /// (up to two ranks for larger squads) with only a small z offset, so nobody stands off
        /// the edge of the roof or hovers in front of the facade.
        ///
        /// Two ranks at count >= 5 is a reference measurement, not a guess: the reference game's
        /// castle tiers pack TWO ranks until the bodies overlap.
        /// </summary>
        public static List<Vector2> Mounted(int count, float anchorX, float width,
                                            float anchorZ = 0f,
                                            float columnSpacing = MountedColumnSpacing,
                                            float? deckCenterX = null)
        {
            var outp = new List<Vector2>();
            if (count <= 0) return outp;
            float deckCenter = deckCenterX ?? anchorX;

            int rows = count < MountedMinCountForTwoRanks ? 1 : 2;
            while (rows < count && width / (((count + rows - 1) / rows) - 1) < MountedMinSpacing)
                rows++;

            int columns = (count + rows - 1) / rows;
            float spacing = columns > 1 ? Mathf.Min(columnSpacing, width / (columns - 1)) : 0f;
            float halfRow = spacing * (Mathf.Min(columns, count) - 1) / 2f;
            float halfDeck = width / 2f;
            float clampedAnchorX = halfRow >= halfDeck
                ? deckCenter
                : Mathf.Clamp(anchorX, deckCenter - halfDeck + halfRow, deckCenter + halfDeck - halfRow);

            for (int index = 0; index < count; index++)
            {
                int row = index / columns;
                int unitsInRow = Mathf.Min(columns, count - row * columns);
                int col = index % columns;
                outp.Add(new Vector2(
                    clampedAnchorX + (col - (unitsInRow - 1) / 2f) * spacing,
                    anchorZ + (rows > 1 ? (row - (rows - 1) / 2f) * 0.16f : 0f)));
            }
            return outp;
        }

        static Vector2 Jitter(Vector2 p, float columnSpacing, float rowSpacing, System.Random random)
            => new(p.x + ((float)random.NextDouble() - 0.5f) * columnSpacing * JitterXFactor,
                   p.y + ((float)random.NextDouble() - 0.5f) * rowSpacing * 0.5f);

        static int Max(int[] a)
        {
            int m = int.MinValue;
            foreach (var v in a) m = Math.Max(m, v);
            return m;
        }
    }
}
