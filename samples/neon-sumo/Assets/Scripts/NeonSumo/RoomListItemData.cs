namespace NeonSumo
{
    /// <summary>
    /// UI-facing view model for a room in the lobby list.
    /// Keeps presentation independent from backend Room schema.
    /// </summary>
    public class RoomListItemData
    {
        public string Id { get; set; }
        /// <summary>Optional; used with <see cref="NeonSumoLobbyService.OwnedRoomListHighlightId"/> when list id differs from game_session.</summary>
        public string GameSession { get; set; }
        public string DisplayName { get; set; }
        public int PlayerCount { get; set; }
        public int MaxPlayers { get; set; }
        public bool IsJoinable { get; set; }
        public string StatusText { get; set; }
    }
}
