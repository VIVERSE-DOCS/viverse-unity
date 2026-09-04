using UnityEngine;
using UnityEngine.UIElements;

namespace NeonSumo
{
    /// <summary>
    /// UI Toolkit view for the countdown panel (3, 2, 1, GO!).
    /// Exposes show/hide (via DisplayStyle), text, opacity, and scale for UIManager-driven animations.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public class CountdownPanelView : MonoBehaviour
    {
        private const string CountdownRootName = "countdownRoot";
        private const string CountdownLabelName = "countdownLabel";

        private VisualElement _countdownRoot;
        private Label _countdownLabel;

        /// <summary>Returns true if the view has valid references (root and label).</summary>
        public bool IsReady => _countdownRoot != null && _countdownLabel != null;

        private void Awake()
        {
            if (!TryBindElements())
                return;

            ApplyInitialState();
        }

        private bool TryBindElements()
        {
            var doc = GetComponent<UIDocument>();
            if (doc == null)
            {
                Debug.LogError("[CountdownPanelView] UIDocument not found on same GameObject.");
                return false;
            }

            var root = doc.rootVisualElement;
            if (root == null)
            {
                Debug.LogError("[CountdownPanelView] UIDocument.rootVisualElement is null.");
                return false;
            }

            _countdownRoot = root.Q<VisualElement>(CountdownRootName);
            _countdownLabel = root.Q<Label>(CountdownLabelName);

            if (_countdownRoot == null)
                Debug.LogError($"[CountdownPanelView] Could not find VisualElement '{CountdownRootName}'.");

            if (_countdownLabel == null)
                Debug.LogError($"[CountdownPanelView] Could not find Label '{CountdownLabelName}'.");

            return IsReady;
        }

        private void ApplyInitialState()
        {
            ResetHiddenState();
            if (_countdownLabel != null)
                _countdownLabel.style.scale = new Scale(Vector3.one);
        }

        /// <summary>Show or hide the panel using DisplayStyle (keeps UIDocument alive, avoids flicker).</summary>
        public void SetVisible(bool visible)
        {
            if (_countdownRoot == null) return;
            _countdownRoot.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
        }

        /// <summary>Set the countdown label text (e.g. "3", "2", "1", "GO!").</summary>
        public void SetCountdownText(string text)
        {
            if (_countdownLabel != null)
                _countdownLabel.text = text ?? string.Empty;
        }

        /// <summary>Set root opacity for fade in/out (0–1).</summary>
        public void SetOpacity(float opacity)
        {
            if (_countdownRoot == null) return;
            _countdownRoot.style.opacity = Mathf.Clamp01(opacity);
        }

        /// <summary>Get current root opacity (for fade animation).</summary>
        public float GetOpacity()
        {
            if (_countdownRoot == null) return 0f;
            return _countdownRoot.style.opacity.value;
        }

        /// <summary>Set countdown label scale for punch animation (e.g. 1.08f then back to 1f).</summary>
        public void SetCountdownScale(float scale)
        {
            if (_countdownLabel == null) return;
            _countdownLabel.style.scale = new Scale(Vector3.one * scale);
        }

        /// <summary>Set countdown label color (e.g. white for numbers, green/yellow for "GO!").</summary>
        public void SetCountdownColor(Color color)
        {
            if (_countdownLabel == null) return;
            _countdownLabel.style.color = new StyleColor(color);
        }

        /// <summary>Reset to hidden state (visible=false, opacity=0). Used at initialization.</summary>
        public void ResetHiddenState()
        {
            if (_countdownRoot != null)
            {
                _countdownRoot.style.display = DisplayStyle.None;
                _countdownRoot.style.opacity = 0f;
            }
        }
    }
}
