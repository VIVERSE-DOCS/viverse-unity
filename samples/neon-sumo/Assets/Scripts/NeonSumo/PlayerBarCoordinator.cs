using System;
using System.Collections.Generic;
using UnityEngine;

namespace NeonSumo
{
    /// <summary>
    /// Owns player bar roster, slot mapping, placement counter, and bar UI updates.
    /// Injects color lookup to avoid coupling to PlayerColorManager.
    /// </summary>
    public class PlayerBarCoordinator
    {
        private const int MaxSlots = 4;

        private readonly PlayerBarCoordinatorContext _ctx;
        private readonly Dictionary<string, int> _slotByUserId = new Dictionary<string, int>();
        private readonly HashSet<string> _placedUserIds = new HashSet<string>();
        private int _nextPlacement;

        public PlayerBarCoordinator(PlayerBarCoordinatorContext ctx)
        {
            _ctx = ctx ?? throw new ArgumentNullException(nameof(ctx));
        }

        public void ResetForRound()
        {
            if (!HasView()) return;

            _placedUserIds.Clear();

            var roster = GetOrderedPlayers();
            ReassignSlots(roster);
            _nextPlacement = roster.Count;

            _ctx.PlayerBar.ResetPlacements();
            RenderRoster(roster);
        }

        public void RebuildRoster()
        {
            if (!HasView()) return;

            var roster = GetOrderedPlayers();
            if (roster.Count == 0)
            {
                _slotByUserId.Clear();
                _ctx.PlayerBar.SetPlayerCount(0);
                _ctx.PlayerBar.ResetPlacements();
                return;
            }

            ReassignSlots(roster);
            RenderRoster(roster);
        }

        /// <summary>
        /// During sequential spawn, the new player is usually last in host-first order. Updates only that bar slot when so;
        /// otherwise falls back to <see cref="RebuildRoster"/>.
        /// </summary>
        public void AppendLatestPlayerToBar(string userId)
        {
            if (!HasView()) return;

            var roster = GetOrderedPlayers();
            if (roster.Count == 0)
            {
                _slotByUserId.Clear();
                _ctx.PlayerBar.SetPlayerCount(0);
                _ctx.PlayerBar.ResetPlacements();
                return;
            }

            if (string.IsNullOrEmpty(userId) || roster[roster.Count - 1].Key != userId)
            {
                RebuildRoster();
                return;
            }

            ReassignSlots(roster);
            _ctx.PlayerBar.SetPlayerCount(roster.Count);
            RenderSlot(roster, roster.Count - 1);
        }

        public void ApplyElimination(string userId)
        {
            if (!HasView()) return;
            if (string.IsNullOrEmpty(userId)) return;
            if (!_slotByUserId.TryGetValue(userId, out int slotIndex)) return;
            if (!_placedUserIds.Add(userId)) return;

            if (_nextPlacement <= 0)
                _nextPlacement = _slotByUserId.Count;

            _ctx.PlayerBar.SetPlacement(slotIndex, _nextPlacement);
            _ctx.PlayerBar.SetEliminated(slotIndex, true);

            _nextPlacement = Math.Max(1, _nextPlacement - 1);
        }

        public void ApplyWinnerPlacement(string winnerId)
        {
            if (!HasView()) return;
            if (string.IsNullOrEmpty(winnerId)) return;
            if (!_slotByUserId.TryGetValue(winnerId, out int slotIndex)) return;
            if (!_placedUserIds.Add(winnerId)) return;

            _ctx.PlayerBar.SetPlacement(slotIndex, 1);
        }

        private bool HasView() => _ctx.PlayerBar != null;

        private List<KeyValuePair<string, NeonSumoPlayer>> GetOrderedPlayers()
        {
            var players = _ctx.GetPlayers?.Invoke();
            var roster = new List<KeyValuePair<string, NeonSumoPlayer>>();

            if (players == null)
                return roster;

            foreach (var pair in players)
                roster.Add(pair);

            string hostId = _ctx.GetRoomHostUserId != null ? _ctx.GetRoomHostUserId.Invoke() : null;
            roster.Sort((a, b) => NeonSumoPlayerOrder.CompareHostFirst(a.Key, b.Key, hostId));

            if (roster.Count > MaxSlots)
                roster.RemoveRange(MaxSlots, roster.Count - MaxSlots);

            return roster;
        }

        private void ReassignSlots(List<KeyValuePair<string, NeonSumoPlayer>> roster)
        {
            _slotByUserId.Clear();

            for (int i = 0; i < roster.Count; i++)
                _slotByUserId[roster[i].Key] = i;
        }

        private void RenderRoster(List<KeyValuePair<string, NeonSumoPlayer>> roster)
        {
            _ctx.PlayerBar.SetPlayerCount(roster.Count);

            for (int i = 0; i < roster.Count; i++)
                RenderSlot(roster, i);
        }

        private void RenderSlot(List<KeyValuePair<string, NeonSumoPlayer>> roster, int i)
        {
            if (i < 0 || i >= roster.Count) return;

            string userId = roster[i].Key;
            NeonSumoPlayer player = roster[i].Value;

            var (hasColor, barColor) = _ctx.GetColor(userId);
            if (!hasColor && player != null)
            {
                barColor = player.CurrentBodyColor;
                hasColor = true;
            }

            _ctx.PlayerBar.SetSlot(i, new PlayerBarFusionModernView.PlayerBarData
            {
                Name = GetDisplayName(userId),
                Status = string.Empty,
                IsEliminated = player != null && player.IsEliminated,
                HasColor = hasColor,
                Color = barColor,
                HasPlacement = false,
                Placement = 0
            });
        }

        private string GetDisplayName(string userId)
        {
            if (string.IsNullOrEmpty(userId))
                return "-";

            if (_ctx.ResolveSlotDisplayName != null)
                return _ctx.ResolveSlotDisplayName.Invoke(userId);

            if (userId == _ctx.LocalPlayerId)
                return "YOU";

            int length = Math.Min(4, userId.Length);
            return userId.Substring(0, length).ToUpperInvariant();
        }
    }

    /// <summary>Context for PlayerBarCoordinator. GameManager populates this.</summary>
    public sealed class PlayerBarCoordinatorContext
    {
        public PlayerBarFusionModernView PlayerBar { get; }
        public Func<IReadOnlyDictionary<string, NeonSumoPlayer>> GetPlayers { get; }
        public string LocalPlayerId { get; }
        /// <summary>Returns (hasColor, color). Use tuple for delegate compatibility.</summary>
        public Func<string, (bool ok, Color color)> GetColor { get; }
        public Func<string> GetRoomHostUserId { get; }
        /// <summary>HUD bar label per peer Id (YOU / uppercase fallback / registry).</summary>
        public Func<string, string> ResolveSlotDisplayName { get; }

        public PlayerBarCoordinatorContext(
            PlayerBarFusionModernView playerBar,
            Func<IReadOnlyDictionary<string, NeonSumoPlayer>> getPlayers,
            string localPlayerId,
            Func<string, (bool ok, Color color)> getColor,
            Func<string> getRoomHostUserId,
            Func<string, string> resolveSlotDisplayName)
        {
            PlayerBar = playerBar;
            GetPlayers = getPlayers;
            LocalPlayerId = localPlayerId;
            GetColor = getColor;
            GetRoomHostUserId = getRoomHostUserId;
            ResolveSlotDisplayName = resolveSlotDisplayName;
        }
    }
}
