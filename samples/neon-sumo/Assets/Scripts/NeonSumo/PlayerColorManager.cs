using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace NeonSumo
{
    /// <summary>
    /// Owns player color assignments and palette lookup.
    /// Side effects (player/UI updates, network broadcast) are explicit.
    /// </summary>
    public class PlayerColorManager
    {
        private const string UserIdKey = "user_id";
        private const string ColorIndexKey = "color_index";

        public const int PaletteSize = 4;

        private readonly PlayerColorContext _ctx;
        private readonly Dictionary<string, int> _assignments = new Dictionary<string, int>();

        // UI / name colors aligned to body material order: Blue, Green, Red, Yellow (color_index 0..3)
        private static readonly Color[] Palette = new[]
        {
            new Color(0.35f, 0.65f, 1f),    // PlasmaBarsBlue
            new Color(0.4f, 1f, 0.45f),     // PlasmaBarsGreen
            new Color(1f, 0.35f, 0.35f),    // PlasmaBarsRed
            new Color(1f, 0.92f, 0.35f),   // PlasmaBarsYellow
        };

        public PlayerColorManager(PlayerColorContext ctx)
        {
            _ctx = ctx ?? throw new ArgumentNullException(nameof(ctx));
        }

        public bool HasAssignment(string userId)
        {
            return NeonSumoUserId.TryNormalize(userId, out userId) && _assignments.ContainsKey(userId);
        }

        public int GetOrAssignColorIndex(string userId)
        {
            if (!NeonSumoUserId.TryNormalize(userId, out userId) || Palette.Length == 0)
                return 0;

            if (_assignments.TryGetValue(userId, out int existing))
                return existing;

            int nextIndex = _assignments.Count % Palette.Length;
            _assignments[userId] = nextIndex;
            return nextIndex;
        }

        public void SetAssignment(string userId, int colorIndex)
        {
            if (!NeonSumoUserId.TryNormalize(userId, out userId) || Palette.Length == 0)
                return;

            _assignments[userId] = NormalizeColorIndex(colorIndex);
        }

        public bool TryGetColorIndex(string userId, out int colorIndex)
        {
            colorIndex = 0;
            return NeonSumoUserId.TryNormalize(userId, out userId) && _assignments.TryGetValue(userId, out colorIndex);
        }

        public bool TryGetColor(string userId, out Color color)
        {
            color = Color.white;

            if (!TryGetColorIndex(userId, out int colorIndex) || Palette.Length == 0)
                return false;

            color = GetPaletteColor(colorIndex);
            return true;
        }

        public void ApplyAssignedColor(string userId)
        {
            if (string.IsNullOrEmpty(userId))
                return;

            if (!TryGetColorIndex(userId, out int colorIndex))
                return;

            if (!TryGetColor(userId, out Color color))
                return;

            var player = _ctx.GetPlayer?.Invoke(userId);
            player?.ApplyBodyMaterial(colorIndex, color);

            _ctx.SetPlayerColorInUI?.Invoke(userId, color);
        }

        public void RefreshIndicators()
        {
            _ctx.RefreshPlayerIndicators?.Invoke();
        }

        public void BroadcastColorAssignment(string userId)
        {
            if (!TryGetColorIndex(userId, out int colorIndex))
                return;

            userId = NeonSumoUserId.Normalize(userId);
            _ctx.Broadcast?.Invoke(NeonSumoNetEvents.PlayerColor, new JObject
            {
                [UserIdKey] = userId,
                [ColorIndexKey] = colorIndex
            });
        }

        public void BroadcastAllAssignments()
        {
            foreach (var userId in _assignments.Keys)
                BroadcastColorAssignment(userId);
        }

        private static int NormalizeColorIndex(int colorIndex)
        {
            if (Palette.Length == 0)
                return 0;

            return ((colorIndex % Palette.Length) + Palette.Length) % Palette.Length;
        }

        private static Color GetPaletteColor(int colorIndex)
        {
            return Palette[NormalizeColorIndex(colorIndex)];
        }
    }

    /// <summary>Context for PlayerColorManager. GameManager populates this.</summary>
    public sealed class PlayerColorContext
    {
        public Func<string, NeonSumoPlayer> GetPlayer { get; }
        public Action<string, Color> SetPlayerColorInUI { get; }
        public Action RefreshPlayerIndicators { get; }
        /// <summary>Broadcast(actionName, payload). Payload is serialized with Json.NET (e.g. <see cref="JObject"/>).</summary>
        public Action<string, object> Broadcast { get; }

        public PlayerColorContext(
            Func<string, NeonSumoPlayer> getPlayer,
            Action<string, Color> setPlayerColorInUI,
            Action refreshPlayerIndicators,
            Action<string, object> broadcast)
        {
            GetPlayer = getPlayer;
            SetPlayerColorInUI = setPlayerColorInUI;
            RefreshPlayerIndicators = refreshPlayerIndicators;
            Broadcast = broadcast;
        }
    }
}
