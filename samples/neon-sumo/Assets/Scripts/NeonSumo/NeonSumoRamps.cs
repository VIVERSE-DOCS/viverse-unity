using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace NeonSumo
{
    public class NeonSumoRamps : MonoBehaviour
    {
        [Header("Ramp Retract")]
        [Tooltip("Optional explicit ramp parents. If empty, direct children are used.")]
        public Transform[] rampParents;
        [Tooltip("World-units to pull ramps away from arena center (procedural retract only).")]
        public float retractDistance = 6f;
        [Tooltip("Seconds to complete retract. Procedural: lerp length. Animator: wait before disabling colliders (match clip length).")]
        public float retractDuration = 1.2f;
        [Tooltip("Curve used to animate retraction (procedural only).")]
        public AnimationCurve retractCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
        [Tooltip("Disable colliders after ramps retract.")]
        public bool disableCollidersOnRetract = true;

        [Header("Animator retract")]
        [Tooltip("If enabled, each ramp plays its Animator retract instead of sliding. Falls back to procedural if no Animator is found.")]
        public bool useAnimatorRetract = true;
        [Tooltip("Optional Animators (same order as ramp roots). Empty = GetComponentInChildren per ramp.")]
        public Animator[] rampAnimators;
        [Tooltip("Trigger parameter on the Animator Controller.")]
        public string retractAnimatorTrigger = "Retract";

        private readonly List<RampState> _ramps = new List<RampState>();
        private readonly List<Transform> _transformBuffer = new List<Transform>();
        private Coroutine _retractRoutine;
        private bool _isRetracting;
        private bool _isRetracted;

        public bool IsRetracted => _isRetracted;
        public bool IsRetracting => _isRetracting;

        /// <summary>Fired when ramp retraction animation completes.</summary>
        public event Action OnRetracted;

        private void Awake()
        {
            RebuildRampCache();
        }

        private void OnValidate()
        {
            retractDistance = Mathf.Max(0f, retractDistance);
            retractDuration = Mathf.Max(0.05f, retractDuration);
        }

        [ContextMenu("Rebuild Ramp Cache")]
        private void EditorRebuildRampCache() => RebuildRampCache();

        public void ResetRamps()
        {
            EnsureRampCache();
            if (_retractRoutine != null)
            {
                StopCoroutine(_retractRoutine);
                _retractRoutine = null;
            }
            _isRetracting = false;

            foreach (var ramp in _ramps)
            {
                if (ramp.Transform == null) continue;
                ramp.Transform.position = ramp.StartPosition;
                if (ramp.Animator != null)
                {
                    ramp.Animator.Rebind();
                    ramp.Animator.Update(0f);
                }
                if (disableCollidersOnRetract)
                {
                    SetCollidersEnabled(ramp.Colliders, true);
                }
            }

            _isRetracted = false;
        }

        public void RetractRamps(Vector3 arenaCenter)
        {
            if (_isRetracted || _isRetracting)
            {
                return;
            }

            EnsureRampCache();
            if (_ramps.Count == 0)
            {
                return;
            }

            bool procedural = ShouldUseProceduralRetract();
            if (procedural)
            {
                ComputeTargetPositions(arenaCenter);
            }

            if (_retractRoutine != null)
            {
                StopCoroutine(_retractRoutine);
            }
            _retractRoutine = StartCoroutine(RetractRoutine(procedural));
        }

        private bool ShouldUseProceduralRetract()
        {
            if (!useAnimatorRetract)
            {
                return true;
            }

            for (int i = 0; i < _ramps.Count; i++)
            {
                if (_ramps[i].Animator != null)
                {
                    return false;
                }
            }

            Debug.LogWarning(
                "[NeonSumo] useAnimatorRetract is on but no Animators were resolved; using procedural retract.",
                this);
            return true;
        }

        private void EnsureRampCache()
        {
            CollectRampTransforms(_transformBuffer);
            if (!RampTransformsChanged(_transformBuffer))
            {
                return;
            }
            RebuildRampCache();
        }

        private void RebuildRampCache()
        {
            CollectRampTransforms(_transformBuffer);
            _ramps.Clear();
            for (int i = 0; i < _transformBuffer.Count; i++)
            {
                var t = _transformBuffer[i];
                if (t == null) continue;
                var state = new RampState
                {
                    Transform = t,
                    StartPosition = t.position,
                    TargetPosition = t.position,
                    Colliders = t.GetComponentsInChildren<Collider>(),
                    Animator = ResolveAnimator(i, t)
                };
                _ramps.Add(state);
            }
        }

        private Animator ResolveAnimator(int index, Transform rampRoot)
        {
            if (rampAnimators != null && index < rampAnimators.Length && rampAnimators[index] != null)
            {
                return rampAnimators[index];
            }

            return rampRoot.GetComponentInChildren<Animator>(true);
        }

        private void CollectRampTransforms(List<Transform> output)
        {
            output.Clear();
            if (rampParents != null && rampParents.Length > 0)
            {
                foreach (var ramp in rampParents)
                {
                    if (ramp == null) continue;
                    output.Add(ramp);
                }
            }
            else
            {
                for (int i = 0; i < transform.childCount; i++)
                {
                    var child = transform.GetChild(i);
                    if (child == null) continue;
                    output.Add(child);
                }
            }
        }

        private bool RampTransformsChanged(List<Transform> found)
        {
            if (found.Count != _ramps.Count) return true;
            for (int i = 0; i < found.Count; i++)
            {
                if (found[i] != _ramps[i].Transform) return true;
            }
            return false;
        }

        private void ComputeTargetPositions(Vector3 arenaCenter)
        {
            for (int i = 0; i < _ramps.Count; i++)
            {
                var ramp = _ramps[i];
                if (ramp.Transform == null)
                {
                    ramp.TargetPosition = Vector3.zero;
                    continue;
                }

                Vector3 start = ramp.StartPosition;
                Vector3 direction = start - arenaCenter;
                if (direction.sqrMagnitude < 0.001f)
                {
                    direction = ramp.Transform.forward;
                }
                direction.Normalize();
                ramp.TargetPosition = start + direction * Mathf.Max(0f, retractDistance);
            }
        }

        private IEnumerator RetractRoutine(bool procedural)
        {
            _isRetracting = true;
            float duration = Mathf.Max(0.05f, retractDuration);

            if (!procedural)
            {
                foreach (var ramp in _ramps)
                {
                    if (ramp.Animator == null) continue;
                    if (!string.IsNullOrEmpty(retractAnimatorTrigger))
                    {
                        ramp.Animator.ResetTrigger(retractAnimatorTrigger);
                        ramp.Animator.SetTrigger(retractAnimatorTrigger);
                    }
                }

                yield return new WaitForSeconds(duration);
            }
            else
            {
                float elapsed = 0f;
                while (elapsed < duration)
                {
                    elapsed += Time.deltaTime;
                    float t = Mathf.Clamp01(elapsed / duration);
                    float eased = retractCurve != null ? retractCurve.Evaluate(t) : t;
                    foreach (var ramp in _ramps)
                    {
                        if (ramp.Transform == null) continue;
                        ramp.Transform.position = Vector3.Lerp(ramp.StartPosition, ramp.TargetPosition, eased);
                    }
                    yield return null;
                }

                foreach (var ramp in _ramps)
                {
                    if (ramp.Transform == null) continue;
                    ramp.Transform.position = ramp.TargetPosition;
                }
            }

            foreach (var ramp in _ramps)
            {
                if (ramp.Transform == null) continue;
                if (disableCollidersOnRetract)
                {
                    SetCollidersEnabled(ramp.Colliders, false);
                }
            }

            _isRetracted = true;
            _isRetracting = false;
            _retractRoutine = null;
            OnRetracted?.Invoke();
        }

        private static void SetCollidersEnabled(Collider[] colliders, bool enabled)
        {
            if (colliders == null) return;
            foreach (var c in colliders)
            {
                if (c != null) c.enabled = enabled;
            }
        }

        [Serializable]
        private class RampState
        {
            public Transform Transform;
            public Vector3 StartPosition;
            public Vector3 TargetPosition;
            public Collider[] Colliders;
            public Animator Animator;
        }
    }
}
