using System.Collections;
using UnityEngine;

namespace NeonSumo
{
    /// <summary>Timer colors and low-time threshold for <see cref="HudPresenter"/>.</summary>
    public readonly struct HudTimerStyle
    {
        public readonly Color NormalColor;
        public readonly Color LowTimeColor;
        public readonly int LowTimeThresholdSeconds;

        public HudTimerStyle(Color normalColor, Color lowTimeColor, int lowTimeThresholdSeconds)
        {
            NormalColor = normalColor;
            LowTimeColor = lowTimeColor;
            LowTimeThresholdSeconds = Mathf.Max(0, lowTimeThresholdSeconds);
        }
    }

    /// <summary>
    /// Owns timer and error display. Delegates rendering to IHudView (UI Toolkit or legacy TMP).
    /// APIs are idempotent: calling SetTimerSeconds twice with same value is safe.
    /// </summary>
    public class HudPresenter
    {
        private readonly ICoroutineRunner _runner;
        private readonly IHudView _view;
        private readonly HudTimerStyle _timerStyle;

        private Coroutine _errorHideRoutine;

        public HudPresenter(ICoroutineRunner runner, IHudView view, HudTimerStyle timerStyle)
        {
            _runner = runner;
            _view = view;
            _timerStyle = timerStyle;
        }

        /// <summary>Set timer to seconds (e.g. 60 = "1:00"). Color from <see cref="HudTimerStyle"/>. Shows timer when seconds &gt; 0.</summary>
        public void SetTimerSeconds(int seconds)
        {
            int clamped = Mathf.Max(0, seconds);
            string text = FormatTime(clamped);
            Color color = GetTimerColor(clamped);

            _view.SetTimerText(text);
            _view.SetTimerColor(color);

            if (clamped > 0)
                _view.SetTimerVisible(true);
        }

        /// <summary>Show or hide the timer. Idempotent.</summary>
        public void SetTimerVisible(bool visible) => _view.SetTimerVisible(visible);

        /// <summary>Set timer color (e.g. normal color for main menu).</summary>
        public void SetTimerColor(Color color) => _view.SetTimerColor(color);

        /// <summary>Show error message. Caller is responsible for scheduling HideError (e.g. after 3s).</summary>
        public void ShowError(string message) => _view.SetError(message ?? string.Empty);

        /// <summary>Hide and clear error. Idempotent.</summary>
        public void HideError()
        {
            _view.SetError(string.Empty);
            _view.SetErrorVisible(false);
        }

        /// <summary>Show error message and auto-hide after delay. Cancels any prior transient error.</summary>
        public void ShowTransientError(string message, float seconds = 3f)
        {
            if (_errorHideRoutine != null)
            {
                _runner.StopCoroutine(_errorHideRoutine);
                _errorHideRoutine = null;
            }

            ShowError(message);
            _errorHideRoutine = _runner.StartCoroutine(ErrorHideAfterDelay(seconds));
        }

        private IEnumerator ErrorHideAfterDelay(float delay)
        {
            yield return new WaitForSecondsRealtime(delay);
            _errorHideRoutine = null;
            HideError();
        }

        private static string FormatTime(int seconds)
        {
            int minutes = seconds / 60;
            int remaining = seconds % 60;
            return minutes.ToString() + ":" + remaining.ToString("00");
        }

        private Color GetTimerColor(int seconds) =>
            seconds <= _timerStyle.LowTimeThresholdSeconds ? _timerStyle.LowTimeColor : _timerStyle.NormalColor;
    }
}
