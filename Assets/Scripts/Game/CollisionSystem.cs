using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using ArmedConflict.Data;

namespace ArmedConflict.Game
{
    /// <summary>A projectile going off this tick — position, weapon type, and what it struck.</summary>
    public readonly struct Detonation
    {
        public readonly float X, Y, Z;
        public readonly ProjectileType Type;
        public readonly bool ByPlayer;
        public readonly int? HitStructureId;
        public readonly float ImpactDirX, ImpactDirY;
        /// <summary>
        /// A splash weapon detonating on the GROUND, nothing struck directly. Detonation y alone
        /// cannot distinguish this — a direct hit at ankle height also sits near y=0.
        /// </summary>
        public readonly bool IsGroundBurst;

        public Detonation(float x, float y, float z, ProjectileType type, bool byPlayer,
                          int? hitStructureId = null, float impactDirX = 0f, float impactDirY = -1f,
                          bool isGroundBurst = false)
        {
            X = x; Y = y; Z = z; Type = type; ByPlayer = byPlayer;
            HitStructureId = hitStructureId;
            ImpactDirX = impactDirX; ImpactDirY = impactDirY;
            IsGroundBurst = isGroundBurst;
        }
    }

    public class HitResult
    {
        public readonly HashSet<int> HitProjectileIds = new();
        public readonly Dictionary<int, int> StructureDamage = new();
        public readonly Dictionary<int, int> UnitDamage = new();
        /// <summary>Normalised velocity of the round that struck each unit — pushes the ragdoll
        /// in the direction of impact.</summary>
        public readonly Dictionary<int, Vector3> UnitHitVelocities = new();
        /// <summary>Units caught in a blast this tick whether or not they died — SURVIVORS get
        /// the airborne knockback hop. Bullets never populate this.</summary>
        public readonly HashSet<int> ExplosiveHitUnitIds = new();
        /// <summary>Units hit by INCENDIARY ammo — survivors are marked burning.</summary>
        public readonly HashSet<int> IncendiaryHitUnitIds = new();
        public readonly List<Detonation> Detonations = new();
    }

    /// <summary>
    /// Port of CollisionSystem.resolveHits. The swept-segment primitives live in
    /// SweptCollision; this is the resolution ORDER built on top of them, which is where the
    /// behaviour actually lives.
    /// </summary>
    public static class CollisionSystem
    {
        public static HitResult ResolveHits(
            IReadOnlyList<ProjectileEntity> projectiles,
            IReadOnlyList<UnitEntity> enemyUnits,
            IReadOnlyList<UnitEntity> playerUnits,
            IReadOnlyList<StructureEntity> structures)
        {
            var r = new HitResult();

            foreach (var p in projectiles)
            {
                if (r.HitProjectileIds.Contains(p.Id)) continue;
                var targets = p.OwnerIsPlayer ? enemyUnits : playerUnits;

                // UNITS INTERCEPT BEFORE STRUCTURES. A soldier standing in front of — or inside
                // the footprint of — a wall physically takes the round. Resolving structures
                // first "shielded" any ground unit inside a wide structure's AABB (a fortress
                // tier is 2.4 wide and its box reaches the ground): advancing shield bearers and
                // the front rank of a garrisoned barracks read as taking direct hits that
                // registered no damage, the round silently spent as a chip on the wall behind.
                //
                // Skip units already dead THIS tick, compared against CURRENT hp, not maxHp —
                // a half-health unit must not be treated as needing a fresh unit's worth of hits.
                UnitEntity closest = null;
                float bestDistSq = float.MaxValue;
                foreach (var t in targets)
                {
                    r.UnitDamage.TryGetValue(t.Id, out int dmg);
                    if (dmg >= t.Hp) continue;
                    float d = SweptCollision.SegmentDistanceSq(p.PrevX, p.PrevY, p.X, p.Y, t.X, t.Y);
                    if (d < bestDistSq) { bestDistSq = d; closest = t; }
                }

                if (closest != null && bestDistSq < SweptCollision.UnitHitRadiusSq)
                {
                    // Detonate at the actual CONTACT POINT along this tick's flight path, not
                    // the possibly-overshot tick-end position.
                    SweptCollision.ClosestPointOnSegment(p.PrevX, p.PrevY, p.X, p.Y,
                                                         closest.X, closest.Y,
                                                         out float cx, out float cy);
                    r.HitProjectileIds.Add(p.Id);
                    r.Detonations.Add(new Detonation(cx, cy, p.Z, p.Type, p.OwnerIsPlayer));

                    if (p.SplashRadius > 0f)
                    {
                        // Splash damages everyone in radius, the trigger target included, centred
                        // on the contact point for the same reason as the detonation.
                        ApplySplash(p, targets, r, cx, cy);
                    }
                    else
                    {
                        // Plain round: one unit's worth of damage into what it struck. Rounds are
                        // one-per-unit, so spill happens naturally — a squad aims at the same
                        // spot, and rounds arriving after the point man drops hit whoever is
                        // nearest by ordinary collision.
                        r.UnitDamage.TryGetValue(closest.Id, out int had);
                        r.UnitDamage[closest.Id] = had + p.Damage;
                        r.UnitHitVelocities[closest.Id] = NormalizedVel(p);
                        if (p.Ammo == AmmoType.Incendiary) r.IncendiaryHitUnitIds.Add(closest.Id);
                    }
                    continue;
                }

                // No unit struck — the OPPOSING side's structures now block. Own-side structures
                // never block, so a garrison fires clean over its own fortress.
                StructureEntity struck = null;
                float bestStructDist = float.MaxValue;
                foreach (var st in structures)
                {
                    if (st.Definition.isPlayerSide == p.OwnerIsPlayer) continue;
                    float halfW = (st.Definition.hasHitWidth ? st.Definition.hitWidth
                                                             : st.Definition.size) / 2f;
                    // The entity's Y is the centre of a size-tall box, so its base is Y - size/2.
                    // The box then rises to the REAL roof (deckY), not to `size` — see
                    // SweptCollision.HitsStructure for why the difference makes a garrison
                    // unkillable.
                    float baseY = st.Y - st.Definition.size / 2f;
                    float height = st.Definition.hasDeckY ? st.Definition.deckY : st.Definition.size;
                    if (!SweptCollision.HitsStructure(p.X, p.Y, st.X, baseY,
                                                      halfW * 2f, height)) continue;
                    float d = (p.X - st.X) * (p.X - st.X) + (p.Y - st.Y) * (p.Y - st.Y);
                    if (d < bestStructDist) { bestStructDist = d; struck = st; }
                }

                if (struck != null)
                {
                    r.HitProjectileIds.Add(p.Id);
                    r.StructureDamage.TryGetValue(struck.Id, out int had);
                    r.StructureDamage[struck.Id] =
                        had + Mathf.RoundToInt(p.Damage * p.StructureDamageMultiplier);
                    var dir = NormalizedVel(p);
                    r.Detonations.Add(new Detonation(p.X, p.Y, p.Z, p.Type, p.OwnerIsPlayer,
                                                     struck.Id, dir.x, dir.y));
                    if (p.SplashRadius > 0f) ApplySplash(p, targets, r);
                    continue;
                }

                if (p.SplashRadius > 0f && p.Y <= 0f)
                {
                    // Splash detonates on the ground — a grenade never wastes into the dirt.
                    r.HitProjectileIds.Add(p.Id);
                    r.Detonations.Add(new Detonation(p.X, 0f, p.Z, p.Type, p.OwnerIsPlayer,
                                                     isGroundBurst: true));
                    ApplySplash(p, targets, r);
                }
            }

            return r;
        }

        /// <summary>
        /// impactX/Y override the blast centre for a confirmed direct hit (the swept contact
        /// point). The structure-hit and ground-burst paths pass none and floor-coerce the
        /// projectile's own tick-end position instead.
        /// </summary>
        static void ApplySplash(ProjectileEntity p, IReadOnlyList<UnitEntity> targets, HitResult r,
                                float? impactX = null, float? impactY = null)
        {
            float radiusSq = p.SplashRadius * p.SplashRadius;
            float blastX = impactX ?? p.X;
            float blastY = impactY ?? Mathf.Max(p.Y, 0f);

            foreach (var t in targets)
            {
                float dx = blastX - t.X, dy = blastY - t.Y;
                if (dx * dx + dy * dy >= radiusSq) continue;
                r.UnitDamage.TryGetValue(t.Id, out int had);
                r.UnitDamage[t.Id] = had + p.Damage;
                r.UnitHitVelocities[t.Id] = NormalizedVel(p);
                r.ExplosiveHitUnitIds.Add(t.Id);
                if (p.Ammo == AmmoType.Incendiary) r.IncendiaryHitUnitIds.Add(t.Id);
            }
        }

        static Vector3 NormalizedVel(ProjectileEntity p)
        {
            float m = Mathf.Sqrt(p.Vx * p.Vx + p.Vy * p.Vy + p.Vz * p.Vz);
            return m < 1e-5f ? new Vector3(0f, -1f, 0f) : new Vector3(p.Vx / m, p.Vy / m, p.Vz / m);
        }

        /// <summary>
        /// Collapse propagation: destroying a structure brings down everything linked to it —
        /// explicit collapseWith partners AND anything physically RESTING on it — transitively.
        /// Take out a fortress's bottom tier and the whole stack comes down, garrisons with it.
        /// Fixpoint loop, because chains exist (tier3 rests on tier2 rests on tier1).
        /// </summary>
        /// <summary>
        /// A structure's solid box, in the one place that knows how to build it. The entity's Y is
        /// the CENTRE of a size-tall box, so the base is Y - size/2, and the top is the measured
        /// deck rather than `size` — the difference is what once made a garrison unkillable, and
        /// it is the same difference that would let a body come to rest inside phantom masonry.
        /// </summary>
        public static void StructureBox(StructureEntity st, out float minX, out float maxX,
                                        out float baseY, out float topY)
        {
            float halfW = (st.Definition.hasHitWidth ? st.Definition.hitWidth
                                                     : st.Definition.size) / 2f;
            minX = st.X - halfW;
            maxX = st.X + halfW;
            baseY = st.Y - st.Definition.size / 2f;
            topY = baseY + (st.Definition.hasDeckY ? st.Definition.deckY : st.Definition.size);
        }

        public static HashSet<int> PropagateCollapse(IReadOnlyList<StructureEntity> surviving,
                                                     IEnumerable<int> directlyDestroyedIds)
        {
            var destroyed = new HashSet<int>(directlyDestroyedIds);
            while (true)
            {
                var next = surviving
                    .Where(st => !destroyed.Contains(st.Id)
                              && ((st.CollapseWith != null && destroyed.Contains(st.CollapseWith.Value))
                               || (st.RestsOnId != null && destroyed.Contains(st.RestsOnId.Value))))
                    .Select(st => st.Id)
                    .ToList();
                if (next.Count == 0) break;
                foreach (var id in next) destroyed.Add(id);
            }
            return destroyed;
        }
    }
}
