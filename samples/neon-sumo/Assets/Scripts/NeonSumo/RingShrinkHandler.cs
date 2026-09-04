using System;

namespace NeonSumo
{
    /// <summary>
    /// Time-based ring shrink event scheduler. Replaces 15 booleans with a data-driven
    /// stage array. Call Tick(secondsRemaining) each countdown tick.
    /// Uses immutable stage definitions plus separate runtime state flags.
    /// Uses &lt;= so skipped countdown values still trigger pending events once (dedupe flags prevent replay).
    /// </summary>
    public sealed class RingShrinkHandler
    {
        private static readonly RingShrinkStageDefinition[] DefaultStages =
        {
            new(secondTrigger: 55, dropSecond: 35, alarmTrigger: 38, dropRingIndex: 0, clockwise: true),
            new(secondTrigger: 45, dropSecond: 29, alarmTrigger: 32, dropRingIndex: 1, clockwise: false),
            new(secondTrigger: 35, dropSecond: 23, alarmTrigger: 26, dropRingIndex: 2, clockwise: true),
            new(secondTrigger: 25, dropSecond: 17, alarmTrigger: 20, dropRingIndex: 3, clockwise: false),
            new(secondTrigger: 15, dropSecond: 11, alarmTrigger: 14, dropRingIndex: 4, clockwise: true),
        };

        private readonly RingShrinkContext _ctx;
        private readonly RingShrinkStageDefinition[] _stages;
        private readonly bool[] _linePlayed;
        private readonly bool[] _alarmPlayed;
        private readonly bool[] _ringDropped;

        private int _lastProcessedSecond = -1;

        public RingShrinkHandler(RingShrinkContext ctx)
            : this(ctx, DefaultStages)
        {
        }

        public RingShrinkHandler(RingShrinkContext ctx, RingShrinkStageDefinition[] stages)
        {
            _ctx = ctx ?? throw new ArgumentNullException(nameof(ctx));
            _stages = stages ?? throw new ArgumentNullException(nameof(stages));

            _linePlayed = new bool[_stages.Length];
            _alarmPlayed = new bool[_stages.Length];
            _ringDropped = new bool[_stages.Length];
        }

        /// <summary>Process ring events for this second. Call once per countdown tick. Returns last processed second for deduplication.</summary>
        public int Tick(int secondsRemaining)
        {
            if (secondsRemaining == _lastProcessedSecond)
                return _lastProcessedSecond;

            _lastProcessedSecond = secondsRemaining;
            bool isMasterClient = _ctx.IsMasterClient?.Invoke() == true;

            for (int i = 0; i < _stages.Length; i++)
            {
                var stage = _stages[i];

                // Line drawing (all clients: local visual)
                if (!_linePlayed[i] && secondsRemaining <= stage.SecondTrigger)
                {
                    _linePlayed[i] = true;
                    _ctx.PlayWarningLine?.Invoke(stage.DropRingIndex, stage.LineDuration, stage.LineClockwise);
                    _ctx.OnWarningLineStarted?.Invoke(stage.DropRingIndex);
                }

                // Warning alarm (all clients: local audio)
                if (!_alarmPlayed[i] && secondsRemaining <= stage.AlarmTrigger)
                {
                    _alarmPlayed[i] = true;
                    _ctx.PlayWarningAlarm?.Invoke();
                }

                // Ring drops (host-only; clients receive via ActionSync ring_drop)
                if (isMasterClient && !_ringDropped[i] && secondsRemaining <= stage.DropSecond)
                {
                    _ringDropped[i] = true;
                    _ctx.TriggerRingDrop?.Invoke(stage.DropRingIndex);
                }
            }

            return _lastProcessedSecond;
        }

        public void Reset()
        {
            _lastProcessedSecond = -1;
            Array.Clear(_linePlayed, 0, _linePlayed.Length);
            Array.Clear(_alarmPlayed, 0, _alarmPlayed.Length);
            Array.Clear(_ringDropped, 0, _ringDropped.Length);
        }
    }

    public readonly struct RingShrinkStageDefinition
    {
        public int SecondTrigger { get; }
        public int AlarmTrigger { get; }
        public int DropSecond { get; }
        public int DropRingIndex { get; }
        public bool LineClockwise { get; }
        public float LineDuration => SecondTrigger - DropSecond;

        public RingShrinkStageDefinition(
            int secondTrigger,
            int dropSecond,
            int alarmTrigger,
            int dropRingIndex,
            bool clockwise)
        {
            SecondTrigger = secondTrigger;
            DropSecond = dropSecond;
            AlarmTrigger = alarmTrigger;
            DropRingIndex = dropRingIndex;
            LineClockwise = clockwise;
        }
    }

    /// <summary>Context for RingShrinkHandler. GameManager populates this.</summary>
    public class RingShrinkContext
    {
        public Action<int, float, bool> PlayWarningLine { get; set; }
        public Action PlayWarningAlarm { get; set; }
        public Action<int> TriggerRingDrop { get; set; }
        public Action<int> OnWarningLineStarted { get; set; }
        public Func<bool> IsMasterClient { get; set; }
    }
}
