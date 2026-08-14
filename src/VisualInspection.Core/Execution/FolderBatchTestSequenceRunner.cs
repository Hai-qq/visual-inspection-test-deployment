using VisualInspection.Core.Analysis;
using VisualInspection.Core.Configuration;
using VisualInspection.Core.Domain;
using VisualInspection.Core.Imaging;

namespace VisualInspection.Core.Execution;

public sealed record FolderBatchImageRunResult
{
    public required int SourceIndex { get; init; }
    public required int TotalFileCount { get; init; }
    public string? FrameOrigin { get; init; }
    public required TestRunResult RunResult { get; init; }
}

public sealed record FolderBatchRunResult
{
    public required Guid BatchId { get; init; }
    public required DateTimeOffset StartedAtUtc { get; init; }
    public required DateTimeOffset CompletedAtUtc { get; init; }
    public required InspectionVerdict Verdict { get; init; }
    public required IReadOnlyList<FolderBatchImageRunResult> Images { get; init; }
    public required int TotalFileCount { get; init; }
    public required int SkippedFileCount { get; init; }
    public required bool WasStopped { get; init; }
    public required string Summary { get; init; }
}

public sealed class FolderBatchTestSequenceRunner(IDelayProvider? delayProvider = null)
{
    private readonly TestSequenceRunner _sequenceRunner = new(delayProvider);

    public async Task<FolderBatchRunResult> RunAsync(
        ProjectConfiguration project,
        TestSequenceDefinition sequence,
        IImageSource imageSource,
        IInspectionProvider inspectionProvider,
        IProgress<TestRunUpdate>? progress = null,
        IProgress<FolderBatchImageRunResult>? imageCompleted = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(sequence);
        ArgumentNullException.ThrowIfNull(imageSource);
        ArgumentNullException.ThrowIfNull(inspectionProvider);

        var enabledItems = sequence.Items
            .Where(item => item.Enabled)
            .OrderBy(item => item.Order)
            .ToArray();
        if (enabledItems.Length == 0)
        {
            throw new InvalidDataException("当前测试序列中没有已启用的测试项。");
        }

        if (enabledItems.Any(item => item.Type != TestItemType.Normal))
        {
            throw new NotSupportedException("文件夹逐图批量测试当前只支持普通检测项；姿态时序仍按连续帧执行。");
        }

        var sourceDefinition = project.InputSources.FirstOrDefault(source => source.Id == sequence.InputSourceId)
            ?? throw new InvalidDataException("测试序列引用的图源不存在。");
        if (sourceDefinition.Type != InputSourceType.Folder)
        {
            throw new InvalidOperationException("文件夹批量执行器只能用于文件夹图源。");
        }

        var batchId = Guid.NewGuid();
        var startedAt = DateTimeOffset.UtcNow;
        var imageResults = new List<FolderBatchImageRunResult>();
        var totalFileCount = 0;
        var skippedFileCount = 0;
        var wasStopped = false;

        try
        {
            await imageSource.OpenAsync(cancellationToken);
            totalFileCount = imageSource.Progress.TotalCount;

            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var frame = await imageSource.ReadAsync(cancellationToken);
                skippedFileCount = imageSource.Progress.FailedCount;
                if (frame is null)
                {
                    break;
                }

                await using var sharedFrameSource = new RepeatedFrameImageSource(frame, enabledItems.Length);
                var cachedProvider = new SingleFrameCachingInspectionProvider(inspectionProvider, frame);
                var runResult = await _sequenceRunner.RunAsync(
                    project,
                    sequence,
                    sharedFrameSource,
                    cachedProvider,
                    progress,
                    cancellationToken);
                var imageResult = new FolderBatchImageRunResult
                {
                    SourceIndex = imageSource.Progress.CurrentIndex,
                    TotalFileCount = totalFileCount,
                    FrameOrigin = frame.Origin,
                    RunResult = runResult
                };
                imageResults.Add(imageResult);
                imageCompleted?.Report(imageResult);

                if (runResult.WasStopped)
                {
                    wasStopped = true;
                    break;
                }

                if (runResult.Verdict == InspectionVerdict.Error)
                {
                    break;
                }
            }
        }
        catch (OperationCanceledException)
        {
            wasStopped = true;
        }
        finally
        {
            skippedFileCount = Math.Max(skippedFileCount, imageSource.Progress.FailedCount);
            await imageSource.CloseAsync(CancellationToken.None);
        }

        var completedImages = imageResults.Where(result => !result.RunResult.WasStopped).ToArray();
        var verdict = GetBatchVerdict(completedImages, wasStopped);
        var summary = FormatSummary(
            completedImages,
            totalFileCount,
            skippedFileCount,
            wasStopped);
        return new FolderBatchRunResult
        {
            BatchId = batchId,
            StartedAtUtc = startedAt,
            CompletedAtUtc = DateTimeOffset.UtcNow,
            Verdict = verdict,
            Images = imageResults,
            TotalFileCount = totalFileCount,
            SkippedFileCount = skippedFileCount,
            WasStopped = wasStopped,
            Summary = summary
        };
    }

    private static InspectionVerdict GetBatchVerdict(
        IReadOnlyCollection<FolderBatchImageRunResult> completedImages,
        bool wasStopped)
    {
        if (wasStopped || completedImages.Count == 0 ||
            completedImages.Any(result => result.RunResult.Verdict == InspectionVerdict.Error))
        {
            return InspectionVerdict.Error;
        }

        return completedImages.Any(result => result.RunResult.Verdict == InspectionVerdict.Fail)
            ? InspectionVerdict.Fail
            : InspectionVerdict.Pass;
    }

    private static string FormatSummary(
        IReadOnlyCollection<FolderBatchImageRunResult> completedImages,
        int totalFileCount,
        int skippedFileCount,
        bool wasStopped)
    {
        var passCount = completedImages.Count(result => result.RunResult.Verdict == InspectionVerdict.Pass);
        var failCount = completedImages.Count(result => result.RunResult.Verdict == InspectionVerdict.Fail);
        var errorCount = completedImages.Count(result => result.RunResult.Verdict == InspectionVerdict.Error);
        var prefix = wasStopped ? "文件夹批量测试已停止" : "文件夹批量测试完成";
        return $"{prefix}：文件 {totalFileCount} 张，已检测 {completedImages.Count} 张，通过 {passCount}，不通过 {failCount}，错误 {errorCount}，跳过 {skippedFileCount}。";
    }

    private sealed class SingleFrameCachingInspectionProvider(
        IInspectionProvider inner,
        ImageFrame expectedFrame) : IInspectionProvider
    {
        private Task<FrameInspectionObservation>? _observationTask;

        public string Name => inner.Name;

        public Task<FrameInspectionObservation> AnalyzeAsync(
            ImageFrame frame,
            CancellationToken cancellationToken = default)
        {
            if (frame.SourceId != expectedFrame.SourceId || frame.SequenceNumber != expectedFrame.SequenceNumber)
            {
                throw new InvalidOperationException("批量执行缓存只能用于当前文件夹图像。");
            }

            return _observationTask ??= inner.AnalyzeAsync(expectedFrame, cancellationToken);
        }
    }

    private sealed class RepeatedFrameImageSource(ImageFrame frame, int repeatCount) : IImageSource
    {
        private int _readCount;

        public Guid Id => frame.SourceId;
        public string Name => frame.Origin ?? "当前文件夹图像";
        public ImageSourceState State { get; private set; } = ImageSourceState.Closed;
        public ImageSourceProgress Progress => new(
            _readCount,
            repeatCount,
            0,
            _readCount == 0 ? null : frame.Origin);

        public Task OpenAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _readCount = 0;
            State = ImageSourceState.Ready;
            return Task.CompletedTask;
        }

        public Task<ImageFrame?> ReadAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (State == ImageSourceState.Closed)
            {
                throw new InvalidOperationException("读取前必须先打开共享帧图源。");
            }

            if (_readCount >= repeatCount)
            {
                State = ImageSourceState.Completed;
                return Task.FromResult<ImageFrame?>(null);
            }

            _readCount++;
            State = _readCount >= repeatCount ? ImageSourceState.Completed : ImageSourceState.Streaming;
            return Task.FromResult<ImageFrame?>(frame);
        }

        public Task ResetAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _readCount = 0;
            State = ImageSourceState.Ready;
            return Task.CompletedTask;
        }

        public Task CloseAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _readCount = 0;
            State = ImageSourceState.Closed;
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
