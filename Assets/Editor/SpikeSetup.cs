using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Headless project configuration for the Unity migration spike.
/// Run with: -batchmode -quit -executeMethod SpikeSetup.Configure
/// See UNITY_SPIKE.md in the ArmedConflict repo for why each value is what it is.
/// </summary>
public static class SpikeSetup
{
    /// <summary>
    /// Deliberately NOT com.dullesengineering.armedconflict: keeping a distinct id leaves the
    /// shipping Filament build installed alongside, which is what makes an A/B on the same
    /// device possible. Change to the exact shipping id only if replacing it is the intent.
    /// </summary>
    const string AppId = "com.dullesengineering.armedconflictspike";

    const string SettingsDir = "Assets/Settings";
    const string RendererPath = SettingsDir + "/SpikeRenderer.asset";
    const string PipelinePath = SettingsDir + "/SpikePipeline.asset";

    /// <summary>
    /// Reverses the graphics API order so GLES3 is used instead of Vulkan. Step 1 tests BOTH
    /// APIs on this PowerVR GPU, but with Vulkan first GLES3 never exercises — it is only
    /// reached on a device where Vulkan is unavailable.
    /// </summary>
    public static void UseGles3First()
    {
        PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.Android, false);
        PlayerSettings.SetGraphicsAPIs(BuildTarget.Android, new[]
        {
            GraphicsDeviceType.OpenGLES3,
            GraphicsDeviceType.Vulkan,
        });
        AssetDatabase.SaveAssets();
        Debug.Log("[SpikeSetup] graphics APIs now GLES3, Vulkan");
    }

    public static void UseVulkanFirst()
    {
        PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.Android, false);
        PlayerSettings.SetGraphicsAPIs(BuildTarget.Android, new[]
        {
            GraphicsDeviceType.Vulkan,
            GraphicsDeviceType.OpenGLES3,
        });
        AssetDatabase.SaveAssets();
        Debug.Log("[SpikeSetup] graphics APIs now Vulkan, GLES3");
    }

    /// <summary>Applies player settings only, without recreating the pipeline assets.</summary>
    public static void ApplyAppId()
    {
        PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, AppId);
        ApplyOrientation();
        AssetDatabase.SaveAssets();
        Debug.Log($"[SpikeSetup] appId={PlayerSettings.GetApplicationIdentifier(NamedBuildTarget.Android)} " +
                  $"orientation={PlayerSettings.defaultInterfaceOrientation}");
    }

    /// <summary>
    /// PORTRAIT, and this is load-bearing rather than cosmetic. GROUND_SCREEN_FRACTION (0.685)
    /// is a fraction of viewport HEIGHT, and the documented screen scale 1200/camZ is really
    /// viewportHeight/(2*camZ) = 2404/(2*camZ). In landscape the height is 1080, the scale
    /// becomes 540/camZ, and every pixel-denominated check in Step 2 comes out ~2.2x small.
    /// </summary>
    static void ApplyOrientation()
    {
        PlayerSettings.defaultInterfaceOrientation = UIOrientation.Portrait;
        PlayerSettings.allowedAutorotateToPortrait = true;
        PlayerSettings.allowedAutorotateToPortraitUpsideDown = false;
        PlayerSettings.allowedAutorotateToLandscapeLeft = false;
        PlayerSettings.allowedAutorotateToLandscapeRight = false;
    }

    public static void Configure()
    {
        Directory.CreateDirectory(SettingsDir);

        // URP Mobile shape: no post-processing, no shadows (the game draws its own blob
        // contact shadows), no HDR, no MSAA.
        var rendererData = ScriptableObject.CreateInstance<UniversalRendererData>();
        AssetDatabase.CreateAsset(rendererData, RendererPath);

        var pipeline = UniversalRenderPipelineAsset.Create(rendererData);
        AssetDatabase.CreateAsset(pipeline, PipelinePath);

        var so = new SerializedObject(pipeline);
        so.FindProperty("m_MainLightShadowsSupported").boolValue = false;
        so.FindProperty("m_AdditionalLightShadowsSupported").boolValue = false;
        so.FindProperty("m_SoftShadowsSupported").boolValue = false;
        so.FindProperty("m_SupportsHDR").boolValue = false;
        so.FindProperty("m_MSAA").intValue = (int)MsaaQuality.Disabled;
        so.ApplyModifiedProperties();

        GraphicsSettings.defaultRenderPipeline = pipeline;
        for (int i = 0; i < QualitySettings.count; i++)
        {
            QualitySettings.SetQualityLevel(i, false);
            QualitySettings.renderPipeline = pipeline;
        }

        PlayerSettings.colorSpace = ColorSpace.Linear;

        // Step 1 tests BOTH graphics APIs on the Pixel 10's PowerVR GPU; Vulkan first.
        PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.Android, false);
        PlayerSettings.SetGraphicsAPIs(BuildTarget.Android, new[]
        {
            GraphicsDeviceType.Vulkan,
            GraphicsDeviceType.OpenGLES3,
        });

        PlayerSettings.SetApplicationIdentifier(
            NamedBuildTarget.Android, AppId);
        PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel26;
        PlayerSettings.Android.targetSdkVersion = AndroidSdkVersions.AndroidApiLevelAuto;
        ApplyOrientation();

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log($"[SpikeSetup] OK. pipeline={GraphicsSettings.defaultRenderPipeline}, " +
                  $"colorSpace={PlayerSettings.colorSpace}, qualityLevels={QualitySettings.count}");
    }
}
