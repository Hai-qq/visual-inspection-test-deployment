using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using VisualInspection.App.Services;
using VisualInspection.Core.Analysis;
using VisualInspection.Core.Configuration;
using VisualInspection.Core.Domain;
using VisualInspection.Core.Execution;
using VisualInspection.Core.Imaging;
using VisualInspection.Core.Rules;
using VisualInspection.Core.Security;
using VisualInspection.Infrastructure.Analysis;
using VisualInspection.Infrastructure.Imaging;
using VisualInspection.Infrastructure.Persistence;

namespace VisualInspection.App.ViewModels;

public sealed class MainWindowViewModel : ObservableObject
{
    internal const double DetectionBorderThickness = 4;
    internal const double DetectionLabelFontSize = 16;
    internal const double RoiBorderThickness = 4;
    internal const double RoiLabelFontSize = 16;
    private const double OverlayReferenceWidth = 640;
    private const double OverlayReferenceHeight = 360;

    private readonly UserSession _session;
    private readonly ProjectConfiguration _project;
    private readonly TestSequenceDefinition _activeSequence;
    private readonly InputSourceDefinition _activeSource;
    private readonly AsyncRelayCommand _startCommand;
    private readonly RelayCommand _stopCommand;
    private readonly RelayCommand _resetCommand;
    private readonly JsonLineExecutionLogStore _logStore;
    private readonly ProductionResultStore _resultStore;
    private readonly List<ExecutionAuditEntry> _pendingAudit = [];
    private CancellationTokenSource? _runCancellation;
    private bool _isRunning;
    private string _statusText;
    private string _currentResult = "等待中";
    private string _currentItemName;
    private string _currentStandard;
    private string _currentRuleCombinationText;
    private string _currentMeasured = "实测：等待开始测试";
    private string _currentExecutionDetails;
    private ImageSource? _currentImage;
    private ImageFrame? _currentFrame;
    private Brush _currentResultBrush = Brushes.SlateGray;
    private bool _isRoiVisible;
    private string _currentRoiLabel = string.Empty;
    private string? _activeSerialNumber;
    private int _folderSourceCursor;

    public MainWindowViewModel(ApplicationBootstrapResult bootstrap, UserSession? session = null)
    {
        _session = session ?? new UserSession(Guid.Empty, "admin", "演示管理员", UserRole.Admin);
        ArgumentNullException.ThrowIfNull(bootstrap);
        _project = bootstrap.Project;
        _activeSequence = _project.TestSequences
            .OrderByDescending(sequence => sequence.IsPublished)
            .ThenBy(sequence => sequence.Name, StringComparer.CurrentCultureIgnoreCase)
            .FirstOrDefault()
            ?? throw new InvalidOperationException("项目中没有测试序列。");
        _activeSource = _project.InputSources.First(input => input.Id == _activeSequence.InputSourceId);
        _logStore = new JsonLineExecutionLogStore(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "VisualInspectionTestDeployment",
            "logs"));
        _resultStore = new ProductionResultStore(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "VisualInspectionTestDeployment",
            "results"));

        ProjectName = _project.Name;
        SequenceName = $"{_activeSequence.Name} · {_activeSequence.Version}" +
            (_activeSequence.IsPublished ? string.Empty : " · 草稿");
        SourceName = FormatSourceName(_activeSource);
        IsInputSourceReady = bootstrap.IsInputSourceReady;
        InputSourceStatus = bootstrap.InputSourceStatus;
        IsRuntimeReady = bootstrap.IsRuntimeReady;
        RuntimeStatus = bootstrap.RuntimeStatus;
        SourceStateText = IsInputSourceReady ? "就绪" : "不可用";
        RuntimeStateText = IsRuntimeReady
            ? RuntimeStatus.StartsWith("真实 ONNX", StringComparison.Ordinal) ? "真实推理" : "可验收"
            : "已阻止";
        _statusText = IsRuntimeReady
            ? RuntimeStatus
            : $"无法开始测试 · {RuntimeStatus}";

        Sequence = new ObservableCollection<TestSequenceItemViewModel>(
            _activeSequence.Items
                .Where(item => item.Enabled)
                .OrderBy(item => item.Order)
                .Select(item => new TestSequenceItemViewModel(
                    item.Order,
                    item.Name,
                    FormatStandard(item))));
        if (Sequence.Count == 0)
        {
            throw new InvalidOperationException("当前测试序列中没有已启用的测试项。");
        }

        var firstItem = _activeSequence.Items.First(item => item.Order == Sequence[0].Number);
        _currentItemName = Sequence[0].Name;
        _currentStandard = $"标准：{Sequence[0].Standard}";
        _currentRuleCombinationText = FormatRuleCombination(firstItem);
        _currentExecutionDetails = FormatExecutionDetails(firstItem);
        UpdateRoi(firstItem);
        if (bootstrap.Project.IsUserConfigured && bootstrap.PreviewFrame is not null)
        {
            _currentFrame = bootstrap.PreviewFrame;
            _currentImage = CreateAnnotatedImageSource(bootstrap.PreviewFrame, firstItem, []);
        }

        Statistics = new StatisticsViewModel();
        Logs = new ObservableCollection<ExecutionLogEntryViewModel>();
        DetectionSummary = new ObservableCollection<DetectionSummaryRowViewModel>();
        PopulatePendingDetectionSummary(firstItem);
        _startCommand = new AsyncRelayCommand(
            StartAsync,
            () => !IsRunning && IsInputSourceReady && IsRuntimeReady);
        _stopCommand = new RelayCommand(Stop, () => IsRunning);
        _resetCommand = new RelayCommand(Reset, () => !IsRunning);
        AddLog("INFO", _statusText);
    }

    public string ProjectName { get; }
    public string SequenceName { get; }
    public string SourceName { get; }
    public bool IsInputSourceReady { get; }
    public string InputSourceStatus { get; }
    public string SourceStateText { get; }
    public bool IsRuntimeReady { get; }
    public string RuntimeStatus { get; }
    public string RuntimeStateText { get; }
    public string ItemCountText => $"{Sequence.Count} 项";
    public string UserName => _session.DisplayName;
    public string RoleName => _session.Role == UserRole.Admin ? "管理员" : "操作员";
    public bool IsAdmin => _session.IsAdmin;
    public Visibility SettingsVisibility => IsAdmin ? Visibility.Visible : Visibility.Collapsed;
    public string CurrentTime => DateTime.Now.ToString("yyyy-MM-dd  HH:mm");
    public string LogFilePath => _resultStore.GetCurrentLogPath();
    public ObservableCollection<TestSequenceItemViewModel> Sequence { get; }
    public ObservableCollection<ExecutionLogEntryViewModel> Logs { get; }
    public ObservableCollection<DetectionSummaryRowViewModel> DetectionSummary { get; }
    public StatisticsViewModel Statistics { get; }
    public ICommand StartCommand => _startCommand;
    public ICommand StopCommand => _stopCommand;
    public ICommand ResetCommand => _resetCommand;
    public bool CanOpenSettings => !IsRunning && IsAdmin;
    public bool CanImportSequence => !IsRunning;
    public bool RequiresSerialNumber => _activeSource.Type != InputSourceType.Folder;
    public Func<string?>? RequestSerialNumber { get; set; }

    public bool IsRunning
    {
        get => _isRunning;
        private set
        {
            if (SetProperty(ref _isRunning, value))
            {
                _startCommand.NotifyCanExecuteChanged();
                _stopCommand.NotifyCanExecuteChanged();
                _resetCommand.NotifyCanExecuteChanged();
                OnPropertyChanged(nameof(CanOpenSettings));
                OnPropertyChanged(nameof(CanImportSequence));
            }
        }
    }

    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    public string CurrentResult
    {
        get => _currentResult;
        private set => SetProperty(ref _currentResult, value);
    }

    public Brush CurrentResultBrush
    {
        get => _currentResultBrush;
        private set => SetProperty(ref _currentResultBrush, value);
    }

    public string CurrentItemName
    {
        get => _currentItemName;
        private set => SetProperty(ref _currentItemName, value);
    }

    public string CurrentStandard
    {
        get => _currentStandard;
        private set => SetProperty(ref _currentStandard, value);
    }

    public string CurrentRuleCombinationText
    {
        get => _currentRuleCombinationText;
        private set => SetProperty(ref _currentRuleCombinationText, value);
    }

    public string CurrentMeasured
    {
        get => _currentMeasured;
        private set => SetProperty(ref _currentMeasured, value);
    }

    public string CurrentExecutionDetails
    {
        get => _currentExecutionDetails;
        private set => SetProperty(ref _currentExecutionDetails, value);
    }

    public ImageSource? CurrentImage
    {
        get => _currentImage;
        private set
        {
            if (SetProperty(ref _currentImage, value)) OnPropertyChanged(nameof(ImageStatusLabel));
        }
    }

    public string ImageStatusLabel => CurrentImage is null ? "等待导入图像" :
        $"{(RuntimeStatus.StartsWith("真实 ONNX", StringComparison.Ordinal) ? "ONNX 推理" : IsRuntimeReady ? "验收数据" : "运行未就绪")} · " +
        (CurrentImage is BitmapSource bitmap ? $"{bitmap.PixelWidth} × {bitmap.PixelHeight}" : "无图像");

    public bool IsRoiVisible
    {
        get => _isRoiVisible;
        private set => SetProperty(ref _isRoiVisible, value);
    }

    public string CurrentRoiLabel
    {
        get => _currentRoiLabel;
        private set => SetProperty(ref _currentRoiLabel, value);
    }

    private async Task StartAsync()
    {
        var serialNumber = RequiresSerialNumber
            ? RequestSerialNumber?.Invoke()?.Trim()
            : null;
        if (RequiresSerialNumber && string.IsNullOrWhiteSpace(serialNumber))
        {
            StatusText = "已取消本次检测；相机图源必须先录入产品序列号。";
            return;
        }

        ResetItems();
        DetectionSummary.Clear();
        _pendingAudit.Clear();
        _activeSerialNumber = serialNumber;
        _runCancellation = new CancellationTokenSource();
        IsRunning = true;
        CurrentResult = "运行中";
        CurrentResultBrush = new SolidColorBrush(Color.FromRgb(0, 145, 95));
        CurrentMeasured = RequiresSerialNumber
            ? $"实测：序列号 {serialNumber} · 正在采集输入图像"
            : "实测：正在读取下一张图片，并以图片文件名作为序列号";
        AddLog("INFO", RequiresSerialNumber
            ? $"序列号 {serialNumber} 已提交，开始单件检测。"
            : "开始单件检测；序列号将从图片文件名自动取得。");
        _pendingAudit.Add(new ExecutionAuditEntry
        {
            TimestampUtc = DateTimeOffset.UtcNow,
            Level = "INFO",
            Event = RequiresSerialNumber ? "camera-serial-run-started" : "folder-image-run-started",
            Message = RequiresSerialNumber
                ? $"序列号 {serialNumber} 开始单件检测。"
                : "开始文件夹单图检测；等待从图片名取得序列号。",
            SerialNumber = serialNumber
        });

        try
        {
            await using var source = ImageSourceFactory.Create(
                _activeSource,
                AppContext.BaseDirectory,
                IsSingleImageFolderSequence() ? _folderSourceCursor : 0);
            var onnxProbe = OnnxYoloInspectionProvider.Probe(_project, _activeSequence, AppContext.BaseDirectory);
            using var onnxProvider = onnxProbe.IsReady
                ? OnnxYoloInspectionProvider.Create(_project, _activeSequence, AppContext.BaseDirectory)
                : null;
            IInspectionProvider provider;
            if (onnxProvider is not null)
            {
                provider = onnxProvider;
            }
            else if (_activeSource.Folder is not null)
            {
                var folderPath = ApplicationBootstrapper.ResolveFolderPath(_activeSource.Folder.FolderPath);
                provider = await ManifestInspectionProvider.LoadAsync(folderPath, _project, _runCancellation.Token);
            }
            else
            {
                throw new InvalidOperationException("相机图源没有可用的真实模型运行时，已阻止本次检测。");
            }

            var progress = new InlineProgress<TestRunUpdate>(HandleRunUpdate);
            if (IsSingleImageFolderSequence())
            {
                var imageResult = await new FolderBatchTestSequenceRunner().RunSingleAsync(
                    _project,
                    _activeSequence,
                    source,
                    provider,
                    progress,
                    _runCancellation.Token);
                AdvanceFolderCursor(imageResult);
                ApplySingleFolderImageResult(imageResult);
                await PersistProductionResultAsync(
                    imageResult.RunResult.Verdict,
                    imageResult.RunResult.CompletedAtUtc,
                    _runCancellation.Token);
            }
            else
            {
                var result = await new TestSequenceRunner().RunAsync(
                    _project,
                    _activeSequence,
                    source,
                    provider,
                    progress,
                    _runCancellation.Token);

                if (!result.WasStopped)
                {
                    UpdateStatistics(result.Verdict);
                }

                StatusText = $"序列号 {_activeSerialNumber} · {result.Summary}";

                _pendingAudit.Add(new ExecutionAuditEntry
                {
                    TimestampUtc = DateTimeOffset.UtcNow,
                    RunId = result.RunId,
                    Level = result.WasStopped ? "WARN" : result.Verdict switch
                    {
                        InspectionVerdict.Fail => "WARN",
                        InspectionVerdict.Error => "ERROR",
                        _ => "INFO"
                    },
                    Event = result.WasStopped ? "run-stopped" : "run-completed",
                    Message = $"序列号 {_activeSerialNumber} · {result.Summary}",
                    SerialNumber = _activeSerialNumber,
                    Verdict = result.Verdict
                });
                await PersistProductionResultAsync(result.Verdict, result.CompletedAtUtc, _runCancellation.Token);
            }

            await _logStore.AppendAsync(_pendingAudit);
        }
        catch (Exception exception)
        {
            CurrentResult = "错误";
            CurrentResultBrush = Brushes.DarkOrange;
            CurrentMeasured = $"实测：{exception.Message}";
            StatusText = "本次测试发生运行错误";
            Statistics.ErrorCount++;
            AddLog("ERROR", exception.Message);
            string? productionLogFailure = null;
            try
            {
                await PersistProductionResultAsync(
                    InspectionVerdict.Error,
                    DateTimeOffset.UtcNow,
                    CancellationToken.None);
            }
            catch (Exception persistenceException)
            {
                productionLogFailure = persistenceException.Message;
                AddLog("ERROR", $"生产 TXT 写入失败：{persistenceException.Message}");
            }

            await _logStore.AppendAsync([
                new ExecutionAuditEntry
                {
                    TimestampUtc = DateTimeOffset.UtcNow,
                    Level = "ERROR",
                    Event = "unhandled-run-error",
                    Message = productionLogFailure is null
                        ? exception.ToString()
                        : $"{exception}{Environment.NewLine}生产 TXT 写入失败：{productionLogFailure}",
                    SerialNumber = _activeSerialNumber
                }
            ]);
        }
        finally
        {
            IsRunning = false;
            _runCancellation?.Dispose();
            _runCancellation = null;
            _activeSerialNumber = null;
        }
    }

    private void Stop()
    {
        StatusText = "正在停止当前操作...";
        AddLog("WARN", "操作员请求停止测试。");
        _runCancellation?.Cancel();
    }

    private void Reset()
    {
        ResetItems();
        DetectionSummary.Clear();
        CurrentResult = "等待中";
        CurrentResultBrush = Brushes.SlateGray;
        CurrentMeasured = "实测：等待开始测试";
        StatusText = IsRuntimeReady
            ? RuntimeStatus
            : $"无法开始测试 · {RuntimeStatus}";
        UpdateCurrentItem(Sequence[0]);
        AddLog("INFO", "工作区已复位；当前会话统计已保留。");
    }

    private void ResetItems()
    {
        foreach (var item in Sequence)
        {
            item.State = ExecutionState.Pending;
        }
    }

    private void HandleRunUpdate(TestRunUpdate update)
    {
        var level = update.Kind is TestRunUpdateKind.RunError || update.Verdict == InspectionVerdict.Error ? "ERROR"
            : update.Kind is TestRunUpdateKind.RunStopped || update.Verdict == InspectionVerdict.Fail ? "WARN"
            : "INFO";
        AddLog(level, string.IsNullOrWhiteSpace(update.ItemName)
            ? update.Message
            : $"{update.ItemName}: {update.Message}");
        _pendingAudit.Add(new ExecutionAuditEntry
        {
            TimestampUtc = DateTimeOffset.UtcNow,
            Level = level,
            Event = update.Kind.ToString(),
            ItemName = string.IsNullOrWhiteSpace(update.ItemName) ? null : update.ItemName,
            Message = update.Message,
            SerialNumber = _activeSerialNumber,
            Verdict = update.Verdict
        });

        if (update.Frame is not null)
        {
            _currentFrame = update.Frame;
            if (_activeSource.Type == InputSourceType.Folder &&
                update.Kind == TestRunUpdateKind.FrameAcquired)
            {
                _activeSerialNumber = DeriveSerialNumber(update.Frame.Origin);
                AddLog("INFO", $"图片文件名已映射为序列号：{_activeSerialNumber}。");
                _pendingAudit.Add(new ExecutionAuditEntry
                {
                    TimestampUtc = DateTimeOffset.UtcNow,
                    Level = "INFO",
                    Event = "folder-serial-derived",
                    Message = $"图片文件名已映射为序列号：{_activeSerialNumber}。",
                    SerialNumber = _activeSerialNumber
                });
            }

            var definition = update.ItemOrder is null
                ? null
                : _activeSequence.Items.FirstOrDefault(item => item.Order == update.ItemOrder);
            CurrentImage = definition is null
                ? CreateImageSource(update.Frame.Data)
                : CreateAnnotatedImageSource(update.Frame, definition, update.Detections ?? []);
        }

        var itemViewModel = update.ItemOrder is null
            ? null
            : Sequence.FirstOrDefault(item => item.Number == update.ItemOrder);
        switch (update.Kind)
        {
            case TestRunUpdateKind.ItemStarted when itemViewModel is not null:
                itemViewModel.State = ExecutionState.Running;
                DetectionSummary.Clear();
                UpdateCurrentItem(itemViewModel);
                CurrentResult = "运行中";
                CurrentResultBrush = new SolidColorBrush(Color.FromRgb(0, 145, 95));
                CurrentMeasured = "实测：正在采集并分析图像";
                StatusText = $"运行中 · 第 {itemViewModel.Number} 项，共 {Sequence.Count} 项";
                break;
            case TestRunUpdateKind.FrameAcquired:
                CurrentMeasured = $"实测：{update.Message}";
                break;
            case TestRunUpdateKind.FrameAnalyzed:
                CurrentMeasured = $"实测：{update.Message}";
                if (update.ItemOrder is { } analyzedOrder && update.Frame is not null)
                {
                    var analyzedItem = _activeSequence.Items.First(item => item.Order == analyzedOrder);
                    UpdateDetectionSummary(analyzedItem, update.Detections ?? [], update.Frame);
                }
                break;
            case TestRunUpdateKind.ItemCompleted when itemViewModel is not null:
                itemViewModel.State = ToExecutionState(update.Verdict);
                CurrentResult = FormatVerdict(update.Verdict);
                CurrentResultBrush = GetVerdictBrush(update.Verdict);
                CurrentMeasured = $"实测：{update.Message}";
                break;
            case TestRunUpdateKind.RunCompleted:
            case TestRunUpdateKind.RunError:
                StatusText = update.Message;
                CurrentResult = FormatVerdict(update.Verdict);
                CurrentResultBrush = GetVerdictBrush(update.Verdict);
                break;
            case TestRunUpdateKind.RunStopped:
                if (Sequence.FirstOrDefault(item => item.State == ExecutionState.Running) is { } running)
                {
                    running.State = ExecutionState.Stopped;
                }

                StatusText = update.Message;
                CurrentResult = "已停止";
                CurrentResultBrush = Brushes.SlateGray;
                CurrentMeasured = "实测：测试完成前已停止";
                break;
        }
    }

    private void UpdateStatistics(InspectionVerdict verdict)
    {
        switch (verdict)
        {
            case InspectionVerdict.Pass:
                Statistics.PassCount++;
                break;
            case InspectionVerdict.Fail:
                Statistics.FailCount++;
                break;
            case InspectionVerdict.Error:
                Statistics.ErrorCount++;
                break;
        }
    }

    private bool IsSingleImageFolderSequence() =>
        _activeSource.Type == InputSourceType.Folder &&
        _activeSequence.Items
            .Where(item => item.Enabled)
            .All(item => item.Type == TestItemType.Normal);

    private void ApplySingleFolderImageResult(
        FolderBatchImageRunResult imageResult)
    {
        var runResult = imageResult.RunResult;
        var serialNumber = _activeSerialNumber ?? DeriveSerialNumber(imageResult.FrameOrigin);
        _activeSerialNumber = serialNumber;
        var fileName = Path.GetFileName(imageResult.FrameOrigin) is { Length: > 0 } name
            ? name
            : "当前图片";
        var verdictText = runResult.WasStopped ? "已停止" : FormatVerdict(runResult.Verdict);
        var level = runResult.WasStopped ? "WARN" : runResult.Verdict switch
        {
            InspectionVerdict.Fail => "WARN",
            InspectionVerdict.Error => "ERROR",
            _ => "INFO"
        };
        var message = $"序列号 {serialNumber} · {fileName} · {verdictText}";

        if (!runResult.WasStopped)
        {
            UpdateStatistics(runResult.Verdict);
        }

        StatusText = message;
        AddLog(level, message);
        _pendingAudit.Add(new ExecutionAuditEntry
        {
            TimestampUtc = DateTimeOffset.UtcNow,
            RunId = runResult.RunId,
            Level = level,
            Event = runResult.WasStopped ? "serial-image-stopped" : "serial-image-completed",
            Message = message,
            SerialNumber = serialNumber,
            Verdict = runResult.Verdict
        });
        CurrentResult = runResult.WasStopped ? "已停止" : FormatVerdict(runResult.Verdict);
        CurrentResultBrush = runResult.WasStopped
            ? Brushes.SlateGray
            : GetVerdictBrush(runResult.Verdict);
    }

    private async Task PersistProductionResultAsync(
        InspectionVerdict verdict,
        DateTimeOffset completedAt,
        CancellationToken cancellationToken)
    {
        var result = await _resultStore.AppendAsync(new ProductionResultRecord
        {
            Machine = Environment.MachineName,
            CompletedAt = completedAt,
            Workstation = _project.Workstation,
            ProductModel = _activeSequence.Name,
            EmployeeNumber = _session.Username,
            SerialNumber = _activeSerialNumber ?? string.Empty,
            Verdict = verdict,
            Frame = _currentFrame
        }, cancellationToken);
        AddLog("INFO", result.ImagePath is null
            ? $"生产结果已写入：{result.LogPath}"
            : $"生产结果与图片已保存：{result.ImagePath}");
    }

    private static string DeriveSerialNumber(string? imageOrigin)
    {
        var serialNumber = string.IsNullOrWhiteSpace(imageOrigin)
            ? string.Empty
            : Path.GetFileNameWithoutExtension(imageOrigin).Trim();
        if (serialNumber.Length == 0)
        {
            throw new InvalidDataException("文件夹图片名不能为空；图片主文件名必须就是产品序列号。");
        }

        return serialNumber;
    }

    private void AdvanceFolderCursor(FolderBatchImageRunResult imageResult)
    {
        _folderSourceCursor = imageResult.TotalFileCount <= 0
            ? 0
            : imageResult.SourceIndex % imageResult.TotalFileCount;
    }

    private void AddLog(string level, string message)
    {
        Logs.Insert(0, new ExecutionLogEntryViewModel(DateTimeOffset.Now, level, message));
        while (Logs.Count > 200)
        {
            Logs.RemoveAt(Logs.Count - 1);
        }
    }

    private void UpdateCurrentItem(TestSequenceItemViewModel activeItem)
    {
        CurrentItemName = activeItem.Name;
        CurrentStandard = $"标准：{activeItem.Standard}";
        var definition = _activeSequence.Items.First(item => item.Order == activeItem.Number);
        CurrentRuleCombinationText = FormatRuleCombination(definition);
        CurrentExecutionDetails = FormatExecutionDetails(definition);
        PopulatePendingDetectionSummary(definition);
        UpdateRoi(definition);
        if (_currentFrame is not null)
        {
            CurrentImage = CreateAnnotatedImageSource(_currentFrame, definition, []);
        }
    }

    private void UpdateDetectionSummary(
        TestItemDefinition item,
        IReadOnlyList<TargetDetection> detections,
        ImageFrame frame)
    {
        DetectionSummary.Clear();
        foreach (var rule in item.Rules)
        {
            var target = _project.Targets.First(candidate => candidate.Id == rule.TargetId);
            var detectedCount = SpatialDetectionCounter.Count(detections, rule, frame.Width, frame.Height);
            var evaluation = CountRuleEvaluator.Evaluate(
                new CountRule(
                    target.Name,
                    rule.Metric,
                    rule.Operator,
                    rule.Threshold,
                    rule.UpperThreshold,
                    rule.ExpectedCount,
                    rule.OutcomeWhenMatched),
                detectedCount);
            DetectionSummary.Add(new DetectionSummaryRowViewModel(
                target.Name,
                rule,
                detectedCount,
                evaluation));
        }
    }

    private void PopulatePendingDetectionSummary(TestItemDefinition item)
    {
        DetectionSummary.Clear();
        foreach (var rule in item.Rules)
        {
            var target = _project.Targets.First(candidate => candidate.Id == rule.TargetId);
            DetectionSummary.Add(new DetectionSummaryRowViewModel(target.Name, rule));
        }
    }

    private static string FormatRuleCombination(TestItemDefinition item)
    {
        if (item.Type == TestItemType.PoseSequence)
        {
            return "判定逻辑：按姿态动作顺序";
        }

        if (item.Rules.Count <= 1)
        {
            return "判定逻辑：单条规则";
        }

        return item.RuleOperator == RuleLogicalOperator.And
            ? "组合逻辑：全部满足（AND）"
            : "组合逻辑：任一满足（OR）";
    }

    private void UpdateRoi(TestItemDefinition definition)
    {
        var roi = definition.Rules
            .SelectMany(rule => rule.Scope.Regions)
            .FirstOrDefault();
        IsRoiVisible = roi is not null;
        CurrentRoiLabel = roi is null
            ? string.Empty
            : $"{roi.Name} · 横坐标:{roi.X1} 纵坐标:{roi.Y1} 宽:{roi.X2 - roi.X1} 高:{roi.Y2 - roi.Y1}";
    }

    private string FormatStandard(TestItemDefinition item)
    {
        if (item.Type == TestItemType.PoseSequence)
        {
            return string.Join(" → ", item.PoseSteps.OrderBy(step => step.Order).Select(step => step.Name));
        }

        return string.Join("; ", item.Rules.Select(rule => RuleStandardFormatter.Format(rule, _project)));
    }

    private string FormatExecutionDetails(TestItemDefinition item)
    {
        var delay = item.DelayMs ?? _activeSequence.DefaultDelayMs;
        var bindingId = item.Type == TestItemType.Normal
            ? item.Rules.First().ModelBindingId
            : item.PoseSteps.First().ModelBindingId;
        var binding = _project.Targets.SelectMany(target => target.ModelBindings).First(candidate => candidate.Id == bindingId);
        var model = _project.Models.First(candidate => candidate.Id == binding.ModelId);
        return $"模型绑定：{model.Name} {model.Version} · 延迟：{delay} 毫秒 · 序列版本：{_activeSequence.Version}";
    }

    private static ImageSource CreateImageSource(ReadOnlyMemory<byte> data)
    {
        using var stream = new MemoryStream(data.ToArray());
        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.StreamSource = stream;
        image.EndInit();
        image.Freeze();
        return image;
    }

    private ImageSource CreateAnnotatedImageSource(
        ImageFrame frame,
        TestItemDefinition item,
        IReadOnlyList<TargetDetection> detections)
    {
        var source = (BitmapSource)CreateImageSource(frame.Data);
        var drawing = new DrawingVisual();
        using (var context = drawing.RenderOpen())
        {
            context.DrawImage(source, new Rect(0, 0, source.PixelWidth, source.PixelHeight));
            var occupiedLabels = new List<Rect>();
            var labelDrawings = new List<Action>();
            DrawRegions(context, item, source.PixelWidth, source.PixelHeight, occupiedLabels, labelDrawings);
            DrawDetections(context, item, detections, frame, source.PixelWidth, source.PixelHeight, occupiedLabels, labelDrawings);
            foreach (var drawLabel in labelDrawings) drawLabel();
        }

        var rendered = new RenderTargetBitmap(
            source.PixelWidth,
            source.PixelHeight,
            96,
            96,
            PixelFormats.Pbgra32);
        rendered.Render(drawing);
        rendered.Freeze();
        return rendered;
    }

    private static void DrawRegions(
        DrawingContext context,
        TestItemDefinition item,
        int imageWidth,
        int imageHeight,
        List<Rect> occupiedLabels,
        List<Action> labelDrawings)
    {
        var overlayScale = GetOverlayScale(imageWidth, imageHeight);
        var roiPen = new Pen(
            new SolidColorBrush(Color.FromRgb(61, 205, 88)),
            RoiBorderThickness * overlayScale)
        {
            DashStyle = DashStyles.Dash,
            LineJoin = PenLineJoin.Round
        };
        roiPen.Freeze();
        foreach (var region in item.Rules.SelectMany(rule => rule.Scope.Regions))
        {
            var scaleX = (double)imageWidth / region.ReferenceWidth;
            var scaleY = (double)imageHeight / region.ReferenceHeight;
            var rectangle = new Rect(
                region.X1 * scaleX,
                region.Y1 * scaleY,
                (region.X2 - region.X1) * scaleX,
                (region.Y2 - region.Y1) * scaleY);
            context.DrawRectangle(null, roiPen, rectangle);
            labelDrawings.Add(() => DrawOverlayLabel(
                context,
                $"ROI · {region.Name}",
                rectangle.Left,
                rectangle.Top,
                Color.FromRgb(0, 112, 74),
                imageWidth,
                imageHeight,
                RoiLabelFontSize * overlayScale, occupiedLabels));
        }
    }

    private void DrawDetections(
        DrawingContext context,
        TestItemDefinition item,
        IReadOnlyList<TargetDetection> detections,
        ImageFrame frame,
        int imageWidth,
        int imageHeight,
        List<Rect> occupiedLabels,
        List<Action> labelDrawings)
    {
        var failTargets = item.Rules
            .Where(rule => rule.OutcomeWhenMatched == InspectionVerdict.Fail)
            .Select(rule => rule.TargetId)
            .ToHashSet();
        var scaleX = (double)imageWidth / frame.Width;
        var scaleY = (double)imageHeight / frame.Height;
        var overlayScale = GetOverlayScale(imageWidth, imageHeight);
        foreach (var detection in detections)
        {
            var color = failTargets.Contains(detection.TargetId)
                ? Color.FromRgb(201, 64, 58)
                : Color.FromRgb(0, 112, 74);
            var pen = new Pen(
                new SolidColorBrush(color),
                DetectionBorderThickness * overlayScale)
            {
                LineJoin = PenLineJoin.Round
            };
            pen.Freeze();
            var rectangle = new Rect(
                detection.X1 * scaleX,
                detection.Y1 * scaleY,
                (detection.X2 - detection.X1) * scaleX,
                (detection.Y2 - detection.Y1) * scaleY);
            context.DrawRectangle(null, pen, rectangle);
            labelDrawings.Add(() => DrawOverlayLabel(
                context,
                ResolveDetectionOverlayLabel(_project, detection.ModelBindingId),
                rectangle.Left,
                rectangle.Top,
                color,
                imageWidth,
                imageHeight,
                DetectionLabelFontSize * overlayScale, occupiedLabels));
        }
    }

    internal static string ResolveDetectionOverlayLabel(
        ProjectConfiguration project,
        Guid modelBindingId)
    {
        ArgumentNullException.ThrowIfNull(project);
        var binding = project.Targets
            .SelectMany(target => target.ModelBindings)
            .FirstOrDefault(candidate => candidate.Id == modelBindingId);
        if (binding is null)
        {
            return "Unknown_Label";
        }

        var labelName = project.Models
            .FirstOrDefault(model => model.Id == binding.ModelId)?
            .Labels
            .FirstOrDefault(label => label.Id == binding.OutputLabelId)?
            .Name;
        return string.IsNullOrWhiteSpace(labelName)
            ? $"Label_{binding.OutputLabelId}"
            : labelName;
    }

    internal static double GetOverlayScale(int imageWidth, int imageHeight)
    {
        if (imageWidth <= 0 || imageHeight <= 0)
        {
            return 1;
        }

        var scale = Math.Min(
            imageWidth / OverlayReferenceWidth,
            imageHeight / OverlayReferenceHeight);
        return Math.Clamp(scale, 0.5, 16);
    }

    private static void DrawOverlayLabel(
        DrawingContext context,
        string text,
        double left,
        double anchorY,
        Color color,
        int imageWidth,
        int imageHeight,
        double fontSize,
        List<Rect> occupiedLabels)
    {
        var formatted = new FormattedText(
            text,
            CultureInfo.GetCultureInfo("zh-CN"),
            FlowDirection.LeftToRight,
            new Typeface(
                new FontFamily("Microsoft YaHei UI"),
                FontStyles.Normal,
                FontWeights.SemiBold,
                FontStretches.Normal),
            fontSize,
            Brushes.White,
            1);
        var horizontalPadding = fontSize * 0.45;
        var verticalPadding = fontSize * 0.24;
        formatted.MaxTextWidth = Math.Max(1, imageWidth - horizontalPadding * 2);
        formatted.MaxLineCount = 1;
        formatted.Trimming = TextTrimming.CharacterEllipsis;
        var labelWidth = Math.Min(imageWidth, formatted.Width + (horizontalPadding * 2));
        var labelHeight = formatted.Height + (verticalPadding * 2);
        var bounds = PlaceOverlayLabel(left, anchorY, labelWidth, labelHeight, imageWidth, imageHeight, occupiedLabels);
        occupiedLabels.Add(bounds);
        if (Math.Abs(bounds.Top - (anchorY - labelHeight)) > labelHeight)
            context.DrawLine(new Pen(new SolidColorBrush(color), Math.Max(1, fontSize / 12)),
                new Point(left, anchorY), new Point(bounds.Left, bounds.Bottom));
        context.DrawRectangle(
            new SolidColorBrush(Color.FromArgb(235, color.R, color.G, color.B)),
            null,
            bounds);
        context.DrawText(
            formatted,
            new Point(bounds.Left + horizontalPadding, bounds.Top + verticalPadding));
    }

    internal static Rect PlaceOverlayLabel(double left, double anchorY, double width, double height,
        int imageWidth, int imageHeight, IReadOnlyList<Rect> occupied)
    {
        width = Math.Min(width, imageWidth);
        height = Math.Min(height, imageHeight);
        var preferred = new Rect(Math.Clamp(left, 0, imageWidth - width),
            Math.Clamp(anchorY - height, 0, imageHeight - height), width, height);
        if (!occupied.Any(value => value.IntersectsWith(preferred))) return preferred;
        var candidates = new List<Rect>();
        for (double y = 0; y <= imageHeight - height; y += height + 2)
        {
            candidates.Add(new Rect(preferred.Left, y, width, height));
            for (double x = 0; x <= imageWidth - width; x += width + 2)
                candidates.Add(new Rect(x, y, width, height));
        }
        return candidates.Where(candidate => !occupied.Any(value => value.IntersectsWith(candidate)))
            .OrderBy(candidate => Math.Abs(candidate.Top - preferred.Top) + Math.Abs(candidate.Left - preferred.Left))
            .FirstOrDefault(preferred);
    }

    private static ExecutionState ToExecutionState(InspectionVerdict? verdict) => verdict switch
    {
        InspectionVerdict.Pass => ExecutionState.Pass,
        InspectionVerdict.Fail => ExecutionState.Fail,
        _ => ExecutionState.Error
    };

    private static Brush GetVerdictBrush(InspectionVerdict? verdict) => verdict switch
    {
        InspectionVerdict.Pass => new SolidColorBrush(Color.FromRgb(0, 112, 74)),
        InspectionVerdict.Fail => Brushes.IndianRed,
        _ => Brushes.DarkOrange
    };

    private static string FormatSourceName(InputSourceDefinition source) => source.Type switch
    {
        InputSourceType.Folder => $"文件夹 · {source.Name}",
        InputSourceType.DirectShowCamera => $"USB 相机 · {source.Name}",
        InputSourceType.VendorCamera => $"工业相机 · {source.Name}",
        _ => source.Name
    };

    private static string FormatVerdict(InspectionVerdict? verdict) => verdict switch
    {
        InspectionVerdict.Pass => "通过",
        InspectionVerdict.Fail => "不通过",
        _ => "错误"
    };

    private sealed class InlineProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }
}
