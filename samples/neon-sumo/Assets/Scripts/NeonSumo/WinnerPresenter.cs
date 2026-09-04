using UnityEngine;

namespace NeonSumo
{
    /// <summary>
    /// Parameter object for winner screen presentation.
    /// </summary>
    public readonly struct WinnerPresentation
    {
        public readonly string WinnerDisplayText;
        public readonly string SubtitleText;
        public readonly Color WinnerColor;
        public readonly bool ShowRestart;

        public WinnerPresentation(string winnerDisplayText, string subtitleText, Color winnerColor, bool showRestart)
        {
            WinnerDisplayText = winnerDisplayText;
            SubtitleText = subtitleText;
            WinnerColor = winnerColor;
            ShowRestart = showRestart;
        }
    }

    /// <summary>
    /// Owns winner panel configuration and initial animation state.
    /// Animation playback is delegated to CountdownController.
    /// </summary>
    public class WinnerPresenter
    {
        private readonly WinnerPanelView _view;
        private readonly float _slideDistance;

        public WinnerPresenter(WinnerPanelView view, float slideDistance)
        {
            _view = view;
            _slideDistance = slideDistance;
        }

        /// <summary>Configure winner panel and set initial animation state. Caller must then show overlay and play animation.</summary>
        public void Show(WinnerPresentation presentation)
        {
            if (_view == null || !_view.IsReady) return;

            _view.SetWinnerText(presentation.WinnerDisplayText);
            _view.SetSubtitleText(presentation.SubtitleText);
            _view.SetWinnerColor(presentation.WinnerColor);
            _view.SetRestartButtonVisible(presentation.ShowRestart);

            _view.SetContentTranslateY(_slideDistance);
            _view.SetContentOpacity(0f);
            _view.SetContentScale(0.9f);
        }
    }
}
