Main Menu Panel (UI Toolkit) – Scene setup
==========================================

The main menu is now implemented with UI Toolkit. To use it in NeonSumoScene:

1. Create a new GameObject (e.g. under Canvas or at root):
   - Name: "MainMenuPanelUIToolkit" (or any name).

2. Add components to this GameObject:
   - UIDocument
     - Source Asset: MainMenuPanel (Assets/UI/MainMenuPanel.uxml)
     - Panel Settings: use the same PanelSettings as your other UIDocument
       (e.g. Assets/UI Toolkit/PanelSettings.asset)
   - MainMenuPanelView (NeonSumo)
     - Player Row Template (optional): MainMenuPlayerRow (Assets/UI/MainMenuPlayerRow.uxml)
        If left empty, rows are built in code.

3. Wire the view to NeonSumoUIManager:
   - Select the Canvas (or the GameObject that has NeonSumoUIManager).
   - In the Inspector, set "Main Menu Panel View" to the GameObject that has
     MainMenuPanelView. If left empty, the manager will find it via
     FindObjectOfType<MainMenuPanelView>() at runtime.

4. Hide the old uGUI main menu (optional but recommended):
   - In the hierarchy, find the original "MainMenuPanel" (Canvas child with
     Ready/Restart/Quit buttons and player list).
   - Disable it so only the UI Toolkit menu is visible.

5. Panel sort order (if clicks go to the wrong UI):
   - In Panel Settings, adjust Sort Order so the menu draws above the Canvas
     when the menu is open.
   - Ensure the Canvas GraphicRaycaster is not blocking UI Toolkit input
     (e.g. disable Canvas or its raycast when showing the menu).

Architecture:
- NeonSumoUIManager delegates to UIFlowController (panel visibility), HudPresenter (timer),
  PlayerListPresenter (player list), and CountdownController (3-2-1-Go, winner animations).

Validation checklist:
- UXML names match: mainMenuRoot, connectionStatusText, winsCounterText,
  playerListContainer, readyButton, restartButton, quitButton.
- Events unsubscribed in NeonSumoUIManager.OnDestroy and in
  MainMenuPanelView.OnDestroy.
- Player rows cleared on restart (ResetUI calls ClearPlayerRows).
