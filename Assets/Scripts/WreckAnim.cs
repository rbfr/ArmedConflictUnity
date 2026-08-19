using UnityEngine;

/// <summary>
/// Plays a structure's authored collapse once and holds the last frame.
///
/// Legacy Animation on purpose — same system as the soldiers, and the
/// collapse GLB is imported that way. Mecanim is a fallback only.
///
/// ClampForever is load-bearing: a looping collapse stands the wreck
/// back up, and a wrap-once that then samples frame 0 pops the intact
/// hut back for a frame. That frozen rest pose is what "the hit did
/// not register" looked like on device: the live mesh hid and an
/// identical intact wreck sat in its place.
/// </summary>
public class WreckAnim : MonoBehaviour
{
    public const string Collapse = "collapse";

    Animation anim;
    Animator animator;
    bool played;

    void Awake()
    {
        anim = GetComponent<Animation>() ?? GetComponentInChildren<Animation>(true);
        animator = GetComponent<Animator>() ?? GetComponentInChildren<Animator>(true);
        if (anim != null) anim.playAutomatically = false;
        if (animator != null)
        {
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            animator.enabled = false;
        }
    }

    public void Play()
    {
        if (played) return;
        played = true;

        if (anim != null)
        {
            var state = anim[Collapse];
            if (state == null)
            {
                foreach (AnimationState s in anim)
                {
                    state = s;
                    break;
                }
            }
            if (state != null)
            {
                state.wrapMode = WrapMode.ClampForever;
                state.speed = 1f;
                state.weight = 1f;
                state.enabled = true;
                anim.Play(state.name);
                anim.Sample();
                return;
            }
        }

        if (animator != null)
        {
            animator.enabled = true;
            if (animator.runtimeAnimatorController != null)
                animator.Play(0, 0, 0f);
            return;
        }

        Debug.LogWarning($"[WreckAnim] {name} has nothing to play — collapse will sit at rest");
    }

    void LateUpdate()
    {
        if (!played || animator == null || !animator.enabled) return;
        var st = animator.GetCurrentAnimatorStateInfo(0);
        if (st.normalizedTime >= 1f)
            animator.Play(st.fullPathHash, 0, 1f);
    }
}
