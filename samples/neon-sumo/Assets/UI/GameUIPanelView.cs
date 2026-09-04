using UnityEngine;
using UnityEngine.UIElements;

namespace NeonSumo
{
    /// <summary>
    /// UI Toolkit view for the game HUD (timer, optional error).
    /// Visibility via DisplayStyle to avoid layout/event issues. Root uses picking-mode: ignore.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public class GameUIPanelView : MonoBehaviour
    {
        private const string RootName = "gameUIRoot";
        private const string TimerLabelName = "timerLabel";
        private const string ErrorLabelName = "errorLabel";

        private VisualElement _root;
        private Label _timerLabel;
        private Label _errorLabel;

        private void Awake()
        {
            if (!TryCacheReferences())
                return;

            SetDisplay(_root, false);
            SetDisplay(_errorLabel, false);
        }

        private bool TryCacheReferences()
        {
            var doc = GetComponent<UIDocument>();
            if (doc == null)
            {
                Debug.LogError("[GameUIPanelView] UIDocument not found on same GameObject.", this);
                return false;
            }

            var docRoot = doc.rootVisualElement;
            if (docRoot == null)
            {
                Debug.LogError("[GameUIPanelView] rootVisualElement is null.", this);
                return false;
            }

            _root = docRoot.Q<VisualElement>(RootName);
            _timerLabel = _root?.Q<Label>(TimerLabelName);
            _errorLabel = _root?.Q<Label>(ErrorLabelName);

            if (_root == null)
                Debug.LogWarning($"[GameUIPanelView] Could not find root '{RootName}'.", this);
            if (_timerLabel == null)
                Debug.LogWarning($"[GameUIPanelView] Could not find timer label '{TimerLabelName}'.", this);
            if (_errorLabel == null)
                Debug.LogWarning($"[GameUIPanelView] Could not find error label '{ErrorLabelName}'.", this);

            return true;
        }

        private static void SetDisplay(VisualElement element, bool visible)
        {
            if (element == null) return;
            element.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
        }

        /// <summary>Show or hide the panel using DisplayStyle (keeps UIDocument alive).</summary>
        public void SetVisible(bool visible)
        {
            SetDisplay(_root, visible);
        }

        /// <summary>Set timer text (e.g. "1:30").</summary>
        public void SetTimerText(string text)
        {
            if (_timerLabel == null) return;
            _timerLabel.text = text ?? string.Empty;
        }

        /// <summary>Set timer color (e.g. orange normal, red when low).</summary>
        public void SetTimerColor(Color color)
        {
            if (_timerLabel == null) return;
            _timerLabel.style.color = color;
        }

        /// <summary>Show or hide the timer within the panel.</summary>
        public void SetTimerVisible(bool visible)
        {
            SetDisplay(_timerLabel, visible);
        }

        /// <summary>Set error message and show the error label.</summary>
        public void SetError(string message)
        {
            if (_errorLabel == null) return;
            _errorLabel.text = message ?? string.Empty;
            SetDisplay(_errorLabel, !string.IsNullOrEmpty(message));
        }

        /// <summary>Hide the error label.</summary>
        public void SetErrorVisible(bool visible)
        {
            SetDisplay(_errorLabel, visible);
        }

        public bool IsReady => _root != null && _timerLabel != null;
    }
}
