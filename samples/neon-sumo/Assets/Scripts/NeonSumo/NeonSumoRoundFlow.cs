using System;

namespace NeonSumo
{
    /// <summary>
    /// Centralized round flow state machine. Validates transitions and fires events.
    /// </summary>
    public sealed class NeonSumoRoundFlow
    {
        public NeonSumoGameState CurrentState { get; private set; } = NeonSumoGameState.WaitingForPlayers;

        public event Action<NeonSumoGameState, NeonSumoGameState> OnStateChanged;

        public bool TryTransition(NeonSumoGameState nextState)
        {
            if (CurrentState == nextState)
                return true; // No-op, already in target state

            if (!IsValidTransition(CurrentState, nextState))
                return false;

            var previous = CurrentState;
            CurrentState = nextState;
            OnStateChanged?.Invoke(previous, nextState);
            return true;
        }

        public void ForceTransition(NeonSumoGameState nextState)
        {
            var previous = CurrentState;
            CurrentState = nextState;
            OnStateChanged?.Invoke(previous, nextState);
        }

        private static bool IsValidTransition(NeonSumoGameState from, NeonSumoGameState to)
        {
            switch (from)
            {
                case NeonSumoGameState.WaitingForPlayers:
                    return to == NeonSumoGameState.ReadyPhase || to == NeonSumoGameState.Playing; // Playing for local mode shortcut

                case NeonSumoGameState.ReadyPhase:
                    return to == NeonSumoGameState.Countdown;

                case NeonSumoGameState.Countdown:
                    return to == NeonSumoGameState.ReadyPhase || to == NeonSumoGameState.Playing;

                case NeonSumoGameState.Playing:
                    return to == NeonSumoGameState.RoundEnding || to == NeonSumoGameState.GameEnd;

                case NeonSumoGameState.RoundEnding:
                    return to == NeonSumoGameState.RoundResults;

                case NeonSumoGameState.RoundResults:
                    return to == NeonSumoGameState.ReadyPhase || to == NeonSumoGameState.GameEnd ||
                           to == NeonSumoGameState.Restarting;

                case NeonSumoGameState.GameEnd:
                    return to == NeonSumoGameState.Restarting;

                case NeonSumoGameState.Restarting:
                    return to == NeonSumoGameState.ReadyPhase;

                default:
                    return false;
            }
        }
    }
}
