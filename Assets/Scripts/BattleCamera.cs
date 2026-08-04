using UnityEngine;

/// <summary>
/// Port of the LOCKED camera solve from ArmedConflict's SceneHost.kt (lines 1303-1318).
/// See CAMERA_ARCHITECTURE.md. The upward tilt is SOLVED PER-FRAME against the camera's
/// actual height and distance so the ground plane (y=0) projects to GROUND_SCREEN_FRACTION
/// at every camera distance. A fixed look-at offset only aligns at one zoom — that is the
/// bug the architecture was written to kill, so the per-frame solve is the thing to port.
/// </summary>
public static class BattleCamera
{
    /// <summary>Fraction of viewport height, measured FROM THE TOP, where y=0 must land.</summary>
    public const float GroundScreenFraction = 0.685f;

    /// <summary>Camera height above the ground plane.</summary>
    public const float CameraY = 1.2f;

    /// <summary>
    /// VFOV 90 makes tan(half-fov) = 1, which is what collapses the NDC math to
    /// tan(beta) = 2*fraction - 1 and makes screen scale exactly height/(2*camZ).
    /// </summary>
    public const float VerticalFovDegrees = 90f;

    /// <summary>
    /// Places and aims the camera. Mirrors the Kotlin exactly:
    ///   beta        = atan(2 * fraction - 1)
    ///   groundAngle = atan(camY / camZ)
    ///   lookAtDy    = camZ * tan(beta - groundAngle)
    /// </summary>
    public static void Apply(Camera cam, float camX, float camY, float camZ)
    {
        float beta = Mathf.Atan(2f * GroundScreenFraction - 1f);
        // Solved against the camera's ACTUAL height, not the constant, so a free camera keeps
        // the ground landing correctly at whatever height it is flown to.
        float groundAngle = Mathf.Atan(camY / camZ);
        float lookAtDy = camZ * Mathf.Tan(beta - groundAngle);

        cam.transform.position = new Vector3(camX, camY, camZ);
        cam.transform.LookAt(new Vector3(camX, camY + lookAtDy, 0f), Vector3.up);
    }

    /// <summary>
    /// The DOC's approximation (CLAUDE.md "px_per_world_unit = 1200 / camZ"). Exact only for
    /// something at depth camZ on the view axis. A ground-standing object is below the axis and
    /// nearer than camZ, so it measures LARGER than this — see ExpectedScreenY.
    /// </summary>
    public static float PixelsPerWorldUnit(float camZ) => Screen.height / (2f * camZ);

    /// <summary>
    /// Exact projected screen y (0 = bottom) for a point in the z=0 plane, derived from the
    /// camera basis rather than from Unity's projection matrix — so comparing this against
    /// Camera.WorldToScreenPoint is a genuine cross-check, not a tautology.
    /// At VFOV 90, tan(half) = 1, so NDC y is simply height-along-up / depth-along-forward.
    /// </summary>
    public static float ExpectedScreenY(float worldY, float worldZ, float camY, float camZ)
    {
        float beta = Mathf.Atan(2f * GroundScreenFraction - 1f);
        float lookAtDy = camZ * Mathf.Tan(beta - Mathf.Atan(camY / camZ));

        float n = Mathf.Sqrt(lookAtDy * lookAtDy + camZ * camZ);
        float fy = lookAtDy / n, fz = -camZ / n;   // forward
        float uy = -fz, uz = fy;                    // up (perpendicular, in the yz plane)

        float vy = worldY - camY, vz = worldZ - camZ;
        float depth = vy * fy + vz * fz;
        float height = vy * uy + vz * uz;

        return Screen.height * (1f + height / depth) * 0.5f;
    }
}
