using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace NeonSumo
{
    /// <summary>
    /// UI Toolkit view for the round-over winner panel.
    /// Exposes show/hide, winner/subtitle text, and animation knobs (content translate Y, opacity, scale).
    /// Root picking is set in code: Position when visible (so buttons work), Ignore when hidden.
    /// </summary>
    public class WinnerPanelView : MonoBehaviour
    {
        public event Action RestartClicked;
        public event Action QuitClicked;

        private const string RootName = "winnerPanelRoot";
        private const string ContentName = "winnerPanelContent";
        private const string WinnerLabelName = "winnerLabel";
        private const string SubtitleLabelName = "subtitleLabel";
        private const string RestartButtonName = "restartButton";
        private const string QuitButtonName = "quitButton";
        private const string WinnerActionFocusedClass = "winner-action-button--focused";

        private VisualElement _root;
        private VisualElement _content;
        private Label _winnerLabel;
        private Label _subtitleLabel;
        private Button _restartButton;
        private Button _quitButton;

        private bool _isRestartButtonVisible = true;

        public bool IsReady =>
            _root != null &&
            _content != null &&
            _winnerLabel != null &&
            _subtitleLabel != null &&
            _restartButton != null &&
            _quitButton != null;

        private void Awake()
        {
            if (!TryCacheElements())
                return;

            ApplyInitialState();
            BindEvents();
        }

        private void OnDestroy()
        {
            UnbindEvents();
        }

        private bool TryCacheElements()
        {
            var doc = GetComponent<UIDocument>();
            if (doc == null)
            {
                Debug.LogError("[WinnerPanelView] UIDocument not found on same GameObject.");
                return false;
            }

            var docRoot = doc.rootVisualElement;
            if (docRoot == null)
            {
                Debug.LogError("[WinnerPanelView] rootVisualElement is null.");
                return false;
            }

            _root = QueryOrWarn<VisualElement>(docRoot, RootName);
            _content = QueryOrWarn<VisualElement>(_root, ContentName);
            _winnerLabel = QueryOrWarn<Label>(_content, WinnerLabelName);
            _subtitleLabel = QueryOrWarn<Label>(_content, SubtitleLabelName);
            _restartButton = QueryOrWarn<Button>(_content, RestartButtonName);
            _quitButton = QueryOrWarn<Button>(_content, QuitButtonName);

            return IsReady;
        }

        private void ApplyInitialState()
        {
            _root.pickingMode = PickingMode.Ignore;
            _root.style.display = DisplayStyle.None;
            _root.style.opacity = 1f;

            _content.pickingMode = PickingMode.Position;
            _content.focusable = false;

            ResetVisualState();
        }

        private void BindEvents()
        {
            _restartButton.clicked += OnRestartClicked;
            _quitButton.clicked += OnQuitClicked;
            RegisterWinnerActionFocusRing(_restartButton);
            RegisterWinnerActionFocusRing(_quitButton);
        }

        private void UnbindEvents()
        {
            UnregisterWinnerActionFocusRing(_restartButton);
            UnregisterWinnerActionFocusRing(_quitButton);
            if (_restartButton != null)
                _restartButton.clicked -= OnRestartClicked;
            if (_quitButton != null)
                _quitButton.clicked -= OnQuitClicked;
        }

        private static void RegisterWinnerActionFocusRing(Button button)
        {
            if (button == null) return;
            button.RegisterCallback<FocusInEvent>(OnWinnerActionFocusIn);
            button.RegisterCallback<FocusOutEvent>(OnWinnerActionFocusOut);
        }

        private static void UnregisterWinnerActionFocusRing(Button button)
        {
            if (button == null) return;
            button.UnregisterCallback<FocusInEvent>(OnWinnerActionFocusIn);
            button.UnregisterCallback<FocusOutEvent>(OnWinnerActionFocusOut);
        }

        private static void OnWinnerActionFocusIn(FocusInEvent evt)
        {
            if (evt.target is Button btn)
                btn.AddToClassList(WinnerActionFocusedClass);
        }

        private static void OnWinnerActionFocusOut(FocusOutEvent evt)
        {
            if (evt.target is Button btn)
                btn.RemoveFromClassList(WinnerActionFocusedClass);
        }

        private void OnRestartClicked() => RestartClicked?.Invoke();
        private void OnQuitClicked() => QuitClicked?.Invoke();

        private static string SafeText(string text) => text ?? string.Empty;

        private T QueryOrWarn<T>(VisualElement parent, string name) where T : VisualElement
        {
            var element = parent?.Q<T>(name);
            if (element == null)
                Debug.LogWarning($"[WinnerPanelView] Element '{name}' not found.");
            return element;
        }

        /// <summary>Show or hide the panel. When visible, root is pickable so buttons receive clicks.</summary>
        public void SetVisible(bool visible)
        {
            if (_root == null) return;
            _root.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
            _root.pickingMode = visible ? PickingMode.Position : PickingMode.Ignore;
        }

        /// <summary>Show or hide the Play Again button (e.g. only for host).</summary>
        public void SetRestartButtonVisible(bool visible)
        {
            _isRestartButtonVisible = visible;
            if (_restartButton == null) return;
            _restartButton.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
        }

        /// <summary>Set the winner name (e.g. player name or "Draw!").</summary>
        public void SetWinnerText(string text)
        {
            if (_winnerLabel != null)
                _winnerLabel.text = SafeText(text);
        }

        /// <summary>Set the winner name color (e.g. player color).</summary>
        public void SetWinnerColor(Color color)
        {
            if (_winnerLabel != null)
                _winnerLabel.style.color = color;
        }

        /// <summary>Set the subtitle (e.g. "Wins!", "Round Over", or blank).</summary>
        public void SetSubtitleText(string text)
        {
            if (_subtitleLabel != null)
                _subtitleLabel.text = SafeText(text);
        }

        /// <summary>Set content container Y offset in pixels (for slide animation). Rest position is 0.</summary>
        public void SetContentTranslateY(float yPx)
        {
            if (_content == null) return;
            _content.style.translate = new Translate(0, new Length(yPx, LengthUnit.Pixel), 0);
        }

        /// <summary>Set content container opacity (0–1) for fade animation.</summary>
        public void SetContentOpacity(float opacity)
        {
            if (_content == null) return;
            _content.style.opacity = Mathf.Clamp01(opacity);
        }

        /// <summary>Set content container scale (for scale-in animation).</summary>
        public void SetContentScale(float scale)
        {
            if (_content == null) return;
            float s = Mathf.Max(0.01f, scale);
            _content.style.scale = new Scale(new Vector3(s, s, 1f));
        }

        /// <summary>Set content opacity, translate Y, and scale in one call (convenience for animation setup).</summary>
        public void SetContentVisuals(float opacity, float translateYPx, float scale)
        {
            SetContentOpacity(opacity);
            SetContentTranslateY(translateYPx);
            SetContentScale(scale);
        }

        /// <summary>Focus the primary action button (Play Again if visible, otherwise Quit) for keyboard/controller.</summary>
        public void FocusPrimaryAction()
        {
            if (_isRestartButtonVisible)
                _restartButton?.Focus();
            else
                _quitButton?.Focus();
        }

        /// <summary>Reset content visual state (opacity 0, translate 0, scale 1). Used before animation or at init.</summary>
        public void ResetVisualState()
        {
            if (_content == null) return;
            _content.style.opacity = 0f;
            _content.style.translate = new Translate(0, new Length(0, LengthUnit.Pixel), 0);
            _content.style.scale = new Scale(Vector3.one);
        }

        /// <summary>Reset to hidden state (visible=false, content opacity 0, translate 0, scale 1). Used at initialization.</summary>
        public void ResetHiddenState()
        {
            SetVisible(false);
            ResetVisualState();
        }
    }
}
