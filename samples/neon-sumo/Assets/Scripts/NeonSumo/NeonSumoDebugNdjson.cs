using System.IO;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace NeonSumo
{
    /// <summary>Compact NDJSON emission for Cursor debug-mode analysis (session ad9ac3).</summary>
    public static class NeonSumoDebugNdjson
    {
        const string SessionId = "ad9ac3";

        public static string TryGetWorkspaceNdjsonLogPath()
        {
            try
            {
#if UNITY_EDITOR || UNITY_STANDALONE_WIN || UNITY_STANDALONE_LINUX || UNITY_STANDALONE_OSX
                var parent = Directory.GetParent(Application.dataPath);
                return parent != null ? Path.Combine(parent.FullName, "debug-ad9ac3.log") : null;
#else
                return null;
#endif
            }
            catch
            {
                return null;
            }
        }

        public static void Log(string hypothesisId, string location, string message, object data = null)
        {
        }
    }
}
