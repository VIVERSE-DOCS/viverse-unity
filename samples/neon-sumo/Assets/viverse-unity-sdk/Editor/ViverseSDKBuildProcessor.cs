#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEngine;

namespace ViverseSDK.EditorTools
{
    /// <summary>
    /// Auto-injects the viverse-sdk loader script into the built WebGL <c>index.html</c>
    /// via Unity's <c>[PostProcessBuild]</c> callback. Works with Unity's default WebGL
    /// template — no custom template required.
    /// </summary>
    public static class ViverseSDKBuildProcessor
    {
        private const string SDK_CDN_URL =
            "https://www.viverse.com/static-assets/viverse-sdk/index.umd.cjs";

        private const string INJECT_MARKER = "viverse-unity-sdk:auto-injected";
        private const string SDK_URL_FRAGMENT = "viverse-sdk";
        private const string LOADER_ANCHOR = "<script src=\"Build/";

        [PostProcessBuild(1)]
        public static void OnPostprocessBuild(BuildTarget target, string pathToBuiltProject)
        {
            if (target != BuildTarget.WebGL) return;

            string indexPath = Path.Combine(pathToBuiltProject, "index.html");
            if (!File.Exists(indexPath))
            {
                Debug.LogWarning(
                    $"[ViverseSDK] index.html not found at '{indexPath}'. " +
                    "SDK script was NOT injected.");
                return;
            }

            string html = File.ReadAllText(indexPath);

            if (html.Contains(INJECT_MARKER))
            {
                Debug.Log("[ViverseSDK] index.html already has injected script (skipped).");
                return;
            }

            if (ContainsSdkScriptTag(html))
            {
                Debug.Log(
                    "[ViverseSDK] Template already loads viverse-sdk (skipped auto-injection).");
                return;
            }

            string scriptTag =
                $"<!-- {INJECT_MARKER} -->\n" +
                $"    <script src=\"{SDK_CDN_URL}\"></script>\n    ";

            string newHtml;
            int loaderIdx = html.IndexOf(LOADER_ANCHOR, System.StringComparison.Ordinal);
            if (loaderIdx >= 0)
            {
                newHtml = html.Insert(loaderIdx, scriptTag);
            }
            else
            {
                int headEnd = html.IndexOf("</head>", System.StringComparison.OrdinalIgnoreCase);
                if (headEnd < 0)
                {
                    Debug.LogError(
                        $"[ViverseSDK] No injection point in '{indexPath}'. " +
                        "Template must contain '<script src=\"Build/...' or '</head>'.");
                    return;
                }
                newHtml = html.Insert(headEnd, "    " + scriptTag + "\n");
            }

            File.WriteAllText(indexPath, newHtml);
            Debug.Log(
                $"[ViverseSDK] Injected viverse-sdk into '{Path.GetFileName(indexPath)}'.");
        }

        private static bool ContainsSdkScriptTag(string html)
        {
            int idx = 0;
            while ((idx = html.IndexOf("<script", idx, System.StringComparison.OrdinalIgnoreCase)) >= 0)
            {
                int end = html.IndexOf("</script>", idx, System.StringComparison.OrdinalIgnoreCase);
                if (end < 0) end = html.Length;
                if (html.IndexOf(SDK_URL_FRAGMENT, idx, end - idx, System.StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
                idx = end;
            }
            return false;
        }
    }
}
#endif
