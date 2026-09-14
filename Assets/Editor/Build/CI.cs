using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Build
{
    /// <summary>Batchmode entry points used by CI. Scenes come from Build Settings.</summary>
    public static class CI
    {
        private const string ProductName = "JurassicPark";

        public static void BuildWindows() =>
            Run(BuildTarget.StandaloneWindows64, Path.Combine("Builds", "Windows", ProductName + ".exe"));

        public static void BuildMac() =>
            Run(BuildTarget.StandaloneOSX, Path.Combine("Builds", "macOS", ProductName + ".app"));

        private static void Run(BuildTarget target, string outputPath)
        {
            string[] scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray();
            if (scenes.Length == 0)
            {
                Debug.LogError("CI build: no scenes enabled in Build Settings.");
                EditorApplication.Exit(2);
                return;
            }

            var options = new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = outputPath,
                target = target,
                options = BuildOptions.None,
            };

            BuildReport report = BuildPipeline.BuildPlayer(options);
            BuildSummary summary = report.summary;
            Debug.Log($"CI build {target}: {summary.result}, {summary.totalSize / (1024 * 1024)} MB, {summary.totalErrors} errors, {summary.totalTime.TotalSeconds:F0} s");
            EditorApplication.Exit(summary.result == BuildResult.Succeeded ? 0 : 1);
        }
    }
}
