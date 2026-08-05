using UnityEngine;

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
///   0  idle   whole-body breathing loop, always running
///   1  hold   rifle at the ready — ARMS ONLY, via mixing transforms, always running
///   2  shoot  recoil, one-shot; ends by itself and the layers below simply reappear
///   3  die    everything including root, ClampForever
///
/// Without layer 1 the troops stand at ease with a rifle floating beside them, because `idle`
/// swings the arms down. Without layer 2 being ABOVE it, firing does nothing visible: the hold
/// would keep winning the arms.
/// </summary>
public class UnitAnim : MonoBehaviour
{
    // Clip names from Kenney's Blocky Characters 2.0 (CC0). Any replacement rig has to ship
    // these four or the mapping moves here, not into the caller.
    public const string Idle = "idle";
    public const string Hold = "holding-both";
    public const string Shoot = "holding-both-shoot";
    public const string Die = "die";

    /// <summary>
    /// Elevation ADDED to the arms' animated pose, in degrees, 0 = the clip's own rest hold.
    /// Set from the live aim; the caller owns what it means, this only draws it.
    /// </summary>
    public float AimDegrees { get; set; }

    // Degrees per second the shown elevation chases the target. Fast enough to feel welded to
    // the finger, slow enough that the release does not snap.
    const float AimFollow = 14f;

    // Which way a positive AimDegrees lifts the muzzle. The soldier is built facing glTF +Z and
    // the `facing` pivot yaws the whole model, so elevation is always a pitch about the arm
    // parent's X — the sign is the only thing the facing can flip, and it does not, because the
    // pivot is ABOVE the torso.
    const float AimSign = -1f;

    Animation anim;
    Transform armL, armR;
    float shownAim;
    bool dead;

    void Awake()
    {
        anim = GetComponentInChildren<Animation>();
        if (anim == null) return;
        anim.playAutomatically = false;

        armL = anim.transform.Find("torso/arm-left");
        armR = anim.transform.Find("torso/arm-right");

        Layer(Idle, 0, WrapMode.Loop);
        Layer(Hold, 1, WrapMode.ClampForever);
        Layer(Shoot, 2, WrapMode.Once);
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
        if (anim == null || (armL == null && armR == null)) return;

        float target = dead ? 0f : AimDegrees;
        // Frame-rate independent chase. A bare per-frame lerp constant would silently change
        // speed with the refresh rate, which is the trap the Android build documents at length.
        shownAim = Mathf.Lerp(shownAim, target, 1f - Mathf.Exp(-AimFollow * Time.deltaTime));
        if (Mathf.Abs(shownAim) < 0.01f) return;

        var lift = Quaternion.AngleAxis(AimSign * shownAim, Vector3.right);
        if (armL != null) armL.localRotation = lift * armL.localRotation;
        if (armR != null) armR.localRotation = lift * armR.localRotation;
    }

    void Layer(string clip, int layer, WrapMode wrap)
    {
        var s = anim[clip];
        if (s == null) return;
        s.layer = layer;
        s.wrapMode = wrap;
        s.weight = 1f;
    }

    void Stand()
    {
        if (anim == null) return;
        if (anim[Idle] != null) anim.Play(Idle);
        if (anim[Hold] != null) anim.Play(Hold);
    }

    public void Set(string clip)
    {
        if (anim == null) return;
        if (clip == Die)
        {
            if (dead) return;                       // re-triggering restarts the fall every frame
            dead = true;
            if (anim[Die] != null) anim.CrossFade(Die, 0.08f);
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
        anim.Stop();
        Stand();
        if (anim[Idle] != null) anim[Idle].time = anim[Idle].length * ((seed * 0.37f) % 1f);
    }
}
