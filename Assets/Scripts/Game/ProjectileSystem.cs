using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace ArmedConflict.Game
{
    /// <summary>
    /// Third slice of the GameViewModel port: advancing projectiles and explosions, and culling
    /// spent rounds.
    ///
    /// THE ORDER IS THE POINT. Step, then resolve collisions, THEN cull — never step-and-cull
    /// before collision. A shot that crosses y=0 through a target on the same tick would
    /// otherwise be floor-culled before the hit was ever tested, and silently disappear instead
    /// of registering. Callers must follow StepAll -> CollisionSystem.ResolveHits -> Cull.
    /// </summary>
    public static class ProjectileSystem
    {
        public const float WorldFloorY = 0f;
        public const float ExplosionDurationSeconds = 1.0f;

        /// <summary>
        /// Physics dt is CLAMPED but never sub-stepped, which is exactly why collision has to be
        /// swept: at this ceiling a round can cover more than a world unit between ticks, well
        /// past the hit radius.
        /// </summary>
        public const float MaxTickSeconds = 0.05f;

        /// <summary>How far past either army a round may travel before it is written off.</summary>
        public const float CullMargin = 10f;

        public static float ClampDt(float dt) => Mathf.Min(dt, MaxTickSeconds);

        /// <summary>
        /// Advances every projectile one tick, recording the pre-step position as Prev — which is
        /// the segment the swept collision check runs against.
        /// </summary>
        public static List<ProjectileEntity> StepAll(IReadOnlyList<ProjectileEntity> projectiles,
                                                     float dt, float windAccelZ)
        {
            var outp = new List<ProjectileEntity>(projectiles.Count);
            foreach (var p in projectiles)
            {
                var pos = new Vector3(p.X, p.Y, p.Z);
                var vel = new Vector3(p.Vx, p.Vy, p.Vz);
                TrajectoryPhysics.Step(ref pos, ref vel, dt, windAccelZ);
                outp.Add(p with
                {
                    PrevX = p.X, PrevY = p.Y, PrevZ = p.Z,
                    X = pos.x, Y = pos.y, Z = pos.z,
                    Vx = vel.x, Vy = vel.y, Vz = vel.z,
                    Age = p.Age + dt,
                });
            }
            return outp;
        }

        /// <summary>
        /// Projectiles that missed everything and reached the floor this tick. Must be read from
        /// the STEPPED list after collisions have resolved, so a round that hit on its way down
        /// is not double-counted as a ground impact.
        /// </summary>
        public static List<ProjectileEntity> GroundImpacts(IReadOnlyList<ProjectileEntity> stepped,
                                                           ICollection<int> hitProjectileIds)
            => stepped.Where(p => !hitProjectileIds.Contains(p.Id) && p.Y <= WorldFloorY).ToList();

        /// <summary>
        /// Removes rounds that hit something, hit the floor, or sailed past either army.
        ///
        /// The side bounds are not decoration: a shot that overshoots everything can never hit
        /// anything, and without a cull it holds the Resolving phase open while it falls from
        /// the stratosphere. They sit well outside both edges so nothing that could still matter
        /// is clipped.
        /// </summary>
        public static List<ProjectileEntity> Cull(IReadOnlyList<ProjectileEntity> stepped,
                                                  ICollection<int> hitProjectileIds,
                                                  IReadOnlyList<UnitEntity> playerUnits,
                                                  IReadOnlyList<UnitEntity> enemyUnits,
                                                  IReadOnlyList<StructureEntity> structures)
        {
            float leftX = (playerUnits.Count > 0 ? playerUnits.Min(u => u.X) : -8f) - CullMargin;

            var rightCandidates = enemyUnits.Select(u => u.X)
                .Concat(structures.Where(s => !s.Definition.isPlayerSide).Select(s => s.X))
                .ToList();
            float rightX = (rightCandidates.Count > 0 ? rightCandidates.Max() : 8f) + CullMargin;

            return stepped
                .Where(p => !hitProjectileIds.Contains(p.Id))
                .Where(p => p.Y > WorldFloorY)
                .Where(p => p.X >= leftX && p.X <= rightX)
                .ToList();
        }

        /// <summary>
        /// Advances explosions, holding a finished one for ONE extra tick at progress 1 before
        /// removing it. On Filament that existed to give the renderer a clean frame before the
        /// node was destroyed, preventing a use-after-free; Unity has no such hazard, but the
        /// extra tick is kept because it is also what makes the last frame of the animation
        /// actually render rather than being dropped on the tick it completes.
        /// </summary>
        public static List<ExplosionEntity> AdvanceExplosions(
            IReadOnlyList<ExplosionEntity> explosions, float dt)
        {
            var wasUnfinished = new HashSet<int>();
            foreach (var e in explosions)
                if (e.Progress < 1f) wasUnfinished.Add(e.Id);

            var outp = new List<ExplosionEntity>(explosions.Count);
            foreach (var e in explosions)
            {
                var advanced = e with
                {
                    Progress = Mathf.Min(e.Progress + dt / ExplosionDurationSeconds, 1f),
                };
                if (advanced.Progress < 1f || wasUnfinished.Contains(advanced.Id))
                    outp.Add(advanced);
            }
            return outp;
        }
    }
}
