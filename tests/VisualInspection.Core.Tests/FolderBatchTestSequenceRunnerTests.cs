using VisualInspection.Core.Analysis;
using VisualInspection.Core.Configuration;
using VisualInspection.Core.Domain;
using VisualInspection.Core.Execution;
using VisualInspection.Core.Imaging;
using VisualInspection.Core.Rules;

namespace VisualInspection.Core.Tests;

public sealed class FolderBatchTestSequenceRunnerTests
{
    [Fact]
    public async Task RunAsync_ProcessesEveryImageAndRunsAllNormalItemsOnOneInference()
    {
        var fixture = CreateFixture();
        var source = new FakeImageSource([Frame(1), Frame(2), Frame(3)]);
        var provider = new RecordingProvider((frame, _) => Task.FromResult(Observation(
            (fixture.Target1, 1),
            (fixture.Target2, frame.SequenceNumber == 2 ? 0 : 1))));
        var completed = new List<FolderBatchImageRunResult>();

        var result = await new FolderBatchTestSequenceRunner(new NoDelayProvider()).RunAsync(
            fixture.Project,
            fixture.Sequence,
            source,
            provider,
            imageCompleted: new InlineProgress<FolderBatchImageRunResult>(completed.Add));

        Assert.Equal(3, result.TotalFileCount);
        Assert.Equal(3, result.Images.Count);
        Assert.Equal(3, completed.Count);
        Assert.Equal(4, source.ReadCount);
        Assert.Equal([1, 2, 3], provider.AnalyzedFrames);
        Assert.All(result.Images, image => Assert.Equal(2, image.RunResult.Items.Count));
        Assert.Equal(2, result.Images.Count(image => image.RunResult.Verdict == InspectionVerdict.Pass));
        Assert.Single(result.Images.Where(image => image.RunResult.Verdict == InspectionVerdict.Fail));
        Assert.Equal(InspectionVerdict.Fail, result.Verdict);
        Assert.Contains("已检测 3 张", result.Summary);
        Assert.Equal(ImageSourceState.Closed, source.State);
    }

    [Fact]
    public async Task RunAsync_StopsAfterInferenceErrorAndPreservesCompletedImages()
    {
        var fixture = CreateFixture(singleItem: true);
        var source = new FakeImageSource([Frame(1), Frame(2), Frame(3)]);
        var provider = new RecordingProvider((frame, _) => frame.SequenceNumber == 2
            ? throw new InvalidOperationException("inference failed")
            : Task.FromResult(Observation((fixture.Target1, 1))));

        var result = await new FolderBatchTestSequenceRunner(new NoDelayProvider()).RunAsync(
            fixture.Project,
            fixture.Sequence,
            source,
            provider);

        Assert.Equal(2, result.Images.Count);
        Assert.Equal([1, 2], provider.AnalyzedFrames);
        Assert.Equal(InspectionVerdict.Pass, result.Images[0].RunResult.Verdict);
        Assert.Equal(InspectionVerdict.Error, result.Images[1].RunResult.Verdict);
        Assert.Equal(InspectionVerdict.Error, result.Verdict);
        Assert.Contains("错误 1", result.Summary);
        Assert.Equal(ImageSourceState.Closed, source.State);
    }

    [Fact]
    public async Task RunAsync_RejectsPoseSequenceWithoutOpeningFolder()
    {
        var fixture = CreateFixture(singleItem: true);
        var bindingId = fixture.Project.Targets[0].ModelBindings[0].Id;
        var poseSequence = fixture.Sequence with
        {
            Items =
            [
                new TestItemDefinition
                {
                    Order = 1,
                    Name = "Pose",
                    Type = TestItemType.PoseSequence,
                    PoseSteps =
                    [
                        new PoseStepDefinition
                        {
                            Order = 1,
                            Name = "Pick",
                            ActionCondition = "pick",
                            ModelBindingId = bindingId,
                            MinimumHoldMs = 100,
                            MaximumWaitMs = 1000
                        }
                    ]
                }
            ]
        };
        var source = new FakeImageSource([Frame(1)]);

        var exception = await Assert.ThrowsAsync<NotSupportedException>(() =>
            new FolderBatchTestSequenceRunner(new NoDelayProvider()).RunAsync(
                fixture.Project,
                poseSequence,
                source,
                new RecordingProvider((_, _) => Task.FromResult(Observation()))));

        Assert.Contains("普通检测项", exception.Message);
        Assert.Equal(0, source.OpenCount);
    }

    private static Fixture CreateFixture(bool singleItem = false)
    {
        var sourceId = Guid.NewGuid();
        var modelId = Guid.NewGuid();
        var target1 = Guid.NewGuid();
        var target2 = Guid.NewGuid();
        var binding1 = Guid.NewGuid();
        var binding2 = Guid.NewGuid();
        var items = new List<TestItemDefinition>
        {
            Item(1, "First", target1, binding1)
        };
        if (!singleItem)
        {
            items.Add(Item(2, "Second", target2, binding2));
        }

        var sequence = new TestSequenceDefinition
        {
            Name = "Batch",
            Version = "1",
            InputSourceId = sourceId,
            DefaultDelayMs = 0,
            Items = items
        };
        var project = new ProjectConfiguration
        {
            Name = "Batch test",
            Models = [new ModelDefinition { Id = modelId, Name = "Model", Version = "1", FilePath = "model.onnx" }],
            Targets =
            [
                Target(target1, "Target 1", binding1, modelId, 0),
                Target(target2, "Target 2", binding2, modelId, 1)
            ],
            InputSources =
            [
                new InputSourceDefinition
                {
                    Id = sourceId,
                    Name = "Folder",
                    Type = InputSourceType.Folder,
                    Folder = new FolderInputOptions { FolderPath = "." }
                }
            ],
            TestSequences = [sequence]
        };
        return new Fixture(project, sequence, target1, target2);
    }

    private static TestItemDefinition Item(int order, string name, Guid targetId, Guid bindingId) => new()
    {
        Order = order,
        Name = name,
        Type = TestItemType.Normal,
        RuleOperator = RuleLogicalOperator.And,
        Rules =
        [
            new TargetRuleDefinition
            {
                TargetId = targetId,
                ModelBindingId = bindingId,
                Metric = QuantityMetric.PresentCount,
                Operator = ComparisonOperator.Equal,
                Threshold = 1,
                OutcomeWhenMatched = InspectionVerdict.Pass
            }
        ]
    };

    private static TargetDefinition Target(
        Guid id,
        string name,
        Guid bindingId,
        Guid modelId,
        int labelId) => new()
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
                    OutputLabelId = labelId
                }
            ]
        };

    private static ImageFrame Frame(long number) => new()
    {
        SourceId = Guid.Parse("80000000-0000-0000-0000-000000000001"),
        SequenceNumber = number,
        CapturedAtUtc = DateTimeOffset.UtcNow,
        Width = 10,
        Height = 10,
        DataFormat = ImageFrameDataFormat.EncodedBmp,
        Data = ReadOnlyMemory<byte>.Empty,
        Origin = $"frame-{number:00}.bmp"
    };

    private static FrameInspectionObservation Observation(params (Guid Target, int Count)[] counts) => new()
    {
        TargetCounts = counts.ToDictionary(pair => pair.Target, pair => pair.Count)
    };

    private sealed record Fixture(
        ProjectConfiguration Project,
        TestSequenceDefinition Sequence,
        Guid Target1,
        Guid Target2);

    private sealed class RecordingProvider(
        Func<ImageFrame, CancellationToken, Task<FrameInspectionObservation>> analyze) : IInspectionProvider
    {
        public string Name => "Recording";
        public List<long> AnalyzedFrames { get; } = [];

        public Task<FrameInspectionObservation> AnalyzeAsync(
            ImageFrame frame,
            CancellationToken cancellationToken = default)
        {
            AnalyzedFrames.Add(frame.SequenceNumber);
            return analyze(frame, cancellationToken);
        }
    }

    private sealed class FakeImageSource(IEnumerable<ImageFrame> frames) : IImageSource
    {
        private readonly IReadOnlyList<ImageFrame> _frames = frames.ToArray();
        private int _index;

        public Guid Id { get; } = Guid.NewGuid();
        public string Name => "Fake folder";
        public ImageSourceState State { get; private set; } = ImageSourceState.Closed;
        public ImageSourceProgress Progress => new(_index, _frames.Count, 0, _index == 0 ? null : _frames[_index - 1].Origin);
        public int OpenCount { get; private set; }
        public int ReadCount { get; private set; }

        public Task OpenAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            OpenCount++;
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

            var frame = _frames[_index++];
            State = _index >= _frames.Count ? ImageSourceState.Completed : ImageSourceState.Streaming;
            return Task.FromResult<ImageFrame?>(frame);
        }

        public Task ResetAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _index = 0;
            State = ImageSourceState.Ready;
            return Task.CompletedTask;
        }

        public Task CloseAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            State = ImageSourceState.Closed;
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class NoDelayProvider : IDelayProvider
    {
        public Task DelayAsync(int milliseconds, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }
    }

    private sealed class InlineProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }
}
