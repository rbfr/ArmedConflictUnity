using UnityEngine;
using ArmedConflict.Game;

/// <summary>
/// Drives a unit's pose from the game state. Deliberately tiny: the game is turn-based and a
/// unit is only ever in one of three visible situations, so there is no blend tree, no state
/// machine and no per-frame logic beyond what Legacy `Animation` already does.
///
/// LEGACY animation on purpose. The rig is boxes parented in a hierarchy — 0 skins, 0 bones — so
/// every clip is a handful of TRS curves on named child transforms. There is nothing for skinning
/// to do, and Mecanim would cost an AnimatorController per character plus the Animator's own
/// per-frame graph evaluation to play one looping clip. This matters: the Android build's profile
/// is a standing warning about `Animator` cost on nodes with nothing to animate
/// (`gltfio::Animator::updateBoneMatrices` at ~5.2% of the main thread for a game with no
/// skinning at all).
///
/// FOUR LAYERS, and the order is the whole design. Legacy gives a higher layer priority over a
/// lower one, so each state only has to override the joints it cares about:
///
///   0  idle   whole-body breathing loop, always running (walk replaces it while marching)
///   1  hold   rifle at the ready — ARMS ONLY, via mixing transforms, always running
///   2  shoot  recoil, one-shot; ends by itself and the layers below simply reappear
///   2  melee  the swing, looping for as long as the fight runs; see SetFighting
///   3  die    everything including root, ClampForever
///
/// Without layer 1 the troops stand at ease with a rifle floating beside them, because `idle`
/// swings the arms down. Without layer 2 being ABOVE it, firing does nothing visible: the hold
/// would keep winning the arms.
///
/// `shoot` and `melee` SHARE layer 2 rather than stacking, because they are alternatives: a man
/// swinging a rifle butt is not also firing it. Legacy resolves same-layer clips by whoever was
/// played last, which is the behaviour wanted — and the volley is held off for the duration of a
/// skirmish anyway (see AdvanceSystems), so the two do not compete in practice.
/// </summary>
public class UnitAnim : MonoBehaviour
{
    // Clip names from Kenney's Blocky Characters 2.0 (CC0). Any replacement rig has to ship
    // these six or the mapping moves here, not into the caller.
    public const string Idle = "idle";
    public const string Hold = "holding-both";
    public const string Shoot = "holding-both-shoot";
    public const string Die = "die";
    /// <summary>Advancing assault squads, added 2026-08-12 with the melee port. Shares layer 0
    /// with the idle — they are both full-body loops and only one may win.</summary>
    public const string Walk = "walk";
    /// <summary>
    /// Kenney's walk is a ±60° hip swing on a 0.667s cycle. The player's opening arrive (and
    /// any later relief march) is a march: same clip, half the swing, slower cadence.
    /// </summary>
    public const float MarchStride = 0.5f;
    public const float MarchAnimSpeed = 0.7f;

    /// <summary>
    /// THE ENEMY CHARGE, re-gaited 2026-08-24. It used to play the clip raw — stride 1,
    /// speed 1 — and Rob read the result as “too dramatic” rather than as a run. Measured,
    /// the clip is not a run at all: ±60° at the hip is a scissor no runner makes, and at
    /// 1.5 cycles/s it is only 3 steps a second, which is a stroll's cadence attached to a
    /// sprint's amplitude. A run is the other way round — quick legs, contained swing.
    /// 0.75 puts the hip at ±45°; the cadence comes from <see cref="GaitSpeed"/>.
    /// </summary>
    public const float ChargeStride = 0.75f;

    /// <summary>Hip swing at stride 1, one way, in degrees. MEASURED off the clip, not
    /// authored here — <c>PortSelfTest</c> samples the asset and asserts this matches.</summary>
    public const float FullSwingDegrees = 60f;

    /// <summary>Seconds per walk cycle at speed 1. Measured off the clip, asserted in
    /// <c>PortSelfTest</c>.</summary>
    public const float WalkCycleSeconds = 0.667f;

    /// <summary>
    /// World distance the FEET carry the body over one cycle at stride 1 — two steps of
    /// 2·L·sin(60°) with the leg joint 1.35 up a 2.70 model scaled to
    /// <see cref="UnitGeometry.UnitScaleUnits"/>. Measured 0.748 on the shield bearer, 0.767
    /// on the rifleman — the joint sits fractionally differently per class, which is why
    /// PortSelfTest measures the rig and allows the spread rather than demanding one number.
    /// </summary>
    public const float CycleCarryUnits = 0.748f;

    /// <summary>Cadence ceiling. 1.7 is ~5.1 steps/s — a sprinter's turnover, and the point
    /// past which quick legs stop reading as a run and start reading as fast-forward.</summary>
    public const float MaxGaitSpeed = 1.7f;

    /// <summary>
    /// Clip speed that makes the feet carry the body at <paramref name="groundSpeed"/>, so the
    /// legs answer to the march instead of windmilling at a fixed rate. The wire-slowed charger
    /// is why this is derived and not a second constant: at
    /// <c>AdvanceSpeed * WireSlowFactor</c> he crawls, and a fixed cadence had him sprinting on
    /// the spot to do it.
    ///
    /// The charge CLAMPS — matching 2.4 u/s outright wants 7.9 steps/s, which is a blur. The
    /// residual skate is deliberate and is affordable only because the camera FOLLOWS the
    /// charge: with no still ground to measure against, amplitude and cadence are what the eye
    /// reads, and those are now a run's.
    /// </summary>
    public static float GaitSpeed(float groundSpeed, float stride)
    {
        float swing = FullSwingDegrees * Mathf.Clamp01(stride);
        float carry = CycleCarryUnits * Mathf.Sin(swing * Mathf.Deg2Rad)
                                      / Mathf.Sin(FullSwingDegrees * Mathf.Deg2Rad);
        if (carry <= 0.0001f) return 1f;
        return Mathf.Clamp(groundSpeed / carry * WalkCycleSeconds, 0.5f, MaxGaitSpeed);
    }
    /// <summary>The melee swing, bound 2026-08-13. Without it a mutual kill is two bodies
    /// falling over at once: the mechanic is real and the player cannot read it.</summary>
    public const string Melee = "attack-melee-right";

    /// <summary>
    /// Elevation ADDED to the arms' animated pose, in degrees, 0 = the clip's own rest hold.
    /// Set from the live aim; the caller owns what it means, this only draws it.
    /// </summary>
    public float AimDegrees { get; set; }

    // Degrees per second the shown elevation chases the target. Fast enough to feel welded to
    // the finger, slow enough that the release does not snap.
    const float AimFollow = 14f;

    // Kenney's holding-both is both arms locked horizontal — present-arms, which is the
    // other half of the "handing it over" read. When nobody is aiming, sit a little below
    // that (low ready, still pointing downfield). A live AimDegrees replaces it so the
    // muzzle still matches the drag. A couple of degrees of breathe stops the rank reading
    // as a freeze-frame once the hold has taken the arms off the idle.
    public const float ReadyDrop = 16f;
    const float ReadyBreathe = 2.4f;

    /// <summary>
    /// How much of the lift above ready is a torso lean. Arms take the
    /// rest, so the muzzle still matches the drag (arms are children of
    /// the torso — adding the same angle twice would overshoot).
    /// </summary>
    public const float TorsoShare = 0.22f;
    public const float TorsoMax = 14f;
    const float HeadFollow = 0.4f;

    // Which way a positive AimDegrees lifts the muzzle. The soldier is built facing glTF +Z and
    // the `facing` pivot yaws the whole model, so elevation is always a pitch about the arm
    // parent's X — the sign is the only thing the facing can flip, and it does not, because the
    // pivot is ABOVE the torso.
    const float AimSign = -1f;

    // ---- the weapon hold -------------------------------------------------------------------
    //
    // Kenney's `holding-both` pitches BOTH arms -90 degrees about X, so they point straight
    // downfield, parallel, at shoulder width. On Kenney's own character the hands meet a weapon
    // in the middle; on ours the rifle hangs off `arm-right` alone (RiggedUnits.AttachGun), so
    // the left hand holds air and the weapon sits out at one shoulder. Rob, 2026-08-25:
    // *"it's not a natural pose. both arms are sticking out/raised, and the gun is in the right
    // hand. Can we try to get the model to show them holding the gun as a person would?"*
    //
    // The correction is applied HERE rather than by re-authoring the clip, for the reason the
    // aim lift is: this is the one place that runs after legacy `Animation` has written the arms,
    // and it composes with the aim instead of fighting it. Re-authoring would also have to be
    // redone against every future Kenney re-import.
    //
    // ONE BONE PER ARM, no elbow — so this cannot be a real firing stance. What it can do is
    // bring both hands onto the centreline where the weapon is and take the arms off the
    // horizontal, which is the difference between "carrying a rifle" and "sleepwalking".
    // The RIGHT arm carries the rifle and the rifle points where the barrel points, so its yaw
    // stays small — every degree inward is a degree the muzzle stops facing downfield. The LEFT
    // arm is free to reach across to the forestock, which is the whole read.
    // Chosen off UnitPosePreview's grid, candidate `c_natural`, judged WITH the ready drop
    // applied — the idle already pitches the arms down by ReadyDrop, so the hold's own drop only
    // has to take the last few degrees off the horizontal. Tuned to 14 first, against a preview
    // that was missing the ready drop, which would have shipped the arms ~16 degrees too low.
    // Chosen by MEASUREMENT, not by eye — UnitPosePreview prints both hands and the weapon in
    // rig space, and this is the pair that puts the rifle BETWEEN them: left hand x 0.138,
    // gun 0.268, right hand 0.338, against a control where the hands sat at -0.421 and +0.421
    // with the gun outboard at +0.625. Going further (50/-0.35, 55/-0.45) pushes the weapon back
    // out past the LEFT hand — the gap closes and then re-opens on the other side.
    public const float HoldLeftInward = 45f;
    public const float HoldLeftDrop = 4f;
    public const float HoldRightInward = 6f;
    public const float HoldRightDrop = 4f;
    /// <summary>
    /// Forearm flex on the LEFT elbow child, about local Z. The clips never write
    /// this joint — that is the whole point of parenting a child rather than
    /// inserting into torso/arm-*. Local Y is the bone axis (twist); X lifts the
    /// hand off the gun; Z is the hinge that puts the left hand on the forestock.
    /// −40 chosen off UnitPosePreview's 3/4 (the camera's view). Right elbow stays
    /// at identity so the gun (still on arm-right) and the right hand do not part.
    /// </summary>
    public const float HoldElbowFlex = -40f;

    /// <summary>
    /// One arm's hold correction, in the SHOULDER's frame — pre-multiply it, exactly as the aim
    /// lift is pre-multiplied, or it rolls the arm about its own length instead of swinging it.
    ///
    /// `inward` yaws the hand toward the body's centreline (mirrored per side, since the two
    /// shoulders face opposite ways); `drop` pitches it down off the horizontal.
    /// </summary>
    /// The drop is -AimSign, NOT AimSign: AimSign is -1 precisely so that a POSITIVE AimDegrees
    /// LIFTS the muzzle, so reusing it here raised the arms instead of lowering them. The preview
    /// caught it on the first render — both rifles pointing up over the shoulder.
    /// <summary>The arm pitch for a shown elevation. Shared with UnitPosePreview so the preview
    /// cannot drift from the runtime.</summary>
    public static Quaternion ArmLift(float armAim)
        => Quaternion.AngleAxis(AimSign * armAim, Vector3.right);

    /// The left arm yaws by +inward and the right by -inward, NOT the other way round. The first
    /// pass had it mirrored and swung the left hand AWAY from the weapon: measured in the rig's
    /// own space the left hand went from x -0.421 to -0.979 while the rifle sat at +0.625, so the
    /// gap between them GREW from 0.84 to 1.51. It survived a look at a 3/4 render because an
    /// arm swinging outward reads as "crossing" from that angle. Measure the hand, not the
    /// picture — `UnitPosePreview.Measure` prints both.
    public static Quaternion HoldCorrection(float inward, float drop, bool isLeft)
        => Quaternion.AngleAxis(-AimSign * drop, Vector3.right)
         * Quaternion.AngleAxis(isLeft ? inward : -inward, Vector3.up);

    Animation anim;
    Transform armL, armR;
    Transform elbowL, elbowR;
    Transform legL, legR;
    Transform torso, head;
    float shownAim;
    float readyPhase;
    bool dead;
    bool walkingNow;
    float walkStride = 1f;
    bool fightingNow;
    int flailSeed;
    float flailAge;
    bool flailing;
    float slump;       // -1..1, + = fold forward in model space

    /// <summary>
    /// The model root's authored rest transform, captured before any clip has played.
    ///
    /// `die` is the ONLY clip that drives the root — everything else is rotation on the joints
    /// below it — and Legacy `Animation` leaves a transform wherever the clip last sampled it when
    /// you stop. So stopping the death and restarting the idle brings every joint back EXCEPT the
    /// root, which stays face-up on the floor: the body plays a perfect breathing loop lying on
    /// its back. Re-arming a recycled slot has to put the root back by hand.
    /// </summary>
    Vector3 restPos;
    Quaternion restRot;
    Quaternion restLegL, restLegR;
    Quaternion restArmL, restArmR;

    void Awake()
    {
        anim = GetComponentInChildren<Animation>();
        if (anim == null) return;
        anim.playAutomatically = false;

        // BEFORE anything plays — this is the only moment the root is guaranteed to be at its
        // authored rest rather than at the last frame of whatever clip ran on this slot.
        restPos = anim.transform.localPosition;
        restRot = anim.transform.localRotation;

        torso = anim.transform.Find("torso");
        head = anim.transform.Find("torso/head");
        armL = anim.transform.Find("torso/arm-left");
        armR = anim.transform.Find("torso/arm-right");
        elbowL = anim.transform.Find("torso/arm-left/elbow-left");
        elbowR = anim.transform.Find("torso/arm-right/elbow-right");
        // `walk` drives the legs; `idle` does not. Capture the authored stance so a march
        // that stops mid-stride can stand back up — same trap as the root, one joint family
        // down. See RestoreStance.
        legL = anim.transform.Find("leg-left");
        legR = anim.transform.Find("leg-right");
        if (legL != null) restLegL = legL.localRotation;
        if (legR != null) restLegR = legR.localRotation;
        if (armL != null) restArmL = armL.localRotation;
        if (armR != null) restArmR = armR.localRotation;

        Layer(Idle, 0, WrapMode.Loop);
        Layer(Walk, 0, WrapMode.Loop);
        Layer(Hold, 1, WrapMode.ClampForever);
        Layer(Shoot, 2, WrapMode.Once);
        Layer(Melee, 2, WrapMode.Loop);      // a fight outlasts one swing; SetFighting ends it
        Layer(Die, 3, WrapMode.ClampForever);   // a looping death stands the corpse back up

        // The hold is restricted to the arms so the body underneath keeps breathing. Authoring a
        // combined breathe-plus-hold clip would work too, and would have to be kept in sync by
        // hand with every future pose.
        var hold = anim[Hold];
        if (hold != null)
            foreach (var joint in new[] { "torso/arm-left", "torso/arm-right" })
            {
                var t = anim.transform.Find(joint);
                if (t != null) hold.AddMixingTransform(t, recursive: true);
            }
        Stand();
    }

    /// <summary>
    /// Aim elevation, applied on top of whatever the clips just posed.
    ///
    /// LateUpdate is load bearing. Legacy `Animation` writes the arm transforms during Update, so
    /// anything set before that is simply overwritten and the arms never move — the same class of
    /// silent no-op as writing to a clip that is already marked legacy. Running after it makes
    /// this an ADDITIVE layer the animation system does not know about, which is what lets a
    /// static two-handed hold and a live aim coexist without an authored clip per angle.
    ///
    /// The rotation is PRE-multiplied so it happens in the shoulder's frame rather than the
    /// arm's: post-multiplying would roll the rifle around its own length instead of raising it.
    ///
    /// A corpse does not aim.
    /// </summary>
    void LateUpdate()
    {
        if (anim == null) return;

        // See SetWalking: `walk` bobs the ROOT, and once it stops nothing puts the root back.
        // Held every frame the unit is not walking, because the crossfade out keeps writing for
        // 0.15s after the march ends — a one-shot restore lands underneath it.
        //
        // `attack-melee-right` is the THIRD clip to drive the root: a ±0.10 lunge in local Z, the
        // step into the strike. Same exemption for the same reason — clamping the root while it
        // plays deletes the step and leaves a man swinging from the waist.
        if (walkingNow && !dead) ApplyStride();
        else if (!walkingNow && !fightingNow && (!dead || !flailing)) RestoreStance();

        // AIRBORNE CORPSES FLAIL. Kenney's `die` is a 0.33s fold then ClampForever, so
        // without this they finish the clip and fly the rest of the throw as a plank —
        // Rob, 2026-08-13. Additive, after Animation has posed them, same slot as aim.
        // Phase is the ragdoll's AGE (dt-integrated in the tick), not Time.deltaTime.
        //
        // Against masonry the flail IS the twitch Rob reported 2026-08-14. A slump
        // replaces it: the part that hit stops, the spine folds toward the contact.
        // Eased on the ragdoll clock, not Time.deltaTime — batchmode dt is ~0, and
        // the flail already lives on that clock for the same reason.
        if (dead && Mathf.Abs(slump) > 0.02f) ApplySlump();
        else if (dead && flailing) ApplyFlail();
        if (dead) return;

        if (armL == null && armR == null) return;
        ApplyElbows();

        // A man swinging a rifle butt is not sighting down it. Without this the PLAYER's victim
        // holds the live drag elevation through the whole fight, because SyncUnits hands his
        // whole line one aim pose and does not know he is busy.
        float target;
        if (dead || fightingNow) target = 0f;
        else if (Mathf.Abs(AimDegrees) < 0.5f)
        {
            // Time.time, not a per-tick multiply: the frequency is on the clock, so a
            // variable dt does not change how often a rank breathes.
            float breathe = Mathf.Sin((Time.time * 1.05f + readyPhase) * Mathf.PI * 2f);
            target = -(ReadyDrop + ReadyBreathe * breathe);
        }
        else target = AimDegrees;
        // Frame-rate independent chase. A bare per-frame lerp constant would silently change
        // speed with the refresh rate, which is the trap the Android build documents at length.
        shownAim = Mathf.Lerp(shownAim, target, 1f - Mathf.Exp(-AimFollow * Time.deltaTime));
        if (Mathf.Abs(shownAim) < 0.01f) return;

        SplitAim(shownAim, wholeBody: !walkingNow && !fightingNow,
                 out float torsoAim, out float armAim);

        if (torso != null && Mathf.Abs(torsoAim) > 0.01f)
            torso.localRotation = Quaternion.AngleAxis(AimSign * torsoAim, Vector3.right)
                                  * torso.localRotation;
        if (head != null && Mathf.Abs(torsoAim) > 0.01f)
            head.localRotation = Quaternion.AngleAxis(AimSign * torsoAim * HeadFollow, Vector3.right)
                                 * head.localRotation;

        var lift = ArmLift(armAim);
        // The hold correction rides UNDER the aim lift: the lift is the elevation the drag asked
        // for and must stay the outermost rotation, or the muzzle stops matching the shot.
        if (armL != null)
            armL.localRotation = lift * HoldCorrection(HoldLeftInward, HoldLeftDrop, true)
                               * armL.localRotation;
        if (armR != null)
            armR.localRotation = lift * HoldCorrection(HoldRightInward, HoldRightDrop, false)
                               * armR.localRotation;
    }

    /// <summary>
    /// Rest bend on the child elbow joints. Local Z is the hinge that reaches the
    /// forestock after the hold has pointed the arm downfield; see HoldElbowFlex.
    /// Only the left arm flexes: the gun is parented to arm-right, and bending that
    /// elbow would walk the hand off the grip.
    /// </summary>
    public static Quaternion ElbowFlex(float degrees)
        => Quaternion.AngleAxis(degrees, Vector3.forward);

    void ApplyElbows()
    {
        if (elbowL != null)
            elbowL.localRotation = ElbowFlex(HoldElbowFlex);
        if (elbowR != null)
            elbowR.localRotation = Quaternion.identity;
    }

    /// <summary>
    /// Split a shown elevation into torso lean + remaining arm pitch.
    /// Ready (−ReadyDrop) is arms-only — that hold is signed. Walking
    /// and fighting stay arms-only so a march does not lean into the
    /// shot. torso + arms == shownAim always, so the muzzle still is
    /// the drag.
    /// </summary>
    public static void SplitAim(float shownAim, bool wholeBody,
                                out float torsoAim, out float armAim)
    {
        armAim = shownAim;
        torsoAim = 0f;
        if (!wholeBody) return;
        float fromReady = shownAim + ReadyDrop;
        if (fromReady <= 1f) return;
        torsoAim = Mathf.Min(fromReady * TorsoShare, TorsoMax);
        armAim = shownAim - torsoAim;
    }

    void Layer(string clip, int layer, WrapMode wrap)
    {
        var s = anim[clip];
        if (s == null) return;
        s.layer = layer;
        s.wrapMode = wrap;
        s.weight = 1f;
    }

    /// <summary>
    /// Back on your feet. The ROOT is restored explicitly — see restPos: no clip below `die`
    /// touches it, so without this a recycled slot keeps the corpse's root transform and the unit
    /// stands its breathing loop up while still lying on its back.
    /// </summary>
    void Stand()
    {
        if (anim == null) return;
        anim.transform.localRotation = restRot;
        RestoreStance();
        walkingNow = false;
        walkStride = 1f;
        flailing = false;
        slump = 0f;
        if (fightingNow) { fightingNow = false; anim.Stop(Melee); }
        if (anim[Die] != null) anim[Die].speed = 1f;
        if (anim[Idle] != null) anim.Play(Idle);
        if (anim[Hold] != null) anim.Play(Hold);
    }

    /// <summary>
    /// Authored stance: root at rest and both legs straight.
    ///
    /// `walk` writes the legs and the root bob; `idle` writes neither. Legacy leaves a
    /// transform wherever the clip last sampled it, so CrossFade to idle mid-stride plants
    /// a man in a frozen running pose for the rest of the hold — Rob, 2026-08-13, after
    /// GrappleGap 0.75. Same family as the corpse-on-its-back recycle: stopping a clip
    /// does not undo it. Held every frame the unit is not walking, because the 0.15s
    /// fade-out keeps sampling the stride over anything written in SetWalking.
    /// </summary>
    void RestoreStance()
    {
        anim.transform.localPosition = restPos;
        if (legL != null) legL.localRotation = restLegL;
        if (legR != null) legR.localRotation = restLegR;
        if (dead)
        {
            if (armL != null) armL.localRotation = restArmL;
            if (armR != null) armR.localRotation = restArmR;
        }
    }

    /// <summary>
    /// Pull Kenney's jog toward a march. The clip still drives the joints; this
    /// slerps each one back toward rest so the authored 60° hip becomes
    /// <see cref="MarchStride"/> of that. Root bob scales with it. Not a new clip
    /// — the enemy charge plays the same asset at stride 1.
    /// </summary>
    void ApplyStride()
    {
        if (walkStride >= 0.999f) return;
        var p = anim.transform.localPosition;
        anim.transform.localPosition = Vector3.Lerp(restPos, p, walkStride);
        if (legL != null)
            legL.localRotation = Quaternion.Slerp(restLegL, legL.localRotation, walkStride);
        if (legR != null)
            legR.localRotation = Quaternion.Slerp(restLegR, legR.localRotation, walkStride);
    }

    /// <summary>
    /// Legs walking, arms still holding the weapon — the advancing assault squads.
    ///
    /// Walk replaces the IDLE on layer 0 rather than stacking on it: two full-body loops on one
    /// layer means the last one played wins, and a marcher blending 50% breathing reads as a
    /// stumble. The HOLD stays untouched on layer 1, restricted to the arms, so the rifle is
    /// still carried across the field.
    ///
    /// Guarded on a change, because a CrossFade re-issued every frame restarts the blend every
    /// frame and the legs never actually swing — the same shape as the death re-trigger below.
    /// </summary>
    public void SetWalking(bool walking, float stride = 1f, float speed = 1f)
    {
        if (anim == null || dead || anim[Walk] == null) return;
        walkStride = walking ? Mathf.Clamp01(stride) : 1f;
        anim[Walk].speed = walking ? speed : 1f;
        if (walking == walkingNow) return;
        walkingNow = walking;
        anim.CrossFade(walking ? Walk : Idle, 0.15f);

        // WALK DRIVES THE ROOT — a vertical bob — and `idle` does not, so nothing restores it when
        // the march ends. Legacy Animation leaves a transform wherever the clip last sampled it,
        // and a crossfade out mid-cycle is not at the bob's zero: the body would stand a few
        // centimetres off its slot for the rest of the battle, and a POOLED slot carries that to
        // whoever inherits it. Exactly the trap `die` already documents in this file — `walk` is
        // simply the second clip to touch the root, and the first one nobody expected to.
        //
        // Restored in LateUpdate rather than here: the crossfade out runs for 0.15s and keeps
        // sampling the walk over the top of anything written now, so a fix applied on this line
        // is undone before it is ever seen.
    }

    /// <summary>
    /// Locked in a hand-to-hand fight — the swing, on top of everything else.
    ///
    /// It runs on layer 2, so it takes the arms off the `hold` and the torso off whatever is
    /// looping underneath, while the LEGS keep whatever layer 0 is doing. That is deliberate: the
    /// attacker is still closing the last of the gap for the first third of the fight
    /// (`SkirmishLungeSpeed` over `GrappleGap`), so a running strike is what the motion actually
    /// is, and a full-body clip that plants the feet would slide him in on frozen legs.
    ///
    /// Faded OUT rather than stopped, and this is the part that is easy to get wrong: the fight
    /// does NOT always end in a death. Kill the attacker mid-scuffle and his victim is spared —
    /// that is the mechanic's whole counter-play — so a survivor has to put his arms down, and a
    /// hard Stop on a looping clip drops him to the hold in one frame.
    ///
    /// Guarded on a change for the same reason SetWalking is: a CrossFade re-issued every frame
    /// restarts the blend every frame and the swing never travels.
    /// </summary>
    public void SetFighting(bool fighting)
    {
        if (anim == null || dead || anim[Melee] == null || fighting == fightingNow) return;
        fightingNow = fighting;
        if (fighting) anim.CrossFade(Melee, 0.08f);
        else anim.Blend(Melee, 0f, 0.12f);
    }

    public void Set(string clip)
    {
        if (anim == null) return;
        if (clip == Die)
        {
            if (dead) return;                       // re-triggering restarts the fall every frame
            dead = true;
            walkingNow = false;                     // a corpse is not mid-stride
            fightingNow = false;                    // nor mid-swing: the fight is over for him
            if (anim[Melee] != null) anim.Stop(Melee);
            if (anim[Hold] != null) anim.Stop(Hold);
            if (anim[Die] != null) anim.Stop(Die);
            if (anim[Idle] != null) anim.Stop(Idle);
            if (anim[Walk] != null) anim.Stop(Walk);
            // No sit-down clip. The GO tumbles; RestoreStance keeps a neutral
            // joint pose so they look like a thrown body, not a seated one.
            RestoreStance();
        }
        else if (dead)
        {
            // Slots are recycled: a slot last used by a corpse comes back still holding the death
            // pose, and `die` is ClampForever so it never releases the joints on its own.
            dead = false;
            anim.Stop(Die);
            Stand();
        }
    }

    /// <summary>
    /// Limb motion for a dying body. Call every frame from the ragdoll draw:
    /// seed is the unit id (same mixer as the launch impulse), age is the ragdoll clock,
    /// airborne is false once it has settled so the flail stops and the die pose holds.
    /// <paramref name="slumpToward"/> is model-space pitch sign: + folds forward
    /// (the way the soldier faces), − back. The caller converts game-X contact
    /// through the facing pivot so this stays side-blind.
    /// </summary>
    public void SetRagdoll(int seed, float age, bool airborne, float slumpToward = 0f)
    {
        flailSeed = seed;
        flailAge = age;
        flailing = airborne;
        slump = Mathf.Clamp(slumpToward, -1f, 1f);
    }

    const float FlailArmDeg = 18f;
    const float FlailLegDeg = 10f;
    const float SlumpFollow = 8f;
    const float SlumpTorsoDeg = 52f;
    const float SlumpHeadDeg = 28f;

    void ApplyFlail()
    {
        // Per-limb rates, not harmonic, salted by id so a rank dying together does not
        // thrash as a chorus line — the same reason ImpulseFor and FlamePhase exist.
        float s = flailSeed * 0.618033988f;
        Wave(armL, FlailArmDeg, 1.4f, 2.0f, s + 0.2f);
        Wave(armR, FlailArmDeg, 1.2f, 1.8f, s + 1.1f);
        Wave(legL, FlailLegDeg, 1.1f, 1.6f, s + 2.4f);
        Wave(legR, FlailLegDeg, 1.0f, 1.5f, s + 3.3f);
    }

    void Wave(Transform t, float deg, float hzA, float hzB, float phase)
    {
        if (t == null) return;
        float a = Mathf.Sin((flailAge * hzA + phase) * Mathf.PI * 2f);
        float b = Mathf.Sin((flailAge * hzB + phase * 1.7f) * Mathf.PI * 2f);
        t.localRotation = Quaternion.Euler(a * deg, 0f, b * deg * 0.55f) * t.localRotation;
    }

    void ApplySlump()
    {
        // Pitch about model X: + nods the soldier the way he faces. The facing
        // pivot has already yawed that onto the battle axis, so this is "into
        // the wall" or "over the parapet" once the caller picked the sign.
        float rise = 1f - Mathf.Exp(-SlumpFollow * flailAge);
        float k = slump * rise;
        if (torso != null)
            torso.localRotation = Quaternion.Euler(k * SlumpTorsoDeg, 0f, 0f)
                                  * torso.localRotation;
        if (head != null)
            head.localRotation = Quaternion.Euler(Mathf.Abs(k) * SlumpHeadDeg, 0f, 0f)
                                 * head.localRotation;
    }

    /// <summary>A one-shot on its own layer — it ends by itself and the hold reappears under it.</summary>
    public void Fire()
    {
        if (anim == null || dead || anim[Shoot] == null) return;
        anim.CrossFade(Shoot, 0.05f);
    }

    /// <summary>
    /// A crowd playing the same looping clip in lockstep reads as a chorus line — the single most
    /// obvious tell that they are instanced copies. Offsetting each unit's start time by a
    /// deterministic fraction of the clip costs nothing and breaks it up.
    /// </summary>
    public void Desync(int seed)
    {
        if (anim == null) return;
        dead = false;
        readyPhase = seed * 0.618033988f;
        anim.Stop();
        Stand();
        if (anim[Idle] != null) anim[Idle].time = anim[Idle].length * ((seed * 0.37f) % 1f);
    }
}
