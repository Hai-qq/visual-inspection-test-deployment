namespace VisualInspection.Core.V2.Execution;

public sealed class LineResultHandshakeStateMachine
{
    private readonly object _sync = new();
    private readonly TimeSpan _ackTimeout;
    private LineResultSnapshot _snapshot = new();

    public LineResultHandshakeStateMachine(TimeSpan? ackTimeout = null)
    {
        _ackTimeout = ackTimeout ?? TimeSpan.FromSeconds(5);
        if (_ackTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(ackTimeout));
        }
    }

    public LineResultSnapshot Snapshot
    {
        get
        {
            lock (_sync)
            {
                return _snapshot;
            }
        }
    }

    public LineResultSnapshot SetReady(bool ready)
    {
        lock (_sync)
        {
            if (!ready)
            {
                _snapshot = Cleared(LineHandshakeState.NotReady, heartbeat: _snapshot.Heartbeat);
                return _snapshot;
            }

            if (_snapshot.State is LineHandshakeState.Busy or LineHandshakeState.ResultLatched)
            {
                throw new InvalidOperationException("Busy 或 ResultLatched 时不能直接切换 Ready。");
            }

            _snapshot = Cleared(LineHandshakeState.Ready, ready: true, heartbeat: _snapshot.Heartbeat);
            return _snapshot;
        }
    }

    public LineResultSnapshot BeginRun(Guid runId)
    {
        if (runId == Guid.Empty)
        {
            throw new ArgumentException("RunId 不能为空。", nameof(runId));
        }

        lock (_sync)
        {
            if (_snapshot.State != LineHandshakeState.Ready)
            {
                throw new InvalidOperationException("线端未处于 Ready，不能开始 Run。");
            }

            _snapshot = Cleared(LineHandshakeState.Busy, busy: true, heartbeat: _snapshot.Heartbeat) with
            {
                ActiveRunId = runId,
                ResultSequenceNumber = _snapshot.ResultSequenceNumber
            };
            return _snapshot;
        }
    }

    public LineResultSnapshot Latch(LineResultPayload result, DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(result);
        lock (_sync)
        {
            if (_snapshot.State != LineHandshakeState.Busy || _snapshot.ActiveRunId != result.RunId)
            {
                throw new InvalidOperationException("迟到或未知 Run 的结果不能覆盖当前线端状态。");
            }

            if (result.ResultSequenceNumber <= _snapshot.ResultSequenceNumber)
            {
                throw new InvalidOperationException("ResultSequenceNumber 必须严格递增。");
            }

            var safePass = result.RunState == RunState.Completed &&
                           result.Verdict == BusinessVerdict.Pass &&
                           result.Error is null;
            var safeFail = result.RunState == RunState.Completed &&
                           result.Verdict == BusinessVerdict.Fail &&
                           result.Error is null;
            _snapshot = new LineResultSnapshot
            {
                State = LineHandshakeState.ResultLatched,
                Ready = false,
                Busy = false,
                ResultValid = true,
                Pass = safePass,
                Fail = safeFail,
                Error = !safePass && !safeFail,
                Heartbeat = _snapshot.Heartbeat,
                Ack = false,
                ResultSequenceNumber = result.ResultSequenceNumber,
                ActiveRunId = result.RunId,
                AckDeadlineUtc = nowUtc + _ackTimeout
            };
            return _snapshot;
        }
    }

    public LineResultSnapshot Acknowledge(long resultSequenceNumber)
    {
        lock (_sync)
        {
            if (_snapshot.State != LineHandshakeState.ResultLatched ||
                _snapshot.ResultSequenceNumber != resultSequenceNumber)
            {
                throw new InvalidOperationException("Ack 与当前锁存结果不匹配。");
            }

            _snapshot = Cleared(LineHandshakeState.Ready, ready: true, heartbeat: _snapshot.Heartbeat) with
            {
                Ack = true,
                ResultSequenceNumber = resultSequenceNumber
            };
            return _snapshot;
        }
    }

    public LineResultSnapshot Tick(DateTimeOffset nowUtc)
    {
        lock (_sync)
        {
            if (_snapshot.State == LineHandshakeState.ResultLatched &&
                _snapshot.AckDeadlineUtc is { } deadline && nowUtc > deadline)
            {
                _snapshot = Cleared(LineHandshakeState.Faulted, error: true, heartbeat: _snapshot.Heartbeat) with
                {
                    ResultSequenceNumber = _snapshot.ResultSequenceNumber
                };
            }

            return _snapshot;
        }
    }

    public LineResultSnapshot Fault()
    {
        lock (_sync)
        {
            _snapshot = Cleared(LineHandshakeState.Faulted, error: true, heartbeat: _snapshot.Heartbeat) with
            {
                ResultSequenceNumber = _snapshot.ResultSequenceNumber
            };
            return _snapshot;
        }
    }

    public LineResultSnapshot Disconnect()
    {
        lock (_sync)
        {
            _snapshot = Cleared(LineHandshakeState.Faulted, error: true, heartbeat: false) with
            {
                ResultSequenceNumber = _snapshot.ResultSequenceNumber
            };
            return _snapshot;
        }
    }

    public LineResultSnapshot ToggleHeartbeat()
    {
        lock (_sync)
        {
            _snapshot = _snapshot with { Heartbeat = !_snapshot.Heartbeat };
            return _snapshot;
        }
    }

    public LineResultSnapshot Restart()
    {
        lock (_sync)
        {
            _snapshot = new LineResultSnapshot();
            return _snapshot;
        }
    }

    private static LineResultSnapshot Cleared(
        LineHandshakeState state,
        bool ready = false,
        bool busy = false,
        bool error = false,
        bool heartbeat = false) => new()
        {
            State = state,
            Ready = ready,
            Busy = busy,
            ResultValid = false,
            Pass = false,
            Fail = false,
            Error = error,
            Heartbeat = heartbeat,
            Ack = false,
            ResultSequenceNumber = 0,
            ActiveRunId = null,
            AckDeadlineUtc = null
        };
}
