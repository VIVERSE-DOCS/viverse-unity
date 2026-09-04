using System;
using System.Collections.Generic;
using UnityEngine;

namespace NeonSumo
{
    /// <summary>
    /// Host-side CPU steering: survival-first blend toward center vs chase, gated boost intent.
    /// </summary>
    public sealed class NeonSumoCpuSumoBrain
    {
        private const float BoostEdgeSuppressThreshold = 0.45f;

        private readonly Func<IEnumerable<NeonSumoPlayer>> _getPlayers;
        private readonly float _edgeStartFraction;
        private readonly float _boostDotThreshold;
        private readonly float _boostRange;
        private readonly float _minBoostIntentInterval;
        private readonly Dictionary<string, float> _lastBoostIntentTimeByUserId = new Dictionary<string, float>();

        public NeonSumoCpuSumoBrain(
            Func<IEnumerable<NeonSumoPlayer>> getPlayers,
            float edgeStartFraction = 0.6f,
            float boostDotThreshold = 0.88f,
            float boostRange = 4f,
            float minBoostIntentInterval = 0.35f)
        {
            _getPlayers = getPlayers ?? throw new ArgumentNullException(nameof(getPlayers));
            _edgeStartFraction = edgeStartFraction;
            _boostDotThreshold = boostDotThreshold;
            _boostRange = boostRange;
            _minBoostIntentInterval = minBoostIntentInterval;
        }

        public (Vector2 move, bool boost) Compute(NeonSumoPlayer self)
        {
            if (self == null || self.IsEliminated)
            {
                return (Vector2.zero, false);
            }

            NeonSumoArena arena = self.Arena;
            if (arena == null)
            {
                return (Vector2.zero, false);
            }

            Vector3 myPos = self.transform.position;
            Vector3 center = arena.transform.position;
            Vector3 toCenter = Vector3.ProjectOnPlane(center - myPos, Vector3.up);
            float radius = arena.CurrentRadius;
            float distFromCenter = toCenter.magnitude;

            float edgeFactor = 0f;
            Vector2 centerSteer = Vector2.zero;
            if (distFromCenter > 0.01f && radius > 0.01f)
            {
                edgeFactor = Mathf.Clamp01(Mathf.InverseLerp(_edgeStartFraction * radius, radius, distFromCenter));
                Vector3 centerDir = toCenter.normalized;
                centerSteer = new Vector2(centerDir.x, centerDir.z);
                if (centerSteer.sqrMagnitude > 0.0001f)
                {
                    centerSteer.Normalize();
                }
            }

            NeonSumoPlayer best = null;
            float bestDist = float.MaxValue;
            foreach (var other in _getPlayers())
            {
                if (other == null || other == self || other.IsEliminated)
                {
                    continue;
                }

                float d = Vector3.Distance(myPos, other.transform.position);
                if (d < bestDist)
                {
                    bestDist = d;
                    best = other;
                }
            }

            Vector2 chaseSteer = Vector2.zero;
            if (best != null)
            {
                Vector3 toTarget = Vector3.ProjectOnPlane(best.transform.position - myPos, Vector3.up);
                if (toTarget.sqrMagnitude > 0.01f)
                {
                    Vector3 td = toTarget.normalized;
                    chaseSteer = new Vector2(td.x, td.z);
                }
            }

            float survivalWeight = Mathf.Lerp(0.25f, 1f, edgeFactor);
            float engageWeight = Mathf.Lerp(1f, 0.15f, edgeFactor);
            Vector2 steer = centerSteer * survivalWeight + chaseSteer * engageWeight;
            if (steer.sqrMagnitude > 0.0001f)
            {
                steer = steer.normalized;
            }

            bool boost = false;
            bool allowBoostByEdge = edgeFactor <= BoostEdgeSuppressThreshold;
            if (allowBoostByEdge && best != null && bestDist <= _boostRange && steer.sqrMagnitude > 0.01f)
            {
                Vector3 face = Vector3.ProjectOnPlane(best.transform.position - myPos, Vector3.up);
                if (face.sqrMagnitude > 0.0001f)
                {
                    face.Normalize();
                    Vector3 want = new Vector3(steer.x, 0f, steer.y);
                    if (want.sqrMagnitude > 0.0001f)
                    {
                        want.Normalize();
                        if (Vector3.Dot(face, want) >= _boostDotThreshold &&
                            CanRequestBoostIntent(self.UserId))
                        {
                            boost = true;
                            _lastBoostIntentTimeByUserId[self.UserId] = Time.time;
                        }
                    }
                }
            }

            return (steer, boost);
        }

        private bool CanRequestBoostIntent(string userId)
        {
            if (string.IsNullOrEmpty(userId) || _minBoostIntentInterval <= 0f)
            {
                return true;
            }

            if (!_lastBoostIntentTimeByUserId.TryGetValue(userId, out float last))
            {
                return true;
            }

            return Time.time - last >= _minBoostIntentInterval;
        }

        public void ClearBoostIntentHistory()
        {
            _lastBoostIntentTimeByUserId.Clear();
        }
    }
}
