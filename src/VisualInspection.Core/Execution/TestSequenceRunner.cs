using VisualInspection.Core.Analysis;
using VisualInspection.Core.Configuration;
using VisualInspection.Core.Domain;
using VisualInspection.Core.Imaging;
using VisualInspection.Core.Rules;

namespace VisualInspection.Core.Execution;

public sealed class TestSequenceRunner(IDelayProvider? delayProvider = null)
{
    private readonly IDelayProvider _delayProvider = delayProvider ?? new SystemDelayProvider();

    public async Task<TestRunResult> RunAsync(
        ProjectConfiguration project,
        TestSequenceDefinition sequence,
        IImageSource imageSource,
        IInspectionProvider inspectionProvider,
        IProgress<TestRunUpdate>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(sequence);
        ArgumentNullException.ThrowIfNull(imageSource);
        ArgumentNullException.ThrowIfNull(inspectionProvider);

        var runId = Guid.NewGuid();
        var startedAt = DateTimeOffset.UtcNow;
        var results = new List<TestItemRunResult>();
        progress?.Report(new TestRunUpdate
        {
            Kind = TestRunUpdateKind.RunStarted,
            Message = $"测试运行 {runId:N} 已启动，分析器：{inspectionProvider.Name}。"
        });

        try
        {
            await imageSource.OpenAsync(cancellationToken);
            foreach (var item in sequence.Items.Where(item => item.Enabled).OrderBy(item => item.Order))
            {
                cancellationToken.ThrowIfCancellationRequested();
                progress?.Report(UpdateFor(item, TestRunUpdateKind.ItemStarted, null, "检测项已开始。"));

                var delayMs = item.DelayMs ?? sequence.DefaultDelayMs;
                await _delayProvider.DelayAsync(delayMs, cancellationToken);

                TestItemRunResult result;
                try
                {
                    result = item.Type switch
                    {
                        TestItemType.Normal => await RunNormalItemAsync(
                            project, item, imageSource, inspectionProvider, progress, cancellationToken),
                        TestItemType.PoseSequence => await RunPoseItemAsync(
                            project, sequence, item, imageSource, inspectionProvider, progress, cancellationToken),
                        _ => throw new InvalidOperationException($"不支持的测试项类型：{item.Type}")
                    };
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    result = new TestItemRunResult
                    {
                        ItemId = item.Id,
                        ItemOrder = item.Order,
                        ItemName = item.Name,
                        IsRequired = item.IsRequired,
                        Verdict = InspectionVerdict.Error,
                        Measured = exception.Message,
                        ErrorCode = "ITEM_EXECUTION_ERROR"
                    };
                }

                results.Add(result);
                progress?.Report(UpdateFor(
                    item,
                    TestRunUpdateKind.ItemCompleted,
                    result.Verdict,
                    result.Measured));

                if (result.Verdict == InspectionVerdict.Error)
                {
                    break;
                }
            }

            var verdict = GetRunVerdict(results);
            var completed = new TestRunResult
            {
                RunId = runId,
                StartedAtUtc = startedAt,
                CompletedAtUtc = DateTimeOffset.UtcNow,
                Verdict = verdict,
                Items = results,
                ErrorCode = verdict == InspectionVerdict.Error ? "RUN_ITEM_ERROR" : null,
                Summary = FormatRunSummary(verdict, results.Count)
            };
            progress?.Report(new TestRunUpdate
            {
                Kind = verdict == InspectionVerdict.Error ? TestRunUpdateKind.RunError : TestRunUpdateKind.RunCompleted,
                Verdict = verdict,
                Message = completed.Summary
            });
            return completed;
        }
        catch (OperationCanceledException)
        {
            var stopped = new TestRunResult
            {
                RunId = runId,
                StartedAtUtc = startedAt,
                CompletedAtUtc = DateTimeOffset.UtcNow,
                Verdict = InspectionVerdict.Error,
                Items = results,
                WasStopped = true,
                ErrorCode = "OPERATOR_STOP",
                Summary = "操作员已停止测试；已完成项目的结果已保留。"
            };
            progress?.Report(new TestRunUpdate
            {
                Kind = TestRunUpdateKind.RunStopped,
                Message = stopped.Summary
            });
            return stopped;
        }
        finally
        {
            await imageSource.CloseAsync(CancellationToken.None);
        }
    }

    private static async Task<TestItemRunResult> RunNormalItemAsync(
        ProjectConfiguration project,
        TestItemDefinition item,
        IImageSource source,
        IInspectionProvider provider,
        IProgress<TestRunUpdate>? progress,
        CancellationToken cancellationToken)
    {
        var frame = await source.ReadAsync(cancellationToken)
            ?? throw new EndOfStreamException("当前检测项没有可用图像。");
        progress?.Report(UpdateFor(item, TestRunUpdateKind.FrameAcquired, null, frame.Origin ?? "已采集图像。", frame));
        var observation = await provider.AnalyzeAsync(frame, cancellationToken);
        progress?.Report(UpdateFor(
            item,
            TestRunUpdateKind.FrameAnalyzed,
            null,
            observation.Detections is null
                ? $"分析结果已就绪。{FormatProviderDetails(observation.ProviderDetails)}"
                : $"已获得 {observation.Detections.Count} 个空间检测框。{FormatProviderDetails(observation.ProviderDetails)}",
            frame,
            observation.Detections));

        var evaluations = item.Rules.Select(rule =>
        {
            var target = project.Targets.First(candidate => candidate.Id == rule.TargetId);
            var countRule = new CountRule(
                target.Name,
                rule.Metric,
                rule.Operator,
                rule.Threshold,
                rule.UpperThreshold,
                rule.ExpectedCount,
                rule.OutcomeWhenMatched);
            var detectedCount = observation.Detections is null
                ? observation.GetTargetCount(rule.TargetId)
                : SpatialDetectionCounter.Count(observation.Detections, rule, frame.Width, frame.Height);
            return CountRuleEvaluator.Evaluate(countRule, detectedCount);
        }).ToArray();

        if (evaluations.Length == 0)
        {
            throw new InvalidDataException("普通检测项至少需要一条规则。");
        }

        var verdict = RuleGroupEvaluator.Evaluate(evaluations, item.RuleOperator);
        var measured = string.Join(
            "; ",
            evaluations.Select(result => $"{result.TargetName}={result.MetricValue}（{FormatVerdict(result.Verdict)}）"));
        return new TestItemRunResult
        {
            ItemId = item.Id,
            ItemOrder = item.Order,
            ItemName = item.Name,
            IsRequired = item.IsRequired,
            Verdict = verdict,
            Measured = measured
        };
    }

    private async Task<TestItemRunResult> RunPoseItemAsync(
        ProjectConfiguration project,
        TestSequenceDefinition sequence,
        TestItemDefinition item,
        IImageSource source,
        IInspectionProvider provider,
        IProgress<TestRunUpdate>? progress,
        CancellationToken cancellationToken)
    {
        var sourceDefinition = project.InputSources.First(candidate => candidate.Id == sequence.InputSourceId);
        var frameIntervalMs = sourceDefinition.Folder?.PoseFrameIntervalMs ?? 33;
        var completedSteps = new List<string>();

        foreach (var step in item.PoseSteps.OrderBy(step => step.Order))
        {
            var heldMs = 0;
            var waitedMs = 0;
            var stepCompleted = false;
            while (waitedMs < step.MaximumWaitMs)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var frame = await source.ReadAsync(cancellationToken);
                if (frame is null)
                {
                    return PoseFailure(item, completedSteps, $"{step.Name}：动作确认前图像序列已结束");
                }

                progress?.Report(UpdateFor(
                    item,
                    TestRunUpdateKind.FrameAcquired,
                    null,
                    $"姿态步骤 {step.Order}：{step.Name}",
                    frame));
                await _delayProvider.DelayAsync(frameIntervalMs, cancellationToken);
                var observation = await provider.AnalyzeAsync(frame, cancellationToken);
                progress?.Report(UpdateFor(
                    item,
                    TestRunUpdateKind.FrameAnalyzed,
                    null,
                    observation.Detections is null
                        ? $"动作分析结果已就绪。{FormatProviderDetails(observation.ProviderDetails)}"
                        : $"已获得 {observation.Detections.Count} 个空间检测框。{FormatProviderDetails(observation.ProviderDetails)}",
                    frame,
                    observation.Detections));
                waitedMs += frameIntervalMs;
                heldMs = observation.HasAction(step.ActionCondition) ? heldMs + frameIntervalMs : 0;
                if (heldMs >= step.MinimumHoldMs)
                {
                    completedSteps.Add(step.Name);
                    stepCompleted = true;
                    break;
                }
            }

            if (!stepCompleted)
            {
                return PoseFailure(item, completedSteps, $"{step.Name}：动作等待超过 {step.MaximumWaitMs} 毫秒");
            }
        }

        return new TestItemRunResult
        {
            ItemId = item.Id,
            ItemOrder = item.Order,
            ItemName = item.Name,
            IsRequired = item.IsRequired,
            Verdict = InspectionVerdict.Pass,
            Measured = $"动作序列已确认：{string.Join(" → ", completedSteps)}"
        };
    }

    private static TestItemRunResult PoseFailure(
        TestItemDefinition item,
        IReadOnlyCollection<string> completedSteps,
        string details) =>
        new()
        {
            ItemId = item.Id,
            ItemOrder = item.Order,
            ItemName = item.Name,
            IsRequired = item.IsRequired,
            Verdict = InspectionVerdict.Fail,
            Measured = completedSteps.Count == 0
                ? details
                : $"已完成 {string.Join(" → ", completedSteps)}；{details}"
        };

    private static TestRunUpdate UpdateFor(
        TestItemDefinition item,
        TestRunUpdateKind kind,
        InspectionVerdict? verdict,
        string message,
        ImageFrame? frame = null,
        IReadOnlyList<TargetDetection>? detections = null) =>
        new()
        {
            Kind = kind,
            ItemId = item.Id,
            ItemOrder = item.Order,
            ItemName = item.Name,
            Verdict = verdict,
            Message = message,
            Frame = frame,
            Detections = detections
        };

    private static InspectionVerdict GetRunVerdict(IReadOnlyCollection<TestItemRunResult> results)
    {
        if (results.Count == 0 || results.Any(result => result.Verdict == InspectionVerdict.Error))
        {
            return InspectionVerdict.Error;
        }

        return results.Any(result => result.IsRequired && result.Verdict == InspectionVerdict.Fail)
            ? InspectionVerdict.Fail
            : InspectionVerdict.Pass;
    }

    private static string FormatRunSummary(InspectionVerdict verdict, int itemCount) =>
        $"测试运行完成：{FormatVerdict(verdict)} · 共 {itemCount} 项。";

    private static string FormatVerdict(InspectionVerdict verdict) => verdict switch
    {
        InspectionVerdict.Pass => "通过",
        InspectionVerdict.Fail => "不通过",
        _ => "错误"
    };

    private static string FormatProviderDetails(string details) =>
        string.IsNullOrWhiteSpace(details) ? string.Empty : $" · {details}";
}
