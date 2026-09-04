using UnityEngine;

namespace NeonSumo
{
    /// <summary>
    /// Configuration for CountdownController. Populate from Inspector or bootstrap.
    /// </summary>
    public sealed class CountdownControllerConfig
    {
        public AudioClip GetReadyClip;
        public AudioClip ThreeClip;
        public AudioClip TwoClip;
        public AudioClip OneClip;
        public AudioClip GoClip;
        public float PunchScale = 1.15f;
        public float PunchDuration = 0.2f;
        public float FadeDuration = 0.15f;
        public float WinnerFadeDuration = 0.2f;
        public float WinnerSlideDistance = 40f;
        public float WinnerSlideDuration = 0.25f;
    }
}
