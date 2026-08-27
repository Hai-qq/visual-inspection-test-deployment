using VisualInspection.Core.V2.Configuration;

namespace VisualInspection.Core.V2.Execution;

public enum PoseProgramStatus
{
    InProgress,
    Completed,
    Failed,
    TimedOut,
    Error
}

public sealed record PoseFrameSample
{
    public required DateTimeOffset FrameTimestampUtc { get; init; }
    public required TimeSpan MonotonicElapsed { get; init; }
    public required IReadOnlySet<string> Actions { get; init; }
    public FrameQualityFlags QualityFlags { get; init; }
}

public sealed record PoseProgramProgress
{
    public required PoseProgramStatus Status { get; init; }
    public required int CurrentActionIndex { get; init; }
    public required IReadOnlyList<Guid> CompletedActionIds { get; init; }
    public StructuredError? Error { get; init; }
}

public sealed class PoseProgramStateMachine
{
    private readonly PoseProgramDefinition _program;
    private readonly PoseActionDefinition[] _actions;
    private readonly List<Guid> _completed = [];
    private int _currentIndex;
    private TimeSpan _actionStartedElapsed;
    private TimeSpan? _holdStartedElapsed;
    private DateTimeOffset? _holdStartedFrameUtc;
    private DateTimeOffset? _lastFrameUtc;
    private PoseProgramProgress? _terminal;

    public PoseProgramStateMachine(PoseProgramDefinition program)
    {
        _program = program ?? throw new ArgumentNullException(nameof(program));
        _actions = program.Actions.OrderBy(action => action.Order).ToArray();
        if (_actions.Length == 0)
        {
            throw new ArgumentException("Pose Program 至少需要一个动作。", nameof(program));
        }
    }

    public PoseProgramProgress Process(PoseFrameSample sample)
    {
        ArgumentNullException.ThrowIfNull(sample);
        if (_terminal is not null)
        {
            return _terminal;
        }

        if (sample.MonotonicElapsed < TimeSpan.Zero)
        {
            return Terminal(PoseProgramStatus.Error, ErrorCategory.FrameCorrelation, "POSE_MONOTONIC", "单调时间不能倒退到零之前。");
        }

        if (sample.MonotonicElapsed >= TimeSpan.FromMilliseconds(_program.ProgramTimeoutMs))
        {
            return Terminal(PoseProgramStatus.TimedOut, ErrorCategory.Timeout, "POSE_PROGRAM_TIMEOUT", "整个 Pose Program 超时。");
        }

        if ((sample.QualityFlags & (FrameQualityFlags.Incomplete | FrameQualityFlags.Corrupt | FrameQualityFlags.DroppedFramesDetected)) != 0)
        {
            return Terminal(PoseProgramStatus.Error, ErrorCategory.FrameCorrelation, "POSE_FRAME_QUALITY", "Pose Frame 不完整、损坏或检测到丢帧。");
        }

        if (_lastFrameUtc is { } previous)
        {
            var gap = sample.FrameTimestampUtc - previous;
            if (gap <= TimeSpan.Zero)
            {
                return Terminal(PoseProgramStatus.Error, ErrorCategory.FrameCorrelation, "POSE_FRAME_ORDER", "Pose Frame 时间戳未严格递增。");
            }

            if (gap > TimeSpan.FromMilliseconds(_program.MaximumFrameGapMs))
            {
                return Terminal(PoseProgramStatus.Error, ErrorCategory.FrameCorrelation, "POSE_FRAME_GAP", "Pose Frame 时间间隔超过允许值。");
            }
        }

        _lastFrameUtc = sample.FrameTimestampUtc;
        var current = _actions[_currentIndex];
        var futureAction = _actions.Skip(_currentIndex + 1)
            .FirstOrDefault(action => sample.Actions.Contains(action.ActionCondition));
        if (futureAction is not null)
        {
            return Terminal(PoseProgramStatus.Failed, ErrorCategory.RuleEvaluation, "POSE_OUT_OF_ORDER", $"未来动作“{futureAction.Name}”在当前动作完成前出现。");
        }

        var waited = sample.MonotonicElapsed - _actionStartedElapsed;
        if (waited > TimeSpan.FromMilliseconds(current.MaximumWaitMs))
        {
            if (!current.IsRequired)
            {
                Advance(sample.MonotonicElapsed);
                return Current(PoseProgramStatus.InProgress);
            }

            return Terminal(PoseProgramStatus.TimedOut, ErrorCategory.Timeout, "POSE_ACTION_TIMEOUT", $"动作“{current.Name}”等待超时。");
        }

        if (!sample.Actions.Contains(current.ActionCondition))
        {
            _holdStartedElapsed = null;
            _holdStartedFrameUtc = null;
            return Current(PoseProgramStatus.InProgress);
        }

        _holdStartedElapsed ??= sample.MonotonicElapsed;
        _holdStartedFrameUtc ??= sample.FrameTimestampUtc;
        var monotonicHold = sample.MonotonicElapsed - _holdStartedElapsed.Value;
        var frameHold = sample.FrameTimestampUtc - _holdStartedFrameUtc.Value;
        var confirmedHold = monotonicHold <= frameHold ? monotonicHold : frameHold;
        if (confirmedHold < TimeSpan.FromMilliseconds(current.MinimumHoldMs))
        {
            return Current(PoseProgramStatus.InProgress);
        }

        _completed.Add(current.ActionId);
        if (_currentIndex == _actions.Length - 1)
        {
            return Terminal(PoseProgramStatus.Completed, null, null, null);
        }

        Advance(sample.MonotonicElapsed);
        return Current(PoseProgramStatus.InProgress);
    }

    private void Advance(TimeSpan elapsed)
    {
        _currentIndex++;
        _actionStartedElapsed = elapsed;
        _holdStartedElapsed = null;
        _holdStartedFrameUtc = null;
    }

    private PoseProgramProgress Current(PoseProgramStatus status) => new()
    {
        Status = status,
        CurrentActionIndex = _currentIndex,
        CompletedActionIds = _completed.ToArray()
    };

    private PoseProgramProgress Terminal(
        PoseProgramStatus status,
        ErrorCategory? category,
        string? code,
        string? message)
    {
        _terminal = new PoseProgramProgress
        {
            Status = status,
            CurrentActionIndex = _currentIndex,
            CompletedActionIds = _completed.ToArray(),
            Error = category is null ? null : new StructuredError
            {
                Category = category.Value,
                Code = code!,
                Message = message!
            }
        };
        return _terminal;
    }
}
