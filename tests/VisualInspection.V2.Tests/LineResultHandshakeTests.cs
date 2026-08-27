using VisualInspection.Core.V2.Execution;

namespace VisualInspection.V2.Tests;

public sealed class LineResultHandshakeTests
{
    [Fact]
    public void Startup_IsNotReady_AndDoesNotRestoreOldPass()
    {
        var machine = new LineResultHandshakeStateMachine();

        var snapshot = machine.Snapshot;

        Assert.Equal(LineHandshakeState.NotReady, snapshot.State);
        Assert.False(snapshot.Ready);
        Assert.False(snapshot.Pass);
        Assert.False(snapshot.ResultValid);
    }

    [Fact]
    public void ReadyBusyAndPassLatch_FollowHandshake()
    {
        var machine = new LineResultHandshakeStateMachine();
        var runId = Guid.NewGuid();

        var ready = machine.SetReady(true);
        var busy = machine.BeginRun(runId);
        var latched = machine.Latch(Payload(runId, 1, RunState.Completed, BusinessVerdict.Pass), DateTimeOffset.UtcNow);

        Assert.True(ready.Ready);
        Assert.True(busy.Busy);
        Assert.False(busy.ResultValid);
        Assert.True(latched.ResultValid);
        Assert.True(latched.Pass);
        Assert.False(latched.Fail);
        Assert.False(latched.Error);
    }

    [Fact]
    public void Fail_IsLatchedWithoutPass()
    {
        var machine = ReadyWithRun(out var runId);

        var snapshot = machine.Latch(Payload(runId, 1, RunState.Completed, BusinessVerdict.Fail), DateTimeOffset.UtcNow);

        Assert.True(snapshot.Fail);
        Assert.False(snapshot.Pass);
        Assert.False(snapshot.Error);
    }

    [Theory]
    [InlineData(RunState.Errored)]
    [InlineData(RunState.TimedOut)]
    [InlineData(RunState.Stopped)]
    public void ErrorTimeoutAndStopped_NeverLatchPass(RunState state)
    {
        var machine = ReadyWithRun(out var runId);

        var snapshot = machine.Latch(Payload(runId, 1, state, BusinessVerdict.NotEvaluated), DateTimeOffset.UtcNow);

        Assert.True(snapshot.Error);
        Assert.False(snapshot.Pass);
        Assert.False(snapshot.Fail);
    }

    [Fact]
    public void Ack_ClearsOutputsAndReturnsReady()
    {
        var machine = ReadyWithRun(out var runId);
        machine.Latch(Payload(runId, 7, RunState.Completed, BusinessVerdict.Pass), DateTimeOffset.UtcNow);

        var snapshot = machine.Acknowledge(7);

        Assert.Equal(LineHandshakeState.Ready, snapshot.State);
        Assert.True(snapshot.Ready);
        Assert.True(snapshot.Ack);
        Assert.False(snapshot.ResultValid);
        Assert.False(snapshot.Pass);
    }

    [Fact]
    public void AckTimeout_TransitionsToFaultedNotReady()
    {
        var now = DateTimeOffset.UtcNow;
        var machine = new LineResultHandshakeStateMachine(TimeSpan.FromMilliseconds(50));
        machine.SetReady(true);
        var runId = Guid.NewGuid();
        machine.BeginRun(runId);
        machine.Latch(Payload(runId, 1, RunState.Completed, BusinessVerdict.Pass), now);

        var snapshot = machine.Tick(now.AddMilliseconds(51));

        Assert.Equal(LineHandshakeState.Faulted, snapshot.State);
        Assert.False(snapshot.Ready);
        Assert.True(snapshot.Error);
        Assert.False(snapshot.Pass);
    }

    [Fact]
    public void Disconnect_IsFaultedAndNotReady()
    {
        var machine = new LineResultHandshakeStateMachine();
        machine.SetReady(true);

        var snapshot = machine.Disconnect();

        Assert.Equal(LineHandshakeState.Faulted, snapshot.State);
        Assert.False(snapshot.Ready);
        Assert.True(snapshot.Error);
    }

    [Fact]
    public void LateOldRun_CannotOverwriteNewRun()
    {
        var machine = new LineResultHandshakeStateMachine();
        machine.SetReady(true);
        var oldRun = Guid.NewGuid();
        machine.BeginRun(oldRun);
        machine.Latch(Payload(oldRun, 1, RunState.Completed, BusinessVerdict.Fail), DateTimeOffset.UtcNow);
        machine.Acknowledge(1);
        var newRun = Guid.NewGuid();
        machine.BeginRun(newRun);

        Assert.Throws<InvalidOperationException>(() =>
            machine.Latch(Payload(oldRun, 2, RunState.Completed, BusinessVerdict.Pass), DateTimeOffset.UtcNow));
        Assert.True(machine.Snapshot.Busy);
        Assert.False(machine.Snapshot.Pass);
    }

    [Fact]
    public void Restart_ClearsPreviouslyLatchedPass()
    {
        var machine = ReadyWithRun(out var runId);
        machine.Latch(Payload(runId, 1, RunState.Completed, BusinessVerdict.Pass), DateTimeOffset.UtcNow);

        var restarted = machine.Restart();

        Assert.Equal(LineHandshakeState.NotReady, restarted.State);
        Assert.False(restarted.ResultValid);
        Assert.False(restarted.Pass);
    }

    private static LineResultHandshakeStateMachine ReadyWithRun(out Guid runId)
    {
        var machine = new LineResultHandshakeStateMachine();
        machine.SetReady(true);
        runId = Guid.NewGuid();
        machine.BeginRun(runId);
        return machine;
    }

    private static LineResultPayload Payload(
        Guid runId,
        long sequence,
        RunState state,
        BusinessVerdict verdict) => new()
        {
            RunId = runId,
            ResultSequenceNumber = sequence,
            RunState = state,
            Verdict = verdict,
            Error = state == RunState.Completed ? null : new StructuredError
            {
                Category = ErrorCategory.Timeout,
                Code = "TEST_ERROR",
                Message = "test"
            }
        };
}
