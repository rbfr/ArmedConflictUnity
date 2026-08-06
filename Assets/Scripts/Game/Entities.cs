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
        /// Cosmetic "this one just took a hit" flash for a unit that SURVIVED. -1 = inactive;
        /// otherwise counts up and the renderer tints the whole body while it is running.
        ///
        /// Per-unit rather than a counter, which is the whole point: the tick already tallies
        /// TotalWoundedHits, and a running total can say that SOMETHING was wounded but never
        /// WHICH. Set on any damaging hit that leaves the unit alive — a kill has the death
        /// animation and the ragdoll to say what happened, and flashing a body on the frame it
        /// starts falling over reads as a second, unrelated event.
        /// </summary>
        public float HitFlashAge { get; init; } = -1f;
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
        public float Age { get; init; }
        public bool Asleep { get; init; }
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
