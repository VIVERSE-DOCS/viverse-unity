using System;
using System.Runtime.InteropServices;
using System.Text;
using UnityEngine;

namespace ViverseSDK
{
    /// <summary>
    /// AuthManager — Thin C# interface for VIVERSE authentication.
    /// WebGL: delegates to viverse-sdk UMD via jslib.
    /// Editor: uses HttpServer on localhost:40078 for OAuth callback.
    /// </summary>
    public class AuthManager : MonoBehaviour
    {
        public static AuthManager Instance { get; private set; }

        // Events
        public event Action<string> OnStatusChanged;
        public event Action<AuthResult> OnLoginSuccess;
        public event Action OnLogout;
        public event Action<string> OnError;

        // State
        public bool IsLoggedIn => !string.IsNullOrEmpty(AccessToken);
        public string AccessToken => PlayerPrefs.GetString("viverse_access_token", "");
        public string AccountId => PlayerPrefs.GetString("viverse_account_id", "");

        [SerializeField] private string appId = "";
        private const string DOMAIN = "account.htcvive.com";
        private bool _sdkReady;

#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")] private static extern void ViverseAuth_LoadSDK(string gameObjectName);
        [DllImport("__Internal")] private static extern void ViverseAuth_InitClient(string clientId, string domain);
        [DllImport("__Internal")] private static extern void ViverseAuth_CheckAuth();
        [DllImport("__Internal")] private static extern void ViverseAuth_LoginWithWorlds();
        [DllImport("__Internal")] private static extern void ViverseAuth_Logout();
#endif

        // Editor-only
#if UNITY_EDITOR || !UNITY_WEBGL
        private AuthHttpServer _httpServer;
#endif

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        /// <summary>
        /// Initialize auth with app ID. Call this first.
        /// </summary>
        public void Initialize(string appId)
        {
            if (string.IsNullOrEmpty(appId) || appId.Length < 3)
            {
                SetStatus("Error: App ID must be 3+ characters");
                return;
            }

            this.appId = appId;
            SetStatus("Initializing auth");

            // Load saved credentials — validate token expiration
            if (IsLoggedIn)
            {
                if (IsTokenExpired(AccessToken))
                {
                    Debug.Log("[AuthManager] Saved token is expired, logging out.");
                    Logout();
                }
                else
                {
                    SetStatus("Found saved credentials");
                    OnLoginSuccess?.Invoke(new AuthResult { access_token = AccessToken, account_id = AccountId });
                }
            }

#if UNITY_WEBGL && !UNITY_EDITOR
            ViverseAuth_LoadSDK(gameObject.name);
#else
            // Editor: init HttpServer
            _httpServer = gameObject.GetComponent<AuthHttpServer>();
            if (_httpServer == null)
                _httpServer = gameObject.AddComponent<AuthHttpServer>();
            _httpServer.OnAuthResult += HandleEditorAuthResult;
            MainThreadDispatcher.Initialize(); // Pre-create dispatcher on main thread
            _sdkReady = true;
            SetStatus("Ready (Editor mode)");
#endif
        }

        /// <summary>
        /// Start login flow.
        /// </summary>
        public void Login()
        {
            if (string.IsNullOrEmpty(appId))
            {
                SetStatus("Error: Call Initialize(appId) first");
                return;
            }

#if UNITY_WEBGL && !UNITY_EDITOR
            if (!_sdkReady)
            {
                SetStatus("SDK not ready yet...");
                return;
            }
            SetStatus("Opening login...");
            ViverseAuth_LoginWithWorlds();
#else
            SetStatus("Opening browser for login...");
            _httpServer.StartServerAndLogin(appId);
#endif
        }

        /// <summary>
        /// Logout and clear credentials.
        /// </summary>
        public void Logout()
        {
            PlayerPrefs.DeleteKey("viverse_access_token");
            PlayerPrefs.DeleteKey("viverse_account_id");
            PlayerPrefs.DeleteKey("viverse_expires_in");
            PlayerPrefs.Save();

#if UNITY_WEBGL && !UNITY_EDITOR
            ViverseAuth_Logout();
#endif
            SetStatus("Logged out");
            OnLogout?.Invoke();
        }

        // ============================================================
        // JS Callbacks (called from jslib via SendMessage)
        // ============================================================

        public void OnAuthSDKLoaded(string _)
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            SetStatus("SDK loaded, initializing client...");
            ViverseAuth_InitClient(appId, DOMAIN);
#endif
        }

        public void OnAuthSDKLoadFailed(string error)
        {
            SetStatus($"SDK load failed: {error}");
            OnError?.Invoke(error);
        }

        public void OnAuthClientInitialized(string _)
        {
            _sdkReady = true;
            SetStatus("Client initialized, checking auth...");
#if UNITY_WEBGL && !UNITY_EDITOR
            ViverseAuth_CheckAuth();
#endif
        }

        public void OnAuthSuccess(string resultJson)
        {
            try
            {
                var result = JsonUtility.FromJson<AuthResult>(resultJson);
                SaveCredentials(result);
                SetStatus("Logged in");
                OnLoginSuccess?.Invoke(result);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[AuthManager] Parse error: {ex.Message}");
                SetStatus("Login success but failed to parse result");
            }
        }

        public void OnAuthNotLoggedIn(string _)
        {
            SetStatus("Not logged in. Click Login to authenticate.");
        }

        public void OnAuthError(string error)
        {
            SetStatus($"Error: {error}");
            OnError?.Invoke(error);
        }

        public void OnAuthLoggedOut(string _)
        {
            SetStatus("Logged out");
            OnLogout?.Invoke();
        }

        // ============================================================
        // Editor callback
        // ============================================================

#if UNITY_EDITOR || !UNITY_WEBGL
        private void HandleEditorAuthResult(AuthResult result)
        {
            if (result != null && !string.IsNullOrEmpty(result.access_token))
            {
                SaveCredentials(result);
                SetStatus("Logged in");
                OnLoginSuccess?.Invoke(result);
            }
            else
            {
                SetStatus("Login failed");
                OnError?.Invoke("No token received");
            }
        }
#endif

        // ============================================================
        // Helpers
        // ============================================================

        private bool IsTokenExpired(string token)
        {
            try
            {
                // JWT = header.payload.signature — decode payload (base64url)
                var parts = token.Split('.');
                if (parts.Length != 3) return true;

                string payload = parts[1];
                // Base64url → Base64
                payload = payload.Replace('-', '+').Replace('_', '/');
                switch (payload.Length % 4)
                {
                    case 2: payload += "=="; break;
                    case 3: payload += "="; break;
                }

                byte[] bytes = Convert.FromBase64String(payload);
                string json = Encoding.UTF8.GetString(bytes);

                var dict = MiniJson.Deserialize(json) as System.Collections.Generic.Dictionary<string, object>;
                if (dict == null || !dict.ContainsKey("exp")) return true;

                long exp = Convert.ToInt64(dict["exp"]);
                long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                return now >= exp;
            }
            catch
            {
                return true;
            }
        }

        private void SaveCredentials(AuthResult result)
        {
            if (!string.IsNullOrEmpty(result.access_token))
                PlayerPrefs.SetString("viverse_access_token", result.access_token);
            if (!string.IsNullOrEmpty(result.account_id))
                PlayerPrefs.SetString("viverse_account_id", result.account_id);
            if (result.expires_in > 0)
                PlayerPrefs.SetInt("viverse_expires_in", result.expires_in);
            PlayerPrefs.Save();
        }

        private void SetStatus(string msg)
        {
            Debug.Log($"[AuthManager] {msg}");
            OnStatusChanged?.Invoke(msg);
        }

        /// <summary>
        /// WebGL callback receiver for Lambda jslib bridge.
        /// Routes to LambdaCallbackRegistry by bridgeId.
        /// </summary>
        public void OnLambdaCallback(string json)
        {
            try
            {
                var dict = MiniJson.Deserialize(json) as System.Collections.Generic.Dictionary<string, object>;
                if (dict == null) return;
                int bridgeId = dict.ContainsKey("bridgeId") ? Convert.ToInt32(dict["bridgeId"]) : -1;
                bool success = dict.ContainsKey("success") && dict["success"] is bool b && b;
                if (success)
                    LambdaCallbackRegistry.Resolve(bridgeId, json);
                else
                    LambdaCallbackRegistry.Reject(bridgeId, dict.ContainsKey("message") ? dict["message"].ToString() : "unknown error");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[AuthManager] OnLambdaCallback error: {ex.Message}");
            }
        }

        /// <summary>
        /// WebGL callback receiver for Cloud Save jslib bridge.
        /// </summary>
        public void OnCloudSaveCallback(string json)
        {
            try
            {
                var dict = MiniJson.Deserialize(json) as System.Collections.Generic.Dictionary<string, object>;
                if (dict == null) return;
                int bridgeId = dict.ContainsKey("bridgeId") ? Convert.ToInt32(dict["bridgeId"]) : -1;
                bool success = dict.ContainsKey("success") && dict["success"] is bool b && b;
                if (success)
                    CloudSaveCallbackRegistry.Resolve(bridgeId, json);
                else
                    CloudSaveCallbackRegistry.Reject(bridgeId, dict.ContainsKey("message") ? dict["message"].ToString() : "unknown error");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[AuthManager] OnCloudSaveCallback error: {ex.Message}");
            }
        }

        /// <summary>
        /// WebGL callback receiver for Leaderboard jslib bridge.
        /// </summary>
        public void OnLeaderboardCallback(string json)
        {
            try
            {
                var dict = MiniJson.Deserialize(json) as System.Collections.Generic.Dictionary<string, object>;
                if (dict == null) return;
                int bridgeId = dict.ContainsKey("bridgeId") ? Convert.ToInt32(dict["bridgeId"]) : -1;
                bool success = dict.ContainsKey("success") && dict["success"] is bool b && b;
                if (success)
                    LeaderboardCallbackRegistry.Resolve(bridgeId, json);
                else
                    LeaderboardCallbackRegistry.Reject(bridgeId, dict.ContainsKey("message") ? dict["message"].ToString() : "unknown error");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[AuthManager] OnLeaderboardCallback error: {ex.Message}");
            }
        }

        /// <summary>
        /// WebGL callback receiver for Achievements jslib bridge.
        /// </summary>
        public void OnAchievementCallback(string json)
        {
            try
            {
                var dict = MiniJson.Deserialize(json) as System.Collections.Generic.Dictionary<string, object>;
                if (dict == null) return;
                int bridgeId = dict.ContainsKey("bridgeId") ? Convert.ToInt32(dict["bridgeId"]) : -1;
                bool success = dict.ContainsKey("success") && dict["success"] is bool b && b;
                if (success)
                    AchievementCallbackRegistry.Resolve(bridgeId, json);
                else
                    AchievementCallbackRegistry.Reject(bridgeId, dict.ContainsKey("message") ? dict["message"].ToString() : "unknown error");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[AuthManager] OnAchievementCallback error: {ex.Message}");
            }
        }

        public void OnAvatarCallback(string json)
        {
            try
            {
                var dict = MiniJson.Deserialize(json) as System.Collections.Generic.Dictionary<string, object>;
                if (dict == null) return;
                int bridgeId = dict.ContainsKey("bridgeId") ? Convert.ToInt32(dict["bridgeId"]) : -1;
                bool success = dict.ContainsKey("success") && dict["success"] is bool b && b;
                if (success)
                    AvatarCallbackRegistry.Resolve(bridgeId, json);
                else
                    AvatarCallbackRegistry.Reject(bridgeId, dict.ContainsKey("message") ? dict["message"].ToString() : "unknown error");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[AuthManager] OnAvatarCallback error: {ex.Message}");
            }
        }
    }

    [Serializable]
    public class AuthResult
    {
        public string access_token;
        public string account_id;
        public int expires_in;
    }
}
