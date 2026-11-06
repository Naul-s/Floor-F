using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace FloorF.EditorTools
{
    /// <summary>
    /// Batch-mode build entry points (called by tools/build-web.ps1).
    /// Example: Unity -batchmode -projectPath P -buildTarget WebGL -executeMethod FloorF.EditorTools.BuildTool.BuildWeb -out Builds/Web [-dev] -quit
    /// Throwing makes Unity exit with code 1, which the harness reports as a failed build.
    /// </summary>
    public static class BuildTool
    {
        [MenuItem("Floor F/Build/Web (WebGL)")]
        public static void BuildWebMenu() => BuildWebTo(Path.GetFullPath("Builds/Web"), false);

        public static void BuildWeb()
        {
            var outDir = HarnessArgs.Get("-out", "Builds/Web");
            BuildWebTo(Path.GetFullPath(outDir), HarnessArgs.Has("-dev"));
        }

        static void BuildWebTo(string outDir, bool dev)
        {
            var scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray();
            if (scenes.Length == 0) throw new InvalidOperationException("BuildTool: no enabled scenes in Build Settings.");

            var options = new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = outDir,
                target = BuildTarget.WebGL,
                options = dev ? BuildOptions.Development : BuildOptions.None,
            };
            var report = BuildPipeline.BuildPlayer(options);
            var s = report.summary;
            Debug.Log($"[BuildTool] {s.result} {s.platform} {s.totalSize / (1024f * 1024f):0.0} MB in {s.totalTime} -> {outDir}");
            if (s.result != BuildResult.Succeeded)
                throw new Exception($"BuildTool: build {s.result} ({s.totalErrors} errors). See log.");
        }
    }
}
