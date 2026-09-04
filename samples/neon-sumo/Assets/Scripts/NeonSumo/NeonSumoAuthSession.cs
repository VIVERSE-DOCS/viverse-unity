using UnityEngine;
using ViverseSDK;

namespace NeonSumo
{
    /// <summary>
    /// Neon Sumo AuthManager wiring. Subscribe before Initialize. Does not import legacy access_token.
    /// </summary>
    public static class NeonSumoAuthSession
    {
        private const string LogPrefix = "[NeonSumoAuth]";
        private static AuthManager _auth;
        private static bool _initialized;
        private static bool _multiplayerLoginWanted;
        private static bool _webGlAutoLoginAttempted;
        private static bool _editorLoginRequested;

        public static AuthManager Auth => _auth;

        public static void EnsureInitialized(NeonSumoConfig config, string appIdOverride, MonoBehaviour coroutineHost)
        {
            string appId = ResolveAppId(config, appIdOverride);
            if (string.IsNullOrWhiteSpace(appId) || appId.Length < 3)
            {
                DebugLogger.LogWarning($"{LogPrefix} Skipping AuthManager init: App ID missing.");
                return;
            }

            if (_initialized && _auth != null)
                return;

            _auth = PlaySdkRuntimeRoot.Instance.GetOrCreateAuthManager();
            _auth.OnLoginSuccess += HandleLoginSuccess;
            _auth.OnLogout += HandleLogout;
            _auth.OnError += HandleError;
            _auth.OnStatusChanged += HandleStatusChanged;

            _auth.Initialize(appId);
            _initialized = true;
            DebugLogger.Log($"{LogPrefix} AuthManager initialized");
        }

        /// <summary>Editor: open OAuth when entering Multiplayer/lobby. WebGL auto-login only after this is requested.</summary>
        public static void RequestLoginForMultiplayer()
        {
            _multiplayerLoginWanted = true;
            if (_auth == null || !_initialized)
                return;
            if (_auth.IsLoggedIn)
            {
                NeonSumoPlatformProfiles.AfterAuthManagerLogin(
                    new AuthResult { access_token = _auth.AccessToken, account_id = _auth.AccountId },
                    PlaySdkRuntimeRoot.Instance);
                return;
            }

#if UNITY_WEBGL && !UNITY_EDITOR
            _webGlAutoLoginAttempted = false;
            TryWebGlAutoLogin();
#else
            if (_editorLoginRequested)
                return;
            _editorLoginRequested = true;
            DebugLogger.Log($"{LogPrefix} Editor Login() for multiplayer");
            _auth.Login();
#endif
        }

        static void HandleLoginSuccess(AuthResult result)
        {
            DebugLogger.Log($"{LogPrefix} Login success account={result?.account_id}");
            NeonSumoPlatformProfiles.AfterAuthManagerLogin(result, PlaySdkRuntimeRoot.Instance);
        }

        static void HandleLogout()
        {
            DebugLogger.Log($"{LogPrefix} Logged out");
            NeonSumoPlatformProfiles.ClearViVerseGameplayDisplayCache();
        }

        static void HandleError(string error)
        {
            DebugLogger.LogWarning($"{LogPrefix} {error}");
        }

        static void HandleStatusChanged(string status)
        {
            if (string.IsNullOrEmpty(status) || !_multiplayerLoginWanted)
                return;
#if UNITY_WEBGL && !UNITY_EDITOR
            if (status.IndexOf("SDK not ready", System.StringComparison.OrdinalIgnoreCase) >= 0)
                _webGlAutoLoginAttempted = false;
            if (status.StartsWith("Not logged in"))
                TryWebGlAutoLogin();
#endif
        }

        static void TryWebGlAutoLogin()
        {
            if (!_multiplayerLoginWanted || _auth == null || _auth.IsLoggedIn || _webGlAutoLoginAttempted)
                return;
            _webGlAutoLoginAttempted = true;
            DebugLogger.Log($"{LogPrefix} WebGL Login()");
            _auth.Login();
        }

        static string ResolveAppId(NeonSumoConfig config, string appIdOverride)
        {
            if (!string.IsNullOrWhiteSpace(appIdOverride))
                return appIdOverride.Trim();
            return config != null ? config.AppId : string.Empty;
        }
    }
}
