using System.Linq;
using UnityEditor;
using UnityEngine;
using ArmedConflict.Data;
using ArmedConflict.Game;

/// <summary>
/// RAGDOLL DIAGNOSTIC. Kept, not deleted — it is the only way to see a corpse's own
/// numbers, and it found the settle snap in one run after the code read as innocent.
/// Written for Rob's 2026-08-25 report: *"when the unit falls off of a building,
/// when they are at/near the ground, they seem to start glitching/going into some kind of
/// animation loop before they finally disappear into the ground."*
///
///   DISPLAY=:1 $U -batchmode -quit -projectPath . -executeMethod RagdollProbe.Run -logFile -
///
/// A corpse plays NO CLIP — `UnitAnim.Set(Die)` stops every one of them and the guard on `dead`
/// means it cannot re-trigger. So whatever is looping is the SIMULATION moving the transform, and
/// that is a thing a headless tick can print. This kills one garrison body on a real level and
/// dumps its ragdoll state per tick, which is the probe this project keeps saying to reach for
/// before a pixel hunt.
///
/// TWO THINGS IT COST TO GET RIGHT, both worth knowing before reusing it:
///   - setting a unit's `Hp = 0` does NOT mint a ragdoll. Deaths are created inside the damage
///     resolution, not by a per-tick sweep for corpses, so the body has to be INJECTED the way
///     the ragdoll self-tests do.
///   - the authored impulse throws an ENEMY body away from the player, so it lands back on its
///     own roof and never reaches dirt. Reproducing "falls off a building" needs the throw
///     pointed at the near side, which is what the -1.6 below is.
/// </summary>
public static class RagdollProbe
{
    public static void Run()
    {
        var level = AssetDatabase.LoadAssetAtPath<LevelDefinitionSO>(
            "Assets/GameData/Levels/GarrisonPost.asset");
        if (level == null) { Debug.LogError("[RagdollProbe] no L2"); return; }

        var built = LevelBuilder.BuildInitialState(level, 2, 12, new System.Random(5));
        var post = built.Structures.First(st => st.Definition != null && !st.Definition.isPlayerSide);
        var victim = built.EnemyUnits.First(u => u.StandingOnStructureId != null);
        Debug.Log($"[RagdollProbe] deck body at x {victim.X:F2} y {victim.Y:F2}; " +
                  $"post x {post.X:F2} y {post.Y:F2} size {post.Definition.size:F2}");

        // Setting Hp = 0 does NOT make a ragdoll — deaths are minted inside the damage
        // resolution, not by a per-tick scan for corpses. Inject the body the way the existing
        // ragdoll checks do, with the real deck-fall impulse.
        var imp = CosmeticSystems.ImpulseFor(victim.Id, victim.IsPlayerSide, tumble: true);
        var body = new DyingUnitEntity(victim.Id, victim.Definition, victim.IsPlayerSide,
                                       victim.X, victim.Y, victim.Z,
                                       // THROWN OFF THE NEAR SIDE. The authored impulse sends an
                                       // enemy body AWAY from the player, so it lands back on its
                                       // own roof and never reaches dirt — which is not the case
                                       // being reported. -3 guarantees it clears the deck edge.
                                       -1.6f, imp.Vy, imp.RotationSpeed)
        {
            Vz = imp.Vz, Rotation = imp.Rotation, Tumble = true,
            YawSpeed = imp.YawSpeed, TiltSpeed = imp.TiltSpeed, SupportY = victim.Y,
        };
        var s = built with
        {
            Phase = GamePhase.Playing, TurnPhase = TurnPhase.Resolving,
            DyingUnits = new System.Collections.Generic.List<DyingUnitEntity> { body },
        };

        const float dt = 1f / 60f;
        int lastBranch = -1, tracked = -1;
        for (int i = 0; i < 260; i++)
        {
            s = BattleTick.Step(s, dt, level, new System.Random(11), null);
            // The ragdoll may not carry the unit's id, so follow whatever body appears first
            // rather than assuming — the probe's job is to observe, not to guess the schema.
            var d = s.DyingUnits.FirstOrDefault(x => tracked < 0 || x.Id == tracked);
            if (d == null)
            {
                if (tracked >= 0) { Debug.Log($"[RagdollProbe] t={i * dt:F2} body GONE"); break; }
                continue;
            }
            tracked = d.Id;

            // Branch: 0 = ballistic, 1 = grounded. Recomputed the same way the tick decides,
            // so a flip-flop between them shows up as a changing number rather than as a guess.
            int branch = d.Y <= d.SupportY + 0.0001f ? 1 : 0;
            bool airborne = CosmeticSystems.RagdollAirborne(d);
            float sink = CosmeticSystems.RagdollSinkY(d.Age, d.SupportY);

            // Print every tick over the last stretch, and on any branch change — the interesting
            // window is the settle, not the flight.
            if (branch != lastBranch || i % 10 == 0 || d.Age > CosmeticSystems.RagdollMaxAgeSeconds - 1.1f)
                Debug.Log($"[RagdollProbe] t={i * dt:F2} age={d.Age:F2} y={d.Y:F3} sup={d.SupportY:F3} " +
                          $"vx={d.Vx:F3} rot={d.Rotation:F1} rotv={d.RotationSpeed:F1} " +
                          $"bend={d.Bend:F3} yaw={d.Yaw:F1} tilt={d.SettleTilt:F1} " +
                          $"branch={branch} air={airborne} sink={sink:F3}");
            lastBranch = branch;
        }
    }
}
