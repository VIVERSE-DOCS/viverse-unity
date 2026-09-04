using UnityEngine;

namespace NeonSumo
{
    public enum UIBaseState
    {
        MainMenu,
        GameHUD
    }

    public enum UIOverlayState
    {
        None,
        Countdown,
        Winner
    }

    /// <summary>
    /// Owns visible UI composition from base state + overlay state.
    /// APIs are idempotent: calling SetBaseState/SetOverlay with same value is a no-op.
    /// </summary>
    public class UIFlowController
    {
        private readonly MainMenuPanelView _mainMenuView;
        private readonly GameUIPanelView _gameUIView;
        private readonly CountdownPanelView _countdownView;
        private readonly WinnerPanelView _winnerView;
        private readonly GameObject _legacyGameUIPanel;

        private UIBaseState _baseState;
        private UIOverlayState _overlayState;

        public UIFlowController(
            MainMenuPanelView mainMenuView,
            GameUIPanelView gameUIView,
            CountdownPanelView countdownView,
            WinnerPanelView winnerView,
            GameObject legacyGameUIPanel)
        {
            _mainMenuView = mainMenuView;
            _gameUIView = gameUIView;
            _countdownView = countdownView;
            _winnerView = winnerView;
            _legacyGameUIPanel = legacyGameUIPanel;

            if (gameUIView == null && legacyGameUIPanel == null)
                Debug.LogWarning("[UIFlowController] Created without any game UI target.");
        }

        /// <summary>Initialize flow state and reset overlay views to hidden. Call once at startup.</summary>
        public void Initialize()
        {
            _countdownView?.ResetHiddenState();
            _winnerView?.ResetHiddenState();

            _baseState = UIBaseState.MainMenu;
            _overlayState = UIOverlayState.None;
            ApplyState();
        }

        /// <summary>Set base screen (MainMenu or GameHUD). Idempotent.</summary>
        public void SetBaseState(UIBaseState state)
        {
            if (_baseState == state)
                return;

            _baseState = state;
            ApplyState();
        }

        /// <summary>Set overlay (None, Countdown, or Winner). Idempotent.</summary>
        public void SetOverlay(UIOverlayState overlay)
        {
            if (_overlayState == overlay)
                return;

            _overlayState = overlay;
            ApplyState();
        }

        /// <summary>Clear overlay to None.</summary>
        public void ClearOverlay()
        {
            SetOverlay(UIOverlayState.None);
        }

        private void ApplyState()
        {
            SetMainMenuVisible(_baseState == UIBaseState.MainMenu);
            SetGameUIVisible(true);
            SetCountdownVisible(_overlayState == UIOverlayState.Countdown);
            SetWinnerVisible(_overlayState == UIOverlayState.Winner);
        }

        private void SetMainMenuVisible(bool visible) => _mainMenuView?.SetVisible(visible);
        private void SetCountdownVisible(bool visible) => _countdownView?.SetVisible(visible);
        private void SetWinnerVisible(bool visible) => _winnerView?.SetVisible(visible);

        private void SetGameUIVisible(bool visible)
        {
            if (_gameUIView != null)
                _gameUIView.SetVisible(visible);
            else
                _legacyGameUIPanel?.SetActive(visible);
        }
    }
}
