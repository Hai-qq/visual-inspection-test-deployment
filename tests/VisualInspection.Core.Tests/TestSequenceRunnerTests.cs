using VisualInspection.Core.Analysis;
using VisualInspection.Core.Configuration;
using VisualInspection.Core.Domain;
using VisualInspection.Core.Execution;
using VisualInspection.Core.Imaging;
using VisualInspection.Core.Rules;

namespace VisualInspection.Core.Tests;

public sealed class TestSequenceRunnerTests
{
    [Fact]
    public async Task RunAsync_EvaluatesNormalRulesOnOneSharedFrameAndAppliesDelay()
    {
        var fixture = CreateNormalFixture(secondRule: true);
        var source = new FakeImageSource([Frame(1)]);
        var provider = new FakeProvider(new Dictionary<long, FrameInspectionObservation>
        {
            [1] = Observation((fixture.Target1, 4), (fixture.Target2, 1))
        });
        var delay = new RecordingDelayProvider();

        var result = await new TestSequenceRunner(delay).RunAsync(
            fixture.Project, fixture.Sequence, source, provider);

        Assert.Equal(InspectionVerdict.Pass, result.Verdict);
        Assert.Equal(InspectionVerdict.Pass, result.Items.Single().Verdict);
        Assert.Equal(1, source.ReadCount);
        Assert.Equal([125], delay.Delays);
        Assert.Contains("Target 1=4", result.Items.Single().Measured);
        Assert.Contains("Target 2=1", result.Items.Single().Measured);
    }

    [Fact]
    public async Task RunAsync_UsesSpatialDetectionsInsteadOfPrecomputedCountsWhenAvailable()
    {
        var fixture = CreateNormalFixture();
        var bindingId = fixture.Project.Targets[0].ModelBindings[0].Id;
        var spatialRule = fixture.Sequence.Items[0].Rules[0] with
        {
            Threshold = 1,
            ConfidenceThreshold = 0.8,
            Scope = new RegionScopeDefinition
            {
                Type = RegionType.Roi,
                Regions =
                [
                    new RegionOfInterestDefinition
                    {
                        Name = "左半区",
                        X1 = 0,
                        Y1 = 0,
                        X2 = 5,
                        Y2 = 10,
                        ReferenceWidth = 10,
                        ReferenceHeight = 10
                    }
                ]
            }
        };
        var sequence = fixture.Sequence with
        {
            Items = [fixture.Sequence.Items[0] with { Rules = [spatialRule] }]
        };
        var project = fixture.Project with { TestSequences = [sequence] };
        var provider = new FakeProvider(new Dictionary<long, FrameInspectionObservation>
        {
            [1] = new FrameInspectionObservation
            {
                TargetCounts = new Dictionary<Guid, int> { [fixture.Target1] = 99 },
                Detections =
                [
                    Detection(fixture.Target1, bindingId, 1, 1, 3, 3, 0.9),
                    Detection(fixture.Target1, bindingId, 7, 1, 9, 3, 0.9),
                    Detection(fixture.Target1, bindingId, 1, 5, 3, 7, 0.7)
                ]
            }
        });

        var result = await new TestSequenceRunner(new RecordingDelayProvider()).RunAsync(
            project, sequence, new FakeImageSource([Frame(1)]), provider);

        Assert.Equal(InspectionVerdict.Pass, result.Verdict);
        Assert.Contains("Target 1=1", result.Items.Single().Measured);
    }

    [Fact]
    public async Task RunAsync_ReturnsBusinessFailWithoutConvertingItToError()
    {
        var fixture = CreateNormalFixture();
        var source = new FakeImageSource([Frame(1)]);
        var provider = new FakeProvider(new Dictionary<long, FrameInspectionObservation>
        {
            [1] = Observation((fixture.Target1, 3))
        });

        var result = await new TestSequenceRunner(new RecordingDelayProvider()).RunAsync(
            fixture.Project, fixture.Sequence, source, provider);

        Assert.Equal(InspectionVerdict.Fail, result.Verdict);
        Assert.Null(result.Items.Single().ErrorCode);
    }

    [Fact]
    public async Task RunAsync_DoesNotFailRunForOptionalBusinessFail()
    {
        var fixture = CreateNormalFixture();
        var optionalSequence = fixture.Sequence with
        {
            Items = [fixture.Sequence.Items[0] with { IsRequired = false }]
        };
        var project = fixture.Project with { TestSequences = [optionalSequence] };
        var provider = new FakeProvider(new Dictionary<long, FrameInspectionObservation>
        {
            [1] = Observation((fixture.Target1, 3))
        });

        var result = await new TestSequenceRunner(new RecordingDelayProvider()).RunAsync(
            project, optionalSequence, new FakeImageSource([Frame(1)]), provider);

        Assert.Equal(InspectionVerdict.Pass, result.Verdict);
        Assert.Equal(InspectionVerdict.Fail, result.Items.Single().Verdict);
        Assert.False(result.Items.Single().IsRequired);
    }

    [Fact]
    public async Task RunAsync_ReportsErrorWhenNormalItemHasNoFrame()
    {
        var fixture = CreateNormalFixture();

        var result = await new TestSequenceRunner(new RecordingDelayProvider()).RunAsync(
            fixture.Project,
            fixture.Sequence,
            new FakeImageSource([]),
            new FakeProvider(new Dictionary<long, FrameInspectionObservation>()));

        Assert.Equal(InspectionVerdict.Error, result.Verdict);
        Assert.Equal("ITEM_EXECUTION_ERROR", result.Items.Single().ErrorCode);
    }

    [Fact]
    public async Task RunAsync_ConfirmsPoseStepsInConfiguredOrderAndHoldDuration()
    {
        var fixture = CreatePoseFixture(maximumWaitMs: 500);
        var frames = Enumerable.Range(1, 6).Select(Frame).ToArray();
        var provider = new FakeProvider(new Dictionary<long, FrameInspectionObservation>
        {
            [1] = Observation(actions: ["pick"]),
            [2] = Observation(actions: ["pick"]),
            [3] = Observation(actions: ["place"]),
            [4] = Observation(actions: ["place"]),
            [5] = Observation(actions: ["clear"]),
            [6] = Observation(actions: ["clear"])
        });

        var result = await new TestSequenceRunner(new RecordingDelayProvider()).RunAsync(
            fixture.Project, fixture.Sequence, new FakeImageSource(frames), provider);

        Assert.Equal(InspectionVerdict.Pass, result.Verdict);
        Assert.Contains("Pick → Place → Confirm", result.Items.Single().Measured);
    }

    [Fact]
    public async Task RunAsync_ReturnsPoseFailWhenExpectedActionTimesOut()
    {
        var fixture = CreatePoseFixture(maximumWaitMs: 200);
        var frames = Enumerable.Range(1, 3).Select(Frame).ToArray();
        var provider = new FakeProvider(new Dictionary<long, FrameInspectionObservation>
        {
            [1] = Observation(actions: ["place"]),
            [2] = Observation(actions: ["place"]),
            [3] = Observation(actions: ["pick"])
        });

        var result = await new TestSequenceRunner(new RecordingDelayProvider()).RunAsync(
            fixture.Project, fixture.Sequence, new FakeImageSource(frames), provider);

        Assert.Equal(InspectionVerdict.Fail, result.Verdict);
        Assert.Contains("超过", result.Items.Single().Measured);
    }

    [Fact]
    public async Task RunAsync_PreservesCompletedResultsWhenCancelled()
    {
        var fixture = CreateNormalFixture();
        var delay = new CancellingDelayProvider();

        var result = await new TestSequenceRunner(delay).RunAsync(
            fixture.Project,
            fixture.Sequence,
            new FakeImageSource([Frame(1)]),
            new FakeProvider(new Dictionary<long, FrameInspectionObservation>()));

        Assert.True(result.WasStopped);
        Assert.Equal("OPERATOR_STOP", result.ErrorCode);
        Assert.Empty(result.Items);
    }

    private static NormalFixture CreateNormalFixture(bool secondRule = false)
    {
        var sourceId = Guid.NewGuid();
        var target1 = Guid.NewGuid();
        var target2 = Guid.NewGuid();
        var modelId = Guid.NewGuid();
        var binding1 = Guid.NewGuid();
        var binding2 = Guid.NewGuid();
        var rules = new List<TargetRuleDefinition>
        {
            Rule(target1, binding1, 4)
        };
        if (secondRule)
        {
            rules.Add(Rule(target2, binding2, 1));
        }

        var sequence = new TestSequenceDefinition
        {
            Name = "Normal",
            Version = "1",
            InputSourceId = sourceId,
            DefaultDelayMs = 125,
            Items =
            [
                new TestItemDefinition
                {
                    Order = 1,
                    Name = "Counts",
                    Type = TestItemType.Normal,
                    RuleOperator = RuleLogicalOperator.And,
                    Rules = rules
                }
            ]
        };
        var project = new ProjectConfiguration
        {
            Name = "Test",
            Models =
            [
                new ModelDefinition { Id = modelId, Name = "Model", Version = "1", FilePath = "model.onnx" }
            ],
            Targets =
            [
                Target(target1, "Target 1", binding1, modelId),
                Target(target2, "Target 2", binding2, modelId)
            ],
            InputSources =
            [
                new InputSourceDefinition
                {
                    Id = sourceId,
                    Name = "Memory",
                    Type = InputSourceType.Folder,
                    Folder = new FolderInputOptions { FolderPath = ".", PoseFrameIntervalMs = 100 }
                }
            ],
            TestSequences = [sequence]
        };
        return new NormalFixture(project, sequence, target1, target2);
    }

    private static NormalFixture CreatePoseFixture(int maximumWaitMs)
    {
        var fixture = CreateNormalFixture();
        var binding = fixture.Project.Targets[0].ModelBindings[0].Id;
        var poseItem = new TestItemDefinition
        {
            Order = 1,
            Name = "Action",
            Type = TestItemType.PoseSequence,
            PoseSteps =
            [
                PoseStep(1, "Pick", "pick", binding, maximumWaitMs),
                PoseStep(2, "Place", "place", binding, maximumWaitMs),
                PoseStep(3, "Confirm", "clear", binding, maximumWaitMs)
            ]
        };
        var sequence = fixture.Sequence with { Items = [poseItem], DefaultDelayMs = 0 };
        return fixture with
        {
            Project = fixture.Project with { TestSequences = [sequence] },
            Sequence = sequence
        };
    }

    private static TargetRuleDefinition Rule(Guid targetId, Guid bindingId, int threshold) => new()
    {
        TargetId = targetId,
        ModelBindingId = bindingId,
        Metric = QuantityMetric.PresentCount,
        Operator = ComparisonOperator.Equal,
        Threshold = threshold,
        OutcomeWhenMatched = InspectionVerdict.Pass
    };

    private static TargetDefinition Target(Guid id, string name, Guid bindingId, Guid modelId) => new()
    {
        Id = id,
        Name = name,
        ModelBindings =
        [
            new ModelBindingDefinition
            {
                Id = bindingId,
                ModelId = modelId,
                ModelVersion = "1",
                OutputLabelId = 0
            }
        ]
    };

    private static TargetDetection Detection(
        Guid targetId,
        Guid bindingId,
        double x1,
        double y1,
        double x2,
        double y2,
        double confidence) => new()
        {
            TargetId = targetId,
            ModelBindingId = bindingId,
            X1 = x1,
            Y1 = y1,
            X2 = x2,
            Y2 = y2,
            Confidence = confidence
        };

    private static PoseStepDefinition PoseStep(
        int order,
        string name,
        string action,
        Guid binding,
        int maximumWaitMs) => new()
        {
            Order = order,
            Name = name,
            ActionCondition = action,
            ModelBindingId = binding,
            MinimumHoldMs = 200,
            MaximumWaitMs = maximumWaitMs
        };

    private static ImageFrame Frame(int number) => new()
    {
        SourceId = Guid.Parse("40000000-0000-0000-0000-000000000001"),
        SequenceNumber = number,
        CapturedAtUtc = DateTimeOffset.UtcNow,
        Width = 10,
        Height = 10,
        DataFormat = ImageFrameDataFormat.EncodedBmp,
        Data = ReadOnlyMemory<byte>.Empty,
        Origin = $"frame-{number:00}.bmp"
    };

    private static FrameInspectionObservation Observation(
        params (Guid Target, int Count)[] counts) => Observation(counts, []);

    private static FrameInspectionObservation Observation(
        (Guid Target, int Count)[]? counts = null,
        IReadOnlyCollection<string>? actions = null) => new()
        {
            TargetCounts = (counts ?? []).ToDictionary(pair => pair.Target, pair => pair.Count),
            Actions = new HashSet<string>(actions ?? [], StringComparer.OrdinalIgnoreCase)
        };

    private sealed record NormalFixture(
        ProjectConfiguration Project,
        TestSequenceDefinition Sequence,
        Guid Target1,
        Guid Target2);

    private sealed class RecordingDelayProvider : IDelayProvider
    {
        public List<int> Delays { get; } = [];

        public Task DelayAsync(int milliseconds, CancellationToken cancellationToken = default)
        {
            Delays.Add(milliseconds);
            return Task.CompletedTask;
        }
    }

    private sealed class CancellingDelayProvider : IDelayProvider
    {
        public Task DelayAsync(int milliseconds, CancellationToken cancellationToken = default) =>
            throw new OperationCanceledException(cancellationToken);
    }

    private sealed class FakeProvider(IReadOnlyDictionary<long, FrameInspectionObservation> observations)
        : IInspectionProvider
    {
        public string Name => "Fake";

        public Task<FrameInspectionObservation> AnalyzeAsync(
            ImageFrame frame,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(observations[frame.SequenceNumber]);
    }

    private sealed class FakeImageSource(IEnumerable<ImageFrame> frames) : IImageSource
    {
        private readonly IReadOnlyList<ImageFrame> _frames = frames.ToArray();
        private int _index;

        public Guid Id { get; } = Guid.NewGuid();
        public string Name => "Fake";
        public ImageSourceState State { get; private set; } = ImageSourceState.Closed;
        public ImageSourceProgress Progress => new(_index, _frames.Count, 0, null);
        public int ReadCount { get; private set; }

        public Task OpenAsync(CancellationToken cancellationToken = default)
        {
            _index = 0;
            State = ImageSourceState.Ready;
            return Task.CompletedTask;
        }

        public Task<ImageFrame?> ReadAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ReadCount++;
            if (_index >= _frames.Count)
            {
                State = ImageSourceState.Completed;
                return Task.FromResult<ImageFrame?>(null);
            }

            State = ImageSourceState.Streaming;
            return Task.FromResult<ImageFrame?>(_frames[_index++]);
        }

        public Task ResetAsync(CancellationToken cancellationToken = default)
        {
            _index = 0;
            return Task.CompletedTask;
        }

        public Task CloseAsync(CancellationToken cancellationToken = default)
        {
            State = ImageSourceState.Closed;
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
