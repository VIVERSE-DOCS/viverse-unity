using UnityEngine;

namespace NeonSumo
{
    /// <summary>No-op IHudView when no UI target is available. Prevents null checks in presenter.</summary>
    public sealed class NoOpHudViewAdapter : IHudView
    {
        public void SetTimerText(string text) { }
        public void SetTimerColor(Color color) { }
        public void SetTimerVisible(bool visible) { }
        public void SetError(string message) { }
        public void SetErrorVisible(bool visible) { }
    }
}
