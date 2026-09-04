Winner Panel (UI Toolkit) – Scene setup
========================================

The round-over winner panel is now implemented with UI Toolkit.

1. Create a new GameObject (e.g. under Canvas or at root):
   - Name: "WinnerPanel" (or "WinnerPanelUIToolkit").

2. Add components to this GameObject:
   - UIDocument
     - Source Asset: WinnerPanel (Assets/UI/WinnerPanel.uxml)
     - Panel Settings: use the same PanelSettings as your other UI Toolkit panels.
     - Sort Order: set higher than Game UI / Main Menu if the winner panel should draw on top.
   - WinnerPanelView (NeonSumo)

3. Wire the view to NeonSumoUIManager:
   - Select the GameObject that has NeonSumoUIManager.
   - In the Inspector, assign "Winner Panel View" to the GameObject that has WinnerPanelView.
   - This field is required; the manager will log an error and disable if it is missing.

4. Remove or disable the old Canvas-based WinnerPanel:
   - The old WinnerPanel (GameObject with CanvasGroup, RectTransform, and TMP_Text for winner/wins)
     is no longer used. Disable or delete it to avoid duplicate UI.

Architecture:
- NeonSumoUIManager delegates to CountdownController for winner panel animations.
- Slide and fade are driven via SetContentTranslateY and SetContentOpacity.

Validation:
- UXML names: winnerPanelRoot, winnerPanelContent, winnerLabel, subtitleLabel.
- Root uses picking-mode: ignore so it does not block other UI.
- Slide and fade animations are driven by NeonSumoUIManager via SetContentTranslateY and SetContentOpacity.
- Initial animation state is set before SetVisible(true) to avoid a one-frame pop.
