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

    /// <summary>3.7: standalone MODEL VIEWER build. Same project, but baked with the product name
    /// "ZombieShooterModelViewer" so GameBootstrap.Boot launches the viewer instead of the game.
    /// Outputs a separate exe to Build/ModelViewer/ (shipped as its own release asset).</summary>
    public static void BuildModelViewer()
    {
        string[] scenes = EditorBuildSettings.scenes
            .Where(s => s.enabled)
            .Select(s => s.path)
            .ToArray();
        if (scenes.Length == 0)
            scenes = new[] { "Assets/Scenes/SampleScene.unity" };

        string prevName = PlayerSettings.productName;
        try
        {
            PlayerSettings.productName = "ZombieShooterModelViewer"; // read at runtime by Boot()
            var opts = new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = "Build/ModelViewer/ZombieShooterModelViewer.exe",
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.None,
            };
            var report = BuildPipeline.BuildPlayer(opts);
            var summary = report.summary;
            Debug.Log($"BUILD RESULT: {summary.result}  size={summary.totalSize} bytes  errors={summary.totalErrors}");
            if (summary.result != UnityEditor.Build.Reporting.BuildResult.Succeeded)
                EditorApplication.Exit(1);
        }
        finally
        {
            PlayerSettings.productName = prevName; // never leave the project renamed
        }
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

        // Post-build: make the canvas fill the page (Yandex/itch embed it full-window). The
        // default template centres a small fixed canvas — this stretches it and hides the footer.
        string idx = "Build/WebGL/index.html";
        if (System.IO.File.Exists(idx))
        {
            string html = System.IO.File.ReadAllText(idx);
            const string css =
                "<style>html,body{margin:0;padding:0;height:100%;background:#0d1320;overflow:hidden}" +
                "#unity-container{position:absolute;left:0;top:0;width:100%;height:100%}" +
                "#unity-canvas{width:100%!important;height:100%!important;display:block}" +
                "#unity-footer{display:none!important}</style>";
            if (html.Contains("</head>") && !html.Contains("unity-canvas{width:100%"))
            {
                html = html.Replace("</head>", css + "\n</head>");
                System.IO.File.WriteAllText(idx, html);
                Debug.Log("WebGL: injected responsive canvas CSS into index.html");
            }
        }
    }
}
