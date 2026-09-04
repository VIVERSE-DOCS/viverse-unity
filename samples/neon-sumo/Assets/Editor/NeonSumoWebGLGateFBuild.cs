using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Gate F helper: apply PROJECT:FullScreen and produce a WebGL player build.
/// Auto-runs once when UserSettings/NeonSumoGateFWebGL.build exists.
/// </summary>
public static class NeonSumoWebGLGateFBuild
{
    public const string TemplateId = "PROJECT:FullScreen";
    public const string OutputDir = "Build/WebGL";

    const string PrefKey = "NeonSumo.GateF.PendingWebGLBuild";
    const string TriggerRel = "UserSettings/NeonSumoGateFWebGL.build";
    const string ResultRel = "Temp/NeonSumoGateFWebGL.result.txt";

    [InitializeOnLoadMethod]
    static void AutoRunFromTrigger()
    {
        EditorApplication.delayCall += RunIfTriggered;
    }

    static void RunIfTriggered()
    {
        try
        {
            string trigger = ProjectPath(TriggerRel);
            bool pending = EditorPrefs.GetBool(PrefKey, false) || File.Exists(trigger);
            if (!pending)
                return;

            if (File.Exists(trigger))
                File.Delete(trigger);

            if (EditorApplication.isPlaying)
            {
                WriteResult("FAILED_PLAY_MODE");
                EditorPrefs.SetBool(PrefKey, false);
                return;
            }

            if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.WebGL)
            {
                EditorPrefs.SetBool(PrefKey, true);
                WriteResult("SWITCHING_TARGET");
                EditorUserBuildSettings.SwitchActiveBuildTarget(
                    NamedBuildTarget.WebGL,
                    BuildTarget.WebGL);
                return;
            }

            EditorPrefs.SetBool(PrefKey, false);
            RunBuild();
        }
        catch (Exception ex)
        {
            EditorPrefs.SetBool(PrefKey, false);
            WriteResult("FAILED\n" + ex);
        }
    }

    [MenuItem("Build/WebGL/Build Neon Sumo (FullScreen)")]
    public static void RunBuild()
    {
        try
        {
            PlayerSettings.WebGL.template = TemplateId;
            EditorSceneManager.SaveOpenScenes();
            AssetDatabase.SaveAssets();

            string output = ProjectPath(OutputDir);
            Directory.CreateDirectory(output);

            string[] scenes = EditorBuildSettings.scenes
                .Where(s => s.enabled)
                .Select(s => s.path)
                .ToArray();

            WriteResult(
                "STARTED\n" +
                "template=" + PlayerSettings.WebGL.template + "\n" +
                "output=" + output + "\n" +
                "scenes=" + string.Join(", ", scenes));

            var options = new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = output,
                target = BuildTarget.WebGL,
                options = BuildOptions.None
            };

            BuildReport report = BuildPipeline.BuildPlayer(options);
            string indexPath = Path.Combine(output, "index.html");
            var summary = new System.Text.StringBuilder();
            summary.AppendLine(report.summary.result.ToString());
            summary.AppendLine("template=" + PlayerSettings.WebGL.template);
            summary.AppendLine("output=" + report.summary.outputPath);
                summary.AppendLine("errors=" + report.summary.totalErrors);
                summary.AppendLine("warnings=" + report.summary.totalWarnings);
                foreach (var step in report.steps)
                {
                    foreach (var msg in step.messages)
                    {
                        if (msg.type == LogType.Error || msg.type == LogType.Exception)
                            summary.AppendLine("error:" + msg.content);
                    }
                }

            if (File.Exists(indexPath))
            {
                string html = File.ReadAllText(indexPath);
                int sdkHits = Count(html, "viverse-sdk");
                int injectHits = Count(html, "viverse-unity-sdk:auto-injected");
                int playSdkHits = Count(html, "play-sdk/1.0.1");
                int playJslibHits = Count(html, "play-sdk.jslib");
                summary.AppendLine("index=" + indexPath);
                summary.AppendLine("viverse-sdk_count=" + sdkHits);
                summary.AppendLine("inject_marker_count=" + injectHits);
                summary.AppendLine("play-sdk/1.0.1_count=" + playSdkHits);
                summary.AppendLine("play-sdk.jslib_count=" + playJslibHits);
            }
            else
            {
                summary.AppendLine("index=MISSING");
            }

            WriteResult(summary.ToString());
        }
        catch (Exception ex)
        {
            WriteResult("FAILED\n" + ex);
        }
    }

    static int Count(string haystack, string needle)
    {
        int count = 0;
        int idx = 0;
        while ((idx = haystack.IndexOf(needle, idx, StringComparison.Ordinal)) >= 0)
        {
            count++;
            idx += needle.Length;
        }
        return count;
    }

    static string ProjectPath(string relative)
    {
        return Path.GetFullPath(Path.Combine(Application.dataPath, "..", relative));
    }

    static void WriteResult(string text)
    {
        string path = ProjectPath(ResultRel);
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        File.WriteAllText(path, text);
        Debug.Log("[GateF] " + text.Replace("\n", " | "));
    }
}
