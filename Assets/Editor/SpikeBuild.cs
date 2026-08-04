using System;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

/// <summary>
/// Headless APK build for the spike. Run with:
///   -batchmode -quit -executeMethod SpikeBuild.Android
/// Optional: -developmentBuild to enable the profiler.
/// </summary>
public static class SpikeBuild
{
    public static void Android()
    {
        bool development = Environment.GetCommandLineArgs().Contains("-developmentBuild");

        PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
        PlayerSettings.SetScriptingBackend(
            UnityEditor.Build.NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);

        var scenes = EditorBuildSettings.scenes
            .Where(s => s.enabled)
            .Select(s => s.path)
            .ToArray();

        var options = new BuildPlayerOptions
        {
            scenes = scenes,
            locationPathName = "Builds/Step1.apk",
            target = BuildTarget.Android,
            targetGroup = BuildTargetGroup.Android,
            // Release by default: CLAUDE.md's rule that a debuggable build is not a
            // measurable one applies here just as much as it does on Filament.
            options = development
                ? BuildOptions.Development | BuildOptions.ConnectWithProfiler
                : BuildOptions.None,
        };

        System.IO.Directory.CreateDirectory("Builds");
        var report = BuildPipeline.BuildPlayer(options);
        var summary = report.summary;

        Debug.Log($"[SpikeBuild] result={summary.result} " +
                  $"development={development} " +
                  $"size={summary.totalSize / (1024 * 1024)}MB " +
                  $"time={summary.totalTime} " +
                  $"errors={summary.totalErrors} warnings={summary.totalWarnings}");

        if (summary.result != BuildResult.Succeeded)
        {
            foreach (var step in report.steps)
                foreach (var msg in step.messages)
                    if (msg.type == LogType.Error || msg.type == LogType.Exception)
                        Debug.LogError($"[SpikeBuild] {step.name}: {msg.content}");

            EditorApplication.Exit(1);
        }
    }
}
