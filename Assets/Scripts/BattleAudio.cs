using UnityEngine;

/// <summary>
/// Port of SoundEffects.kt. Fire-and-forget playback on a pool of AudioSources — the Unity
/// equivalent of SoundPool's stream pool.
///
/// The RATE LIMITS are the whole design, not an optimisation. A volley is 10-20 rounds landing
/// within a few ticks; playing one clip per event turns a battle into white noise. Each trigger
/// therefore has its own minimum interval, and the intervals differ by an order of magnitude
/// because the sounds do different jobs:
///   ground impact  50ms  — a spatter of rounds landing SHOULD overlap; it reads as volume of fire
///   explosion     330ms  — a blast is a punctuation mark, not a texture
///   unit hit      500ms  — pain, occasional
///   unit death    700ms  — the rarest and most significant, so it is never buried
/// </summary>
public class BattleAudio : MonoBehaviour
{
    public const float GroundImpactMinInterval = 0.050f;
    public const int GroundImpactMaxPerTick = 4;
    public const float UnitDeathMinInterval = 0.700f;
    public const float UnitHitMinInterval = 0.500f;
    public const float ExplosionMinInterval = 0.330f;

    [SerializeField] AudioClip volleyFire;
    [SerializeField] AudioClip groundImpact;
    [SerializeField] AudioClip unitDeath;
    [SerializeField] AudioClip unitHit;
    [SerializeField] AudioClip explosion;
    [SerializeField] AudioClip victory;
    [SerializeField] AudioClip defeat;
    [SerializeField] AudioClip helicopterLoop;
    [SerializeField] AudioClip planePassby;

    const int Voices = 12;
    AudioSource[] voices;
    int nextVoice;

    float lastGroundImpact, lastUnitDeath, lastUnitHit, lastExplosion;

    void Awake()
    {
        // A round-robin voice pool, sized above the biggest simultaneous burst. Unity will
        // happily allocate an AudioSource per Play call otherwise, which is the same unbounded
        // growth the projectile pools exist to avoid.
        voices = new AudioSource[Voices];
        for (int i = 0; i < Voices; i++)
        {
            var src = gameObject.AddComponent<AudioSource>();
            src.playOnAwake = false;
            src.spatialBlend = 0f;      // 2D — the camera moves constantly; panning would swim
            voices[i] = src;
        }

        // FORCE THE CLIP DATA RESIDENT. Measured on device: the first Play of every clip
        // reported loadState=Unloaded, which means the first volley of a battle, the first
        // explosion and the first ground impact were all silent — the sound arrived only from
        // the second occurrence onward. Preloading at import covers this, and this call is the
        // belt-and-braces for any clip whose importer settings drift.
        foreach (var c in new[] { volleyFire, groundImpact, unitDeath, unitHit, explosion,
                                  victory, defeat, helicopterLoop, planePassby })
            if (c != null && c.loadState != AudioDataLoadState.Loaded) c.LoadAudioData();
    }

    /// <summary>Diagnostic only — proves the trigger path executes when you cannot hear it.</summary>
    public static bool TracePlays = false;

    void Play(AudioClip clip, float volume, float pitch = 1f)
    {
        if (clip == null) { if (TracePlays) Debug.Log("[Audio] NULL CLIP"); return; }
        if (TracePlays) Debug.Log($"[Audio] {clip.name} vol={volume:F2} pitch={pitch:F2} " +
                                  $"len={clip.length:F2}s state={clip.loadState}");
        var src = voices[nextVoice];
        nextVoice = (nextVoice + 1) % Voices;
        src.clip = clip;
        src.volume = volume;
        src.pitch = pitch;
        src.Play();
    }

    /// <summary>ONE burst per volley, deliberately not per projectile.</summary>
    public void PlayVolleyFire() => Play(volleyFire, 0.8f);

    /// <summary>
    /// Rounds landing in the DIRT — misses and ground bursts only; a unit hit plays a scream
    /// instead. Roughly one per projectile, because the clip is a tiny flyby snap and
    /// overlapping copies read as a spatter rather than as noise. Per-copy pitch jitter stops
    /// identical same-frame copies phasing into one louder thud.
    /// </summary>
    public void PlayGroundImpact(int count = 1)
    {
        if (Time.time - lastGroundImpact < GroundImpactMinInterval) return;
        lastGroundImpact = Time.time;
        int n = Mathf.Min(count, GroundImpactMaxPerTick);
        for (int i = 0; i < n; i++) Play(groundImpact, 0.5f, Random.Range(0.9f, 1.1f));
    }

    public void PlayUnitDeath()
    {
        if (Time.time - lastUnitDeath < UnitDeathMinInterval) return;
        lastUnitDeath = Time.time;
        Play(unitDeath, 0.7f, Random.Range(0.95f, 1.05f));
    }

    public void PlayUnitHit()
    {
        if (Time.time - lastUnitHit < UnitHitMinInterval) return;
        lastUnitHit = Time.time;
        Play(unitHit, 0.55f, Random.Range(0.95f, 1.05f));
    }

    public void PlayExplosion()
    {
        if (Time.time - lastExplosion < ExplosionMinInterval) return;
        lastExplosion = Time.time;
        Play(explosion, 0.75f, Random.Range(0.95f, 1.05f));
    }

    /// <summary>
    /// The airstrike aircraft's pass. Fired ONCE as the run begins, not per frame.
    ///
    /// The clip is cut so its PEAK lands as the aircraft crosses the drop point — the source is an
    /// 8.3s recording whose whoosh peaks at 3.30s, and the plane reaches centre 1.29s into its run,
    /// so it is trimmed from 2.01s. Play it at any other moment and the loudest part of the sound
    /// arrives over empty sky. It is louder than the other cues on purpose: this is the most
    /// expensive thing in the shop announcing itself.
    /// </summary>
    public void PlayPlanePassby()
    {
        // LOGGED UNCONDITIONALLY, like [Burn], and for the same reason: on a release build with no
        // way to listen, this line is the only evidence the cue fired at all. It prints loadState
        // because the documented failure here is silent — a clip that reports Unloaded on its first
        // Play produces NO SOUND and no error, which is what once made the first volley, the first
        // explosion and the first ground impact of every battle inaudible.
        Debug.Log(planePassby == null
            ? "[Audio] plane pass-by: CLIP IS NULL — check the scene wiring"
            : $"[Audio] plane pass-by: {planePassby.name} len={planePassby.length:F2}s " +
              $"state={planePassby.loadState} ch={planePassby.channels}");
        Play(planePassby, 0.95f);
    }

    public void PlayVictory() => Play(victory, 0.8f);
    public void PlayDefeat() => Play(defeat, 0.8f);
}
