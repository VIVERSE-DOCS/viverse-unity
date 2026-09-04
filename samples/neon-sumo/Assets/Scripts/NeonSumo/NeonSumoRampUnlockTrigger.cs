using UnityEngine;
using UnityEngine.Serialization;

namespace NeonSumo
{
    [DisallowMultipleComponent]
    public sealed class NeonSumoRampUnlockTrigger : MonoBehaviour
    {
        [Tooltip("Assign a component that implements IRampUnlockHandler (e.g. NeonSumoGameManager).")]
        [SerializeField]
        [FormerlySerializedAs("listener")]
        private MonoBehaviour handlerComponent;

        private IRampUnlockHandler _rampUnlockHandler;
        private bool _hasFired;

        private void Awake()
        {
#if UNITY_EDITOR
            if (handlerComponent == null)
                handlerComponent = FindFirstObjectByType<NeonSumoGameManager>();
#endif

            _rampUnlockHandler = handlerComponent as IRampUnlockHandler;

            if (_rampUnlockHandler == null)
            {
                Debug.LogError(
                    "[NeonSumo] NeonSumoRampUnlockTrigger requires a handler that implements IRampUnlockHandler.",
                    this);
                enabled = false;
            }
        }

        private void OnTriggerEnter(Collider other)
        {
            if (!enabled || _hasFired)
                return;

            if (!TryGetPlayer(other, out var player))
                return;

            _rampUnlockHandler.OnRampUnlocked(player);
            _hasFired = true;
        }

        /// <summary>Call when restarting a match so each ramp can fire unlock again next round.</summary>
        public void ResetForNewRound()
        {
            _hasFired = false;
        }

        private static bool TryGetPlayer(Collider other, out NeonSumoPlayer player)
        {
            player = other.GetComponentInParent<NeonSumoPlayer>();
            return player != null;
        }
    }
}
