using UnityEditor;
using UnityEngine;

// Menu: Build/WebGL — switch WebGL custom templates under Assets/WebGLTemplates/.
public static class WebGLFireTVTemplateMenu
{
    public const string FireTVTemplateId = "PROJECT:FireTV";
    public const string FireTvLegacyTemplateId = "PROJECT:firetv_diagnostics";

    [MenuItem("Build/WebGL/Use Fire TV template (fullscreen)")]
    static void ApplyFireTVTemplate()
    {
        PlayerSettings.WebGL.template = FireTVTemplateId;
        Debug.Log($"WebGL template set to {FireTVTemplateId}. Path: Assets/WebGLTemplates/FireTV/index.html");
    }

    [MenuItem("Build/WebGL/Use Fire TV template (firetv_diagnostics alias)")]
    static void ApplyFireTvLegacyTemplate()
    {
        PlayerSettings.WebGL.template = FireTvLegacyTemplateId;
        Debug.Log($"WebGL template set to {FireTvLegacyTemplateId}. Path: Assets/WebGLTemplates/firetv_diagnostics/index.html");
    }
}
