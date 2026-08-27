namespace VisualInspection.Core.V2.Execution;

public enum FrameCorrelationStatus
{
    Correlated,
    UnknownCapture,
    DuplicateFrame,
    TriggerMismatch,
    ProductMismatch,
    StationMismatch,
    DeadlineExceeded,
    FrameTooOld,
    LateForCancelledRun,
    InvalidQuality
}

public sealed record FrameCorrelationResult
{
    public required FrameCorrelationStatus Status { get; init; }
    public FrameEnvelope? Frame { get; init; }
    public StructuredError? Error { get; init; }
    public bool IsCorrelated => Status == FrameCorrelationStatus.Correlated;
}

public sealed class FrameCorrelationService
{
    private readonly object _sync = new();
    private readonly int _retentionCapacity;
    private readonly Dictionary<Guid, PendingCapture> _captures = [];
    private readonly HashSet<(Guid CaptureId, long FrameCounter)> _seenFrames = [];
    private readonly Queue<(Guid CaptureId, long FrameCounter)> _seenFrameOrder = [];
    private readonly HashSet<Guid> _cancelledRuns = [];
    private readonly Queue<Guid> _cancelledRunOrder = [];
    private readonly HashSet<Guid> _cancelledCaptures = [];
    private readonly Queue<Guid> _cancelledCaptureOrder = [];

    public FrameCorrelationService(int retentionCapacity = 4096)
    {
        if (retentionCapacity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(retentionCapacity));
        }

        _retentionCapacity = retentionCapacity;
    }

    public void Register(CaptureRequest request, string stationId, int maximumFrameAgeMs)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (maximumFrameAgeMs <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumFrameAgeMs));
        }

        lock (_sync)
        {
            if (_cancelledRuns.Contains(request.RunId))
            {
                Retain(_cancelledCaptures, _cancelledCaptureOrder, request.CaptureId);
                return;
            }

            if (!_captures.TryAdd(request.CaptureId, new PendingCapture(request, stationId, maximumFrameAgeMs)))
            {
                throw new InvalidOperationException($"CaptureId {request.CaptureId} 已注册。");
            }
        }
    }

    public void CancelRun(Guid runId)
    {
        lock (_sync)
        {
            Retain(_cancelledRuns, _cancelledRunOrder, runId);
            foreach (var captureId in _captures
                         .Where(entry => entry.Value.Request.RunId == runId)
                         .Select(entry => entry.Key)
                         .ToArray())
            {
                _captures.Remove(captureId);
                Retain(_cancelledCaptures, _cancelledCaptureOrder, captureId);
            }
        }
    }

    public void AbandonCapture(Guid captureId)
    {
        lock (_sync)
        {
            _captures.Remove(captureId);
        }
    }

    public FrameCorrelationResult Correlate(FrameEnvelope frame, DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(frame);
        lock (_sync)
        {
            if (_seenFrames.Contains((frame.CaptureId, frame.FrameCounter)))
            {
                return Failure(FrameCorrelationStatus.DuplicateFrame, "FRAME_DUPLICATE", "同一 CaptureId 的 FrameCounter 重复。");
            }

            if (_cancelledCaptures.Contains(frame.CaptureId))
            {
                Retain(_seenFrames, _seenFrameOrder, (frame.CaptureId, frame.FrameCounter));
                return Failure(FrameCorrelationStatus.LateForCancelledRun, "FRAME_CANCELLED_RUN", "已取消 Run 的迟到 Frame 已丢弃。");
            }

            if (!_captures.TryGetValue(frame.CaptureId, out var pending))
            {
                return Failure(FrameCorrelationStatus.UnknownCapture, "FRAME_UNKNOWN", "收到未注册 CaptureId 的 Frame。");
            }

            _captures.Remove(frame.CaptureId);
            Retain(_seenFrames, _seenFrameOrder, (frame.CaptureId, frame.FrameCounter));

            if (_cancelledRuns.Contains(pending.Request.RunId))
            {
                return Failure(FrameCorrelationStatus.LateForCancelledRun, "FRAME_CANCELLED_RUN", "已取消 Run 的迟到 Frame 已丢弃。");
            }

            if (frame.TriggerEventId != pending.Request.TriggerEventId)
            {
                return Failure(FrameCorrelationStatus.TriggerMismatch, "FRAME_TRIGGER_MISMATCH", "Frame 与 TriggerEvent 关联不一致。");
            }

            if (!string.Equals(frame.ProductId, pending.Request.ProductId, StringComparison.Ordinal))
            {
                return Failure(FrameCorrelationStatus.ProductMismatch, "FRAME_PRODUCT_MISMATCH", "Frame ProductId 与 CaptureRequest 不一致。");
            }

            if (!string.Equals(frame.StationId, pending.StationId, StringComparison.Ordinal))
            {
                return Failure(FrameCorrelationStatus.StationMismatch, "FRAME_STATION_MISMATCH", "Frame StationId 与当前 Deployment 不一致。");
            }

            if (nowUtc > pending.Request.DeadlineUtc || frame.ReceivedAtUtc > pending.Request.DeadlineUtc)
            {
                return Failure(FrameCorrelationStatus.DeadlineExceeded, "FRAME_DEADLINE", "Frame 超过 Capture Deadline。");
            }

            var frameAge = frame.ReceivedAtUtc - frame.HardwareTimestampUtc;
            if (frameAge < TimeSpan.Zero || frameAge > TimeSpan.FromMilliseconds(pending.MaximumFrameAgeMs))
            {
                return Failure(FrameCorrelationStatus.FrameTooOld, "FRAME_AGE", "Frame 硬件时间戳无效或超过最大帧龄。");
            }

            if ((frame.QualityFlags & (FrameQualityFlags.Incomplete | FrameQualityFlags.Corrupt)) != 0)
            {
                return Failure(FrameCorrelationStatus.InvalidQuality, "FRAME_QUALITY", "Frame 标记为不完整或损坏。");
            }

            return new FrameCorrelationResult
            {
                Status = FrameCorrelationStatus.Correlated,
                Frame = frame
            };
        }
    }

    private static FrameCorrelationResult Failure(FrameCorrelationStatus status, string code, string message) => new()
    {
        Status = status,
        Error = new StructuredError
        {
            Category = ErrorCategory.FrameCorrelation,
            Code = code,
            Message = message
        }
    };

    private void Retain<T>(HashSet<T> set, Queue<T> order, T value)
        where T : notnull
    {
        if (!set.Add(value))
        {
            return;
        }

        order.Enqueue(value);
        while (order.Count > _retentionCapacity && order.TryDequeue(out var expired))
        {
            set.Remove(expired);
        }
    }

    private sealed record PendingCapture(CaptureRequest Request, string StationId, int MaximumFrameAgeMs);
}
