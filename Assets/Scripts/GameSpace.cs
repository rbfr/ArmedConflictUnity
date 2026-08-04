using UnityEngine;

/// <summary>
/// THE HANDEDNESS CONVENTION. Decided once, here, so it cannot be half-applied.
///
/// ArmedConflict's world is Filament's: right-handed, camera at +Z looking toward -Z, which
/// puts +X on the RIGHT of the screen and makes a LARGER z NEARER the camera.
///
/// Unity is left-handed. With the camera in the same place (+Z, looking toward -Z), screen
/// right becomes -X while depth keeps the same sense. So the entire correction is a single
/// negation of X, and depth semantics carry over untouched.
///
/// Verified against L1, which is deliberately asymmetric: player tank at game x = -9.5,
/// enemy outpost at game x = +7.0. Before this, the green player squad rendered on the RIGHT
/// and the red enemy on the LEFT — plausible-looking, and wrong. That is exactly why the
/// spike doc warns a mirrored scene hides its own errors: nothing looks broken.
///
/// Anything reading a hardcoded axis sign from the Kotlin — xSign, gun offsets,
/// gunRotZ = 180 - gunAngle, CAMERA_MIDFIELD_X, CAMERA_ENEMY_LEAN_X, per-level x placement —
/// must come through here rather than being flipped ad hoc at the call site.
/// </summary>
public static class GameSpace
{
    public static Vector3 ToUnity(float gameX, float gameY, float gameZ)
        => new Vector3(-gameX, gameY, gameZ);

    public static Vector3 ToUnity(Vector3 game) => ToUnity(game.x, game.y, game.z);

    /// <summary>Camera x lives in game space too, and needs the same flip.</summary>
    public static float CameraX(float gameCamX) => -gameCamX;
}
