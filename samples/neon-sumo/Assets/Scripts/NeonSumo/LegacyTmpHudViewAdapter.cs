using UnityEngine;
using TMPro;

namespace NeonSumo
{
    /// <summary>Adapts legacy TMP_Text elements to IHudView.</summary>
    public sealed class LegacyTmpHudViewAdapter : IHudView
    {
        private readonly TMP_Text _timerText;
        private readonly TMP_Text _errorText;

        public LegacyTmpHudViewAdapter(TMP_Text timerText, TMP_Text errorText)
        {
            _timerText = timerText;
            _errorText = errorText;
        }

        public void SetTimerText(string text)
        {
            if (_timerText != null) _timerText.text = text;
        }

        public void SetTimerColor(Color color)
        {
            if (_timerText != null) _timerText.color = color;
        }

        public void SetTimerVisible(bool visible)
        {
            if (_timerText != null) _timerText.gameObject.SetActive(visible);
        }

        public void SetError(string message)
        {
            if (_errorText != null)
            {
                _errorText.text = message ?? string.Empty;
                _errorText.gameObject.SetActive(true);
            }
        }

        public void SetErrorVisible(bool visible)
        {
            if (_errorText != null) _errorText.gameObject.SetActive(visible);
        }
    }
}
