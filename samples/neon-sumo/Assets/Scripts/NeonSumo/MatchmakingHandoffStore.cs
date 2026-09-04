using System;
using UnityEngine;

namespace NeonSumo
{
    /// <summary>
    /// Centralized read/write for matchmaking handoff data (PlayerPrefs).
    /// Used by LobbyManager (write) and MatchmakingFlow + SceneBootstrap (read).
    /// </summary>
    public static class MatchmakingHandoffStore
    {
        private const string KeyRoomId = "MultiplayerRoomId";
        private const string KeyAppId = "MultiplayerAppId";
        private const string KeyUserSessionId = "MultiplayerUserSessionId";
        private const string KeyShouldAutoConnect = "ShouldAutoConnect";
        private const string KeyCurrentRoomData = "CurrentRoomData";
        private const string KeyCurrentActorData = "CurrentActorData";
        private const string KeyLocalDisplayNameSeed = "NeonSumoLocalDisplayNameSeed";

        public readonly struct HandoffData
        {
            public string RoomId { get; }
            public string AppId { get; }
            public string UserSessionId { get; }

            public HandoffData(string roomId, string appId, string userSessionId)
            {
                RoomId = roomId;
                AppId = appId;
                UserSessionId = userSessionId;
            }

            public bool IsValid =>
                !string.IsNullOrWhiteSpace(RoomId) &&
                !string.IsNullOrWhiteSpace(AppId) &&
                !string.IsNullOrWhiteSpace(UserSessionId);
        }

        /// <summary>
        /// Returns true only when a valid handoff payload exists and auto-connect is enabled.
        /// </summary>
        public static bool HasPendingHandoff()
        {
            return TryLoad(out _);
        }

        /// <summary>
        /// Loads handoff data. Returns true only when auto-connect is enabled and the payload is valid.
        /// Invalid persisted state is cleared automatically.
        /// </summary>
        public static bool TryLoad(out HandoffData data)
        {
            data = default;

            if (PlayerPrefs.GetInt(KeyShouldAutoConnect, 0) != 1)
                return false;

            data = new HandoffData(
                PlayerPrefs.GetString(KeyRoomId, string.Empty),
                PlayerPrefs.GetString(KeyAppId, string.Empty),
                NeonSumoUserId.Normalize(PlayerPrefs.GetString(KeyUserSessionId, string.Empty)));

            if (data.IsValid)
                return true;

            Clear();
            data = default;
            return false;
        }

        /// <summary>
        /// Saves handoff data and enables auto-connect for the gameplay scene.
        /// Optionally persists a sanitized local display name seed (lobby Actor name until platform profile overrides).
        /// </summary>
        public static void SaveHandoff(string roomId, string appId, string userSessionId, string localDisplayNameSeed = null)
        {
            if (string.IsNullOrWhiteSpace(roomId))
                throw new ArgumentException("roomId is required.", nameof(roomId));
            if (string.IsNullOrWhiteSpace(appId))
                throw new ArgumentException("appId is required.", nameof(appId));
            userSessionId = NeonSumoUserId.Normalize(userSessionId);
            if (string.IsNullOrWhiteSpace(userSessionId))
                throw new ArgumentException("userSessionId is required.", nameof(userSessionId));

            PlayerPrefs.SetString(KeyRoomId, roomId);
            PlayerPrefs.SetString(KeyAppId, appId);
            PlayerPrefs.SetString(KeyUserSessionId, userSessionId);
            PlayerPrefs.SetInt(KeyShouldAutoConnect, 1);

            if (!string.IsNullOrWhiteSpace(localDisplayNameSeed))
            {
                string sanitized = NeonSumoDisplayNameRegistry.Sanitize(localDisplayNameSeed);
                if (!string.IsNullOrEmpty(sanitized))
                    PlayerPrefs.SetString(KeyLocalDisplayNameSeed, sanitized);
                else
                    PlayerPrefs.DeleteKey(KeyLocalDisplayNameSeed);
            }
            else
            {
                PlayerPrefs.DeleteKey(KeyLocalDisplayNameSeed);
            }

            PlayerPrefs.Save();
        }

        /// <summary>
        /// Reads optional display name seed once and removes key so stale values never reapply mid-session incorrectly.
        /// </summary>
        public static bool TryConsumeLocalDisplayNameSeed(out string sanitizedName)
        {
            sanitizedName = null;

            string raw = PlayerPrefs.GetString(KeyLocalDisplayNameSeed, string.Empty);
            PlayerPrefs.DeleteKey(KeyLocalDisplayNameSeed);
            PlayerPrefs.Save();

            if (string.IsNullOrEmpty(raw))
                return false;

            sanitizedName = NeonSumoDisplayNameRegistry.Sanitize(raw);
            return !string.IsNullOrEmpty(sanitizedName);
        }

        /// <summary>
        /// Clears all handoff-related persisted state.
        /// </summary>
        public static void Clear()
        {
            PlayerPrefs.DeleteKey(KeyRoomId);
            PlayerPrefs.DeleteKey(KeyAppId);
            PlayerPrefs.DeleteKey(KeyUserSessionId);
            PlayerPrefs.DeleteKey(KeyShouldAutoConnect);
            PlayerPrefs.DeleteKey(KeyCurrentRoomData);
            PlayerPrefs.DeleteKey(KeyCurrentActorData);
            PlayerPrefs.DeleteKey(KeyLocalDisplayNameSeed);
            PlayerPrefs.Save();
        }
    }
}
