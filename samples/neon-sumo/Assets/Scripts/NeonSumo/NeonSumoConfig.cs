using UnityEngine;
using UnityEngine.Serialization;

namespace NeonSumo
{
    [CreateAssetMenu(fileName = "NeonSumoConfig", menuName = "NeonSumo/Config")]
    public sealed class NeonSumoConfig : ScriptableObject
    {
        private const string DefaultAppId = "example-app-id";

        [SerializeField, HideInInspector]
        // Reserved for future asset migration/versioning.
#pragma warning disable CS0414
        private int version = 1;
#pragma warning restore CS0414

        [Header("Connection")]
        [Tooltip("Application ID for matchmaking/multiplayer. Replace with your actual App ID.")]
        [SerializeField] private string appId = DefaultAppId;

        [Header("Domain Settings")]
        [SerializeField] private PlayerSettings player = new();
        [SerializeField] private MatchSettings match = new();

        // Public read-only scalar API (avoids exposing mutable nested references)
        public string AppId => string.IsNullOrWhiteSpace(appId) ? DefaultAppId : appId;

        public int MinPlayers => player.MinPlayers;
        public int MaxPlayers => player.MaxPlayers;
        public int PlayerCountRange => player.PlayerCountRange;
        public bool IsSoloMode => player.IsSoloMode;

        public int ReadyTime => match.ReadyTime;
        public float StartDelayTime => match.StartDelayTime;
        public int PlayTime => match.PlayTime;
        public int RingShrinkAccelerationSecond => match.RingShrinkAccelerationSecond;
        public int WaitPlayerTimeout => match.WaitPlayerTimeout;

        public bool IsValidPlayerCount(int count) => player.IsValidPlayerCount(count);
        public int ClampPlayerCount(int count) => player.ClampPlayerCount(count);

        /// <summary>Call once at startup. OnValidate only runs in editor; this safeguards runtime builds.</summary>
        public void EnsureRuntimeValidity() => ValidateAll(logIssues: true);

#if UNITY_EDITOR
        private void OnValidate() => ValidateAll(logIssues: false);

        private void Reset()
        {
            player = new PlayerSettings();
            match = new MatchSettings();
        }
#endif

        private void ValidateAll(bool logIssues)
        {
            player.ValidateRuntime(logIssues);
            match.ValidateRuntime(logIssues);
        }
    }

    [System.Serializable]
    internal class PlayerSettings
    {
        [Header("Lobby Player Counts")]
        [Tooltip("Minimum players required to start a match.")]
        [Min(1)] [SerializeField] private int minPlayers = 2;

        [Tooltip("Maximum players allowed in a match.")]
        [Min(1)] [SerializeField] private int maxPlayers = 4;

        public int MinPlayers => minPlayers;
        public int MaxPlayers => maxPlayers;
        public int PlayerCountRange => maxPlayers - minPlayers + 1;
        public bool IsSoloMode => maxPlayers == 1;

        public bool IsValidPlayerCount(int count) => count >= minPlayers && count <= maxPlayers;
        public int ClampPlayerCount(int count) => Mathf.Clamp(count, minPlayers, maxPlayers);

        public void ValidateRuntime(bool logIssues)
        {
            if (maxPlayers >= minPlayers) return;
            if (logIssues)
                Debug.LogError($"[{nameof(NeonSumoConfig)}] Invalid config detected at runtime: {nameof(maxPlayers)} < {nameof(minPlayers)}. Fixing.");
            maxPlayers = minPlayers;
        }
    }

    [System.Serializable]
    internal class MatchSettings
    {
        [Header("Game Module (Multiplayer)")]
        [Tooltip("Seconds to wait before starting when all players ready.")]
        [Min(0)] [SerializeField] private int readyTime = 3;

        [Tooltip("Delay before game start after countdown.")]
        [Min(0f)] [SerializeField] private float startDelayTime = 0.5f;

        [Tooltip("Round duration in seconds.")]
        [Min(1)] [SerializeField] private int playTime = 60;

        [Tooltip("Seconds before round end when ring shrink accelerates.")]
        [Min(0)] [SerializeField] [FormerlySerializedAs("changeSecond")] private int ringShrinkAccelerationSecond = 10;

        [Tooltip("Timeout in seconds waiting for players.")]
        [Min(1)] [SerializeField] private int waitPlayerTimeout = 100;

        public int ReadyTime => readyTime;
        public float StartDelayTime => startDelayTime;
        public int PlayTime => playTime;
        public int RingShrinkAccelerationSecond => ringShrinkAccelerationSecond;
        public int WaitPlayerTimeout => waitPlayerTimeout;

        public void ValidateRuntime(bool logIssues)
        {
            if (ringShrinkAccelerationSecond <= playTime) return;
            if (logIssues)
                Debug.LogWarning($"[{nameof(MatchSettings)}] {nameof(ringShrinkAccelerationSecond)} exceeded {nameof(playTime)}. Clamping.");
            ringShrinkAccelerationSecond = playTime;
        }
    }
}
