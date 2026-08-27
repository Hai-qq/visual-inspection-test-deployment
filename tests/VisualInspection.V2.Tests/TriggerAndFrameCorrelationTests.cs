using VisualInspection.Core.V2.Configuration;
using VisualInspection.Core.V2.Execution;

namespace VisualInspection.V2.Tests;

public sealed class TriggerAndFrameCorrelationTests
{
    [Fact]
    public void RisingAndFallingEdges_FireOnlyOnMatchingStableTransition()
    {
        var now = DateTimeOffset.UtcNow;
        var rising = Binding(TriggerCondition.RisingEdge);
        var falling = Binding(TriggerCondition.FallingEdge);
        var processor = new TriggerSignalProcessor();

        Assert.False(processor.Process(rising, false, now).IsTriggered);
        Assert.True(processor.Process(rising, true, now.AddMilliseconds(1)).IsTriggered);
        Assert.False(processor.Process(rising, true, now.AddMilliseconds(2)).IsTriggered);
        Assert.False(processor.Process(falling, true, now).IsTriggered);
        Assert.True(processor.Process(falling, false, now.AddMilliseconds(1)).IsTriggered);
    }

    [Fact]
    public void HighLevel_IsOneShotUntilRearmed()
    {
        var now = DateTimeOffset.UtcNow;
        var binding = Binding(TriggerCondition.HighLevel);
        var processor = new TriggerSignalProcessor();

        Assert.True(processor.Process(binding, true, now).IsTriggered);
        Assert.False(processor.Process(binding, true, now.AddMilliseconds(1)).IsTriggered);
        Assert.False(processor.Process(binding, false, now.AddMilliseconds(2)).IsTriggered);
        Assert.True(processor.Process(binding, true, now.AddMilliseconds(3)).IsTriggered);
    }

    [Fact]
    public void LowLevel_FiresOnInitialLowAndAfterRearm()
    {
        var now = DateTimeOffset.UtcNow;
        var binding = Binding(TriggerCondition.LowLevel);
        var processor = new TriggerSignalProcessor();

        Assert.True(processor.Process(binding, false, now).IsTriggered);
        Assert.False(processor.Process(binding, false, now.AddMilliseconds(1)).IsTriggered);
        Assert.False(processor.Process(binding, true, now.AddMilliseconds(2)).IsTriggered);
        Assert.True(processor.Process(binding, false, now.AddMilliseconds(3)).IsTriggered);
    }

    [Fact]
    public void Debounce_RequiresStableCandidateForConfiguredDuration()
    {
        var now = DateTimeOffset.UtcNow;
        var binding = Binding(TriggerCondition.RisingEdge) with { DebounceMs = 50 };
        var processor = new TriggerSignalProcessor();

        Assert.True(processor.Process(binding, false, now).IsDebouncing);
        Assert.False(processor.Process(binding, false, now.AddMilliseconds(51)).IsTriggered);
        Assert.True(processor.Process(binding, true, now.AddMilliseconds(60)).IsDebouncing);
        Assert.False(processor.Process(binding, true, now.AddMilliseconds(100)).IsTriggered);
        Assert.True(processor.Process(binding, true, now.AddMilliseconds(111)).IsTriggered);
    }

    [Fact]
    public void MatchingFrame_Correlates_AndSecondCopyIsDuplicate()
    {
        var now = DateTimeOffset.UtcNow;
        var (service, request, frame) = CreateCorrelation(now);

        var first = service.Correlate(frame, now.AddMilliseconds(20));
        var duplicate = service.Correlate(frame, now.AddMilliseconds(30));

        Assert.True(first.IsCorrelated);
        Assert.Equal(FrameCorrelationStatus.DuplicateFrame, duplicate.Status);
    }

    [Theory]
    [InlineData("product")]
    [InlineData("trigger")]
    [InlineData("station")]
    public void IdentityMismatch_NeverCorrelates(string mismatch)
    {
        var now = DateTimeOffset.UtcNow;
        var (service, request, frame) = CreateCorrelation(now);
        frame = mismatch switch
        {
            "product" => frame with { ProductId = "OTHER" },
            "trigger" => frame with { TriggerEventId = Guid.NewGuid() },
            "station" => frame with { StationId = "OTHER" },
            _ => frame
        };

        var result = service.Correlate(frame, now.AddMilliseconds(20));

        Assert.False(result.IsCorrelated);
        Assert.Equal(ErrorCategory.FrameCorrelation, result.Error?.Category);
    }

    [Fact]
    public void UnknownAndLateFrames_AreRejected()
    {
        var now = DateTimeOffset.UtcNow;
        var service = new FrameCorrelationService();
        var unknown = V2TestFactory.CreateFrame(now) with { ReceivedAtUtc = now };
        var unknownResult = service.Correlate(unknown, now);
        var (registeredService, request, registeredFrame) = CreateCorrelation(now);
        var lateResult = registeredService.Correlate(
            registeredFrame with { ReceivedAtUtc = request.DeadlineUtc.AddMilliseconds(1) },
            request.DeadlineUtc.AddMilliseconds(1));

        Assert.Equal(FrameCorrelationStatus.UnknownCapture, unknownResult.Status);
        Assert.Equal(FrameCorrelationStatus.DeadlineExceeded, lateResult.Status);
    }

    [Fact]
    public void OldFrameAndCancelledRun_AreRejected()
    {
        var now = DateTimeOffset.UtcNow;
        var (service, request, frame) = CreateCorrelation(now, maximumFrameAgeMs: 50);
        var old = service.Correlate(
            frame with { HardwareTimestampUtc = now.AddMilliseconds(-100), ReceivedAtUtc = now },
            now);
        var (cancelledService, cancelledRequest, cancelledFrame) = CreateCorrelation(now);
        cancelledService.CancelRun(cancelledRequest.RunId);
        var cancelled = cancelledService.Correlate(cancelledFrame, now);

        Assert.Equal(FrameCorrelationStatus.FrameTooOld, old.Status);
        Assert.Equal(FrameCorrelationStatus.LateForCancelledRun, cancelled.Status);
    }

    [Fact]
    public void InvalidQuality_IsRejected()
    {
        var now = DateTimeOffset.UtcNow;
        var (service, _, frame) = CreateCorrelation(now);

        var result = service.Correlate(frame with { QualityFlags = FrameQualityFlags.Corrupt }, now);

        Assert.Equal(FrameCorrelationStatus.InvalidQuality, result.Status);
    }

    [Fact]
    public void CompletedAndCancelledCaptureTombstones_AreBounded()
    {
        var now = DateTimeOffset.UtcNow;
        var service = new FrameCorrelationService(retentionCapacity: 2);
        var completedFrames = Enumerable.Range(0, 3)
            .Select(index => RegisterCorrelation(service, now.AddMilliseconds(index), index + 1))
            .ToArray();

        foreach (var frame in completedFrames)
        {
            Assert.True(service.Correlate(frame.Frame, frame.Frame.ReceivedAtUtc).IsCorrelated);
        }

        Assert.Equal(FrameCorrelationStatus.UnknownCapture, service.Correlate(completedFrames[0].Frame, now.AddSeconds(1)).Status);
        Assert.Equal(FrameCorrelationStatus.DuplicateFrame, service.Correlate(completedFrames[2].Frame, now.AddSeconds(1)).Status);

        var cancelled = RegisterCorrelation(service, now.AddSeconds(2), 10);
        service.CancelRun(cancelled.RunId);
        Assert.Equal(FrameCorrelationStatus.LateForCancelledRun, service.Correlate(cancelled.Frame, now.AddSeconds(2)).Status);
    }

    private static ExternalTriggerBinding Binding(TriggerCondition condition) => new()
    {
        BindingId = Guid.NewGuid(),
        LogicalSignalTag = "Line.PartReady",
        TriggerCondition = condition,
        DebounceMs = 0,
        TimeoutMs = 1000,
        MaxConcurrency = 1,
        QueueCapacity = 1
    };

    private static (FrameCorrelationService Service, CaptureRequest Request, FrameEnvelope Frame) CreateCorrelation(
        DateTimeOffset now,
        int maximumFrameAgeMs = 1000)
    {
        var service = new FrameCorrelationService();
        var request = new CaptureRequest
        {
            RunId = Guid.NewGuid(),
            TriggerEventId = Guid.NewGuid(),
            ProductId = "P-1",
            SourceBindingId = Guid.NewGuid(),
            RequestedAtUtc = now,
            DeadlineUtc = now.AddSeconds(1)
        };
        service.Register(request, "Station-1", maximumFrameAgeMs);
        var frame = V2TestFactory.CreateFrame(now, frameCounter: 7) with
        {
            CaptureId = request.CaptureId,
            TriggerEventId = request.TriggerEventId,
            ProductId = request.ProductId,
            StationId = "Station-1",
            HardwareTimestampUtc = now,
            ReceivedAtUtc = now.AddMilliseconds(10)
        };
        return (service, request, frame);
    }

    private static RegisteredFrame RegisterCorrelation(
        FrameCorrelationService service,
        DateTimeOffset now,
        long frameCounter)
    {
        var request = new CaptureRequest
        {
            RunId = Guid.NewGuid(),
            TriggerEventId = Guid.NewGuid(),
            ProductId = $"P-{frameCounter}",
            SourceBindingId = Guid.NewGuid(),
            RequestedAtUtc = now,
            DeadlineUtc = now.AddSeconds(5)
        };
        service.Register(request, "Station-1", 1000);
        var frame = V2TestFactory.CreateFrame(now, frameCounter) with
        {
            CaptureId = request.CaptureId,
            TriggerEventId = request.TriggerEventId,
            ProductId = request.ProductId,
            StationId = "Station-1",
            HardwareTimestampUtc = now,
            ReceivedAtUtc = now.AddMilliseconds(10)
        };
        return new RegisteredFrame(request.RunId, frame);
    }

    private sealed record RegisteredFrame(Guid RunId, FrameEnvelope Frame);
}
