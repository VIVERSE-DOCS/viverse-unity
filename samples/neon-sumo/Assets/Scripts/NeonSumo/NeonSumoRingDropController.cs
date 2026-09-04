using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace NeonSumo
{
    /// <summary>
    /// Pairs a stationary intact ring (for static batching / baked lighting) with a pre-placed inactive
    /// duplicate that animates only when the ring is eliminated. The logical ring root does not move;
    /// gameplay colliders live under <see cref="colliderRoot"/> and are disabled as soon as the drop starts.
    /// Neon Sumo expects <see cref="fallingRingRoot"/> to be assigned in the scene — no runtime Instantiate.
    /// </summary>
    /// <remarks>
    /// Suggested hierarchy: logical ring root (this component) → IntactVisual (mark mesh child Static for batching/GI),
    /// ColliderRoot (walk colliders), FallingRing (duplicate mesh, inactive, not Static, no Rigidbody).
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class NeonSumoRingDropController : MonoBehaviour
    {
        [Header("References (Neon Sumo)")]
        [Tooltip("Visible mesh/renderers while the ring is still in play. Mark this object Static for batching/GI, not necessarily the logical ring root.")]
        [SerializeField] private Transform intactVisualRoot;

        [Tooltip("Colliders that define walkable ring surface. Disabled immediately when the ring drops.")]
        [SerializeField] private Transform colliderRoot;

        [Tooltip("Pre-placed duplicate mesh; inactive until drop. Keep it non-static; no Rigidbody required.")]
        [SerializeField] private Transform fallingRingRoot;

        [Header("Motion (single model)")]
        [Tooltip("World-space direction along which the ring falls (normalized at runtime).")]
        [SerializeField] private Vector3 dropDirectionWorld = Vector3.down;

        [Tooltip("Total distance moved along drop direction over fall duration.")]
        [SerializeField] private float fallDistance = 20f;

        [Tooltip("Seconds to complete the fall translation.")]
        [SerializeField] private float fallDuration = 1.2f;

        [Tooltip("Degrees per second around rotation axis (world space).")]
        [SerializeField] private float rotationSpeedDegreesPerSecond = 90f;

        [Tooltip("World-space axis for spin during fall (normalized at runtime).")]
        [SerializeField] private Vector3 rotationAxisWorld = Vector3.up;

        [Tooltip("Seconds after the fall translation finishes before the falling object is hidden.")]
        [SerializeField] private float disableDelayAfterFallSeconds = 0f;

        private Vector3 _fallingInitialLocalPosition;
        private Quaternion _fallingInitialLocalRotation;
        private Vector3 _fallingInitialLocalScale;
        private Transform _fallingParent;

        private Coroutine _fallRoutine;
        private bool _dropped;
        private string _debugName;
        private readonly List<Collider> _colliderScratch = new List<Collider>();

        public bool IsDropped => _dropped;

        private void Awake()
        {
            _debugName = gameObject.name;
            CacheFallingInitialLocalPose();
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (dropDirectionWorld.sqrMagnitude > 1e-6f)
                dropDirectionWorld = dropDirectionWorld.normalized;
            else
                dropDirectionWorld = Vector3.down;

            if (rotationAxisWorld.sqrMagnitude > 1e-6f)
                rotationAxisWorld = rotationAxisWorld.normalized;
            else
                rotationAxisWorld = Vector3.up;
        }
#endif

        private void CacheFallingInitialLocalPose()
        {
            if (fallingRingRoot == null)
                return;

            _fallingParent = fallingRingRoot.parent;
            _fallingInitialLocalPosition = fallingRingRoot.localPosition;
            _fallingInitialLocalRotation = fallingRingRoot.localRotation;
            _fallingInitialLocalScale = fallingRingRoot.localScale;
        }

        /// <summary>Starts the drop: hides intact, disables colliders, shows and animates the pre-placed falling object.</summary>
        public void BeginDrop()
        {
            if (_dropped)
                return;

            if (fallingRingRoot == null)
            {
                DebugLogger.LogWarning("[NeonSumoRingDropController] " + _debugName + ": fallingRingRoot not assigned; cannot drop.");
                return;
            }

            if (intactVisualRoot == null)
            {
                DebugLogger.LogWarning("[NeonSumoRingDropController] " + _debugName + ": intactVisualRoot not assigned.");
                return;
            }

            _dropped = true;

            SetIntactVisible(false);
            SetColliderRootEnabled(false);

            CopyWorldPoseFromTo(intactVisualRoot, fallingRingRoot);
            fallingRingRoot.gameObject.SetActive(true);

            if (_fallRoutine != null)
            {
                StopCoroutine(_fallRoutine);
                _fallRoutine = null;
            }
            _fallRoutine = StartCoroutine(FallAndDespawnRoutine());
        }

        /// <summary>Restores arena state between rounds: stops motion, hides falling, restores intact and colliders.</summary>
        public void ResetForArena()
        {
            if (_fallRoutine != null)
            {
                StopCoroutine(_fallRoutine);
                _fallRoutine = null;
            }

            _dropped = false;

            if (fallingRingRoot != null)
            {
                fallingRingRoot.SetParent(_fallingParent, false);
                fallingRingRoot.localPosition = _fallingInitialLocalPosition;
                fallingRingRoot.localRotation = _fallingInitialLocalRotation;
                fallingRingRoot.localScale = _fallingInitialLocalScale;
                fallingRingRoot.gameObject.SetActive(false);
            }

            SetIntactVisible(true);
            SetColliderRootEnabled(true);
        }

        private IEnumerator FallAndDespawnRoutine()
        {
            Vector3 dir = dropDirectionWorld.sqrMagnitude > 1e-6f ? dropDirectionWorld.normalized : Vector3.down;
            Vector3 axis = rotationAxisWorld.sqrMagnitude > 1e-6f ? rotationAxisWorld.normalized : Vector3.up;

            Vector3 start = fallingRingRoot.position;
            Vector3 end = start + dir * Mathf.Max(0f, fallDistance);
            float duration = Mathf.Max(0.05f, fallDuration);
            float elapsed = 0f;

            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / duration);
                fallingRingRoot.position = Vector3.Lerp(start, end, t);
                fallingRingRoot.Rotate(axis, rotationSpeedDegreesPerSecond * Time.deltaTime, Space.World);
                yield return null;
            }

            fallingRingRoot.position = end;

            if (disableDelayAfterFallSeconds > 0f)
                yield return new WaitForSeconds(disableDelayAfterFallSeconds);

            if (fallingRingRoot != null)
                fallingRingRoot.gameObject.SetActive(false);

            _fallRoutine = null;
        }

        private static void CopyWorldPoseFromTo(Transform from, Transform to)
        {
            to.SetPositionAndRotation(from.position, from.rotation);
            MatchLossyScale(from, to);
        }

        /// <summary>Sets <paramref name="to"/>.localScale so its world scale matches <paramref name="from"/>.lossyScale.</summary>
        private static void MatchLossyScale(Transform from, Transform to)
        {
            Vector3 g = from.lossyScale;
            Transform p = to.parent;
            if (p == null)
            {
                to.localScale = g;
                return;
            }
            Vector3 pl = p.lossyScale;
            to.localScale = new Vector3(
                SafeDiv(g.x, pl.x),
                SafeDiv(g.y, pl.y),
                SafeDiv(g.z, pl.z));
        }

        private static float SafeDiv(float a, float b)
        {
            return Mathf.Approximately(b, 0f) ? a : a / b;
        }

        private void SetIntactVisible(bool visible)
        {
            if (intactVisualRoot != null)
                intactVisualRoot.gameObject.SetActive(visible);
        }

        private void SetColliderRootEnabled(bool enabled)
        {
            if (colliderRoot == null)
                return;
            _colliderScratch.Clear();
            colliderRoot.GetComponentsInChildren(true, _colliderScratch);
            int n = _colliderScratch.Count;
            for (int i = 0; i < n; i++)
            {
                Collider c = _colliderScratch[i];
                if (c != null)
                    c.enabled = enabled;
            }
        }
    }
}
