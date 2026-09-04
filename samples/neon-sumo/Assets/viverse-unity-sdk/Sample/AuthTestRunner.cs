using UnityEngine;
using ViverseSDK;

public class AuthTestRunner : MonoBehaviour
{
    [Header("Configuration")]
    [SerializeField] private string appId = "YOUR_APP_ID";

    [Header("Status (read-only)")]
    [SerializeField] private string status = "Not initialized";
    [SerializeField] private string token = "";
    [SerializeField] private string accountId = "";

    private AuthManager _auth;
    private Vector2 _scrollPos;
    private bool _initialized;

    void Start()
    {
        _auth = FindObjectOfType<AuthManager>();
        if (_auth == null)
        {
            var go = new GameObject("[AuthManager]");
            _auth = go.AddComponent<AuthManager>();
        }

        _auth.OnStatusChanged += (msg) => status = msg;
        _auth.OnLoginSuccess += (result) =>
        {
            token = result.access_token ?? "";
            accountId = result.account_id ?? "";
        };
        _auth.OnLogout += () =>
        {
            token = "";
            accountId = "";
        };
        _auth.OnError += (err) => Debug.LogError($"[AuthTest] Error: {err}");

        _auth.Initialize(appId);
        _initialized = true;
    }

    void OnGUI()
    {
        float dpi = Screen.dpi > 0 ? Screen.dpi / 96f : 1f;
        float scale = Mathf.Max(1f, dpi);
        int fontSize = Mathf.RoundToInt(14 * scale);
        int btnHeight = Mathf.RoundToInt(40 * scale);
        int padding = Mathf.RoundToInt(16 * scale);
        int panelWidth = Mathf.RoundToInt(420 * scale);

        GUIStyle boxStyle = new GUIStyle(GUI.skin.box) { fontSize = fontSize };
        GUIStyle btnStyle = new GUIStyle(GUI.skin.button) { fontSize = fontSize };
        GUIStyle labelStyle = new GUIStyle(GUI.skin.label) { fontSize = fontSize, wordWrap = true };

        GUILayout.BeginArea(new Rect(padding, padding, panelWidth, Screen.height - padding * 2));
        _scrollPos = GUILayout.BeginScrollView(_scrollPos);

        GUILayout.Box("VIVERSE Auth Test", boxStyle, GUILayout.ExpandWidth(true));
        GUILayout.Space(8);

        if (_auth != null && _auth.IsLoggedIn)
        {
            GUILayout.Label($"Logged in as: {accountId}", labelStyle);
            GUILayout.Space(8);

            if (GUILayout.Button("Logout", btnStyle, GUILayout.Height(btnHeight)))
                _auth.Logout();
        }
        else
        {
            GUILayout.Label($"Status: {status}", labelStyle);
            GUILayout.Space(8);

            if (GUILayout.Button("Login", btnStyle, GUILayout.Height(btnHeight)))
                _auth.Login();
        }

        GUILayout.Space(16);

        GUILayout.Box("Token", boxStyle, GUILayout.ExpandWidth(true));
        if (!string.IsNullOrEmpty(token))
        {
            string display = token.Length > 60 ? token.Substring(0, 60) + "..." : token;
            GUILayout.Label(display, labelStyle);

            if (GUILayout.Button("Copy Token", btnStyle, GUILayout.Height(btnHeight * 0.8f)))
            {
                GUIUtility.systemCopyBuffer = token;
                status = "Token copied to clipboard!";
            }
        }
        else
        {
            GUILayout.Label("(no token)", labelStyle);
        }

        GUILayout.EndScrollView();
        GUILayout.EndArea();
    }
}
