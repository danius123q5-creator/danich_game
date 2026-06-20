using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>Headless build entry point. Invoke from the command line with:
/// Unity.exe -batchmode -quit -projectPath "..." -executeMethod BuildScript.BuildWindows
/// Builds a StandaloneWindows64 player into Build/Windows/.</summary>
public static class BuildScript
{
    public static void BuildWindows()
    {
        string[] scenes = EditorBuildSettings.scenes
            .Where(s => s.enabled)
            .Select(s => s.path)
            .ToArray();

        if (scenes.Length == 0)
            scenes = new[] { "Assets/Scenes/SampleScene.unity" };

        var opts = new BuildPlayerOptions
        {
            scenes = scenes,
            locationPathName = "Build/Windows/ZombieShooter.exe",
            target = BuildTarget.StandaloneWindows64,
            options = BuildOptions.None,
        };

        var report = BuildPipeline.BuildPlayer(opts);
        var summary = report.summary;
        Debug.Log($"BUILD RESULT: {summary.result}  size={summary.totalSize} bytes  errors={summary.totalErrors}");

        if (summary.result != UnityEditor.Build.Reporting.BuildResult.Succeeded)
            EditorApplication.Exit(1);
    }
}
