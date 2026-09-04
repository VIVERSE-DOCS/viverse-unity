using System;
using System.Collections.Generic;
using UnityEngine;

namespace NeonSumo
{
    /// <summary>
    /// Owns player list model and rendering. Single place for AddOrUpdatePlayerRow.
    /// Model (PlayerUiState) is separate from rendering (RenderPlayerRow).
    /// Display color is owned by external caller (PlayerColorManager); presenter only stores and renders it.
    /// Player numbers follow NeonSumoPlayerOrder (room host first, then ordinal id).
    /// </summary>
    public class PlayerListPresenter
    {
        private readonly Func<string> _getRoomHostUserId;

        private sealed class PlayerUiState
        {
            public string UserId;
            public bool IsLocal;
            public string BaseDisplayName;
            public Color Color;
            public bool Eliminated;
        }

        private readonly MainMenuPanelView _view;
        private readonly Dictionary<string, PlayerUiState> _players = new Dictionary<string, PlayerUiState>();
        private readonly Func<string, string> _resolveListNicknameSegment;

        public PlayerListPresenter(MainMenuPanelView view, Func<string> getRoomHostUserId, Func<string, string> resolveListNicknameSegment)
        {
            _view = view ?? throw new ArgumentNullException(nameof(view));
            _getRoomHostUserId = getRoomHostUserId;
            _resolveListNicknameSegment = resolveListNicknameSegment;
        }

        /// <summary>Rebuild roster labels after display-name registry changes.</summary>
        public void RefreshNicknameLabelsFromRegistry()
        {
            if (_players.Count == 0)
                return;

            ApplySortedPlayerNumbersAndRender();
        }

        /// <summary>Recompute Player N labels after room host id becomes known.</summary>
        public void RefreshHostFirstNumbers()
        {
            if (_players.Count == 0) return;
            ApplySortedPlayerNumbersAndRender();
        }

        /// <summary>Add player. Idempotent: no-op if already exists.</summary>
        public void AddPlayer(string userId, bool isLocal, Color nameColor)
        {
            if (string.IsNullOrEmpty(userId)) return;
            if (_players.ContainsKey(userId)) return;

            _players[userId] = new PlayerUiState
            {
                UserId = userId,
                IsLocal = isLocal,
                BaseDisplayName = string.Empty,
                Color = nameColor,
                Eliminated = false
            };

            ApplySortedPlayerNumbersAndRender();
        }

        /// <summary>Update player color. Idempotent.</summary>
        public void SetPlayerColor(string userId, Color nameColor)
        {
            UpdatePlayer(userId, state => state.Color = nameColor);
        }

        /// <summary>Mark player as eliminated. Idempotent.</summary>
        public void MarkEliminated(string userId)
        {
            UpdatePlayer(userId, state =>
            {
                if (state.Eliminated) return;
                state.Eliminated = true;
            });
        }

        /// <summary>Get player color (e.g. for winner display). Returns white if not found.</summary>
        public Color GetPlayerColor(string userId)
        {
            if (string.IsNullOrEmpty(userId)) return Color.white;
            return _players.TryGetValue(userId, out var state) ? state.Color : Color.white;
        }

        /// <summary>Remove player from list. Idempotent.</summary>
        public void RemovePlayer(string userId)
        {
            if (string.IsNullOrEmpty(userId)) return;
            if (!_players.Remove(userId)) return;

            _view.RemovePlayerRow(userId);
            if (_players.Count > 0)
                ApplySortedPlayerNumbersAndRender();
        }

        /// <summary>Set ready button state. Delegates to view.</summary>
        public void SetReadyState(bool isReady)
        {
            _view.SetReadyButtonState(!isReady, isReady ? "Waiting for players..." : "Ready");
        }

        /// <summary>Update wins counter. Delegates to view.</summary>
        public void UpdateWinsCounter(int wins)
        {
            _view.SetWinsCounter(wins);
        }

        /// <summary>Clear all player state and rows. Idempotent.</summary>
        public void Clear()
        {
            _players.Clear();

            _view.ClearPlayerRows();
        }

        private void UpdatePlayer(string userId, Action<PlayerUiState> update)
        {
            if (string.IsNullOrEmpty(userId)) return;
            if (!_players.TryGetValue(userId, out var state)) return;

            update(state);
            RenderPlayerRow(userId);
        }

        private void ApplySortedPlayerNumbersAndRender()
        {
            var ids = NeonSumoPlayerOrder.OrderedUserIds(_players.Keys, _getRoomHostUserId != null ? _getRoomHostUserId.Invoke() : null);

            for (int i = 0; i < ids.Count; i++)
            {
                var state = _players[ids[i]];
                string id = ids[i];
                string baseName = _resolveListNicknameSegment?.Invoke(id);
                if (string.IsNullOrEmpty(baseName))
                {
                    baseName = state.IsLocal ? "You" : id.Length >= 4 ? id.Substring(0, 4) : id;
                }

                state.BaseDisplayName = $"Player {i + 1}: {baseName}".ToUpperInvariant();
            }

            foreach (var id in ids)
                RenderPlayerRow(id);
        }

        private void RenderPlayerRow(string userId)
        {
            if (!_players.TryGetValue(userId, out var state)) return;

            string displayName = state.Eliminated
                ? $"{state.BaseDisplayName} OUT"
                : state.BaseDisplayName;

            _view.AddOrUpdatePlayerRow(userId, displayName, state.Color, state.Eliminated);
        }
    }
}
