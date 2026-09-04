using System;
using System.Collections;
using UnityEngine;

namespace NeonSumo
{
    /// <summary>
    /// Owns countdown punch/fade coroutines, audio, and winner animation.
    /// Uses ICoroutineRunner; does not require MonoBehaviour.
    /// ShowStartingIn cancels any prior routine (idempotent).
    /// </summary>
    public class CountdownController
    {
        private static readonly Color CountdownGoColor = new Color(57f / 255f, 1f, 20f / 255f, 1f);
        private static readonly Color CountdownDefaultColor = Color.white;

        private readonly ICoroutineRunner _runner;
        private readonly CountdownPanelView _countdownView;
        private readonly WinnerPanelView _winnerView;
        private readonly AudioSource _audioSource;
        private readonly CountdownControllerConfig _config;

        private Coroutine _fadeRoutine;
        private Coroutine _punchRoutine;
        private Coroutine _hideCountdownRoutine;
        private Coroutine _winnerRoutine;

        public CountdownController(
            ICoroutineRunner runner,
            CountdownPanelView countdownView,
            WinnerPanelView winnerView,
            AudioSource audioSource,
            CountdownControllerConfig config)
        {
            _runner = runner;
            _countdownView = countdownView;
            _winnerView = winnerView;
            _audioSource = audioSource;
            _config = config ?? new CountdownControllerConfig();
        }

        public float GetReadyClipLength => _config.GetReadyClip != null ? _config.GetReadyClip.length : 2f;

        private bool IsCountdownReady() => _countdownView != null && _countdownView.IsReady;
        private bool IsWinnerReady() => _winnerView != null && _winnerView.IsReady;

        private void StopRoutine(ref Coroutine routine)
        {
            if (routine == null) return;
            _runner.StopCoroutine(routine);
            routine = null;
        }

        private void ShowCountdownMessage(string text, Color color, bool punch = false)
        {
            if (!IsCountdownReady()) return;

            _countdownView.SetVisible(true);
            _countdownView.SetCountdownText(text);
            _countdownView.SetCountdownColor(color);
            FadeTo(1f, null);

            if (punch)
                TriggerPunch();
        }

        private void ScheduleHideCountdown(float delay)
        {
            StopRoutine(ref _hideCountdownRoutine);
            _hideCountdownRoutine = _runner.StartCoroutine(DelayedHideCountdown(delay));
        }

        private void ResetCountdownVisualState()
        {
            if (_countdownView == null) return;
            _countdownView.SetVisible(false);
            _countdownView.SetCountdownColor(CountdownDefaultColor);
            _countdownView.SetCountdownScale(1f);
        }

        private AudioClip GetCountdownClip(int seconds) => seconds switch
        {
            3 => _config.ThreeClip,
            2 => _config.TwoClip,
            1 => _config.OneClip,
            0 => _config.GoClip,
            _ => null
        };

        /// <summary>
        /// Touches the countdown label with common intro/HUD strings so UI Toolkit layout and any shared font paths pay cost off the first visible frame.
        /// Does not change overlay state; restores hidden countdown panel when done.
        /// </summary>
        public void WarmupCountdownGlyphs()
        {
            if (!IsCountdownReady()) return;

            string[] warmupStrings =
            {
                "Get Ready", "READY", "GO!", "3", "2", "1",
                "PLAYER 1", "PLAYER 2", "YOU WIN"
            };
            foreach (var s in warmupStrings)
                _countdownView.SetCountdownText(s);

            ResetCountdownVisualState();
        }

        /// <summary>Show "Get Ready". Idempotent.</summary>
        public void ShowGetReady()
        {
            ShowCountdownMessage("Get Ready", CountdownDefaultColor, punch: true);
        }

        /// <summary>Show countdown number or "GO!". If seconds==0, schedules Hide after 1s.</summary>
        public void ShowCountdown(int seconds)
        {
            ShowCountdownMessage(
                seconds > 0 ? seconds.ToString() : "GO!",
                seconds > 0 ? CountdownDefaultColor : CountdownGoColor,
                punch: true);

            if (seconds <= 0)
                ScheduleHideCountdown(1f);
        }

        /// <summary>Show "Starting in X...". Cancels any prior StartingIn. Idempotent.</summary>
        public void ShowStartingIn(int seconds)
        {
            if (!IsCountdownReady()) return;

            CancelFade();
            ShowCountdownMessage($"Starting in {seconds}...", CountdownDefaultColor);
        }

        /// <summary>Update "Starting in X..." text.</summary>
        public void UpdateStartingIn(int seconds)
        {
            if (_countdownView != null)
                _countdownView.SetCountdownText($"Starting in {seconds}...");
        }

        /// <summary>Hide countdown overlay. Fades out then hides.</summary>
        public void HideStartingIn(Action onComplete = null)
        {
            FadeTo(0f, () =>
            {
                ResetCountdownVisualState();
                onComplete?.Invoke();
            });
        }

        /// <summary>Hide countdown (e.g. after GO!).</summary>
        public void HideCountdown()
        {
            FadeTo(0f, () => ResetCountdownVisualState());
        }

        public void PlayGetReadyAudio()
        {
            if (_audioSource != null && _config.GetReadyClip != null)
                _audioSource.PlayOneShot(_config.GetReadyClip);
        }

        public void PlayCountdownAudio(int seconds)
        {
            if (_audioSource == null) return;
            var clip = GetCountdownClip(seconds);
            if (clip != null)
                _audioSource.PlayOneShot(clip);
        }

        /// <summary>Play winner panel slide + fade animation.</summary>
        public void PlayWinnerAnimation()
        {
            if (!IsWinnerReady()) return;

            StopRoutine(ref _winnerRoutine);
            _winnerRoutine = _runner.StartCoroutine(AnimateWinnerRoutine());
        }

        /// <summary>Cancel all running coroutines (punch, fade, delayed hide, winner animation). Call when returning to menu.</summary>
        public void CancelAll()
        {
            StopRoutine(ref _fadeRoutine);
            StopRoutine(ref _punchRoutine);
            StopRoutine(ref _hideCountdownRoutine);
            StopRoutine(ref _winnerRoutine);
        }

        private void TriggerPunch()
        {
            if (!IsCountdownReady()) return;

            StopRoutine(ref _punchRoutine);
            _punchRoutine = _runner.StartCoroutine(PunchRoutine());
        }

        private void FadeTo(float targetAlpha, Action onComplete)
        {
            if (!IsCountdownReady())
            {
                onComplete?.Invoke();
                return;
            }

            StopRoutine(ref _fadeRoutine);
            _fadeRoutine = _runner.StartCoroutine(FadeRoutine(targetAlpha, onComplete));
        }

        private void CancelFade()
        {
            StopRoutine(ref _fadeRoutine);
        }

        private IEnumerator PunchRoutine()
        {
            if (_countdownView == null) yield break;

            float elapsed = 0f;
            const float baseScale = 1f;
            float punchDuration = _config.PunchDuration;
            float punchScale = _config.PunchScale;

            if (punchDuration <= 0f)
            {
                _countdownView.SetCountdownScale(baseScale);
                _punchRoutine = null;
                yield break;
            }

            while (elapsed < punchDuration)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(elapsed / punchDuration);
                float eased = t < 0.5f
                    ? Mathf.SmoothStep(0f, 1f, t * 2f)
                    : 1f - Mathf.SmoothStep(0f, 1f, (t - 0.5f) * 2f);
                float scale = Mathf.Lerp(baseScale, punchScale, eased);
                _countdownView.SetCountdownScale(scale);
                yield return null;
            }

            _countdownView.SetCountdownScale(baseScale);
            _punchRoutine = null;
        }

        private IEnumerator FadeRoutine(float targetAlpha, Action onComplete)
        {
            if (_countdownView == null)
            {
                onComplete?.Invoke();
                _fadeRoutine = null;
                yield break;
            }

            float startAlpha = _countdownView.GetOpacity();
            float elapsed = 0f;
            float fadeDuration = _config.FadeDuration;

            if (fadeDuration <= 0f)
            {
                _countdownView.SetOpacity(targetAlpha);
                onComplete?.Invoke();
                _fadeRoutine = null;
                yield break;
            }

            while (elapsed < fadeDuration)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(elapsed / fadeDuration);
                float eased = Mathf.SmoothStep(0f, 1f, t);
                _countdownView.SetOpacity(Mathf.Lerp(startAlpha, targetAlpha, eased));
                yield return null;
            }

            _countdownView.SetOpacity(targetAlpha);
            _fadeRoutine = null;
            onComplete?.Invoke();
        }

        private IEnumerator DelayedHideCountdown(float delay)
        {
            yield return new WaitForSecondsRealtime(delay);
            _hideCountdownRoutine = null;
            HideCountdown();
        }

        private IEnumerator AnimateWinnerRoutine()
        {
            if (_winnerView == null) yield break;

            float slideDistance = _config.WinnerSlideDistance;
            float slideDuration = _config.WinnerSlideDuration;
            float fadeDuration = _config.WinnerFadeDuration;

            float startY = slideDistance;
            float elapsed = 0f;

            while (elapsed < slideDuration)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(elapsed / slideDuration);
                float eased = Mathf.SmoothStep(0f, 1f, t);
                _winnerView.SetContentTranslateY(Mathf.Lerp(startY, 0f, eased));
                _winnerView.SetContentScale(Mathf.Lerp(0.9f, 1f, eased));
                yield return null;
            }

            _winnerView.SetContentTranslateY(0f);
            _winnerView.SetContentScale(1f);

            elapsed = 0f;
            while (elapsed < fadeDuration)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(elapsed / fadeDuration);
                float eased = Mathf.SmoothStep(0f, 1f, t);
                _winnerView.SetContentOpacity(eased);
                yield return null;
            }

            _winnerView.SetContentOpacity(1f);
            _winnerRoutine = null;
            _winnerView.FocusPrimaryAction();
        }
    }
}
