using UnityEngine;
using ViverseSDK;

namespace NeonSumo
{
    /// <summary>
    /// Get-or-create locator for SDK clients. Persists across scene loads.
    /// VIVERSE 1.2 client GameObjects stay root-level (SDK applies DontDestroyOnLoad).
    /// </summary>
    public sealed class PlaySdkRuntimeRoot : MonoBehaviour
    {
        public const string ViverseMatchmakingGameObjectName = "NeonSumoViverseMatchmakingClient";
        public const string ViverseMultiplayerGameObjectName = "NeonSumoViverseMultiplayerClient";
        public const string ViverseAuthGameObjectName = "NeonSumoViverseAuthManager";

        private static PlaySdkRuntimeRoot _instance;

        public static PlaySdkRuntimeRoot Instance
        {
            get
            {
                if (_instance == null)
                    CreateInstance();
                return _instance;
            }
        }

        private static void CreateInstance()
        {
            var go = new GameObject(nameof(PlaySdkRuntimeRoot));
            _instance = go.AddComponent<PlaySdkRuntimeRoot>();
        }

        private MatchmakingClient _viverseMatchmakingClient;
        private MultiplayerClient _viverseMultiplayerClient;
        private AuthManager _viverseAuthManager;

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }
            _instance = this;
            DontDestroyOnLoad(gameObject);
        }

        private void OnDestroy()
        {
            if (_instance == this)
                _instance = null;
        }

        /// <summary>Get or create ViverseSDK.AuthManager as a root-level object.</summary>
        public AuthManager GetOrCreateAuthManager()
        {
            if (_viverseAuthManager != null)
                return _viverseAuthManager;

            if (AuthManager.Instance != null)
            {
                _viverseAuthManager = AuthManager.Instance;
                DebugLogger.Log($"[PlaySdkRuntimeRoot] Reusing existing ViverseSDK.AuthManager on '{_viverseAuthManager.gameObject.name}'");
                return _viverseAuthManager;
            }

            var go = new GameObject(ViverseAuthGameObjectName);
            _viverseAuthManager = go.AddComponent<AuthManager>();
            DebugLogger.Log($"[PlaySdkRuntimeRoot] Created root-level {typeof(AuthManager).FullName} on '{go.name}'");
            return _viverseAuthManager;
        }

        /// <summary>Get or create ViverseSDK.MatchmakingClient as a root-level object.</summary>
        public MatchmakingClient GetOrCreateMatchmakingClient()
        {
            if (_viverseMatchmakingClient != null)
                return _viverseMatchmakingClient;

            if (MatchmakingClient.Instance != null)
            {
                _viverseMatchmakingClient = MatchmakingClient.Instance;
                DebugLogger.Log($"[PlaySdkRuntimeRoot] Reusing existing ViverseSDK.MatchmakingClient on '{_viverseMatchmakingClient.gameObject.name}'");
                return _viverseMatchmakingClient;
            }

            var go = new GameObject(ViverseMatchmakingGameObjectName);
            _viverseMatchmakingClient = go.AddComponent<MatchmakingClient>();
            DebugLogger.Log($"[PlaySdkRuntimeRoot] Created root-level {typeof(MatchmakingClient).FullName} on '{go.name}'");
            return _viverseMatchmakingClient;
        }

        /// <summary>Get or create ViverseSDK.MultiplayerClient as a root-level object.</summary>
        public MultiplayerClient GetOrCreateMultiplayerClient()
        {
            if (_viverseMultiplayerClient != null)
                return _viverseMultiplayerClient;

            if (MultiplayerClient.Instance != null)
            {
                _viverseMultiplayerClient = MultiplayerClient.Instance;
                DebugLogger.Log($"[PlaySdkRuntimeRoot] Reusing existing ViverseSDK.MultiplayerClient on '{_viverseMultiplayerClient.gameObject.name}'");
                return _viverseMultiplayerClient;
            }

            var go = new GameObject(ViverseMultiplayerGameObjectName);
            _viverseMultiplayerClient = go.AddComponent<MultiplayerClient>();
            DebugLogger.Log($"[PlaySdkRuntimeRoot] Created root-level {typeof(MultiplayerClient).FullName} on '{go.name}'");
            return _viverseMultiplayerClient;
        }

        public void DestroyMatchmakingClient()
        {
            if (_viverseMatchmakingClient == null)
                return;

            var client = _viverseMatchmakingClient;
            _viverseMatchmakingClient = null;
            if (client != null)
                Destroy(client.gameObject);
        }

        public void DestroyMultiplayerClient()
        {
            if (_viverseMultiplayerClient == null)
                return;

            var client = _viverseMultiplayerClient;
            _viverseMultiplayerClient = null;
            client.Disconnect();
            Destroy(client.gameObject);
        }
    }
}
