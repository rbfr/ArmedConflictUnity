using System.Collections.Generic;
using UnityEngine;
using ArmedConflict.Data;

namespace ArmedConflict.Game
{
    /// <summary>
    /// Port of the entity types in GameState.kt.
    ///
    /// These are C# `record`s so the immutable-state architecture ports across unchanged:
    /// `with` is Kotlin's `copy()`, and value equality is what the tick relies on. `record class`
    /// is C# 10 and will NOT compile in Unity 6000.0 — plain `record` is C# 9 and does, given the
    /// IsExternalInit shim.
    /// </summary>
    public record UnitEntity(
        int Id,
        UnitDefinitionSO Definition,
        float X, float Y, float Z,
        int Hp,
        bool IsPlayerSide)
    {
        /// <summary>
        /// If set, this unit stands on that structure and dies the instant it is destroyed,
        /// regardless of its own remaining HP.
        /// </summary>
        public int? StandingOnStructureId { get; init; }

        /// <summary>
        /// Advancing assault units (enemy only): how far this unit marches toward the player
        /// after each enemy volley, and the unconsumed budget for the current march. Forces a
        /// target-priority decision — stop the advancers, or hit the force behind them.
        /// </summary>
        public float AdvancePerTurn { get; init; }
        public float AdvanceRemaining { get; init; }

        /// <summary>
        /// Reinforcements (player only): the formation slot this unit is still jogging toward
        /// after entering from the player's edge. null = in position.
        /// </summary>
        public float? MarchTargetX { get; init; }

        /// <summary>
        /// Cosmetic "blown into the air" hop for a unit that SURVIVED a splash hit — distinct
        /// from the death ragdoll launch. -1 = inactive; otherwise counts up and the renderer
        /// derives a sine-arc offset from it, snapping back to the formation slot on expiry.
        /// </summary>
        public float KnockbackAge { get; init; } = -1f;
        public float KnockbackDirX { get; init; }

        /// <summary>
        /// Seconds since this unit last took damage and survived, or -1 for "not recently hit".
        /// Drives the health bar, which shows on a hit and fades out a few seconds later.
        ///
        /// Per-unit rather than a counter, which is the whole point: the tick already tallies
        /// TotalWoundedHits, and a running total can say SOMETHING was wounded but never WHICH.
        /// A kill does not set it — the death clip and the ragdoll say what happened, and a bar
        /// appearing over a body as it starts to fall is a second, contradictory event.
        /// </summary>
        public float LastHitAge { get; init; } = -1f;
    }

    public record StructureEntity(
        int Id,
        StructureDefinitionSO Definition,
        float X, float Y, float Z,
        int Hp)
    {
        /// <summary>
        /// Starting HP for THIS PLACEMENT — definition.maxHp already multiplied by the level's
        /// hpScale. Damage must be a fraction of what the structure actually had, not of what
        /// its definition says: a 4x rig wall read hp/definition.maxHp = 2.75 at full health, so
        /// `1 - fraction` went negative and it never shed a chunk however hard it was hit.
        /// Every hpScale != 1 placement had the same silent hole.
        ///
        /// Defaults to Hp, correct by construction: a structure is built at full health and the
        /// original carries through every later damage update.
        /// </summary>
        public int MaxHp { get; init; } = Hp;

        /// <summary>Auto-collapses the tick the structure with this runtime id is destroyed.</summary>
        public int? CollapseWith { get; init; }

        /// <summary>
        /// Runtime id of the structure this one physically rests on. When the supporter dies this
        /// collapses with it, transitively up the stack — take out a fortress's bottom tier and
        /// everything above comes down, garrisons included.
        /// </summary>
        public int? RestsOnId { get; init; }

        /// <summary>
        /// How many damage-chunk groups have broken off. Monotonic: the tick spawns falling
        /// rubble for each newly shed group exactly once and the renderer hides the same groups,
        /// so the gap in the silhouette and the pile at the foot agree.
        /// </summary>
        public int ShedChunks { get; init; }

        public float HpFraction => (float)Hp / Mathf.Max(MaxHp, 1);
    }

    public record ProjectileEntity(
        int Id,
        float X, float Y, float Z,
        float Vx, float Vy, float Vz,
        int Damage,
        bool OwnerIsPlayer)
    {
        public ProjectileType Type { get; init; } = ProjectileType.Bullet;
        public BulletVariant BulletVariant { get; init; } = BulletVariant.Standard;
        public float SplashRadius { get; init; }
        public float StructureDamageMultiplier { get; init; } = 1f;

        /// <summary>
        /// Excluded from the volley-follow camera's target mean, so a helicopter's door-gunner
        /// bullets never pull the camera off the ground volley.
        /// </summary>
        public bool IsHeliShot { get; init; }
        public bool IsAirstrike { get; init; }

        /// <summary>
        /// A round of the aircraft's CANNON, drawn as a stretched tracer streak rather than as a
        /// round dot. Not cosmetic trim: at the run's framing a 0.22-scale bullet covers a third
        /// of its own width per frame, so a burst of them draws a faint dotted chain — and Rob,
        /// looking at the real thing, reported no visible difference between SEVEN rounds and
        /// FOURTEEN. Count was never the bottleneck; the round has to read as gunfire in one
        /// frame, which means a streak.
        /// </summary>
        public bool IsStrafe { get; init; }
        public AmmoType Ammo { get; init; } = AmmoType.Standard;

        public float SpawnX { get; init; } = X;
        public float SpawnY { get; init; } = Y;
        public float SpawnZ { get; init; } = Z;
        public float Age { get; init; }

        /// <summary>
        /// Previous tick position. The collision check is SWEPT against this, so a fast or
        /// steeply-descending round cannot tunnel past a target between two ticks.
        /// </summary>
        public float PrevX { get; init; } = X;
        public float PrevY { get; init; } = Y;
        public float PrevZ { get; init; } = Z;
    }

    public record ExplosionEntity(int Id, float X, float Y, float Z)
    {
        public float Scale { get; init; } = 1f;
        public float Progress { get; init; }
        public bool IsEnemyFire { get; init; }
        public bool IsStructureHit { get; init; }
        public bool ShowFlash { get; init; } = true;
    }

    public enum HeliMode { Preview, Entering, Hovering, GunRun, Retreating, Crashing }

    public record HelicopterEntity(float X, float Y, float Vx, HeliMode Mode, int BurstsLeft)
    {
        public int Hp { get; init; }
        /// <summary>Hp &lt; MaxHp means wounded: trails smoke and RETREATS instead of gun-running.</summary>
        public int MaxHp { get; init; }
        public float HoverX { get; init; }
        public float Vy { get; init; }
        public float Rotation { get; init; }
        public float Age { get; init; }
        public float FireCooldown { get; init; }
    }

    public record SkirmishEntity(int AttackerId, int VictimId)
    {
        public float Age { get; init; }
    }

    public record WreckEntity(
        int Id,
        string DefinitionId,
        float X, float Y, float Z,
        float Width, float Height)
    {
        public bool Crushed { get; init; }
        public int? SupporterId { get; init; }
        public float Vy { get; init; }
        public float Pile { get; init; } = 1f;
        public int? RootId { get; init; }
        public float Age { get; init; }
    }

    public record DebrisPiece(
        int Id,
        string DefinitionId,
        bool Accent,
        float X, float Y, float Z,
        float Vx, float Vy,
        float Rotation, float RotationSpeed,
        float Size,
        float Ttl)
    {
        public bool Asleep { get; init; }
        public bool IsRubble => Ttl >= float.MaxValue;

        /// <summary>
        /// Vertical squash, 1 = the cube a tumbling chunk renders as.
        ///
        /// A RUIN is not a pile of cubes. Masonry that has come down lies FLAT and WIDE — the
        /// silhouette is what makes a wreck read as a collapsed building rather than as scattered
        /// crates, and at this camera's ~6° the height of a lump is most of what you can see of
        /// it. Slabs use ~0.3.
        /// </summary>
        public float Squash { get; init; } = 1f;
    }

    public record ScorchMark(int Id, float X, float Z)
    {
        public float Scale { get; init; } = 1f;
    }

    public record ImpactEntity(int Id, float X)
    {
        public float Y { get; init; }
        public float Progress { get; init; }
    }

    public record DyingUnitEntity(
        int Id,
        UnitDefinitionSO Definition,
        bool IsPlayerSide,
        float X, float Y, float Z,
        float Vx, float Vy,
        float RotationSpeed)
    {
        public float Vz { get; init; }
        public float Rotation { get; init; }
        public float SettleTilt { get; init; }
        public float Yaw { get; init; }
        public float YawSpeed { get; init; }
        public float TiltSpeed { get; init; }

        /// <summary>
        /// True when this body fell off a deck. Dirt deaths tip over and do not
        /// tumble or flail against masonry.
        /// </summary>
        public bool Tumble { get; init; }
        public float Age { get; init; }
        public bool Asleep { get; init; }

        /// <summary>
        /// Y of the surface this body is sitting on this tick (ground rest or a roof).
        /// Negative means airborne. The renderer keys the flail off THIS, not off
        /// <see cref="CosmeticSystems.RagdollRestY"/> — that is dirt, so a garrison
        /// on a deck at y=2.5 read as airborne for the whole 5s they exist and
        /// thrashed on the roof. Rob, 2026-08-14.
        /// </summary>
        public float SupportY { get; init; } = -1f;

        /// <summary>
        /// Fold toward this sign in GAME x when the body is against masonry.
        /// +1 = toward +X (into a wall approached from the left, or off the right lip),
        /// -1 = toward -X, 0 = no contact. Carried so a slump at the base of a wall
        /// survives the tick they leave the box.
        /// </summary>
        public float Bend { get; init; }
    }

    /// <summary>
    /// The airstrike's aircraft, mid-pass. Cosmetic in every respect EXCEPT the moment it releases
    /// its bomb — it cannot be shot at, does not collide, and carries no health, which is why it is
    /// not a UnitEntity.
    ///
    /// X is GAME space, so it INCREASES toward the enemy; the renderer routes it through
    /// GameSpace.ToUnity like everything else. `HasDropped` is the latch that stops one pass
    /// releasing a second bomb — the drop is an edge, and edges in this tick have to be recorded
    /// rather than re-derived, because dt varies and a position test can straddle the point.
    /// </summary>
    /// <param name="ExitX">
    /// Where the aircraft stops existing. Carried ON THE ENTITY so its motion needs nothing else:
    /// the plane outlives the phase that launched it — it is still exiting frame while the volley
    /// resolves — and a despawn test that had to re-derive the target from the aim would stop
    /// being computable the moment that aim was spent. The first build did exactly that, and the
    /// aircraft hung motionless in the sky for the rest of the battle.
    /// </param>
    public record AirstrikePlaneEntity(float X, float Y, float Vx, float ExitX)
    {
        public bool HasDropped { get; init; }

        /// <summary>
        /// How many cannon rounds of the strafing burst have been fired. A COUNT, not a timer:
        /// each round is released at the position that lands it on its own point of the walk, and
        /// dt varies, so a timer would space the burst differently on a stuttering frame.
        /// </summary>
        public int StrafeFired { get; init; }

        /// <summary>
        /// The ground the strafing run rakes — CARRIED, and fixed the moment the aircraft is
        /// committed.
        ///
        /// **The burst is independent of the player's volley** (Rob, 2026-08-11: *"the strafe is
        /// independent of the player unit volley. it should start from the left, strafe should
        /// cover the whole enemy position and its structures."*). So this is derived from where
        /// the ENEMY is, not from where the shot was aimed — the bomb is the only part of an
        /// airstrike that cares about the aim.
        ///
        /// It is carried rather than recomputed per tick for two reasons: the run OUTLIVES its own
        /// phase, so anything recomputed from live state would keep changing under it; and the
        /// enemy set shrinks as the rake kills, which would walk the far end of the burst backwards
        /// while it was still firing.
        /// </summary>
        public float StrafeFromX { get; init; }
        public float StrafeToX { get; init; }

        /// <summary>
        /// Id of this run's FIRST cannon round; the rest follow it. Carried because the burst is
        /// fired from the always-run physics path, which has no access to the state's slot
        /// counters — and because ids must stay globally unique, since hit tracking keys off them.
        /// </summary>
        public int StrafeIdFirst { get; init; }

        /// <summary>
        /// Where this run's BOMB is going. Carried, because the aim it came from is cleared the
        /// moment the volley launches — and the volley now usually launches BEFORE the aircraft is
        /// even released, so deriving the target from `PendingVolleyAim` would read a null the
        /// whole time the bomb was in the air.
        /// </summary>
        public float BombTargetX { get; init; }
    }

    public static class StructureDamage
    {
        /// <summary>
        /// How many damage-chunk groups a structure at this HP fraction has shed. ONE definition,
        /// shared by the tick (which spawns the falling rubble) and the renderer (which hides the
        /// geometry) — they described the same curve separately before, which is the kind of
        /// duplication that drifts. First group goes after ~1/(n+1) of the damage, last just
        /// before death.
        /// </summary>
        public static int ShedChunkCount(float hpFraction, int groups)
            => Mathf.Clamp((int)((1f - hpFraction) * (groups + 1)), 0, groups);
    }
}
