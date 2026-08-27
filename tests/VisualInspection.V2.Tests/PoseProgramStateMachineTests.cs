using VisualInspection.Core.V2.Configuration;
using VisualInspection.Core.V2.Execution;

namespace VisualInspection.V2.Tests;

public sealed class PoseProgramStateMachineTests
{
    [Fact]
    public void ActionSettings_AreIndependent_AndUseActualTimestamps()
    {
        var first = Action(1, "pick", holdMs: 50, waitMs: 500);
        var second = Action(2, "place", holdMs: 200, waitMs: 1000);
        var machine = Machine(first, second);
        var start = DateTimeOffset.UtcNow;

        Assert.Equal(PoseProgramStatus.InProgress, machine.Process(Sample(start, 0, "pick")).Status);
        Assert.Equal(PoseProgramStatus.InProgress, machine.Process(Sample(start.AddMilliseconds(50), 50, "pick")).Status);
        Assert.Equal(PoseProgramStatus.InProgress, machine.Process(Sample(start.AddMilliseconds(60), 60, "place")).Status);
        var complete = machine.Process(Sample(start.AddMilliseconds(260), 260, "place"));

        Assert.Equal(PoseProgramStatus.Completed, complete.Status);
        Assert.Equal([first.ActionId, second.ActionId], complete.CompletedActionIds);
    }

    [Fact]
    public void HoldMustBeContinuous()
    {
        var action = Action(1, "hold", holdMs: 100, waitMs: 500);
        var machine = Machine(action);
        var start = DateTimeOffset.UtcNow;

        machine.Process(Sample(start, 0, "hold"));
        machine.Process(Sample(start.AddMilliseconds(50), 50));
        machine.Process(Sample(start.AddMilliseconds(60), 60, "hold"));
        var tooEarly = machine.Process(Sample(start.AddMilliseconds(150), 150, "hold"));
        var complete = machine.Process(Sample(start.AddMilliseconds(160), 160, "hold"));

        Assert.Equal(PoseProgramStatus.InProgress, tooEarly.Status);
        Assert.Equal(PoseProgramStatus.Completed, complete.Status);
    }

    [Fact]
    public void FutureActionAppearingEarly_IsSequenceFailure()
    {
        var machine = Machine(Action(1, "pick", 0, 500), Action(2, "place", 0, 500));
        var result = machine.Process(Sample(DateTimeOffset.UtcNow, 0, "place"));

        Assert.Equal(PoseProgramStatus.Failed, result.Status);
        Assert.Equal("POSE_OUT_OF_ORDER", result.Error?.Code);
    }

    [Fact]
    public void ActionTimeout_IsNotPass()
    {
        var start = DateTimeOffset.UtcNow;
        var machine = Machine(Action(1, "pick", 0, 100));
        machine.Process(Sample(start, 0));

        var result = machine.Process(Sample(start.AddMilliseconds(101), 101));

        Assert.Equal(PoseProgramStatus.TimedOut, result.Status);
        Assert.Equal("POSE_ACTION_TIMEOUT", result.Error?.Code);
    }

    [Fact]
    public void ProgramTimeout_IsExplicit()
    {
        var action = Action(1, "pick", 0, 5000);
        var machine = new PoseProgramStateMachine(new PoseProgramDefinition
        {
            ProgramTimeoutMs = 100,
            MaximumFrameGapMs = 200,
            Actions = [action]
        });
        var start = DateTimeOffset.UtcNow;
        machine.Process(Sample(start, 0));

        var result = machine.Process(Sample(start.AddMilliseconds(100), 100));

        Assert.Equal(PoseProgramStatus.TimedOut, result.Status);
        Assert.Equal("POSE_PROGRAM_TIMEOUT", result.Error?.Code);
    }

    [Fact]
    public void DroppedFrameAndLargeGap_AreErrors()
    {
        var start = DateTimeOffset.UtcNow;
        var droppedMachine = Machine(Action(1, "pick", 100, 1000));
        var dropped = droppedMachine.Process(Sample(
            start,
            0,
            FrameQualityFlags.DroppedFramesDetected,
            "pick"));
        var gapMachine = Machine(Action(1, "pick", 100, 1000), maximumFrameGapMs: 50);
        gapMachine.Process(Sample(start, 0, "pick"));
        var gap = gapMachine.Process(Sample(start.AddMilliseconds(51), 51, "pick"));

        Assert.Equal(PoseProgramStatus.Error, dropped.Status);
        Assert.Equal("POSE_FRAME_QUALITY", dropped.Error?.Code);
        Assert.Equal(PoseProgramStatus.Error, gap.Status);
        Assert.Equal("POSE_FRAME_GAP", gap.Error?.Code);
    }

    [Fact]
    public void NonIncreasingFrameTimestamp_IsError()
    {
        var start = DateTimeOffset.UtcNow;
        var machine = Machine(Action(1, "pick", 100, 1000));
        machine.Process(Sample(start, 0, "pick"));

        var result = machine.Process(Sample(start, 10, "pick"));

        Assert.Equal(PoseProgramStatus.Error, result.Status);
        Assert.Equal("POSE_FRAME_ORDER", result.Error?.Code);
    }

    [Fact]
    public void OptionalTimedOutAction_IsSkipped()
    {
        var optional = Action(1, "optional", 0, 50) with { IsRequired = false };
        var required = Action(2, "required", 0, 500);
        var machine = Machine(optional, required);
        var start = DateTimeOffset.UtcNow;
        machine.Process(Sample(start, 0));
        var skipped = machine.Process(Sample(start.AddMilliseconds(51), 51));
        var completed = machine.Process(Sample(start.AddMilliseconds(52), 52, "required"));

        Assert.Equal(PoseProgramStatus.InProgress, skipped.Status);
        Assert.Equal(PoseProgramStatus.Completed, completed.Status);
    }

    private static PoseActionDefinition Action(int order, string condition, int holdMs, int waitMs) => new()
    {
        Order = order,
        Name = condition,
        ActionCondition = condition,
        ModelBindingId = Guid.NewGuid(),
        ConfidenceThreshold = 0.5,
        MinimumHoldMs = holdMs,
        MaximumWaitMs = waitMs,
        IsRequired = true
    };

    private static PoseProgramStateMachine Machine(
        PoseActionDefinition first,
        PoseActionDefinition? second = null,
        int maximumFrameGapMs = 500) =>
        new(new PoseProgramDefinition
        {
            ProgramTimeoutMs = 10000,
            MaximumFrameGapMs = maximumFrameGapMs,
            Actions = second is null ? [first] : [first, second]
        });

    private static PoseFrameSample Sample(
        DateTimeOffset timestamp,
        int monotonicMs,
        params string[] actions) =>
        Sample(timestamp, monotonicMs, FrameQualityFlags.None, actions);

    private static PoseFrameSample Sample(
        DateTimeOffset timestamp,
        int monotonicMs,
        FrameQualityFlags flags,
        params string[] actions) => new()
        {
            FrameTimestampUtc = timestamp,
            MonotonicElapsed = TimeSpan.FromMilliseconds(monotonicMs),
            Actions = actions.ToHashSet(StringComparer.OrdinalIgnoreCase),
            QualityFlags = flags
        };
}
