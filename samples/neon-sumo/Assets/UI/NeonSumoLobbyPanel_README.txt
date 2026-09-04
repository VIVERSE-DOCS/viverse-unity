NeonSumo Lobby Panel (UI Toolkit) – Scene setup
===============================================

The lobby is implemented with UI Toolkit. To use it in NeonSumoLobbyScene:

1. Create a new GameObject (e.g. at scene root):
   - Name: "LobbyUI" (or any name).

2. Add components to this GameObject:
   - UIDocument
     - Source Asset: NeonSumoLobbyPanel (Assets/UI/NeonSumoLobbyPanel.uxml)
     - Panel Settings: use the same PanelSettings as other UIDocuments
       (e.g. Assets/UI Toolkit/PanelSettings.asset)
   - NeonSumoLobbyPanelView (NeonSumo)

3. Wire the view to NeonSumoLobbyManager:
   - Select the GameObject that has NeonSumoLobbyManager.
   - In the Inspector, set "Lobby Panel View" to the GameObject that has
     NeonSumoLobbyPanelView.

4. Remove or disable the old Canvas lobby UI:
   - In the hierarchy, find the Canvas that contained LobbyTitle, RoomCodeLabel,
     RoomCodeInput, CreateJoinButton, EnterGameButton, StatusText.
   - Disable or delete it so only the UI Toolkit lobby is visible.

Configuration:
- NeonSumoLobbyManager uses NeonSumoConfig.AppId for matchmaking. Set AppId in the
  NeonSumoConfig asset inspector.

Validation checklist:
- UXML names: lobbyRoot, LobbyTitle, RoomCodeLabel, RoomCodeInput,
  ActorNameInput, CreateJoinButton, EnterGameButton, StatusText.
- Manager keeps all fallback/trim logic (e.g. GetRoomCode default);
  view only exposes raw getters/setters.
