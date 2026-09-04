#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// DPR 1 + metrics strip: open/copy in desktop browser, or bake query into index.html for Fire TV file:// loads, then deploy.
/// </summary>
public sealed class FireTvDprTestWindow : EditorWindow
{
    private const string PrefLastWebGlBuild = "ViverseFireTv_LastWebGlPath";
    private const string MenuViverseDeploy = "Viverse/Fire TV/Deploy WebGL build to Fire TV…";

    /// <summary>Unity WebGL: force devicePixelRatio=1 and show resolution/DPR strip.</summary>
    private const string DprTestQuery = "dpr=1&showMetrics=1";

    private string _buildFolder = "";

    [MenuItem("Build/WebGL/Fire TV DPR test…", false, 500)]
    private static void Open()
    {
        var w = GetWindow<FireTvDprTestWindow>(true, "Fire TV DPR test", true);
        w.minSize = new Vector2(440, 220);
    }

    private void OnEnable()
    {
        _buildFolder = EditorPrefs.GetString(PrefLastWebGlBuild, "");
    }

    private void OnGUI()
    {
        EditorGUILayout.Space(6);
        EditorGUILayout.LabelField("WebGL build folder (contains index.html)", EditorStyles.boldLabel);
        EditorGUILayout.BeginHorizontal();
        EditorGUI.BeginChangeCheck();
        _buildFolder = EditorGUILayout.TextField(_buildFolder);
        if (EditorGUI.EndChangeCheck())
            EditorPrefs.SetString(PrefLastWebGlBuild, _buildFolder);
        if (GUILayout.Button("Browse…", GUILayout.Width(80)))
        {
            var start = Directory.Exists(_buildFolder) ? _buildFolder : "";
            var p = EditorUtility.OpenFolderPanel("WebGL output (contains index.html)", start, "");
            if (!string.IsNullOrEmpty(p))
            {
                _buildFolder = p;
                EditorPrefs.SetString(PrefLastWebGlBuild, _buildFolder);
            }
        }
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.HelpBox(
            "Sets Unity **devicePixelRatio = 1** and shows the **metrics strip** (display vs canvas vs DPR).\n\n" +
            "**Open** / **Copy** — desktop browser.\n**TV** — bakes the same query into `index.html` for Fire TV, then deploy with Viverse. Rebuild WebGL so `index.html` has `FIRETV_PRESET` markers.",
            MessageType.Info);

        EditorGUILayout.Space(6);
        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Open Viverse deploy window", GUILayout.Height(26)))
        {
            if (!EditorApplication.ExecuteMenuItem(MenuViverseDeploy))
                EditorUtility.DisplayDialog("Fire TV", "Menu not found: " + MenuViverseDeploy, "OK");
        }
        if (GUILayout.Button("Clear baked preset", GUILayout.Height(26)))
            ClearPreset();
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space(10);
        EditorGUILayout.LabelField("DPR = 1 + show metrics", EditorStyles.boldLabel);
        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Open in browser", GUILayout.Height(34)))
            OpenBrowser();
        if (GUILayout.Button("Copy URL", GUILayout.Width(88), GUILayout.Height(34)))
            CopyUrl();
        if (GUILayout.Button(new GUIContent("TV", "Bake into index.html for Fire TV"), GUILayout.Width(40), GUILayout.Height(34)))
            ApplyPreset();
        EditorGUILayout.EndHorizontal();
    }

    private string ResolveIndexPath()
    {
        var folder = _buildFolder;
        if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder))
            return null;
        var index = Path.Combine(folder, "index.html");
        return File.Exists(index) ? index : null;
    }

    private void OpenBrowser()
    {
        var indexPath = ResolveIndexPath();
        if (indexPath == null)
        {
            EditorUtility.DisplayDialog("Fire TV DPR test", "Pick a WebGL build folder that contains index.html.", "OK");
            return;
        }

        var full = new System.Uri(Path.GetFullPath(indexPath)).AbsoluteUri + "?" + DprTestQuery;
        Debug.Log("[Fire TV DPR test] " + full);
        Application.OpenURL(full);
    }

    private void CopyUrl()
    {
        var indexPath = ResolveIndexPath();
        if (indexPath == null)
        {
            EditorUtility.DisplayDialog("Fire TV DPR test", "Pick a valid WebGL build folder first.", "OK");
            return;
        }

        var full = new System.Uri(Path.GetFullPath(indexPath)).AbsoluteUri + "?" + DprTestQuery;
        EditorGUIUtility.systemCopyBuffer = full;
        Debug.Log("[Fire TV DPR test] Copied: " + full);
    }

    private void ApplyPreset()
    {
        var indexPath = ResolveIndexPath();
        if (indexPath == null)
        {
            EditorUtility.DisplayDialog("Fire TV DPR test", "Pick a WebGL build folder that contains index.html.", "OK");
            return;
        }

        if (!FireTvBuildIndexHtmlPreset.TryApply(indexPath, DprTestQuery, out var err))
        {
            EditorUtility.DisplayDialog("Fire TV — could not bake preset", err, "OK");
            return;
        }

        Debug.Log("[Fire TV DPR test] Baked preset into " + indexPath);
        EditorUtility.DisplayDialog(
            "Fire TV",
            "Preset saved into index.html.\n\nNext: Viverse → Fire TV → Deploy WebGL build…",
            "OK");
    }

    private void ClearPreset()
    {
        var indexPath = ResolveIndexPath();
        if (indexPath == null)
        {
            EditorUtility.DisplayDialog("Fire TV DPR test", "Pick a valid WebGL build folder first.", "OK");
            return;
        }

        if (!FireTvBuildIndexHtmlPreset.TryClear(indexPath, out var err))
        {
            EditorUtility.DisplayDialog("Fire TV", err, "OK");
            return;
        }

        Debug.Log("[Fire TV DPR test] Cleared preset in " + indexPath);
    }
}

internal static class FireTvBuildIndexHtmlPreset
{
    private const string BeginMarker = "<!--FIRETV_PRESET_BEGIN-->";
    private const string EndMarker = "<!--FIRETV_PRESET_END-->";

    internal static bool TryApply(string indexHtmlPath, string query, out string error)
    {
        error = null;
        if (string.IsNullOrEmpty(indexHtmlPath) || !File.Exists(indexHtmlPath))
        {
            error = "index.html not found.";
            return false;
        }

        var html = File.ReadAllText(indexHtmlPath);
        var i0 = html.IndexOf(BeginMarker, System.StringComparison.Ordinal);
        var i1 = html.IndexOf(EndMarker, System.StringComparison.Ordinal);
        if (i0 < 0 || i1 < 0 || i1 < i0)
        {
            error =
                "This index.html does not contain FIRETV preset markers.\n\n" +
                "Rebuild WebGL from this project (Fire TV template).";
            return false;
        }

        var inner = "<script>window.__UNITY_FIRETV_TEST_QUERY__='" + EscapeJsSingleQuoted(query) + "';</script>";
        var newHtml = html.Substring(0, i0 + BeginMarker.Length) + "\n" + inner + "\n" + html.Substring(i1);
        File.WriteAllText(indexHtmlPath, newHtml);
        return true;
    }

    internal static bool TryClear(string indexHtmlPath, out string error)
    {
        error = null;
        if (string.IsNullOrEmpty(indexHtmlPath) || !File.Exists(indexHtmlPath))
        {
            error = "index.html not found.";
            return false;
        }

        var html = File.ReadAllText(indexHtmlPath);
        var i0 = html.IndexOf(BeginMarker, System.StringComparison.Ordinal);
        var i1 = html.IndexOf(EndMarker, System.StringComparison.Ordinal);
        if (i0 < 0 || i1 < 0 || i1 < i0)
        {
            error = "Preset markers not found in index.html.";
            return false;
        }

        var newHtml = html.Substring(0, i0 + BeginMarker.Length) + "\n" + html.Substring(i1);
        File.WriteAllText(indexHtmlPath, newHtml);
        return true;
    }

    private static string EscapeJsSingleQuoted(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        return s.Replace("\\", "\\\\").Replace("'", "\\'");
    }
}
#endif
