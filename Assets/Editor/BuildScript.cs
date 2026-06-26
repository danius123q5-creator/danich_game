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

    /// <summary>WebGL ("инвалид эдишн") build for Yandex Games / itch.io — single-player only
    /// (networking is hidden in browser since WebGL has no UDP). Outputs to Build/WebGL/.</summary>
    public static void BuildWebGL()
    {
        string[] scenes = EditorBuildSettings.scenes
            .Where(s => s.enabled)
            .Select(s => s.path)
            .ToArray();
        if (scenes.Length == 0)
            scenes = new[] { "Assets/Scenes/SampleScene.unity" };

        // Browser-friendly settings. CRITICAL for Yandex Games / itch.io: decompressionFallback
        // = true, so the gzip build self-decompresses in JS even when the host doesn't send the
        // Content-Encoding header. Without it the loader can't unpack the wasm → "не запускается".
        PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Gzip;
        PlayerSettings.WebGL.decompressionFallback = true;
        PlayerSettings.WebGL.exceptionSupport = WebGLExceptionSupport.None;
        PlayerSettings.WebGL.dataCaching = true;
        PlayerSettings.runInBackground = true;

        var opts = new BuildPlayerOptions
        {
            scenes = scenes,
            locationPathName = "Build/WebGL",
            target = BuildTarget.WebGL,
            options = BuildOptions.None,
        };

        var report = BuildPipeline.BuildPlayer(opts);
        var summary = report.summary;
        Debug.Log($"BUILD RESULT: {summary.result}  size={summary.totalSize} bytes  errors={summary.totalErrors}");

        if (summary.result != UnityEditor.Build.Reporting.BuildResult.Succeeded)
            EditorApplication.Exit(1);
    }
}
