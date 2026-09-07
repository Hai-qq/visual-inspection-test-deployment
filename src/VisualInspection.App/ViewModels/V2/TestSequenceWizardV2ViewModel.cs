using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Input;
using VisualInspection.App.Demo;
using VisualInspection.Core.V2.Configuration;
using VisualInspection.Infrastructure.V2.Persistence;

namespace VisualInspection.App.ViewModels.V2;

public sealed class TestSequenceWizardV2ViewModel : ObservableObject
{
    private readonly V2WizardServices _services;
    private readonly string _lastDraftPointerPath;
    private string _projectName = SampleProjectFactory.SampleProjectName;
    private string _workstation = "装配线 1 号工位";
    private string _sequenceName = SampleProjectFactory.SampleProductModel;
    private string _sequenceVersion = "V2.0";
    private string _sourceAddress = @"C:\检测图片\Fan";
    private int _sourceKindIndex;
    private int _environmentModeIndex = 1;
    private string _statusMessage = "草稿尚未保存；发布前必须完成 Schema 与 Runtime Validation。";
    private string _issueSummary = "尚未校验";
    private ConfigurationDraftV2? _currentDraft;
    private PublishedConfigurationPackage? _publishedPackage;
    private DeploymentAssignment? _assignment;
    private ActiveDeploymentPointer? _activePointer;

    public TestSequenceWizardV2ViewModel(
        ObservableCollection<TestSequenceWizardV2Window.ModelPreview> models,
        ObservableCollection<TestSequenceWizardV2Window.InspectionItemPreview> inspectionItems)
        : this(models, inspectionItems, V2WizardCompositionRoot.Create())
    {
    }

    internal TestSequenceWizardV2ViewModel(
        ObservableCollection<TestSequenceWizardV2Window.ModelPreview> models,
        ObservableCollection<TestSequenceWizardV2Window.InspectionItemPreview> inspectionItems,
        V2WizardServices services)
    {
        Models = models ?? throw new ArgumentNullException(nameof(models));
        InspectionItems = inspectionItems ?? throw new ArgumentNullException(nameof(inspectionItems));
        _services = services ?? throw new ArgumentNullException(nameof(services));
        _lastDraftPointerPath = Path.Combine(_services.StorageRoot, "last-draft.txt");

        SaveDraftCommand = new AsyncRelayCommand(SaveDraftAsync);
        LoadDraftCommand = new AsyncRelayCommand(LoadDraftAsync);
        ValidateCommand = new AsyncRelayCommand(ValidateAsync);
        PublishCommand = new AsyncRelayCommand(PublishAsync, () => CanPublish);
        AssignCommand = new AsyncRelayCommand(AssignAsync, () => CanAssign);
        ActivateCommand = new AsyncRelayCommand(ActivateAsync, () => CanActivate);
        RollbackCommand = new AsyncRelayCommand(RollbackAsync, () => CanRollback);
        RefreshSummaries();
    }

    public event EventHandler? DraftApplied;

    public ObservableCollection<TestSequenceWizardV2Window.ModelPreview> Models { get; }
    public ObservableCollection<TestSequenceWizardV2Window.InspectionItemPreview> InspectionItems { get; }
    public List<string> RetiredFunctionCodes { get; } = [];

    public Guid ProjectId { get; internal set; } = Guid.NewGuid();
    public Guid SequenceId { get; internal set; } = Guid.NewGuid();
    public Guid InputSourceId { get; internal set; } = Guid.NewGuid();
    public Guid SourceBindingId { get; internal set; } = Guid.NewGuid();
    public Guid DeploymentBindingId { get; internal set; } = Guid.NewGuid();

    public string ProjectName
    {
        get => _projectName;
        set => SetEditorProperty(ref _projectName, value);
    }

    public string Workstation
    {
        get => _workstation;
        set => SetEditorProperty(ref _workstation, value);
    }

    public string SequenceName
    {
        get => _sequenceName;
        set => SetEditorProperty(ref _sequenceName, value);
    }

    public string SequenceVersion
    {
        get => _sequenceVersion;
        set => SetEditorProperty(ref _sequenceVersion, value);
    }

    public string SourceAddress
    {
        get => _sourceAddress;
        set => SetEditorProperty(ref _sourceAddress, value);
    }

    public int SourceKindIndex
    {
        get => _sourceKindIndex;
        set => SetEditorProperty(ref _sourceKindIndex, value);
    }

    public int EnvironmentModeIndex
    {
        get => _environmentModeIndex;
        set
        {
            if (SetProperty(ref _environmentModeIndex, value))
            {
                MarkDirty();
                OnPropertyChanged(nameof(EnvironmentMode));
                OnPropertyChanged(nameof(EnvironmentLabel));
            }
        }
    }

    public RuntimeEnvironmentMode EnvironmentMode => EnvironmentModeIndex switch
    {
        0 => RuntimeEnvironmentMode.Development,
        2 => RuntimeEnvironmentMode.Production,
        _ => RuntimeEnvironmentMode.Acceptance
    };

    public string EnvironmentLabel => EnvironmentMode switch
    {
        RuntimeEnvironmentMode.Production => "Production（真实适配器门禁）",
        RuntimeEnvironmentMode.Development => "Development",
        _ => "Acceptance（模拟外围适配器）"
    };

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    public string IssueSummary
    {
        get => _issueSummary;
        private set => SetProperty(ref _issueSummary, value);
    }

    public string LifecycleLabel => _activePointer is not null
        ? $"Active · {_activePointer.PackageId:N}"
        : _assignment is not null
            ? $"Assigned · {_assignment.PackageId:N}"
            : _publishedPackage is not null
                ? $"Published · {_publishedPackage.PackageId:N}"
                : _currentDraft is not null
                    ? $"{_currentDraft.Stage} · Draft {_currentDraft.DraftId:N}"
                    : "未保存 Draft";

    public string SchemaStatus => _currentDraft?.LastValidation is { } report
        ? report.SchemaValidated ? "Schema：通过" : "Schema：未通过"
        : "Schema：待校验";

    public string RuntimeStatus => _currentDraft?.LastValidation is { } report
        ? report.RuntimeValidated ? "Runtime：通过" : "Runtime：未通过"
        : "Runtime：待校验";

    public bool CanPublish => _currentDraft?.LastValidation?.CanPublish == true;
    public bool CanAssign => _publishedPackage is not null;
    public bool CanActivate => _assignment is not null && _activePointer?.PackageId != _assignment.PackageId;
    public bool CanRollback => _activePointer?.PreviousPackageId is not null;

    public string ProjectSummary => $"{ProjectName} · {SequenceName} {SequenceVersion}";
    public string SourceSummary => $"{SourceKindLabel} · {SourceAddress}";
    public string SourceKindLabel => SourceKindIndex switch
    {
        1 => "USB 摄像头",
        2 => "工业相机",
        3 => "视频文件夹",
        _ => "图片文件夹"
    };

    public string FunctionCodesSummary => InspectionItems.Count == 0
        ? "无 Test Step"
        : string.Join(" · ", InspectionItems.Select(item => item.FunctionCode));

    public string InvocationOrderSummary => InspectionItems.Count == 0
        ? "尚未添加测试步"
        : string.Join(" → ", InspectionItems.Select(item => item.FunctionCode));

    public string ModelContractSummary => Models.Count == 0
        ? "无模型"
        : string.Join("；", Models.Select(model =>
            $"{model.Name} {model.Version} · {(model.Sha256.Length == 64 ? model.Sha256[..8] : "SHA待补")} · {model.AdapterId}"));

    public string TriggerSummary => InspectionItems.Count == 0
        ? "无调用入口"
        : string.Join("；", InspectionItems.Select(item => $"{item.FunctionCode}: {item.TriggerSummaryLabel}"));

    public string FramePolicySummary => InspectionItems.Count == 0
        ? "无帧输入策略"
        : string.Join("；", InspectionItems.Select(item => $"{item.FunctionCode}: {item.FrameInputPolicyLabel}"));

    public string DeploymentSummary => $"{Workstation} · {EnvironmentLabel} · Deployment {DeploymentBindingId:N}";

    public ICommand SaveDraftCommand { get; }
    public ICommand LoadDraftCommand { get; }
    public ICommand ValidateCommand { get; }
    public ICommand PublishCommand { get; }
    public ICommand AssignCommand { get; }
    public ICommand ActivateCommand { get; }
    public ICommand RollbackCommand { get; }

    public async Task InitializeAsync()
    {
        if (!File.Exists(_lastDraftPointerPath))
        {
            return;
        }

        var value = (await File.ReadAllTextAsync(_lastDraftPointerPath)).Trim();
        if (!Guid.TryParse(value, out var draftId))
        {
            StatusMessage = "最近草稿指针无效；已保留当前编辑内容。";
            return;
        }

        await LoadDraftByIdAsync(draftId);
    }

    public async Task SaveDraftAsync() => _ = await TrySaveDraftAsync();

    private async Task<bool> TrySaveDraftAsync()
    {
        try
        {
            _currentDraft = await _services.Publication.SaveDraftAsync(
                V2DraftMapper.ToProject(this),
                Environment.UserName,
                _currentDraft?.DraftId);
            Directory.CreateDirectory(_services.StorageRoot);
            var temporaryPath = _lastDraftPointerPath + ".tmp";
            await File.WriteAllTextAsync(temporaryPath, _currentDraft.DraftId.ToString("D"));
            File.Move(temporaryPath, _lastDraftPointerPath, true);
            StatusMessage = $"Draft 已原子保存：{_currentDraft.DraftId:D}";
            IssueSummary = "草稿已保存，尚未校验。";
            NotifyLifecycleChanged();
            return true;
        }
        catch (Exception exception)
        {
            StatusMessage = $"保存 Draft 失败：{exception.Message}";
            return false;
        }
    }

    public async Task LoadDraftAsync()
    {
        if (_currentDraft is not null)
        {
            await LoadDraftByIdAsync(_currentDraft.DraftId);
            return;
        }

        await InitializeAsync();
    }

    public async Task ValidateAsync()
    {
        try
        {
            if (!await TrySaveDraftAsync() || _currentDraft is null)
            {
                return;
            }

            _currentDraft = await _services.Publication.ValidateAsync(
                _currentDraft,
                SequenceId,
                SequenceVersion,
                RuntimeContext());
            UpdateValidationStatus(_currentDraft.LastValidation);
            StatusMessage = CanPublish
                ? "Schema 与 Runtime Validation 均通过，可以发布不可变版本。"
                : "校验未通过；Publish 保持禁用。";
            NotifyLifecycleChanged();
        }
        catch (Exception exception)
        {
            StatusMessage = $"校验失败：{exception.Message}";
            MarkDirty();
        }
    }

    public async Task PublishAsync()
    {
        if (_currentDraft?.LastValidation?.CanPublish != true)
        {
            StatusMessage = "Publish 被拒绝：必须先同时通过 Schema 与 Runtime Validation。";
            return;
        }

        try
        {
            _publishedPackage = await _services.Publication.PublishAsync(
                _currentDraft,
                SequenceId,
                SequenceVersion,
                RuntimeContext(),
                Environment.UserName);
            _currentDraft = _currentDraft with
            {
                Project = _publishedPackage.ProjectSnapshot,
                Stage = ConfigurationLifecycleStage.Published,
                LastValidation = null
            };
            StatusMessage = $"Published Package 已创建：{_publishedPackage.PackageId:D}；Snapshot 不可覆盖。";
            NotifyLifecycleChanged();
        }
        catch (ConfigurationPublishException exception)
        {
            _currentDraft = _currentDraft with
            {
                Stage = exception.ValidationReport.SchemaValidated
                    ? ConfigurationLifecycleStage.SchemaValidated
                    : ConfigurationLifecycleStage.Draft,
                LastValidation = exception.ValidationReport
            };
            UpdateValidationStatus(exception.ValidationReport);
            StatusMessage = exception.Message;
            NotifyLifecycleChanged();
        }
        catch (Exception exception)
        {
            StatusMessage = $"发布失败：{exception.Message}";
        }
    }

    public async Task AssignAsync()
    {
        if (_publishedPackage is null)
        {
            StatusMessage = "Assign 被拒绝：当前没有 Published Package。";
            return;
        }

        try
        {
            _assignment = await _services.Publication.AssignAsync(
                _publishedPackage.PackageId,
                DeploymentBindingId,
                Environment.UserName);
            StatusMessage = $"Package 已 Assigned 到 Deployment：{DeploymentBindingId:D}。";
            NotifyLifecycleChanged();
        }
        catch (Exception exception)
        {
            StatusMessage = $"Assign 失败：{exception.Message}";
        }
    }

    public async Task ActivateAsync()
    {
        if (_assignment is null)
        {
            StatusMessage = "Activate 被拒绝：必须先 Assign。";
            return;
        }

        try
        {
            _activePointer = await _services.Publication.ActivateAsync(
                _assignment.PackageId,
                DeploymentBindingId,
                Environment.UserName);
            StatusMessage = $"Active Pointer 已切换到 Package {_activePointer.PackageId:D}。";
            NotifyLifecycleChanged();
        }
        catch (Exception exception)
        {
            StatusMessage = $"Activate 失败：{exception.Message}";
        }
    }

    public async Task RollbackAsync()
    {
        if (_activePointer?.PreviousPackageId is not { } previousPackageId)
        {
            StatusMessage = "Rollback 被拒绝：没有可回退的 Previous Package。";
            return;
        }

        try
        {
            _activePointer = await _services.Publication.RollbackAsync(
                DeploymentBindingId,
                previousPackageId,
                Environment.UserName);
            StatusMessage = $"Rollback 完成，Active Package：{_activePointer.PackageId:D}。";
            NotifyLifecycleChanged();
        }
        catch (Exception exception)
        {
            StatusMessage = $"Rollback 失败：{exception.Message}";
        }
    }

    public bool MoveStep(TestSequenceWizardV2Window.InspectionItemPreview step, int offset)
    {
        ArgumentNullException.ThrowIfNull(step);
        var current = InspectionItems.IndexOf(step);
        var target = current + offset;
        if (current < 0 || target < 0 || target >= InspectionItems.Count)
        {
            return false;
        }

        InspectionItems.Move(current, target);
        MarkDirty();
        RefreshSummaries();
        return true;
    }

    public void RemoveStep(TestSequenceWizardV2Window.InspectionItemPreview step)
    {
        if (!RetiredFunctionCodes.Contains(step.FunctionCode, StringComparer.OrdinalIgnoreCase))
        {
            RetiredFunctionCodes.Add(step.FunctionCode);
        }

        MarkDirty();
        RefreshSummaries();
    }

    public void MarkDirty()
    {
        if (_currentDraft?.LastValidation is not null)
        {
            _currentDraft = _currentDraft with
            {
                Stage = ConfigurationLifecycleStage.Draft,
                LastValidation = null
            };
        }

        StatusMessage = "编辑内容已变化；原校验结果失效，请重新保存并校验。";
        IssueSummary = "待重新校验";
        NotifyLifecycleChanged();
    }

    public void RefreshSummaries()
    {
        OnPropertyChanged(nameof(ProjectSummary));
        OnPropertyChanged(nameof(SourceSummary));
        OnPropertyChanged(nameof(SourceKindLabel));
        OnPropertyChanged(nameof(FunctionCodesSummary));
        OnPropertyChanged(nameof(InvocationOrderSummary));
        OnPropertyChanged(nameof(ModelContractSummary));
        OnPropertyChanged(nameof(TriggerSummary));
        OnPropertyChanged(nameof(FramePolicySummary));
        OnPropertyChanged(nameof(DeploymentSummary));
    }

    internal void ReplaceLifecycle(ConfigurationDraftV2 draft)
    {
        _currentDraft = draft;
        _publishedPackage = null;
        _assignment = null;
        _activePointer = null;
        UpdateValidationStatus(draft.LastValidation);
        NotifyLifecycleChanged();
    }

    private async Task LoadDraftByIdAsync(Guid draftId)
    {
        try
        {
            var draft = await _services.Publication.LoadDraftAsync(draftId);
            if (draft is null)
            {
                StatusMessage = $"Draft 不存在：{draftId:D}";
                return;
            }

            V2DraftMapper.ApplyProject(this, draft.Project);
            ReplaceLifecycle(draft);
            var expectedPackageId = ConfigurationPublicationService.CreatePackageId(SequenceId, SequenceVersion);
            _publishedPackage = await _services.Store.LoadPublishedPackageAsync(expectedPackageId);
            _assignment = await _services.Store.LoadAssignmentAsync(DeploymentBindingId);
            _activePointer = await _services.Store.LoadActivePointerAsync(DeploymentBindingId);
            if (_publishedPackage is not null && _currentDraft is not null)
            {
                _currentDraft = _currentDraft with
                {
                    Project = _publishedPackage.ProjectSnapshot,
                    Stage = ConfigurationLifecycleStage.Published,
                    LastValidation = null
                };
            }

            StatusMessage = _publishedPackage is null
                ? $"已加载 Draft：{draft.DraftId:D}（{draft.SavedAtUtc:yyyy-MM-dd HH:mm:ss} UTC）。"
                : $"已加载 Draft 与 Published Package：{_publishedPackage.PackageId:D}。";
            RefreshSummaries();
            NotifyLifecycleChanged();
            DraftApplied?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception exception)
        {
            StatusMessage = $"加载 Draft 失败：{exception.Message}";
        }
    }

    private RuntimeValidationContext RuntimeContext() => new()
    {
        EnvironmentMode = EnvironmentMode,
        BaseDirectory = _services.BaseDirectory,
        DeploymentBindingId = DeploymentBindingId
    };

    private void UpdateValidationStatus(ConfigurationValidationReport? report)
    {
        if (report is null)
        {
            IssueSummary = "尚未校验";
            return;
        }

        var errors = report.SchemaIssues.Concat(report.RuntimeIssues)
            .Where(issue => issue.Severity == V2ValidationSeverity.Error)
            .ToArray();
        var warnings = report.SchemaIssues.Concat(report.RuntimeIssues)
            .Count(issue => issue.Severity == V2ValidationSeverity.Warning);
        IssueSummary = errors.Length == 0
            ? $"0 个错误 · {warnings} 个警告"
            : string.Join(Environment.NewLine, errors.Take(5).Select(issue => $"{issue.Code} · {issue.Path} · {issue.Message}")) +
              (errors.Length > 5 ? $"{Environment.NewLine}另有 {errors.Length - 5} 个错误。" : string.Empty);
    }

    private bool SetEditorProperty<T>(ref T field, T value, [System.Runtime.CompilerServices.CallerMemberName] string? propertyName = null)
    {
        if (!SetProperty(ref field, value, propertyName))
        {
            return false;
        }

        MarkDirty();
        RefreshSummaries();
        return true;
    }

    private void NotifyLifecycleChanged()
    {
        OnPropertyChanged(nameof(LifecycleLabel));
        OnPropertyChanged(nameof(SchemaStatus));
        OnPropertyChanged(nameof(RuntimeStatus));
        OnPropertyChanged(nameof(CanPublish));
        OnPropertyChanged(nameof(CanAssign));
        OnPropertyChanged(nameof(CanActivate));
        OnPropertyChanged(nameof(CanRollback));
        (PublishCommand as AsyncRelayCommand)?.NotifyCanExecuteChanged();
        (AssignCommand as AsyncRelayCommand)?.NotifyCanExecuteChanged();
        (ActivateCommand as AsyncRelayCommand)?.NotifyCanExecuteChanged();
        (RollbackCommand as AsyncRelayCommand)?.NotifyCanExecuteChanged();
    }
}
