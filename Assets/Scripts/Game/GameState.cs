using System.Collections.Generic;
using UnityEngine;
using ArmedConflict.Data;

namespace ArmedConflict.Game
{
    public enum GamePhase { Preview, Playing, Victory, Defeat }
    public enum TurnSide { Player, Enemy }

    public enum TurnPhase
    {
        /// <summary>
        /// The player tank rolls on from the left edge with its crew riding. First beat after
        /// BEGIN, only on levels that field a cannon. Hands over to PlayerScout.
        /// </summary>
        TankArrive,
        PlayerScout,   // camera pans to the enemy side so the player sees the layout before aiming
        Aiming,
        EnemyWindup,
        Resolving,
        /// <summary>
        /// The airstrike's aircraft is making its pass, BEFORE the volley it was fired with.
        ///
        /// The only phase that is not a turn handover, and it exists for a measured reason: the
        /// bomb used to detonate off-screen, ~0.85s before the volley-follow camera finished
        /// panning (device capture, 2026-08-10). Nothing the player paid 250 coins for was ever
        /// visible. With no rounds in the air yet there is nothing to chase, so the pass can own
        /// the frame — see `_plans/archive/AIRSTRIKE_PLANE.md`.
        /// </summary>
        AirstrikeRun,
    }

    /// <summary>
    /// Port of GameState.kt — the immutable state the whole tick operates on.
    ///
    /// A `record`, so `with` is Kotlin's `copy()` and value equality is preserved. Note the
    /// Android build put this behind a StateFlow that conflated by equality, which is what made
    /// a single never-settling float catastrophically expensive (see SpringFollow's rest
    /// deadband). Unity has no StateFlow, so that particular blast radius is gone — but the
    /// deadband stays, because a resting value should be bit-identical tick to tick regardless.
    ///
    /// Collections are exposed as IReadOnlyList: `with` gives shallow copies, so a mutable list
    /// shared between two states would let a "previous" state change underneath the tick.
    /// </summary>
    public record GameState
    {
        // ---- level identity -------------------------------------------------------------
        public int BattleId { get; init; }
        public string LevelId { get; init; } = "";
        public string LevelDisplayName { get; init; } = "";
        public string LevelGoal { get; init; } = "";
        public int LevelNumber { get; init; } = 1;
        public int TotalLevels { get; init; } = 5;
        public BackgroundDefinitionSO Background { get; init; }
        public IReadOnlyList<PropPlacement> Props { get; init; } = new List<PropPlacement>();

        // ---- progression / results ------------------------------------------------------
        public int InitialPlayerCount { get; init; }
        public bool PlayerMarchInProgress { get; init; }
        public bool ReinforcementsSent { get; init; }
        public int StarsEarned { get; init; }
        public int CoinsEarned { get; init; }
        public string CoinsBonusTag { get; init; }
        public string UnitUnlockedName { get; init; }

        // ---- entities -------------------------------------------------------------------
        public IReadOnlyList<UnitEntity> PlayerUnits { get; init; } = new List<UnitEntity>();
        public IReadOnlyList<UnitEntity> EnemyUnits { get; init; } = new List<UnitEntity>();
        public IReadOnlyList<StructureEntity> Structures { get; init; } = new List<StructureEntity>();
        public IReadOnlyList<ProjectileEntity> Projectiles { get; init; } = new List<ProjectileEntity>();
        public IReadOnlyList<ExplosionEntity> Explosions { get; init; } = new List<ExplosionEntity>();
        public IReadOnlyList<ScorchMark> Scorches { get; init; } = new List<ScorchMark>();
        public IReadOnlyList<WreckEntity> Wrecks { get; init; } = new List<WreckEntity>();
        public IReadOnlyList<DebrisPiece> Debris { get; init; } = new List<DebrisPiece>();
        public IReadOnlyList<ImpactEntity> Impacts { get; init; } = new List<ImpactEntity>();
        public IReadOnlyList<DyingUnitEntity> DyingUnits { get; init; } = new List<DyingUnitEntity>();

        /// <summary>
        /// Launch elevation each ENEMY unit actually fired at, in degrees, keyed by unit id.
        /// Populated by FireEnemyVolley from the velocity it really used and cleared when the
        /// turn comes back, so the raised rifles can never describe a different shot than the one
        /// in the air. Keyed rather than indexed because units die and a positional list would
        /// re-point every survivor's pose at its neighbour's shot.
        ///
        /// Cosmetic, like the scorch marks and wrecks above it — nothing in the simulation reads
        /// it. The angle is DERIVED rather than chosen here on purpose: EnemyAI picks a random
        /// arc inside AimAt, and a second draw for display would be a different number than the
        /// one fired.
        ///
        /// Populated at the START of EnemyWindup (PrepareEnemyVolley) so the rifles can raise
        /// for the whole windup and then fire the same arc. Cleared when the turn comes back.
        /// </summary>
        public IReadOnlyDictionary<int, float> EnemyAimDegrees { get; init; }
            = new Dictionary<int, float>();

        /// <summary>
        /// The launch velocity each enemy will fire, keyed by unit id. Written with
        /// <see cref="EnemyAimDegrees"/> so FireEnemyVolley cannot re-roll a different shot
        /// than the one the rifles were posed at.
        /// </summary>
        public IReadOnlyDictionary<int, Vector3> EnemyLaunch { get; init; }
            = new Dictionary<int, Vector3>();
        public IReadOnlyList<SkirmishEntity> Skirmishes { get; init; } = new List<SkirmishEntity>();
        public HelicopterEntity Helicopter { get; init; }

        // ---- BOUNDED round-robin slot pools ---------------------------------------------
        // Never monotonic ids: the Android renderer's zero-disposal registries would grow
        // without limit and accumulate per-frame callbacks, which was a real lag bug. Kept on
        // the port because the id BANDS also guarantee raw ids stay globally unique for
        // hit-tracking, which the tick depends on independently of any renderer.
        public int NextScorchSlot { get; init; }
        public int NextRubbleSlot { get; init; }
        public int NextDebrisSlot { get; init; }
        public int NextBulletSlot { get; init; }
        public int NextRocketSlot { get; init; }
        public int NextGrenadeSlot { get; init; }
        public int NextShellSlot { get; init; }
        public int NextExplosionSlot { get; init; }

        // ---- turn flow ------------------------------------------------------------------
        public GamePhase Phase { get; init; } = GamePhase.Preview;
        public TurnSide TurnSide { get; init; } = TurnSide.Player;
        public TurnPhase TurnPhase { get; init; } = TurnPhase.Aiming;
        public float EnemyAimTimer { get; init; }
        public float TurnHandoverDelay { get; init; }

        /// <summary>
        /// Seconds left on the opening scout. Armed by LoadLevel; the tick counts it down and
        /// hands over to Aiming. Zero after the first aim, and after every later turn.
        /// </summary>
        public float ScoutTimer { get; init; }

        /// <summary>
        /// Seconds left on the tank's roll-in. Armed by <see cref="TurnFlow.StartBattle"/>;
        /// the tick eases the vehicle and its crew to <see cref="TankParkX"/> then hands
        /// over to PlayerScout. Zero on levels with no cannon.
        /// </summary>
        public float TankArriveTimer { get; init; }

        /// <summary>Authored X of the player tank — the slot the roll-in parks in.</summary>
        public float TankParkX { get; init; }

        /// <summary>
        /// THE BEAT THE CAMERA HOLDS ON A MELEE AFTER THE LAST PAIR HAS FALLEN, and the frame it
        /// holds while doing it.
        ///
        /// Rob, fourth device build: *"we still are in a hurry to zoom back to the main force. we
        /// need to show the melee assault the whole time and pause so it registers with the
        /// player."* Releasing the camera on the tick the last skirmish resolved meant the payoff
        /// — the two bodies actually falling — happened as the camera was already leaving. Same
        /// family as `TurnHandoverDelay`, which exists because the handover used to tread on the
        /// impact the player was still reading.
        ///
        /// The anchor and half-width are CARRIED rather than recomputed, because once the fight is
        /// over its participants are gone from the unit lists and there is nothing left to frame
        /// from — recomputing would snap to whatever remains on the tick the hold begins.
        /// </summary>
        public float MeleeHold { get; init; }
        public float MeleeHoldAnchorX { get; init; }
        public float MeleeHoldHalfWidth { get; init; }

        /// <summary>
        /// Camera hold on a structure that just fell with its garrison. Armed the
        /// tick the building dies. The first beat rides the falling bodies
        /// (anchor/half-width recomputed from the live tumble set); after that
        /// the hold only freezes the windup so the spring can pan back to
        /// whoever is still standing. Decays on every tick path.
        /// </summary>
        public float CollapseHold { get; init; }
        public float CollapseHoldAnchorX { get; init; }
        public float CollapseHoldHalfWidth { get; init; }

        /// <summary>
        /// Seconds the camera stays on the last kill after Victory. Armed on the Playing →
        /// Victory edge and decayed on every path, including the cosmetic-over one — a hold
        /// that only decayed in the combat block would freeze on the victory screen.
        /// </summary>
        public float VictoryCamHold { get; init; }
        public int TurnNumber { get; init; } = 1;

        // ---- player armament ------------------------------------------------------------
        public int TankShellsRemaining { get; init; }
        public bool CannonArmed { get; init; } = true;
        public IReadOnlyDictionary<ConsumableType, int> LoadedConsumables { get; init; }
            = new Dictionary<ConsumableType, int>();
        public bool AirstrikeArmed { get; init; }
        public bool SmokeScreenArmed { get; init; }

        /// <summary>The aircraft in the middle of its pass, or null. See TurnPhase.AirstrikeRun.</summary>
        public AirstrikePlaneEntity AirstrikePlane { get; init; }

        /// <summary>
        /// The aim the player released, HELD across the airstrike run so the volley can be built
        /// from it a beat later.
        ///
        /// It lives in the state rather than on the runner because the volley it builds is
        /// gameplay: a runner-held aim would put turn sequencing in a MonoBehaviour, and would be
        /// lost by anything that rebuilds the state mid-run.
        /// </summary>
        public Vector3? PendingVolleyAim { get; init; }

        /// <summary>
        /// Seconds until the aircraft is released onto the field, and seconds until the held volley
        /// launches. **At most one of these is ever non-zero** — they are the two halves of one
        /// alignment, and whichever half takes LONGER to reach the target starts first.
        ///
        /// The two used to be added together: the plane made its whole pass, and only when its bomb
        /// landed did the volley launch. That cost 4.53s from release to impact on an ordinary 86%
        /// shot, a third of it spent watching an aircraft with none of the player's own rounds in
        /// the air. Rob: *"i wonder if we can sync the player projectile volley with the plane.
        /// right now it's a little awkward."* Landing them TOGETHER makes the beat
        /// `max(flight, run)` instead of `flight + run` — 2.91s on that same shot — and turns two
        /// events into one impact.
        ///
        /// Both tick down on the ALWAYS-RUN physics path, not inside a phase: the volley's timer in
        /// particular has to survive the phase it was started in, and this beat has already paid
        /// twice for putting time-dependent work inside `TurnPhase.AirstrikeRun`.
        /// </summary>
        public float AirstrikeSpawnDelay { get; init; }
        public float PendingVolleyDelay { get; init; }

        public bool OverwatchFlareArmed { get; init; }
        public AmmoType SelectedAmmo { get; init; } = AmmoType.Standard;
        public IReadOnlyCollection<int> BurningEnemyIds { get; init; } = new HashSet<int>();

        // ---- events ---------------------------------------------------------------------
        public IReadOnlyCollection<int> TriggeredBossPhases { get; init; } = new HashSet<int>();
        public string BossAnnouncement { get; init; }
        public float BossAnnouncementTimer { get; init; }
        public IReadOnlyCollection<int> TriggeredReinforcementWaves { get; init; } = new HashSet<int>();

        /// <summary>
        /// The standing warning for a wave arriving NEXT turn, or null.
        ///
        /// Deliberately not an announcement with a timer: an announcement is a flash that reports
        /// something that just happened, a telegraph is a condition that stays true until it
        /// resolves. Pillar 7 is "telegraph, don't blindside", and a warning that fades after two
        /// seconds has blindsided anyone who looked away — it has to still be on screen while the
        /// player takes the turn it is warning them about.
        /// </summary>
        public string TelegraphText { get; init; }

        public float WindAccelZ { get; init; }
        public string WindShiftAnnouncement { get; init; }
        public float WindShiftAnnouncementTimer { get; init; }

        // ---- tallies --------------------------------------------------------------------
        public IReadOnlyDictionary<int, Vector3> EnemyAimVelocities { get; init; }
            = new Dictionary<int, Vector3>();
        public int LastPlayerVolleyKills { get; init; }
        public int LastEnemyVolleyKills { get; init; }
        public int TotalPlayerKills { get; init; }
        public int TotalEnemyKills { get; init; }
        public int TotalWoundedHits { get; init; }
        public int TotalGroundImpacts { get; init; }
        public int TotalStructureImpacts { get; init; }
        public int TotalHeliHits { get; init; }
        public int TotalHeliCrashes { get; init; }

        /// <summary>
        /// Blasts worth an EXPLOSION sound — splash weapons and structure hits only.
        /// A rifle round striking a soldier is not an explosion; it gets the hit/death sound.
        /// Kept separate from the explosion LIST because that list also carries the small
        /// cosmetic puffs, and treating every one of them as a bang is what made a rifle volley
        /// sound like artillery.
        /// </summary>
        public int TotalBlasts { get; init; }

        // ---- camera ---------------------------------------------------------------------
        // Everything here is computed IN THE TICK, never in a UI coroutine, so the camera and
        // the projectiles it tracks advance atomically in one state. A separate follow loop
        // beats against the tick clock and makes projectiles jitter on screen.
        public float ShakeIntensity { get; init; }
        public float? CameraFollowX { get; init; }
        /// <summary>SpringFollow velocity for CameraFollowX — carried tick to tick so a
        /// retargeted chase stays continuous instead of snapping. Reset to 0 whenever
        /// CameraFollowX resets.</summary>
        public float CameraFollowXVelocity { get; init; }
        public float? CameraFollowZ { get; init; }
        public float CameraFollowZVelocity { get; init; }

        /// <summary>
        /// Sticky once a REAL gameplay helicopter has been active, reserving camera margin for
        /// its hover spot. Deliberately never resets mid-battle even if the heli retreats or
        /// crashes: a one-time pull-out reads as "the camera noticed something", whereas a
        /// margin that arrives, leaves and returns reads as broken.
        /// </summary>
        public bool HeliEverActive { get; init; }

        /// <summary>
        /// Separate, much slower blend for the heli margin's width contribution. HeliEverActive
        /// flipping true often lands on the same tick as an unrelated zoom transition, and the
        /// normal ~0.12s camera smoothing compresses the pair into one lurch (measured: a
        /// ~3.4-unit swing became ~7.9). Its own long smooth time spreads the margin's arrival
        /// so it cannot compound with whatever else the zoom is doing.
        /// </summary>
        public float HeliMarginBlend { get; init; }
        public float HeliMarginBlendVelocity { get; init; }

        /// <summary>
        /// Stable per-side anchors. The player's is the GROUND LINE (not the tank crew).
        /// The enemy's is recaptured when a structure falls or a boss/wave lands — never
        /// on a casualty, which is the membership twitch the camera architecture forbids.
        /// </summary>
        public float PlayerCamXAnchor { get; init; } = -6f;
        public float EnemyCamXAnchor { get; init; } = 6f;

        /// <summary>
        /// Framing half-widths. Player is the ground line, captured at load. Enemy is
        /// recaptured on the same events as the enemy anchor. Casualties do not resize
        /// either: a shrinking roster twitching the zoom is the bug these exist to prevent.
        /// </summary>
        public float PlayerCamHalfWidth { get; init; } = 3f;
        public float EnemyCamHalfWidth { get; init; } = 3f;

        /// <summary>
        /// Push-in on the group that just walked onto the field. Armed with the
        /// announcement timer; half-width 0 means nothing to reveal.
        ///
        /// L12's shield escort is the reason: after the citadel falls the captured
        /// enemy frame is still the fortress, and four men with riot shields read as
        /// a speck. The leftover cluster is recaptured too; this is tighter still,
        /// for the 2.5s the banner is up.
        /// </summary>
        public float ArrivalCamXAnchor { get; init; }
        public float ArrivalCamHalfWidth { get; init; }

        /// <summary>
        /// A ZOOM CEILING ONLY. It used to pin camera X as well, which disabled the whole
        /// per-phase choreography and left each phase sizing its zoom about a centre the camera
        /// wasn't using — cropping the subject instead of framing it.
        /// </summary>
        public bool StaticCamera { get; init; }
        public float StaticCamZ { get; init; } = 19f;

        public float? VolleyCenterX { get; init; }
        public float VolleyCenterXVelocity { get; init; }

        // ---- derived --------------------------------------------------------------------

        /// <summary>
        /// True when nothing on screen is MOVING. Two traps are baked into this, both of which
        /// silently disabled it permanently in the Android build:
        ///
        /// SLEEPING debris does not count — structure rubble persists for the whole level, so
        /// `Debris.Count == 0` would be false forever the moment anything was destroyed.
        ///
        /// Wrecks are tested against the COLLAPSE WINDOW, not an arbitrary timeout. A wreck's
        /// age stops advancing once it passes WreckCollapseSeconds, so it freezes at ~0.55 and
        /// any threshold above that is never satisfied again.
        /// </summary>
        public bool IsVisuallyIdle
        {
            get
            {
                if (Phase != GamePhase.Playing || TurnPhase != TurnPhase.Aiming) return false;
                if (Projectiles.Count > 0 || Explosions.Count > 0) return false;
                if (DyingUnits.Count > 0 || Skirmishes.Count > 0) return false;
                foreach (var d in Debris) if (!d.Asleep) return false;
                foreach (var w in Wrecks) if (w.Age < WreckCollapseSeconds) return false;
                foreach (var e in EnemyUnits) if (e.AdvanceRemaining > 0f) return false;
                foreach (var p in PlayerUnits) if (p.MarchTargetX != null) return false;
                // A camera still settling: SpringFollow carries velocity, and a rate change
                // mid-glide is exactly where a judder would be most visible.
                if (Mathf.Abs(CameraFollowXVelocity) > 1e-3f) return false;
                if (Mathf.Abs(CameraFollowZVelocity) > 1e-3f) return false;
                return true;
            }
        }

        public const float WreckCollapseSeconds = 0.55f;

        /// <summary>
        /// Reference equality, DELIBERATELY replacing the record's synthesized value equality.
        ///
        /// Two reasons, and the first is not optional. With ~90 fields the compiler synthesises
        /// an Equals chaining ~90 `&&` comparisons; IL2CPP transpiles that to nested parentheses
        /// deeper than clang's 256-bracket limit, and the Android build fails outright with
        /// "bracket nesting level exceeded maximum of 256".
        ///
        /// The second is that value equality bought something specific in the Android build and
        /// buys nothing here. There, GameState sat behind a StateFlow that conflated by equality,
        /// so comparing every field decided whether the UI recomposed — which is exactly why one
        /// never-settling float was catastrophic. Unity has no StateFlow; nothing conflates, and
        /// a 90-field comparison every tick would be pure cost.
        ///
        /// `with` is unaffected — copy semantics are what the architecture actually depends on.
        /// The entity records keep their value equality; they are small and the tick uses it.
        /// </summary>
        public virtual bool Equals(GameState other) => ReferenceEquals(this, other);

        public override int GetHashCode()
            => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(this);
    }
}
