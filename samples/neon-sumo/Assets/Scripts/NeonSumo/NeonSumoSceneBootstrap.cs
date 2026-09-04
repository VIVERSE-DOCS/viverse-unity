using UnityEngine;

namespace NeonSumo
{
    /// <summary>
    /// Chooses offline vs online bootstrap for the gameplay scene. See plan: session mode first, then handoff, then default.
    /// </summary>
    [DisallowMultipleComponent]
    public class NeonSumoSceneBootstrap : MonoBehaviour
    {
        [Tooltip("When enabled, ignores session mode and handoff; clears handoff and starts offline (kiosk / direct launch).")]
        [SerializeField] private bool forceOffline;

        [SerializeField] private NeonSumoMatchmakingFlow matchmakingFlow;
        [SerializeField] private NeonSumoOfflineBootstrap offlineBootstrap;

        private void Awake()
        {
            if (!ValidateReferences())
            {
                enabled = false;
                return;
            }

            var modeAtEntry = NeonSumoSessionStore.Mode;
            bool handoffPending = MatchmakingHandoffStore.HasPendingHandoff();
            string branch;
            string detail = string.Empty;

            if (forceOffline)
            {
                NeonSumoSessionStore.Clear();
                MatchmakingHandoffStore.Clear();
                EnableOfflineMode();
                branch = "ForceOffline";
            }
            else if (modeAtEntry == NeonSumoGameMode.OfflineSinglePlayer)
            {
                MatchmakingHandoffStore.Clear();
                EnableOfflineMode();
                NeonSumoSessionStore.Clear();
                branch = "SessionOffline";
            }
            else if (modeAtEntry == NeonSumoGameMode.OnlineMultiplayer)
            {
                if (handoffPending)
                {
                    EnableMatchmakingMode();
                    NeonSumoSessionStore.Clear();
                    branch = "SessionOnlinePlusHandoff";
                }
                else
                {
                    DebugLogger.LogError(
                        "[NeonSumoBootstrap] Session mode OnlineMultiplayer but no valid handoff — falling back to offline. Use lobby flow before gameplay.");
                    detail = "missing_handoff";
                    MatchmakingHandoffStore.Clear();
                    EnableOfflineMode();
                    NeonSumoSessionStore.Clear();
                    branch = "SessionOnlineMissingHandoff_FallbackOffline";
                }
            }
            else if (handoffPending)
            {
                EnableMatchmakingMode();
                branch = "HandoffOnly";
            }
            else
            {
                EnableOfflineMode();
                branch = "DefaultOffline";
            }

            DebugLogger.Log(
                $"[NeonSumoBootstrap] path={branch} modeAtEntry={modeAtEntry} handoffPending={handoffPending} forceOffline={forceOffline} detail={detail}");

            // #region agent log
            NeonSumoDebugNdjson.Log(
                hypothesisId: "H1",
                location: "NeonSumoSceneBootstrap.Awake",
                message: "scene_bootstrap_branch",
                data: new { branch, modeAtEntry = modeAtEntry.ToString(), handoffPending, forceOffline, detail });
            // #endregion
        }

        private bool ValidateReferences()
        {
            bool isValid = true;

            if (matchmakingFlow == null)
            {
                DebugLogger.LogError("[NeonSumoBootstrap] NeonSumoMatchmakingFlow is not assigned. Assign in Inspector.");
                isValid = false;
            }

            if (offlineBootstrap == null)
            {
                DebugLogger.LogError("[NeonSumoBootstrap] NeonSumoOfflineBootstrap is not assigned. Assign in Inspector.");
                isValid = false;
            }

            return isValid;
        }

        private void EnableMatchmakingMode()
        {
            offlineBootstrap.enabled = false;
            matchmakingFlow.enabled = true;
        }

        private void EnableOfflineMode()
        {
            MatchmakingHandoffStore.Clear();
            matchmakingFlow.enabled = false;
            offlineBootstrap.enabled = true;
        }

#if UNITY_EDITOR
        private void Reset()
        {
            matchmakingFlow = GetComponent<NeonSumoMatchmakingFlow>();
            offlineBootstrap = GetComponent<NeonSumoOfflineBootstrap>();
        }
#endif
    }
}
