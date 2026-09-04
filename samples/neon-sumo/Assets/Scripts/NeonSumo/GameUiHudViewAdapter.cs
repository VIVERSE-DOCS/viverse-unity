using UnityEngine;

namespace NeonSumo
{
    /// <summary>Adapts GameUIPanelView to IHudView.</summary>
    public sealed class GameUiHudViewAdapter : IHudView
    {
        private readonly GameUIPanelView _view;

        public GameUiHudViewAdapter(GameUIPanelView view) => _view = view;

        public void SetTimerText(string text) => _view.SetTimerText(text);
        public void SetTimerColor(Color color) => _view.SetTimerColor(color);
        public void SetTimerVisible(bool visible) => _view.SetTimerVisible(visible);
        public void SetError(string message) => _view.SetError(message);
        public void SetErrorVisible(bool visible) => _view.SetErrorVisible(visible);
    }
}
