using UnityEngine;

namespace NeonSumo
{
    /// <summary>
    /// Abstraction for HUD rendering. Hides UI Toolkit vs legacy TMP implementation.
    /// </summary>
    public interface IHudView
    {
        void SetTimerText(string text);
        void SetTimerColor(Color color);
        void SetTimerVisible(bool visible);
        void SetError(string message);
        void SetErrorVisible(bool visible);
    }
}
