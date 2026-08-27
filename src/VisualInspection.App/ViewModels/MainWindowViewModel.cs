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
using VisualInspection.Core.Security;
using VisualInspection.Infrastructure.Analysis;
using VisualInspection.Infrastructure.Imaging;
using VisualInspection.Infrastructure.Persistence;

namespace VisualInspection.App.ViewModels;

public sealed class MainWindowViewModel : ObservableObject
{
    internal const double DetectionBorderThickness = 4;
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
    private readonly List<ExecutionAuditEntry> _pendingAudit = [];
    private CancellationTokenSource? _runCancellation;
    private bool _isRunning;
    private string _statusText;
    private string _currentResult = "等待中";
    private string _currentItemName;
    private string _currentStandard;
    private string _currentMeasured = "实测：等待开始测试";
    private string _currentExecutionDetails;
    private ImageSource? _currentImage;
    private ImageFrame? _currentFrame;
    private Brush _currentResultBrush = Brushes.SlateGray;
    private bool _isRoiVisible;
    private string _currentRoiLabel = string.Empty;
    private string _serialNumberInput = string.Empty;
    private string? _activeSerialNumber;
    private bool _serialNumberConsumed;
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

        _currentItemName = Sequence[0].Name;
        _currentStandard = $"标准：{Sequence[0].Standard}";
        _currentExecutionDetails = FormatExecutionDetails(
            _activeSequence.Items.First(item => item.Order == Sequence[0].Number));
        UpdateRoi(_activeSequence.Items.First(item => item.Order == Sequence[0].Number));
        if (bootstrap.PreviewFrame is not null)
        {
            _currentFrame = bootstrap.PreviewFrame;
            var firstItem = _activeSequence.Items.First(item => item.Order == Sequence[0].Number);
            _currentImage = CreateAnnotatedImageSource(bootstrap.PreviewFrame, firstItem, []);
        }

        Statistics = new StatisticsViewModel();
        Logs = new ObservableCollection<ExecutionLogEntryViewModel>();
        _startCommand = new AsyncRelayCommand(
            StartAsync,
            () => !IsRunning && IsRuntimeReady && HasSerialNumber);
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
    public string LogFilePath => _logStore.GetCurrentLogPath();
    public ObservableCollection<TestSequenceItemViewModel> Sequence { get; }
    public ObservableCollection<ExecutionLogEntryViewModel> Logs { get; }
    public StatisticsViewModel Statistics { get; }
    public ICommand StartCommand => _startCommand;
    public ICommand StopCommand => _stopCommand;
    public ICommand ResetCommand => _resetCommand;
    public bool CanOpenSettings => !IsRunning && IsAdmin;

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
        private set => SetProperty(ref _currentImage, value);
    }

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

    public string SerialNumberInput
    {
        get => _serialNumberInput;
        set
        {
            if (SetProperty(ref _serialNumberInput, value ?? string.Empty))
            {
                OnPropertyChanged(nameof(SerialNumberStatusText));
                OnPropertyChanged(nameof(HasSerialNumber));
                _startCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public bool HasSerialNumber => !string.IsNullOrWhiteSpace(SerialNumberInput);

    public string SerialNumberStatusText => string.IsNullOrWhiteSpace(SerialNumberInput)
        ? "必须先输入序列号才能开始 · 支持人工输入、扫码枪或二维码回填"
        : "序列号已录入 · 点击“开始”后只检测一个产品 / 一张图片 · 工厂格式规则待提供";

    private async Task StartAsync()
    {
        var serialNumber = SerialNumberInput.Trim();
        if (serialNumber.Length == 0)
        {
            StatusText = "请先输入产品序列号。";
            return;
        }

        ResetItems();
        _pendingAudit.Clear();
        _activeSerialNumber = serialNumber;
        _serialNumberConsumed = false;
        _runCancellation = new CancellationTokenSource();
        IsRunning = true;
        CurrentResult = "运行中";
        CurrentResultBrush = new SolidColorBrush(Color.FromRgb(0, 145, 95));
        CurrentMeasured = $"实测：序列号 {serialNumber} · 正在采集输入图像";
        AddLog("INFO", $"序列号 {serialNumber} 已提交，开始单件检测。");
        _pendingAudit.Add(new ExecutionAuditEntry
        {
            TimestampUtc = DateTimeOffset.UtcNow,
            Level = "INFO",
            Event = "serial-run-started",
            Message = $"序列号 {serialNumber} 开始单件检测。",
            SerialNumber = serialNumber
        });

        try
        {
            var folderPath = _activeSource.Folder is null
                ? throw new InvalidOperationException("验收运行模式需要使用文件夹图源。")
                : ApplicationBootstrapper.ResolveFolderPath(_activeSource.Folder.FolderPath);
            await using var source = ImageSourceFactory.Create(
                _activeSource,
                AppContext.BaseDirectory,
                IsSingleImageFolderSequence() ? _folderSourceCursor : 0);
            var onnxProbe = OnnxYoloInspectionProvider.Probe(_project, _activeSequence, AppContext.BaseDirectory);
            using var onnxProvider = onnxProbe.IsReady
                ? OnnxYoloInspectionProvider.Create(_project, _activeSequence, AppContext.BaseDirectory)
                : null;
            IInspectionProvider provider = (IInspectionProvider?)onnxProvider ??
                await ManifestInspectionProvider.LoadAsync(folderPath, _project, _runCancellation.Token);
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
                _serialNumberConsumed = true;
                AdvanceFolderCursor(imageResult);
                ApplySingleFolderImageResult(imageResult, serialNumber);
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

                StatusText = $"序列号 {serialNumber} · {result.Summary}";

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
                    Message = $"序列号 {serialNumber} · {result.Summary}",
                    SerialNumber = serialNumber,
                    Verdict = result.Verdict
                });
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
            await _logStore.AppendAsync([
                new ExecutionAuditEntry
                {
                    TimestampUtc = DateTimeOffset.UtcNow,
                    Level = "ERROR",
                    Event = "unhandled-run-error",
                    Message = exception.ToString(),
                    SerialNumber = _activeSerialNumber
                }
            ]);
        }
        finally
        {
            IsRunning = false;
            _runCancellation?.Dispose();
            _runCancellation = null;
            if (_serialNumberConsumed)
            {
                SerialNumberInput = string.Empty;
            }

            _activeSerialNumber = null;
            _serialNumberConsumed = false;
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
                UpdateCurrentItem(itemViewModel);
                CurrentResult = "运行中";
                CurrentResultBrush = new SolidColorBrush(Color.FromRgb(0, 145, 95));
                CurrentMeasured = "实测：正在采集并分析图像";
                StatusText = $"运行中 · 第 {itemViewModel.Number} 项，共 {Sequence.Count} 项";
                break;
            case TestRunUpdateKind.FrameAcquired:
                _serialNumberConsumed = true;
                CurrentMeasured = $"实测：{update.Message}";
                break;
            case TestRunUpdateKind.FrameAnalyzed:
                CurrentMeasured = $"实测：{update.Message}";
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
        FolderBatchImageRunResult imageResult,
        string serialNumber)
    {
        var runResult = imageResult.RunResult;
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
        CurrentExecutionDetails = FormatExecutionDetails(definition);
        UpdateRoi(definition);
        if (_currentFrame is not null)
        {
            CurrentImage = CreateAnnotatedImageSource(_currentFrame, definition, []);
        }
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
            DrawRegions(context, item, source.PixelWidth, source.PixelHeight);
            DrawDetections(context, item, detections, frame, source.PixelWidth, source.PixelHeight);
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
        int imageHeight)
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
            DrawOverlayLabel(
                context,
                $"ROI · {region.Name}",
                rectangle.Left,
                rectangle.Top,
                Color.FromRgb(0, 112, 74),
                imageWidth,
                imageHeight,
                RoiLabelFontSize * overlayScale);
        }
    }

    private void DrawDetections(
        DrawingContext context,
        TestItemDefinition item,
        IReadOnlyList<TargetDetection> detections,
        ImageFrame frame,
        int imageWidth,
        int imageHeight)
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
        }
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
        double fontSize)
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
        var labelWidth = formatted.Width + (horizontalPadding * 2);
        var labelHeight = formatted.Height + (verticalPadding * 2);
        var boundedLeft = Math.Clamp(left, 0, Math.Max(0, imageWidth - labelWidth));
        var top = Math.Clamp(anchorY - labelHeight, 0, Math.Max(0, imageHeight - labelHeight));
        context.DrawRectangle(
            new SolidColorBrush(Color.FromArgb(235, color.R, color.G, color.B)),
            null,
            new Rect(boundedLeft, top, labelWidth, labelHeight));
        context.DrawText(
            formatted,
            new Point(boundedLeft + horizontalPadding, top + verticalPadding));
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
