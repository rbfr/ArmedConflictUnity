using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using ArmedConflict.Game;

/// <summary>
/// Behavioural checks on the ported game/ modules. "It compiles" is not evidence a port is
/// faithful — these assert the properties the Kotlin originals were written to guarantee.
/// Run: -batchmode -quit -executeMethod PortSelfTest.Run
/// </summary>
public static class PortSelfTest
{
    static int failed;
    static readonly StringBuilder Log = new();

    static void Check(bool ok, string what)
    {
        if (!ok) failed++;
        Log.AppendLine($"  [{(ok ? "ok  " : "FAIL")}] {what}");
    }

    static void Near(float a, float b, float eps, string what)
        => Check(Mathf.Abs(a - b) <= eps, $"{what} ({a:F5} vs {b:F5})");

    public static void Run()
    {
        failed = 0;

        // --- SpringFollow: the REST DEADBAND is the whole point of not using Mathf.SmoothDamp.
        {
            float v = 0f, vel = 0f;
            for (int i = 0; i < 2000; i++) SpringFollow.Step(ref v, ref vel, 10f, 1f / 60f, 0.25f);
            Check(v == 10f, "spring lands EXACTLY on target (bit-identical, not merely close)");
            Check(vel == 0f, "spring velocity reaches exactly zero");

            float before = v;
            SpringFollow.Step(ref v, ref vel, 10f, 1f / 60f, 0.25f);
            Check(v == before && vel == 0f, "a settled spring does not change on further ticks");

            // Critically damped: must never overshoot a static target.
            float p = 0f, pv = 0f, maxSeen = 0f;
            for (int i = 0; i < 600; i++)
            {
                SpringFollow.Step(ref p, ref pv, 1f, 1f / 60f, 0.3f);
                maxSeen = Mathf.Max(maxSeen, p);
            }
            Check(maxSeen <= 1f + 1e-5f, "spring never overshoots a static target");

            // Stability across wildly different dt — the reason for this formula.
            float q = 0f, qv = 0f;
            SpringFollow.Step(ref q, ref qv, 5f, 2f, 0.25f);
            Check(!float.IsNaN(q) && Mathf.Abs(q) <= 5.001f, "spring stable at an absurd dt");
        }

        // --- Formation.Grid: centred, correct row/column split.
        {
            var g = Formation.Grid(8, anchorX: 0f, anchorZ: 0f);
            Check(g.Count == 8, "grid returns every unit");
            Near(g.Take(5).Average(p => p.x), 0f, 1e-4f, "full row is centred on the anchor");
            Check(g.Take(5).All(p => Mathf.Abs(p.y - g[0].y) < 1e-5f), "first five share a row");
            Check(Mathf.Abs(g[5].y - g[0].y) > 1e-3f, "sixth unit starts a new row");
            Near(Mathf.Abs(g[1].x - g[0].x), Formation.DefaultColumnSpacing, 1e-5f,
                 "column spacing is DefaultColumnSpacing");
        }

        // --- Formation.Mounted: nobody may stand off the deck.
        {
            const float width = 1.2f;
            foreach (int n in new[] { 3, 5, 9, 14 })
            {
                var m = Formation.Mounted(n, anchorX: 0f, width: width);
                Check(m.Count == n, $"mounted({n}) returns every unit");
                bool onDeck = m.All(p => Mathf.Abs(p.x) <= width / 2f + 1e-4f);
                Check(onDeck, $"mounted({n}) keeps every defender on the deck");
            }
            Check(Formation.Mounted(4, 0f, 1.2f).All(p => Mathf.Abs(p.y) < 1e-6f),
                  "fewer than 5 defenders stand in ONE rank");
            Check(Formation.Mounted(6, 0f, 1.2f).Select(p => p.y).Distinct().Count() == 2,
                  "5+ defenders pack into TWO ranks (reference: castle tiers)");

            // An anchor off the side of the deck must be pulled back onto it.
            var shoved = Formation.Mounted(5, anchorX: 4f, width: width, deckCenterX: 0f);
            Check(shoved.All(p => Mathf.Abs(p.x) <= width / 2f + 1e-4f),
                  "an off-deck anchor is clamped back onto the deck");
        }

        // --- Formation.Clustered: gaps between clumps must exceed spacing within one.
        {
            var c = Formation.Clustered(9, 0f, random: new System.Random(1234));
            Check(c.Count == 9, "clustered returns every unit");
            var xs = c.Select(p => p.x).OrderBy(x => x).ToList();
            var gaps = Enumerable.Range(1, xs.Count - 1).Select(i => xs[i] - xs[i - 1]).ToList();
            Check(gaps.Max() > gaps.Where(g => g > 1e-4f).Min() * 1.4f,
                  "clumps separate — largest gap clearly exceeds intra-clump spacing");
        }

        // --- CameraFraming
        {
            Near(CameraFraming.HalfWidth(0f, new List<float> { -3f, 2f }), 3f, 1e-5f,
                 "half-width covers the furthest point from the anchor");
            Near(CameraFraming.HalfWidth(10f, new List<float> { -3f, 3f }), 13f, 1e-5f,
                 "off-centre anchor still frames the whole set");
            Check(CameraFraming.HalfWidth(0f, new List<float>()) == 0f, "empty set gives zero");
        }

        // --- EnemyAI: jitter is the ONLY inaccuracy, and speed is capped.
        {
            Near(EnemyAI.JitterRadius(2f), 4f, 1e-5f, "smoke screen doubles the jitter radius");
            Near(EnemyAI.AdvanceBudget(2f, true), 1f, 1e-5f, "overwatch flare halves advance budget");
            Near(EnemyAI.AdvanceBudget(2f, false), 2f, 1e-5f, "no flare leaves advance budget alone");
            bool capped = true;
            for (int i = 0; i < 500; i++)
            {
                var v = EnemyAI.AimAt(Vector3.zero, new Vector3(30f, 0f, 0f));
                if (v.magnitude > 12.0001f) capped = false;
            }
            Check(capped, "launch speed never exceeds the cap, however far the target");
        }

        // --- TrajectoryPhysics / SweptCollision (ported in Step 4, re-checked here)
        {
            Near(SweptCollision.UnitHitRadius, 0.5f * (0.48f / 0.77f) * 1.22f, 1e-6f,
                 "hit radius stays 1.22x body-proportional");
            // The sweep must catch a target the endpoints both miss — the tunnelling case.
            float d2 = SweptCollision.SegmentDistanceSq(0f, 5f, 0f, -5f, 0f, 0f);
            Check(d2 < 1e-6f, "swept segment catches a target passed BETWEEN two ticks");
        }

        Debug.Log($"[PortSelfTest] {(failed == 0 ? "ALL PASS" : $"{failed} FAILURES")}\n{Log}");
        if (failed > 0 && Application.isBatchMode) EditorApplication.Exit(1);
    }
}
