using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace NeonSumo
{
    /// <summary>
    /// Maps multiplayer PeerIds to sanitized display strings for UI. No VIVERSE dependency.
    /// </summary>
    public sealed class NeonSumoDisplayNameRegistry
    {
        public const int MaxDisplayNameLength = 24;

        private readonly Dictionary<string, string> _byUserId = new Dictionary<string, string>(StringComparer.Ordinal);

        public void Clear()
        {
            _byUserId.Clear();
        }

        /// <summary>Drop mapping when peer leaves mid-session.</summary>
        public void RemoveUser(string userId)
        {
            if (!NeonSumoUserId.TryNormalize(userId, out userId)) return;
            _byUserId.Remove(userId);
        }

        public bool TryGet(string userId, out string sanitized)
        {
            sanitized = null;
            return NeonSumoUserId.TryNormalize(userId, out userId) && _byUserId.TryGetValue(userId, out sanitized);
        }

        /// <summary>
        /// Sanitize and register. Returns false if nothing stored (invalid input).
        /// </summary>
        public bool TrySetRaw(string userId, string raw, out string sanitized)
        {
            sanitized = Sanitize(raw);
            if (!NeonSumoUserId.TryNormalize(userId, out userId) || sanitized == null)
                return false;

            _byUserId[userId] = sanitized;
            return true;
        }

        /// <summary>Trim, drop control chars, cap length — null if unusable.</summary>
        public static string Sanitize(string raw)
        {
            if (string.IsNullOrEmpty(raw))
                return null;

            var sb = new StringBuilder(Math.Min(raw.Length, MaxDisplayNameLength + 16));
            string trimmed = raw.Trim();
            for (int i = 0; i < trimmed.Length && sb.Length < MaxDisplayNameLength; i++)
            {
                char c = trimmed[i];
                if (!char.IsControl(c))
                    sb.Append(c);
            }

            string s = sb.ToString().Trim();
            return s.Length > 0 ? s : null;
        }

        public static string RemoteIdFallbackSubstring(string userId)
        {
            if (string.IsNullOrEmpty(userId))
                return string.Empty;

            int n = Mathf.Min(4, userId.Length);
            return n <= 0 ? string.Empty : userId.Substring(0, n);
        }

        /// <summary>Suffix for roster line before caller applies uppercase (local default "You").</summary>
        public string ResolvePlayerListNicknameSegment(string userId, string localPlayerId)
        {
            userId = NeonSumoUserId.Normalize(userId);
            localPlayerId = NeonSumoUserId.Normalize(localPlayerId);
            if (TryGet(userId, out var name))
                return name;

            if (!string.IsNullOrEmpty(localPlayerId) && userId == localPlayerId)
                return "You";

            string sub = RemoteIdFallbackSubstring(userId);
            DebugLogger.Log($"[NeonSumo][DisplayNames] Fallback list nickname PeerId-prefix={ShortId(userId)}");
            return sub;
        }

        /// <summary>Player bar chip: YOU or uppercase id prefix vs stored name.</summary>
        public string ResolvePlayerBarLabel(string userId, string localPlayerId)
        {
            userId = NeonSumoUserId.Normalize(userId);
            localPlayerId = NeonSumoUserId.Normalize(localPlayerId);
            if (TryGet(userId, out var name))
                return name.ToUpperInvariant();

            if (!string.IsNullOrEmpty(localPlayerId) && userId == localPlayerId)
                return "YOU";

            string sub = RemoteIdFallbackSubstring(userId);
            DebugLogger.Log($"[NeonSumo][DisplayNames] Fallback player bar PeerId-prefix={ShortId(userId)}");
            return sub.ToUpperInvariant();
        }

        /// <summary>Winner headline: prefers registered display name.</summary>
        public string ResolveWinnerHeadline(string winnerUserId)
        {
            winnerUserId = NeonSumoUserId.Normalize(winnerUserId);
            if (TryGet(winnerUserId, out var name))
                return name.ToUpperInvariant();

            string fallback = $"Player {RemoteIdFallbackSubstring(winnerUserId)}".ToUpperInvariant();
            DebugLogger.Log($"[NeonSumo][DisplayNames] Fallback winner line PeerId-prefix={ShortId(winnerUserId)}");
            return fallback;
        }

        static string ShortId(string userId)
        {
            if (string.IsNullOrEmpty(userId)) return "∅";
            return userId.Length <= 8 ? userId : userId.Substring(0, 8) + "…";
        }
    }
}
