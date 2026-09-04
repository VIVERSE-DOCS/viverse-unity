Countdown Panel (UI Toolkit) – Scene setup
==========================================

The countdown panel (3, 2, 1, GO!) is now implemented with UI Toolkit.

1. Create a new GameObject (e.g. under Canvas or at root):
   - Name: "CountdownPanel" (or "CountdownPanelUIToolkit").

2. Add components to this GameObject:
   - UIDocument
     - Source Asset: CountdownPanel (Assets/UI/CountdownPanel.uxml)
     - Panel Settings: use the same PanelSettings as your other UI Toolkit panels
       (e.g. the one used by MainMenuPanel).
   - CountdownPanelView (NeonSumo)

3. Wire the view to NeonSumoUIManager:
   - Select the GameObject that has NeonSumoUIManager.
   - In the Inspector, set "Countdown Panel View" to the GameObject that has
     CountdownPanelView. If left empty, the manager will find it via
     FindObjectOfType<CountdownPanelView>() at runtime.

4. Remove or disable the old Canvas-based CountdownPanel:
   - The old CountdownPanel (GameObject with Canvas, CanvasGroup, and TMP_Text)
     is no longer used. Disable or delete it to avoid duplicate UI.

Validation:
- UXML names: countdownRoot, countdownLabel.
- Visibility uses DisplayStyle (no GameObject.SetActive) for smooth transitions.
- Fade and punch animations are driven by NeonSumoUIManager via the view's
  SetOpacity and SetCountdownScale.
