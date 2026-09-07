using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using VisualInspection.App.Demo;
using VisualInspection.Core.Configuration;
using VisualInspection.Core.V2.Configuration;
using VisualInspection.App.ViewModels.V2;
using VisualInspection.Infrastructure.Analysis;
using VisualInspection.Infrastructure.V2.Persistence;

namespace VisualInspection.App;

public partial class TestSequenceWizardV2Window : Window, INotifyPropertyChanged
{
    private const double DefaultRoiReferenceWidth = 640;
    private const double DefaultRoiReferenceHeight = 480;
    private readonly FrameworkElement[] _wizardPanels;
    private readonly FrameworkElement[] _testBlockStagePanels;
    private readonly ObservableCollection<PoseStepPreview> _emptyPoseSteps = [];
    private static readonly string[] TestBlockModuleTitles = ["基本信息", "动作顺序弹窗", "判定条件（已并入基本信息）", "自定义函数"];
    private readonly HashSet<int> _confirmedStepIndexes = [];
    private readonly bool _returnToOperatorOnCompletion;
    private bool _wizardReady;
    private bool _isRoiDrawing;
    private Point? _roiDragStart;
    private Rect _roiBeforeDrag;
    private Rect _roiLogicalRect = new(120, 80, 400, 340);
    private ModelPreview? _selectedModel;
    private InspectionItemPreview? _selectedInspectionItem;
    private NamedRoiPreview? _selectedNamedRoi;
    private PoseActionEditorSnapshot? _poseActionEditorSnapshot;
    private int _currentTestBlockStageIndex;
    private int _nextCustomTestStepIndex = 1;
    private bool _isRestoringPoseActionSnapshot;
    private bool _isRefreshingCompatibleInspectionModels;
    private readonly string[] _sourceAddresses = [string.Empty, string.Empty, string.Empty, string.Empty];
    private bool _isSwitchingSourceKind;

    internal bool SuppressSequenceExportForSmoke { get; set; }
    internal bool SuppressApplyToOperatorForSmoke { get; set; }
    internal bool SuppressModelSelectionDialogForSmoke { get; set; }

    public ProjectConfigurationV2? AppliedProject { get; private set; }

    public TestSequenceWizardV2Window()
        : this(false, null)
    {
    }

    internal TestSequenceWizardV2Window(bool returnToOperatorOnCompletion)
        : this(returnToOperatorOnCompletion, null)
    {
    }

    internal TestSequenceWizardV2Window(
        bool returnToOperatorOnCompletion,
        ProjectConfiguration? initialProject)
    {
        _returnToOperatorOnCompletion = returnToOperatorOnCompletion;
        InitializeComponent();

        Steps =
        [
            new(0, "01", "项目信息", "填写项目、工位、型号和版本。"),
            new(1, "02", "选择图源", "图片文件夹与视频文件夹二选一；USB 和工业相机暂列为待开发。"),
            new(2, "03", "导入模型", "建立项目模型库，可连续导入多个 ONNX / PT 模型。"),
            new(3, "04", "测试步设置", "测试步按列表从上到下执行；在基本信息中配置检测标签，姿态模型按需弹出动作顺序。"),
            new(4, "05", "应用与导出", "检查配置后，可直接应用到当前操作台，也可另行选择位置导出 Sequence 与对应模型。")
        ];

        Models =
        [
            new(SampleProjectFactory.SampleModelName, "fan.onnx", 0, true,
                ["Labell", "Black_wire", "white_wire", "reverse_Labell", "reverse_Black_wire", "reverse_white_wire"]),
            new("叶片缺陷检测", "blade-defect.onnx", 0, true, ["blade_defect", "crack"]),
            new("装配姿态时序", "assembly-pose.onnx", 1, false, ["取件", "放置", "按压到位"])
        ];

        InspectionItems =
        [
            CreateDefaultFanInspectionItem(Models[0])
        ];

        Editor = new TestSequenceWizardV2ViewModel(Models, InspectionItems);
        Editor.DraftApplied += Editor_DraftApplied;
        _sourceAddresses[0] = Editor.SourceAddress;
        DetectionEditor = new DetectionEditorPreview();

        _wizardPanels =
        [
            Step1Panel,
            Step2Panel,
            Step3Panel,
            Step9Panel
        ];

        _testBlockStagePanels =
        [
            Step4Panel,
            Step5Panel,
            Step6Panel,
            Step8Panel
        ];

        foreach (var model in Models)
        {
            TrackModel(model);
        }

        foreach (var item in InspectionItems)
        {
            TrackInspectionItem(item);
        }

        DataContext = this;
        ModelItemsList.SelectedIndex = 0;
        SelectedModel = Models[0];
        InspectionItemsList.SelectedIndex = 0;
        SelectedInspectionItem = InspectionItems[0];
        _wizardReady = true;
        if (initialProject is not null)
        {
            V2DraftMapper.ApplyProject(Editor, ProjectConfigurationV1Migrator.Migrate(initialProject));
            Editor_DraftApplied(this, EventArgs.Empty);
        }

        UpdateSourceSettingsPanels(Editor.SourceKindIndex);
        UpdateRuleEditorState();
        NavigateTo(0);

        if (_returnToOperatorOnCompletion)
        {
            PreviewModeText.Text = "sequence 设置、应用与导出";
            ClosePreviewButton.Content = "返回操作台";
        }
    }

    private static InspectionItemPreview CreateDefaultFanInspectionItem(ModelPreview model)
    {
        var item = new InspectionItemPreview("TS-FAN-CHECK", "风扇检测", 0, true, model)
        {
            AllowSequenceInvocation = true,
            ExternalTriggerEnabled = true,
            TriggerSignal = "PLC.Line1.FanPresent",
            TriggerConditionIndex = 0,
            TriggerDebounceMsText = "50",
            RuleLogicalOperatorIndex = 0,
            TargetLabel = "Labell",
            RuleMetricIndex = 0,
            RuleMethodIndex = 0,
            ExpectedCountText = "3",
            ConfidenceThresholdText = "0.5",
            RuleOutcomeIndex = 0
        };

        item.DetectionChildren.Clear();
        AddDefaultFanRule(item, model, "Labell", 0, "3", 0);
        AddDefaultFanRule(item, model, "Black_wire", 0, "3", 0);
        AddDefaultFanRule(item, model, "white_wire", 0, "1", 0);
        AddDefaultFanRule(item, model, "reverse_Labell", 2, "0", 1);
        AddDefaultFanRule(item, model, "reverse_Black_wire", 2, "0", 1);
        AddDefaultFanRule(item, model, "reverse_white_wire", 2, "0", 1);
        return item;
    }

    private static void AddDefaultFanRule(
        InspectionItemPreview item,
        ModelPreview model,
        string label,
        int methodIndex,
        string threshold,
        int outcomeIndex)
    {
        var method = methodIndex == 2 ? ">" : "=";
        var outcome = outcomeIndex == 1 ? "Fail" : "Pass";
        item.DetectionChildren.Add(new DetectionChildPreview(
            label,
            "整张图",
            $"识别数量 {method} {threshold} 时 {outcome} · 置信度阈值 0.5"));
        if (item.DetectionChildren.Count == 1)
        {
            return;
        }

        item.AdditionalRules.Add(new RulePreviewViewModel(label, model)
        {
            MetricIndex = 0,
            RuleMethodIndex = methodIndex,
            ThresholdText = threshold,
            ConfidenceText = "0.5",
            OutcomeIndex = outcomeIndex
        });
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<WizardStepItem> Steps { get; }

    public ObservableCollection<ModelPreview> Models { get; }

    public ObservableCollection<ModelPreview> CompatibleInspectionModels { get; } = [];

    public ObservableCollection<InspectionItemPreview> InspectionItems { get; }

    public TestSequenceWizardV2ViewModel Editor { get; }

    public DetectionEditorPreview DetectionEditor { get; }

    public ObservableCollection<PoseStepPreview> PoseSteps => SelectedInspectionItem?.PoseSteps ?? _emptyPoseSteps;

    public ModelPreview? SelectedModel
    {
        get => _selectedModel;
        private set
        {
            if (ReferenceEquals(_selectedModel, value))
            {
                return;
            }

            _selectedModel = value;
            OnPropertyChanged();
        }
    }

    public InspectionItemPreview? SelectedInspectionItem
    {
        get => _selectedInspectionItem;
        private set
        {
            if (ReferenceEquals(_selectedInspectionItem, value))
            {
                return;
            }

            if (_selectedInspectionItem is not null)
            {
                _selectedInspectionItem.RoiRect = _roiLogicalRect;
            }

            _selectedInspectionItem = value;
            _roiLogicalRect = value?.RoiRect ?? new Rect(120, 80, 400, 340);
            OnPropertyChanged();
            OnPropertyChanged(nameof(PoseSteps));
            RefreshCompatibleInspectionModels(repairSelection: true);
            UpdateTypePanels();
            UpdateTriggerEditorState();
            UpdateRoiVisual();
        }
    }

    public NamedRoiPreview? SelectedNamedRoi
    {
        get => _selectedNamedRoi;
        private set
        {
            if (ReferenceEquals(_selectedNamedRoi, value))
            {
                return;
            }

            _selectedNamedRoi = value;
            if (value is not null)
            {
                _roiLogicalRect = value.Rect;
            }

            OnPropertyChanged();
            UpdateRoiVisual();
        }
    }

    public int CurrentStepIndex { get; private set; }

    public int CurrentTestBlockStageIndex => _currentTestBlockStageIndex;

    public static string SnapshotPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "VisualInspectionTestDeployment",
        "v2-wizard-preview.png");

    public static string PoseSnapshotPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "VisualInspectionTestDeployment",
        "v2-wizard-pose-preview.png");

    public static string SourceSnapshotPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "VisualInspectionTestDeployment",
        "v2-wizard-source-preview.png");

    public static string ModelsSnapshotPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "VisualInspectionTestDeployment",
        "v2-wizard-models-preview.png");

    public static string RoiSnapshotPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "VisualInspectionTestDeployment",
        "v2-wizard-roi-preview.png");

    public static string RuleSnapshotPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "VisualInspectionTestDeployment",
        "v2-wizard-rule-preview.png");

    public static string TriggerSnapshotPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "VisualInspectionTestDeployment",
        "v2-wizard-trigger-preview.png");

    public void SaveSnapshot() => SaveSnapshot(SnapshotPath);

    public void SavePoseSnapshot() => SaveSnapshot(PoseSnapshotPath);

    public void SaveSourceSnapshot() => SaveSnapshot(SourceSnapshotPath);

    public void SaveModelsSnapshot() => SaveSnapshot(ModelsSnapshotPath);

    public void SaveRoiSnapshot() => SaveSnapshot(RoiSnapshotPath);

    public void SaveRuleSnapshot() => SaveSnapshot(RuleSnapshotPath);

    public void SaveTriggerSnapshot() => SaveSnapshot(TriggerSnapshotPath);

    private void SaveSnapshot(string path)
    {
        UpdateLayout();
        var width = Math.Max(1, (int)Math.Ceiling(ActualWidth));
        var height = Math.Max(1, (int)Math.Ceiling(ActualHeight));
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(this);

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path);
        encoder.Save(stream);
    }

    public void ShowInspectionItemsStepForPreview()
    {
        NavigateTo(3);
        ShowTestBlockStage(0);
        UpdateLayout();
    }

    public void ShowModelsStepForPreview()
    {
        NavigateTo(2);
        UpdateLayout();
    }

    public void ShowSourceStepForPreview()
    {
        NavigateTo(1);
        UpdateLayout();
    }

    public void ShowPoseContentStepForPreview()
    {
        var poseItem = InspectionItems.FirstOrDefault(item => item.TypeIndex == 1)
            ?? CreatePosePreviewItem();
        InspectionItemsList.SelectedItem = poseItem;
        SelectedInspectionItem = poseItem;
        NavigateTo(3);
        ShowTestBlockStage(0);
        OpenPoseActionEditor();
        UpdateLayout();
    }

    private InspectionItemPreview CreatePosePreviewItem()
    {
        var poseModel = Models.First(model => model.TypeIndex == 1);
        var poseItem = new InspectionItemPreview(
            "TS-POSE-PREVIEW",
            "姿态动作预览",
            1,
            false,
            poseModel,
            ["取件", "放置", "按压到位"]);
        TrackInspectionItem(poseItem);
        InspectionItems.Add(poseItem);
        Editor.RefreshSummaries();
        return poseItem;
    }

    public void ShowTargetContentStepForPreview()
    {
        var targetItem = InspectionItems.First(item => item.TypeIndex == 0);
        InspectionItemsList.SelectedItem = targetItem;
        SelectedInspectionItem = targetItem;
        NavigateTo(3);
        ShowTestBlockStage(0);
        OpenDetectionEditor();
        UpdateLayout();
        UpdateRoiVisual();
    }

    public void ShowTargetRuleStepForPreview()
    {
        var targetItem = InspectionItems.First(item => item.TypeIndex == 0);
        InspectionItemsList.SelectedItem = targetItem;
        SelectedInspectionItem = targetItem;
        NavigateTo(3);
        ShowTestBlockStage(0);
        OpenDetectionEditor();
        var configuredLabel = DetectionEditor.LabelOptions.FirstOrDefault(option => option.IsConfigured)
            ?? DetectionEditor.LabelOptions.FirstOrDefault();
        if (configuredLabel is not null)
        {
            OpenDetectionLabelEditor(configuredLabel);
        }

        UpdateLayout();
        UpdateRuleEditorState();
    }

    public void ShowTriggerStepForPreview()
    {
        var externallyTriggeredItem = InspectionItems.First(item => item.ExternalTriggerEnabled);
        InspectionItemsList.SelectedItem = externallyTriggeredItem;
        SelectedInspectionItem = externallyTriggeredItem;
        NavigateTo(3);
        ShowTestBlockStage(3);
        UpdateLayout();
        UpdateTriggerEditorState();
    }

    public void ApplyRoiSelectionForSmoke(Point start, Point end)
    {
        UpdateLayout();
        ApplyRoiFromPreviewPoints(start, end);
    }

    public Rect RoiLogicalRect => _roiLogicalRect;

    internal Rect RoiImageViewport => GetRoiImageViewport();

    private void StepButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { CommandParameter: int index })
        {
            NavigateTo(index);
        }
    }

    private void TestBlockStageButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string stageText } && int.TryParse(stageText, out var stageIndex))
        {
            if (stageIndex == 1)
            {
                OpenPoseActionEditor();
                return;
            }

            ShowTestBlockStage(stageIndex);
        }
    }

    private void ShowTestBlockStage(int stageIndex)
    {
        if (stageIndex is 1 or 2)
        {
            stageIndex = 0;
        }

        _currentTestBlockStageIndex = Math.Clamp(stageIndex, 0, _testBlockStagePanels.Length - 1);
        for (var index = 0; index < _testBlockStagePanels.Length; index++)
        {
            _testBlockStagePanels[index].Visibility = CurrentStepIndex == 3 && index == _currentTestBlockStageIndex
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        var stageButtons = new[]
        {
            TestBlockBasicStageButton,
            TestBlockTriggerStageButton
        };
        foreach (var stageButton in stageButtons)
        {
            var stageButtonIndex = int.Parse((string)stageButton.Tag, System.Globalization.CultureInfo.InvariantCulture);
            var isCurrent = stageButtonIndex == _currentTestBlockStageIndex;
            stageButton.Background = isCurrent ? (Brush)FindResource("GreenPaleBrush") : Brushes.White;
            stageButton.BorderBrush = (Brush)FindResource(isCurrent ? "GreenDarkBrush" : "BorderBrush");
            stageButton.Foreground = (Brush)FindResource(isCurrent ? "GreenDarkBrush" : "TextBrush");
            stageButton.FontWeight = isCurrent ? FontWeights.SemiBold : FontWeights.Normal;
        }

        if (CurrentStepIndex == 3)
        {
            UpdateTypePanels();
            UpdateTriggerEditorState();
            FooterHintText.Text = $"测试步设置 · {TestBlockModuleTitles[_currentTestBlockStageIndex]}";
            FooterHintText.ToolTip = "基本信息与自定义函数共同属于当前测试步，并统一参与保存校验。";
        }
    }

    private void PreviousButton_Click(object sender, RoutedEventArgs e) => NavigateTo(CurrentStepIndex - 1);

    private void NextButton_Click(object sender, RoutedEventArgs e)
    {
        if (!IsStepValid(CurrentStepIndex, out var validationMessage))
        {
            RefreshStepCompletionStates();
            if (CurrentStepIndex == 3 &&
                TryGetInvalidTestBlock(out var invalidItem, out var invalidStageIndex, out _))
            {
                if (invalidItem is not null)
                {
                    InspectionItemsList.SelectedItem = invalidItem;
                    SelectedInspectionItem = invalidItem;
                }

                ShowTestBlockStage(invalidStageIndex);
                if (invalidStageIndex == 1 && invalidItem?.TypeIndex == 1)
                {
                    OpenPoseActionEditor();
                }
            }

            FooterHintText.Text = validationMessage;
            return;
        }

        _confirmedStepIndexes.Add(CurrentStepIndex);
        RefreshStepCompletionStates();

        if (CurrentStepIndex == Steps.Count - 1)
        {
            ApplyReviewToOperator();
            return;
        }

        NavigateTo(CurrentStepIndex + 1);
    }

    private async void ExportSequenceButton_Click(object sender, RoutedEventArgs e)
    {
        if (!IsStepValid(Steps.Count - 1, out var validationMessage))
        {
            FooterHintText.Text = validationMessage;
            return;
        }

        await CompleteReviewAsync();
    }

    private void AddInspectionItem_Click(object sender, RoutedEventArgs e)
    {
        var defaultModel = Models.FirstOrDefault(model => model.TypeIndex == 0) ?? Models.FirstOrDefault();
        if (defaultModel is null)
        {
            FooterHintText.Text = "模型库为空。请先在第 3 步添加并选择模型文件。";
            NavigateTo(2);
            return;
        }
        string functionCode;
        do
        {
            functionCode = $"TS-CUSTOM-{_nextCustomTestStepIndex++:00}";
        }
        while (InspectionItems.Any(item => string.Equals(item.FunctionCode, functionCode, StringComparison.OrdinalIgnoreCase)) ||
               Editor.RetiredFunctionCodes.Contains(functionCode, StringComparer.OrdinalIgnoreCase));

        var item = new InspectionItemPreview(
            functionCode,
            "未命名测试步",
            0,
            true,
            defaultModel);
        TrackInspectionItem(item);
        InspectionItems.Add(item);
        Editor.MarkDirty();
        Editor.RefreshSummaries();
        InspectionItemsList.SelectedItem = item;
        SelectedInspectionItem = item;
        RefreshStepCompletionStates();
    }

    private void AddModel_Click(object sender, RoutedEventArgs e)
    {
        var model = new ModelPreview(
            $"新模型 {Models.Count + 1}",
            "未选择模型文件",
            0,
            true,
            ["label_0"]);
        TrackModel(model);
        Models.Add(model);
        RefreshCompatibleInspectionModels(repairSelection: true);
        Editor.MarkDirty();
        ModelItemsList.SelectedItem = model;
        SelectedModel = model;
        RefreshStepCompletionStates();
        FooterHintText.Text = "已新增模型卡片；请在右侧依次填写带红色 * 的内容。";
    }

    private async void SelectModelFile_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedModel is null)
        {
            return;
        }

        var dialog = new OpenFileDialog
        {
            Filter = "模型文件 (*.onnx;*.pt)|*.onnx;*.pt|ONNX 模型 (*.onnx)|*.onnx|PyTorch 模型 (*.pt)|*.pt",
            CheckFileExists = true,
            Multiselect = false
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            SelectedModel.FileName = dialog.FileName;
            await using var stream = new FileStream(dialog.FileName, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, true);
            SelectedModel.Sha256 = Convert.ToHexString(await SHA256.HashDataAsync(stream)).ToLowerInvariant();
            if (string.Equals(Path.GetExtension(dialog.FileName), ".onnx", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    var labels = OnnxModelLabelImporter.Import(dialog.FileName);
                    SelectedModel.ReplaceLabels(labels);
                    RepairBindingsAfterLabelReplacement(SelectedModel);
                    FooterHintText.Text = $"已选择模型文件并识别 {labels.Count} 个标签；可用加号、减号或直接改名继续调整。";
                }
                catch (Exception labelException) when (labelException is InvalidDataException or NotSupportedException)
                {
                    FooterHintText.Text = $"模型文件已选择，但未自动识别标签：{labelException.Message} 请使用下方加号、减号或直接改名。";
                }
            }
            else
            {
                FooterHintText.Text = "已选择模型文件；请使用下方加号、减号或直接改名维护标签。";
            }
        }
        catch (Exception exception)
        {
            FooterHintText.Text = $"读取模型失败：{exception.Message}";
        }
    }

    private void MoveInspectionItemUp_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { CommandParameter: InspectionItemPreview item })
        {
            MoveInspectionItem(item, -1);
        }
    }

    private void MoveInspectionItemDown_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { CommandParameter: InspectionItemPreview item })
        {
            MoveInspectionItem(item, 1);
        }
    }

    internal bool MoveInspectionItem(InspectionItemPreview item, int offset)
    {
        if (!Editor.MoveStep(item, offset))
        {
            return false;
        }

        InspectionItemsList.SelectedItem = item;
        SelectedInspectionItem = item;
        RefreshStepCompletionStates();
        return true;
    }

    private void RemoveSelectedModel_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedModel is null)
        {
            return;
        }

        var removedModelName = SelectedModel.Name;
        var previousIndex = Models.IndexOf(SelectedModel);
        var dependentItems = InspectionItems.Where(item => ReferenceEquals(item.Model, SelectedModel)).ToArray();
        foreach (var item in dependentItems)
        {
            item.PropertyChanged -= ConfigurationPropertyChanged;
            foreach (var poseStep in item.PoseSteps)
            {
                poseStep.PropertyChanged -= ConfigurationPropertyChanged;
            }

            Editor.RemoveStep(item);
            InspectionItems.Remove(item);
        }

        SelectedModel.PropertyChanged -= ConfigurationPropertyChanged;
        Models.Remove(SelectedModel);
        RefreshCompatibleInspectionModels(repairSelection: true);
        Editor.MarkDirty();
        ModelItemsList.SelectedIndex = Models.Count == 0 ? -1 : Math.Clamp(previousIndex, 0, Models.Count - 1);
        SelectedModel = ModelItemsList.SelectedItem as ModelPreview;
        InspectionItemsList.SelectedIndex = InspectionItems.Count == 0 ? -1 : 0;
        SelectedInspectionItem = InspectionItemsList.SelectedItem as InspectionItemPreview;
        RefreshStepCompletionStates();
        FooterHintText.Text = dependentItems.Length == 0
            ? $"已删除模型“{removedModelName}”；模型库可保持为空。"
            : $"已删除模型“{removedModelName}”及其绑定的 {dependentItems.Length} 个测试步；请重新添加模型和测试步。";
    }

    private void AddModelLabel_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedModel is null)
        {
            return;
        }

        var label = NewModelLabelTextBox.Text.Trim();
        if (!SelectedModel.TryAddLabel(label, out var error))
        {
            FooterHintText.Text = error;
            return;
        }

        NewModelLabelTextBox.Clear();
        RepairBindingsAfterLabelReplacement(SelectedModel);
        FooterHintText.Text = $"已添加标签“{label}”。";
    }

    private void RemoveModelLabel_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedModel is null || sender is not Button { CommandParameter: string label })
        {
            return;
        }

        if (!SelectedModel.TryRemoveLabel(label))
        {
            return;
        }

        RepairBindingsAfterLabelReplacement(SelectedModel);
        FooterHintText.Text = $"已删除标签“{label}”。";
    }

    private void ModelLabelTextBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (SelectedModel is null || sender is not TextBox { Tag: string oldLabel } textBox)
        {
            return;
        }

        var newLabel = textBox.Text.Trim();
        if (!SelectedModel.TryRenameLabel(oldLabel, newLabel, out var error))
        {
            textBox.Text = oldLabel;
            FooterHintText.Text = error;
            return;
        }

        RenameBoundLabel(SelectedModel, oldLabel, newLabel);
        FooterHintText.Text = $"标签“{oldLabel}”已改名为“{newLabel}”。";
    }

    private void RepairBindingsAfterLabelReplacement(ModelPreview model)
    {
        foreach (var item in InspectionItems.Where(item => ReferenceEquals(item.Model, model)))
        {
            item.RepairLabels();
        }

        if (SelectedInspectionItem is not null)
        {
            DetectionEditor.Load(SelectedInspectionItem);
        }

        Editor.MarkDirty();
        Editor.RefreshSummaries();
        RefreshStepCompletionStates();
    }

    private void RenameBoundLabel(ModelPreview model, string oldLabel, string newLabel)
    {
        foreach (var item in InspectionItems.Where(item => ReferenceEquals(item.Model, model)))
        {
            item.RenameLabel(oldLabel, newLabel);
        }

        if (SelectedInspectionItem is not null)
        {
            DetectionEditor.Load(SelectedInspectionItem);
        }

        Editor.MarkDirty();
        Editor.RefreshSummaries();
        RefreshStepCompletionStates();
    }

    private void ModelItemsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        SelectedModel = ModelItemsList.SelectedItem as ModelPreview;
    }

    private void RemoveInspectionItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { CommandParameter: InspectionItemPreview item })
        {
            RemoveInspectionItem(item);
        }
    }

    private void RemoveSelectedInspectionItem_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedInspectionItem is not null)
        {
            RemoveInspectionItem(SelectedInspectionItem);
        }
    }

    private void RemoveInspectionItem(InspectionItemPreview item)
    {
        if (InspectionItems.Count <= 1)
        {
            FooterHintText.Text = "至少保留 1 个测试步；最后一个测试步不能删除。";
            return;
        }

        var previousIndex = InspectionItems.IndexOf(item);
        item.PropertyChanged -= ConfigurationPropertyChanged;
        foreach (var poseStep in item.PoseSteps)
        {
            poseStep.PropertyChanged -= ConfigurationPropertyChanged;
        }

        Editor.RemoveStep(item);
        InspectionItems.Remove(item);
        Editor.RefreshSummaries();
        InspectionItemsList.SelectedIndex = Math.Clamp(previousIndex, 0, InspectionItems.Count - 1);
        SelectedInspectionItem = InspectionItemsList.SelectedItem as InspectionItemPreview;
        RefreshStepCompletionStates();
    }

    private void InspectionItemsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        SelectedInspectionItem = InspectionItemsList.SelectedItem as InspectionItemPreview;
        RefreshStepCompletionStates();
    }

    private void CustomFunctionStepComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_wizardReady ||
            CustomFunctionStepComboBox.SelectedItem is not InspectionItemPreview item ||
            ReferenceEquals(item, SelectedInspectionItem))
        {
            return;
        }

        InspectionItemsList.SelectedItem = item;
        SelectedInspectionItem = item;
        RefreshStepCompletionStates();
        ChooseInspectionModel(item);
    }

    private void InspectionTypeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (SelectedInspectionItem is null || InspectionTypeComboBox.SelectedIndex < 0)
        {
            return;
        }

        ApplyInspectionTypeSelection(
            InspectionTypeComboBox.SelectedIndex,
            openPoseEditor: _wizardReady && InspectionTypeComboBox.IsKeyboardFocusWithin);
    }

    private void ApplyInspectionTypeSelection(int typeIndex, bool openPoseEditor)
    {
        if (SelectedInspectionItem is not { } item || typeIndex is < 0 or > 2)
        {
            return;
        }

        item.TypeIndex = typeIndex;
        RefreshCompatibleInspectionModels(repairSelection: true);
        UpdateTypePanels();
        RefreshStepCompletionStates();

        if (openPoseEditor && item.TypeIndex == 1 && item.Model.TypeIndex == 1)
        {
            Dispatcher.BeginInvoke(new Action(OpenPoseActionEditor));
        }
    }

    internal void SelectInspectionTypeForSmoke(int typeIndex) =>
        ApplyInspectionTypeSelection(typeIndex, openPoseEditor: true);

    private void ChooseInspectionModel_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedInspectionItem is not { } item)
        {
            return;
        }

        ChooseInspectionModel(item);
    }

    private void ChooseInspectionModel(InspectionItemPreview item)
    {
        if (!ReferenceEquals(SelectedInspectionItem, item))
        {
            InspectionItemsList.SelectedItem = item;
            SelectedInspectionItem = item;
        }

        RefreshCompatibleInspectionModels(repairSelection: false);
        if (CompatibleInspectionModels.Count == 0)
        {
            FooterHintText.Text = $"项目模型库中没有与“{item.TypeLabel}”匹配的模型，请先返回第 3 步添加模型。";
            return;
        }

        if (SuppressModelSelectionDialogForSmoke)
        {
            return;
        }

        var dialog = new ModelSelectionDialog(CompatibleInspectionModels, item.Model) { Owner = this };
        if (dialog.ShowDialog() == true && dialog.SelectedModel is { } model)
        {
            ApplyInspectionModelSelection(model, openEditor: true);
        }
    }

    private void ApplyInspectionModelSelection(ModelPreview model, bool openEditor)
    {
        if (SelectedInspectionItem is not { } item)
        {
            return;
        }

        item.Model = model;
        item.TypeIndex = ToInspectionTypeIndex(model);
        RefreshCompatibleInspectionModels(repairSelection: false);
        if (item.TypeIndex != 1)
        {
            UpdateTypePanels();
            RefreshStepCompletionStates();
            if (openEditor)
            {
                OpenDetectionEditor();
            }

            return;
        }

        item.TypeIndex = 1;
        foreach (var action in item.PoseSteps.Where(action => action.Model.TypeIndex != 1))
        {
            action.Model = model;
        }

        UpdateTypePanels();
        RefreshStepCompletionStates();
        if (openEditor)
        {
            OpenPoseActionEditor();
        }
    }

    internal void SelectInspectionModelForSmoke(ModelPreview model) =>
        ApplyInspectionModelSelection(model, openEditor: true);

    private void AddPoseStep_Click(object sender, RoutedEventArgs e)
    {
        var step = new PoseStepPreview(
            PoseSteps.Count + 1,
            $"新动作 {PoseSteps.Count + 1}",
            true,
            SelectedInspectionItem!.Model);
        TrackPoseStep(step);
        PoseSteps.Add(step);
        Editor.MarkDirty();
        RefreshStepCompletionStates();
    }

    private void OpenPoseActionEditor_Click(object sender, RoutedEventArgs e) => OpenPoseActionEditor();

    internal void OpenPoseActionEditorForSmoke() => OpenPoseActionEditor();

    private void OpenPoseActionEditor()
    {
        if (SelectedInspectionItem is not { TypeIndex: 1 } item)
        {
            FooterHintText.Text = "只有姿态 / 时序测试步需要配置动作顺序。";
            return;
        }

        if (item.Model.TypeIndex != 1)
        {
            FooterHintText.Text = "请先在基本信息中选择姿态 / 时序模型；选择后会自动弹出动作顺序配置。";
            return;
        }

        if (Step5Panel.Visibility == Visibility.Visible)
        {
            return;
        }

        ShowTestBlockStage(0);
        _poseActionEditorSnapshot = PoseActionEditorSnapshot.Capture(item);
        PoseActionEditorStatusText.Text = "动作设置";
        Step5Panel.Visibility = Visibility.Visible;
        UpdateLayout();
    }

    private void ApplyPoseActionEditor_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedInspectionItem is not { TypeIndex: 1 } item)
        {
            return;
        }

        if (!TryValidatePoseActions(item, out var validationMessage))
        {
            PoseActionEditorStatusText.Text = validationMessage;
            return;
        }

        _poseActionEditorSnapshot = null;
        Step5Panel.Visibility = Visibility.Collapsed;
        Editor.MarkDirty();
        RefreshStepCompletionStates();
        FooterHintText.Text = "动作顺序已保存，并已回到基本信息；可通过摘要旁的“重新编辑动作顺序”再次打开。";
    }

    private void CancelPoseActionEditor_Click(object sender, RoutedEventArgs e) => ClosePoseActionEditor(restoreSnapshot: true);

    private void ClosePoseActionEditor(bool restoreSnapshot)
    {
        if (restoreSnapshot && _poseActionEditorSnapshot is { } snapshot)
        {
            _isRestoringPoseActionSnapshot = true;
            try
            {
                snapshot.Restore(this);
            }
            finally
            {
                _isRestoringPoseActionSnapshot = false;
            }
        }

        _poseActionEditorSnapshot = null;
        Step5Panel.Visibility = Visibility.Collapsed;
        RefreshStepCompletionStates();
        FooterHintText.Text = restoreSnapshot
            ? "已取消本次动作顺序编辑，并恢复打开弹窗前的内容。"
            : "动作顺序弹窗已关闭。";
    }

    private static bool TryValidatePoseActions(InspectionItemPreview item, out string validationMessage)
    {
        if (item.PoseSteps.Count == 0)
        {
            validationMessage = "请至少保留 1 个动作。";
            return false;
        }

        if (item.PoseSteps.Any(action =>
                string.IsNullOrWhiteSpace(action.Name) ||
                action.Model.TypeIndex != 1 ||
                string.IsNullOrWhiteSpace(action.ActionCondition) ||
                !IsConfidence(action.ConfidenceThresholdText) ||
                !IsNonNegativeInteger(action.MinimumHoldMsText) ||
                !IsPositiveInteger(action.MaximumWaitMsText)))
        {
            validationMessage = "请补全每个动作的名称、姿态模型、检测标签、置信度、保持时间和最大等待时间。";
            return false;
        }

        validationMessage = string.Empty;
        return true;
    }

    private void AddAdditionalRule_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedInspectionItem is null)
        {
            return;
        }

        var rule = new RulePreviewViewModel(SelectedInspectionItem.TargetLabel, SelectedInspectionItem.Model);
        rule.PropertyChanged += ConfigurationPropertyChanged;
        SelectedInspectionItem.AdditionalRules.Add(rule);
        Editor.MarkDirty();
        RefreshStepCompletionStates();
    }

    private void OpenDetectionEditor_Click(object sender, RoutedEventArgs e) => OpenDetectionEditor();

    private void OpenDetectionEditor()
    {
        if (SelectedInspectionItem is not { } item || !IsSingleFrameInspectionType(item.TypeIndex))
        {
            FooterHintText.Text = "姿态 / 时序测试步的动作顺序从基本信息摘要中重新编辑。";
            return;
        }

        DetectionEditor.Load(item);
        DetectionLabelEditorOverlay.Visibility = Visibility.Collapsed;
        DetectionEditorOverlay.Visibility = Visibility.Visible;
        DetectionEditorStatusText.Text = "检测标签配置";
        UpdateLayout();
    }

    private void CloseDetectionEditor_Click(object sender, RoutedEventArgs e) => CloseDetectionEditor();

    private void CloseDetectionEditor()
    {
        DetectionLabelEditorOverlay.Visibility = Visibility.Collapsed;
        DetectionEditorOverlay.Visibility = Visibility.Collapsed;
        DetectionEditor.EndLabelEdit();
        _isRoiDrawing = false;
        _roiDragStart = null;
        RoiPreviewSurface.ReleaseMouseCapture();
    }

    internal void CloseDetectionEditorForSmoke() => CloseDetectionEditor();

    private void OpenDetectionLabelEditor_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { CommandParameter: DetectionLabelOptionPreview option })
        {
            OpenDetectionLabelEditor(option);
        }
    }

    private void EditDetectionChild_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedInspectionItem is null || sender is not Button { CommandParameter: DetectionChildPreview child })
        {
            return;
        }

        OpenDetectionEditor();
        var option = DetectionEditor.LabelOptions.FirstOrDefault(candidate =>
            string.Equals(candidate.Label, child.Label, StringComparison.Ordinal));
        if (option is null)
        {
            DetectionEditorStatusText.Text = $"当前模型中找不到检测标签“{child.Label}”，请先确认模型绑定。";
            return;
        }

        OpenDetectionLabelEditor(option);
    }

    private void OpenDetectionLabelEditor(DetectionLabelOptionPreview option)
    {
        if (SelectedInspectionItem is not { } item || !IsSingleFrameInspectionType(item.TypeIndex))
        {
            return;
        }

        DetectionEditor.BeginLabelEdit(option);
        DetectionLabelEditorStatusText.Text = option.IsConfigured
            ? $"正在重新编辑检测标签“{option.Label}”；保存只更新这一项。"
            : $"正在配置新检测标签“{option.Label}”；保存后可继续选择下一个。";
        DetectionLabelEditorOverlay.Visibility = Visibility.Visible;

        var selectedRoiName = option.RoiOptions.FirstOrDefault(roi => roi.IsSelected)?.Name;
        NamedRoisListBox.SelectedItem = selectedRoiName is null
            ? item.NamedRois.FirstOrDefault()
            : item.NamedRois.FirstOrDefault(roi => string.Equals(roi.Name, selectedRoiName, StringComparison.Ordinal));
        SelectedNamedRoi = NamedRoisListBox.SelectedItem as NamedRoiPreview;
        UpdateLayout();
        UpdateDetectionLabelEditorLayout();
        option.RefreshSummaries();
        UpdateRoiVisual();
    }

    internal void OpenDetectionLabelEditorForSmoke(DetectionLabelOptionPreview option) =>
        OpenDetectionLabelEditor(option);

    private void CancelDetectionLabelEditor_Click(object sender, RoutedEventArgs e) => CancelDetectionLabelEditor();

    private void CancelDetectionLabelEditor()
    {
        DetectionLabelEditorOverlay.Visibility = Visibility.Collapsed;
        if (SelectedInspectionItem is { } item && IsSingleFrameInspectionType(item.TypeIndex))
        {
            DetectionEditor.Load(item);
        }

        DetectionEditorStatusText.Text = "已取消当前检测标签的修改；之前保存的检测标签保持不变。";
        _isRoiDrawing = false;
        _roiDragStart = null;
        RoiPreviewSurface.ReleaseMouseCapture();
    }

    private void DetectionArea_Changed(object sender, RoutedEventArgs e)
    {
        if (DetectionEditor.ActiveLabel is { } label)
        {
            label.RefreshSummaries();
            UpdateDetectionLabelEditorLayout();
            DetectionLabelEditorStatusText.Text = label.UseFullImage
                ? $"检测标签“{label.Label}”将检测整张图。"
                : $"检测标签“{label.Label}”将只检测：{label.ScopeSummary}。";
        }
    }

    private void DetectionRoiSelection_Changed(object sender, RoutedEventArgs e) =>
        RefreshActiveDetectionLabelScope();

    private void RefreshActiveDetectionLabelScope()
    {
        if (DetectionEditor.ActiveLabel is not { } label)
        {
            return;
        }

        label.RefreshSummaries();
        DetectionLabelEditorStatusText.Text = label.UseRoi
            ? $"检测标签“{label.Label}”当前检测：{label.ScopeSummary}。"
            : $"检测标签“{label.Label}”将检测整张图。";
    }

    internal void RefreshDetectionLabelScopeForSmoke() => RefreshActiveDetectionLabelScope();

    private void ApplyDetectionLabelEditor_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedInspectionItem is not { } item ||
            !IsSingleFrameInspectionType(item.TypeIndex) ||
            DetectionEditor.Model is null ||
            DetectionEditor.ActiveLabel is not { } option)
        {
            return;
        }

        if (option.UseRoi && item.RoiBackgroundImage is null)
        {
            DetectionLabelEditorStatusText.Text = "请先导入一张标注底图，再框选 ROI。";
            return;
        }

        if (option.UseRoi && option.RoiOptions.All(roi => !roi.IsSelected))
        {
            DetectionLabelEditorStatusText.Text = $"检测标签“{option.Label}”已选择 ROI 模式，请至少勾选 1 个区域。";
            return;
        }

        if (!IsNonNegativeInteger(DetectionEditor.ThresholdText) ||
            !IsConfidence(DetectionEditor.ConfidenceText) ||
            (DetectionEditor.RuleMethodIndex == 1 &&
             (!IsNonNegativeInteger(DetectionEditor.UpperThresholdText) ||
              int.Parse(DetectionEditor.ThresholdText) > int.Parse(DetectionEditor.UpperThresholdText))))
        {
            DetectionLabelEditorStatusText.Text = "请检查当前检测标签的阈值、范围与置信度。";
            return;
        }

        DetectionEditor.CommitActiveLabel();
        var child = new DetectionChildPreview(option.Label, option.ScopeSummary, option.RuleSummary);
        var existingChild = item.DetectionChildren.FirstOrDefault(candidate =>
            string.Equals(candidate.Label, option.Label, StringComparison.Ordinal));
        if (existingChild is null)
        {
            item.DetectionChildren.Add(child);
        }
        else
        {
            item.DetectionChildren[item.DetectionChildren.IndexOf(existingChild)] = child;
        }

        SynchronizeDetectionRules(item);

        Editor.MarkDirty();
        Editor.RefreshSummaries();
        RefreshStepCompletionStates();
        DetectionLabelEditorOverlay.Visibility = Visibility.Collapsed;
        DetectionEditor.EndLabelEdit();
        DetectionEditorStatusText.Text = $"检测标签“{option.Label}”已保存；当前共 {item.DetectionChildren.Count} 个检测标签，可继续配置下一个。";
        FooterHintText.Text = $"已保存检测标签“{option.Label}”；其他已配置检测标签均已保留。";
    }

    private void SynchronizeDetectionRules(InspectionItemPreview item)
    {
        foreach (var rule in item.AdditionalRules)
        {
            rule.PropertyChanged -= ConfigurationPropertyChanged;
        }

        item.AdditionalRules.Clear();
        if (item.DetectionChildren.Count == 0 || DetectionEditor.Model is null)
        {
            return;
        }

        var configuredOptions = item.DetectionChildren
            .Select(child => DetectionEditor.LabelOptions.FirstOrDefault(option =>
                string.Equals(option.Label, child.Label, StringComparison.Ordinal)))
            .Where(option => option is not null)
            .Cast<DetectionLabelOptionPreview>()
            .ToArray();
        if (configuredOptions.Length == 0)
        {
            return;
        }

        var primary = configuredOptions[0];
        item.Model = DetectionEditor.Model;
        item.TargetLabel = primary.Label;
        item.UseRoi = primary.UseRoi;
        item.RuleMetricIndex = primary.MetricIndex;
        item.RuleMethodIndex = primary.RuleMethodIndex;
        item.ExpectedCountText = primary.ThresholdText;
        item.RangeMaximumCountText = primary.UpperThresholdText;
        item.ConfidenceThresholdText = primary.ConfidenceText;
        item.RuleOutcomeIndex = primary.OutcomeIndex;

        var firstSelectedRoi = primary.RoiOptions.FirstOrDefault(roi => roi.IsSelected);
        var firstRoi = firstSelectedRoi is null
            ? null
            : item.NamedRois.FirstOrDefault(roi => string.Equals(roi.Name, firstSelectedRoi.Name, StringComparison.Ordinal));
        if (firstRoi is not null)
        {
            item.RoiRect = firstRoi.Rect;
            _roiLogicalRect = firstRoi.Rect;
        }

        foreach (var option in configuredOptions.Skip(1))
        {
            var rule = new RulePreviewViewModel(option.Label, DetectionEditor.Model)
            {
                MetricIndex = option.MetricIndex,
                RuleMethodIndex = option.RuleMethodIndex,
                ThresholdText = option.ThresholdText,
                UpperThresholdText = option.UpperThresholdText,
                ConfidenceText = option.ConfidenceText,
                OutcomeIndex = option.OutcomeIndex
            };
            rule.PropertyChanged += ConfigurationPropertyChanged;
            item.AdditionalRules.Add(rule);
        }
    }

    private void RemoveDetectionChild_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedInspectionItem is null || sender is not Button { CommandParameter: DetectionChildPreview child })
        {
            return;
        }

        SelectedInspectionItem.DetectionChildren.Remove(child);
        DetectionEditor.Load(SelectedInspectionItem);
        SynchronizeDetectionRules(SelectedInspectionItem);
        Editor.MarkDirty();
        Editor.RefreshSummaries();
        RefreshStepCompletionStates();
        FooterHintText.Text = $"已移除检测标签“{child.Label}”。";
    }

    private void AddNamedRoi_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedInspectionItem is null)
        {
            return;
        }

        var index = SelectedInspectionItem.NamedRois.Count + 1;
        var offset = Math.Min((index - 1) * 18, 90);
        var roi = new NamedRoiPreview(
            $"ROI-{index:00}",
            new Rect(90 + offset, 70 + offset, 300, 250));
        SelectedInspectionItem.NamedRois.Add(roi);
        DetectionEditor.AddRoiOption(roi.Name);
        NamedRoisListBox.SelectedItem = roi;
        SelectedNamedRoi = roi;
        DetectionEditorStatusText.Text = $"已新增“{roi.Name}”；可重命名并在右侧重新框选。";
    }

    private void RemoveNamedRoi_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedInspectionItem is null || sender is not Button { CommandParameter: NamedRoiPreview roi })
        {
            return;
        }

        if (SelectedInspectionItem.NamedRois.Count <= 1)
        {
            DetectionEditorStatusText.Text = "至少保留 1 个命名 ROI；若不使用区域，请为标签选择“是，检测整张图”。";
            return;
        }

        SelectedInspectionItem.NamedRois.Remove(roi);
        DetectionEditor.RemoveRoiOption(roi.Name);
        NamedRoisListBox.SelectedIndex = 0;
    }

    private void NamedRoisListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        SelectedNamedRoi = NamedRoisListBox.SelectedItem as NamedRoiPreview;
    }

    private void SelectCustomFunctionFile_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedInspectionItem is null)
        {
            return;
        }

        var dialog = new OpenFileDialog
        {
            Filter = "Python 函数文件 (*.py)|*.py|所有文件 (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = false
        };
        if (dialog.ShowDialog(this) == true)
        {
            SelectedInspectionItem.CustomFunctionFilePath = dialog.FileName;
            FooterHintText.Text = "已选择自定义函数文件；本轮仅保存前端展示，不执行 Python。";
        }
    }

    private void RemoveAdditionalRule_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedInspectionItem is null || sender is not Button { CommandParameter: RulePreviewViewModel rule })
        {
            return;
        }

        rule.PropertyChanged -= ConfigurationPropertyChanged;
        SelectedInspectionItem.AdditionalRules.Remove(rule);
        Editor.MarkDirty();
        RefreshStepCompletionStates();
    }

    private void RemovePoseStep_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { CommandParameter: PoseStepPreview step })
        {
            return;
        }

        if (PoseSteps.Count <= 1)
        {
            FooterHintText.Text = "姿态检测至少保留 1 个动作步骤。";
            return;
        }

        step.PropertyChanged -= ConfigurationPropertyChanged;
        PoseSteps.Remove(step);
        Editor.MarkDirty();
        RenumberPoseSteps();
        RefreshStepCompletionStates();
    }

    private void MovePoseStepUp_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { CommandParameter: PoseStepPreview step })
        {
            MovePoseStep(step, -1);
        }
    }

    private void MovePoseStepDown_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { CommandParameter: PoseStepPreview step })
        {
            MovePoseStep(step, 1);
        }
    }

    private void MovePoseStep(PoseStepPreview step, int offset)
    {
        var currentIndex = PoseSteps.IndexOf(step);
        var destinationIndex = currentIndex + offset;
        if (currentIndex < 0 || destinationIndex < 0 || destinationIndex >= PoseSteps.Count)
        {
            return;
        }

        PoseSteps.Move(currentIndex, destinationIndex);
        RenumberPoseSteps();
        Editor.MarkDirty();
    }

    internal void MovePoseStepForSmoke(PoseStepPreview step, int offset) => MovePoseStep(step, offset);

    private void RenumberPoseSteps()
    {
        for (var index = 0; index < PoseSteps.Count; index++)
        {
            PoseSteps[index].Order = index + 1;
        }
    }

    private void UpdateTypePanels()
    {
        if (!IsInitialized || SelectedInspectionItem is null)
        {
            return;
        }

        var isPose = SelectedInspectionItem.TypeIndex == 1;
        TargetContentPanel.Visibility = Visibility.Collapsed;
        PoseContentPanel.Visibility = isPose ? Visibility.Visible : Visibility.Collapsed;
        TargetRulePanel.Visibility = isPose ? Visibility.Collapsed : Visibility.Visible;
        PoseRulePanel.Visibility = isPose ? Visibility.Visible : Visibility.Collapsed;
        NormalDetectionSetupPanel.Visibility = isPose ? Visibility.Collapsed : Visibility.Visible;
        PoseBasicInfoHint.Visibility = isPose ? Visibility.Visible : Visibility.Collapsed;
        EditPoseActionsButton.IsEnabled = isPose && SelectedInspectionItem.Model.TypeIndex == 1;
        if (!isPose && Step5Panel.Visibility == Visibility.Visible)
        {
            _poseActionEditorSnapshot = null;
            Step5Panel.Visibility = Visibility.Collapsed;
        }

        UpdateInspectionModelStatus();
        UpdateRuleEditorState();
    }

    private ModelPreview? RefreshCompatibleInspectionModels(bool repairSelection)
    {
        if (_isRefreshingCompatibleInspectionModels)
        {
            return null;
        }

        _isRefreshingCompatibleInspectionModels = true;
        try
        {
            if (SelectedInspectionItem is not { } item)
            {
                CompatibleInspectionModels.Clear();
                UpdateInspectionModelStatus();
                return null;
            }

            var expectedModelTypeIndex = ToModelTypeIndex(item.TypeIndex);
            var compatibleModels = Models.Where(model => model.TypeIndex == expectedModelTypeIndex).ToArray();
            if (!CompatibleInspectionModels.SequenceEqual(compatibleModels))
            {
                CompatibleInspectionModels.Clear();
                foreach (var model in compatibleModels)
                {
                    CompatibleInspectionModels.Add(model);
                }
            }

            if (CompatibleInspectionModels.Contains(item.Model) ||
                !repairSelection ||
                CompatibleInspectionModels.FirstOrDefault() is not { } replacement)
            {
                UpdateInspectionModelStatus();
                return null;
            }

            item.Model = replacement;
            if (item.TypeIndex == 1)
            {
                foreach (var action in item.PoseSteps.Where(action => action.Model.TypeIndex != 1))
                {
                    action.Model = replacement;
                }
            }

            UpdateInspectionModelStatus();
            return replacement;
        }
        finally
        {
            _isRefreshingCompatibleInspectionModels = false;
        }
    }

    private void UpdateInspectionModelStatus()
    {
        if (!IsInitialized || SelectedInspectionItem is not { } item)
        {
            return;
        }

        var typeName = item.TypeLabel;
        var hasCompatibleModel = CompatibleInspectionModels.Count > 0;
        ChooseInspectionModelButton.IsEnabled = hasCompatibleModel;
        InspectionModelStatusText.Foreground = hasCompatibleModel
            ? (Brush)FindResource("MutedTextBrush")
            : Brushes.DarkGoldenrod;
        var compatibilityHint = $"仅显示与“{typeName}”匹配的模型；更换模型后按该模型的检测标签分别配置。";
        ChooseInspectionModelButton.ToolTip = compatibilityHint;
        InspectionModelStatusText.Visibility = hasCompatibleModel ? Visibility.Collapsed : Visibility.Visible;
        InspectionModelStatusText.Text = hasCompatibleModel
            ? string.Empty
            : $"项目模型库中没有“{typeName}”模型，请先返回第 03 步导入或修改模型类型。";
    }

    private void UpdateDetectionLabelEditorLayout()
    {
        if (!IsInitialized)
        {
            return;
        }

        var useRoi = DetectionEditor.ActiveLabel?.UseRoi == true;
        DetectionLabelRegionColumn.Width = new GridLength(useRoi ? 3 : 2, GridUnitType.Star);
        DetectionLabelRuleColumn.Width = new GridLength(useRoi ? 2 : 3, GridUnitType.Star);
    }

    private void UpdateRuleEditorState()
    {
        if (!_wizardReady || !IsInitialized)
        {
            return;
        }

        if (SelectedInspectionItem is { } item &&
            IsSingleFrameInspectionType(item.TypeIndex) &&
            TargetRuleTargetComboBox.Items.Count > 0 &&
            TargetRuleTargetComboBox.SelectedIndex < 0)
        {
            TargetRuleTargetComboBox.SelectedIndex = 0;
        }

        if (PoseSteps.Count > 0 && PoseActionComboBox.SelectedIndex < 0)
        {
            PoseActionComboBox.SelectedIndex = 0;
        }

        var isRange = TargetRuleMethodComboBox.SelectedIndex == 1;
        RangeMaximumPanel.Visibility = isRange ? Visibility.Visible : Visibility.Collapsed;
        ExpectedCountLabelText.Text = TargetRuleMethodComboBox.SelectedIndex switch
        {
            1 => "最小数量",
            2 => "阈值数量",
            _ => "期望数量"
        };

        UpdateRuleSummaries();
    }

    private void UpdateRuleSummaries()
    {
        if (!_wizardReady || !IsInitialized)
        {
            return;
        }

        var target = TargetRuleTargetComboBox.SelectedItem?.ToString();
        var region = FullImageRegionRadioButton.IsChecked == true
            ? "整张图片"
            : $"指定 ROI（{RoiCoordinatesText.Text.Replace("  ·  ", "、", StringComparison.Ordinal)}）";

        if (string.IsNullOrWhiteSpace(target))
        {
            TargetRuleSummaryText.Text = "请先选择本条判定要统计的检测目标。";
        }
        else if (!IsNonNegativeInteger(ExpectedCountTextBox.Text))
        {
            TargetRuleSummaryText.Text = "请填写有效的非负整数数量，系统会在这里生成最终判定。";
        }
        else
        {
            var count = int.Parse(ExpectedCountTextBox.Text);
            var outcome = PrimaryRuleOutcomeComboBox.SelectedIndex == 1 ? "不通过（Fail）" : "通过（Pass）";
            switch (TargetRuleMethodComboBox.SelectedIndex)
            {
                case 0:
                    TargetRuleSummaryText.Text = $"在{region}内统计目标“{target}”，识别数量等于 {count} 时，当前测试步{outcome}。";
                    break;
                case 1 when IsNonNegativeInteger(RangeMaximumCountTextBox.Text):
                    var maximum = int.Parse(RangeMaximumCountTextBox.Text);
                    TargetRuleSummaryText.Text = count <= maximum
                        ? $"在{region}内统计目标“{target}”，识别数量在 {count} 到 {maximum} 之间（含边界）时，当前测试步{outcome}。"
                        : "数量范围无效：最小数量不能大于最大数量。";
                    break;
                case 1:
                    TargetRuleSummaryText.Text = "请填写有效的最大数量，系统会在这里生成数量范围判定。";
                    break;
                case 2:
                    TargetRuleSummaryText.Text = $"在{region}内统计目标“{target}”，识别数量大于 {count} 时，当前测试步{outcome}。";
                    break;
                case 3:
                    TargetRuleSummaryText.Text = $"在{region}内统计目标“{target}”，识别数量不等于 {count} 时，当前测试步{outcome}。";
                    break;
                case 4:
                    TargetRuleSummaryText.Text = $"在{region}内统计目标“{target}”，识别数量大于等于 {count} 时，当前测试步{outcome}。";
                    break;
                case 5:
                    TargetRuleSummaryText.Text = $"在{region}内统计目标“{target}”，识别数量小于 {count} 时，当前测试步{outcome}。";
                    break;
                case 6:
                    TargetRuleSummaryText.Text = $"在{region}内统计目标“{target}”，识别数量小于等于 {count} 时，当前测试步{outcome}。";
                    break;
                default:
                    TargetRuleSummaryText.Text = "请先选择判断方式。";
                    break;
            }
        }

        var poseAction = PoseActionComboBox.SelectedItem as PoseStepPreview;
        if (poseAction is null ||
            !IsNonNegativeInteger(PoseHoldTimeTextBox.Text) ||
            !IsPositiveInteger(PoseMaxWaitTextBox.Text))
        {
            PoseRuleSummaryText.Text = "请选中动作并填写有效的保持时间和最大等待时间。";
            return;
        }

        PoseRuleSummaryText.Text =
            $"动作“{poseAction.Name}”需连续保持至少 {PoseHoldTimeTextBox.Text} ms，并在 {PoseMaxWaitTextBox.Text} ms 内完成；" +
            "系统按画布顺序检查全部必选动作，全部满足时当前测试步通过。";
    }

    private void ImportRoiBackground_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedInspectionItem is not { } item)
        {
            return;
        }

        var dialog = new OpenFileDialog
        {
            Filter = "图像文件 (*.png;*.jpg;*.jpeg;*.bmp)|*.png;*.jpg;*.jpeg;*.bmp",
            CheckFileExists = true,
            Multiselect = false
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.UriSource = new Uri(dialog.FileName, UriKind.Absolute);
            image.EndInit();
            image.Freeze();
            item.RoiBackgroundPath = dialog.FileName;
            item.RoiBackgroundImage = image;
            UpdateRoiReferenceFrame(item, image.PixelWidth, image.PixelHeight);
            UpdateRoiVisual();
            DetectionLabelEditorStatusText.Text = "标注底图已导入；现在可以在图上拖动框选唯一的 ROI。";
            RefreshStepCompletionStates();
        }
        catch (Exception exception)
        {
            DetectionLabelEditorStatusText.Text = $"导入标注底图失败：{exception.Message}";
        }
    }

    internal void SetRoiBackgroundForSmoke()
    {
        if (SelectedInspectionItem is not { } item)
        {
            return;
        }

        var image = new DrawingImage(new GeometryDrawing(
            Brushes.LightGray,
            null,
            new RectangleGeometry(new Rect(0, 0, DefaultRoiReferenceWidth, DefaultRoiReferenceHeight))));
        image.Freeze();
        item.RoiBackgroundPath = "roi-smoke-background.png";
        item.RoiBackgroundImage = image;
        UpdateRoiReferenceFrame(item, DefaultRoiReferenceWidth, DefaultRoiReferenceHeight);
        UpdateRoiVisual();
    }

    private void RedrawRoiButton_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedInspectionItem?.RoiBackgroundImage is null)
        {
            DetectionLabelEditorStatusText.Text = "请先导入一张标注底图。";
            return;
        }

        RoiRegionRadioButton.IsChecked = true;
        RoiPreviewSurface.Focus();
        FooterHintText.Text = "请在右侧图像预览中按住鼠标左键拖动；松开后会更新 ROI 坐标。";
    }

    private void RoiPreviewSurface_Loaded(object sender, RoutedEventArgs e) => UpdateRoiVisual();

    private void RoiPreviewSurface_SizeChanged(object sender, SizeChangedEventArgs e) => UpdateRoiVisual();

    private void RoiPreviewSurface_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (SelectedInspectionItem?.RoiBackgroundImage is null)
        {
            DetectionLabelEditorStatusText.Text = "请先导入一张标注底图，再框选 ROI。";
            e.Handled = true;
            return;
        }

        var pointerPosition = e.GetPosition(RoiPreviewSurface);
        if (!GetRoiImageViewport().Contains(pointerPosition))
        {
            DetectionLabelEditorStatusText.Text = "请在实际图像区域内框选 ROI；灰色留白不属于图像。";
            e.Handled = true;
            return;
        }

        RoiRegionRadioButton.IsChecked = true;
        _roiBeforeDrag = _roiLogicalRect;
        _roiDragStart = ClampToRoiSurface(pointerPosition);
        _isRoiDrawing = RoiPreviewSurface.CaptureMouse();
        if (!_isRoiDrawing)
        {
            _roiDragStart = null;
            return;
        }

        ApplyRoiFromPreviewPoints(_roiDragStart.Value, _roiDragStart.Value);
        e.Handled = true;
    }

    private void RoiPreviewSurface_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_isRoiDrawing || _roiDragStart is null || e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        ApplyRoiFromPreviewPoints(_roiDragStart.Value, ClampToRoiSurface(e.GetPosition(RoiPreviewSurface)));
        e.Handled = true;
    }

    private void RoiPreviewSurface_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_isRoiDrawing || _roiDragStart is null)
        {
            return;
        }

        ApplyRoiFromPreviewPoints(_roiDragStart.Value, ClampToRoiSurface(e.GetPosition(RoiPreviewSurface)));
        var selectionIsLargeEnough = _roiLogicalRect.Width >= 4 && _roiLogicalRect.Height >= 4;
        _isRoiDrawing = false;
        _roiDragStart = null;
        RoiPreviewSurface.ReleaseMouseCapture();

        if (!selectionIsLargeEnough)
        {
            _roiLogicalRect = _roiBeforeDrag;
            if (SelectedInspectionItem is not null)
            {
                SelectedInspectionItem.RoiRect = _roiLogicalRect;
            }

            UpdateRoiVisual();
            FooterHintText.Text = "框选范围过小，已保留上一次 ROI；请按住鼠标拖出一个矩形区域。";
        }
        else
        {
            FooterHintText.Text = $"{SelectedNamedRoi?.Name ?? "ROI"} 已更新：{RoiCoordinatesText.Text}。";
        }

        RefreshStepCompletionStates();
        e.Handled = true;
    }

    private void RoiPreviewSurface_LostMouseCapture(object sender, MouseEventArgs e)
    {
        if (!_isRoiDrawing)
        {
            return;
        }

        _isRoiDrawing = false;
        _roiDragStart = null;
        _roiLogicalRect = _roiBeforeDrag;
        if (SelectedInspectionItem is not null)
        {
            SelectedInspectionItem.RoiRect = _roiLogicalRect;
        }

        if (SelectedNamedRoi is not null)
        {
            SelectedNamedRoi.Rect = _roiLogicalRect;
        }

        UpdateRoiVisual();
        RefreshStepCompletionStates();
    }

    private void DetectionRegion_Changed(object sender, RoutedEventArgs e)
    {
        if (_wizardReady)
        {
            RefreshStepCompletionStates();
            UpdateRuleSummaries();
        }
    }

    private void TriggerEntry_Changed(object sender, RoutedEventArgs e)
    {
        if (_wizardReady)
        {
            UpdateTriggerEditorState();
            RefreshStepCompletionStates();
        }
    }

    private void UpdateTriggerEditorState()
    {
        // “触发与运行”已在本轮前端中收敛为“自定义函数”。
        // 旧 Trigger Contract 仍留在 Draft 模型中，当前界面不再展示或扩展它。
    }

    private void ApplyRoiFromPreviewPoints(Point start, Point end)
    {
        var item = SelectedInspectionItem;
        var referenceWidth = item?.RoiReferenceWidth ?? DefaultRoiReferenceWidth;
        var referenceHeight = item?.RoiReferenceHeight ?? DefaultRoiReferenceHeight;
        var viewport = GetRoiImageViewport();
        start = ClampToRoiSurface(start);
        end = ClampToRoiSurface(end);
        var left = Math.Min(start.X, end.X);
        var top = Math.Min(start.Y, end.Y);
        var right = Math.Max(start.X, end.X);
        var bottom = Math.Max(start.Y, end.Y);

        _roiLogicalRect = new Rect(
            Math.Round((left - viewport.Left) / Math.Max(1, viewport.Width) * referenceWidth),
            Math.Round((top - viewport.Top) / Math.Max(1, viewport.Height) * referenceHeight),
            Math.Round((right - left) / Math.Max(1, viewport.Width) * referenceWidth),
            Math.Round((bottom - top) / Math.Max(1, viewport.Height) * referenceHeight));
        if (item is not null)
        {
            item.RoiRect = _roiLogicalRect;
        }

        if (SelectedNamedRoi is not null)
        {
            SelectedNamedRoi.Rect = _roiLogicalRect;
        }

        UpdateRoiVisual();
        RefreshStepCompletionStates();
    }

    private Point ClampToRoiSurface(Point point)
    {
        var viewport = GetRoiImageViewport();
        return new Point(
            Math.Clamp(point.X, viewport.Left, viewport.Right),
            Math.Clamp(point.Y, viewport.Top, viewport.Bottom));
    }

    private Rect GetRoiImageViewport()
    {
        var surfaceWidth = Math.Max(1, RoiPreviewSurface.ActualWidth);
        var surfaceHeight = Math.Max(1, RoiPreviewSurface.ActualHeight);
        var referenceWidth = Math.Max(1, SelectedInspectionItem?.RoiReferenceWidth ?? DefaultRoiReferenceWidth);
        var referenceHeight = Math.Max(1, SelectedInspectionItem?.RoiReferenceHeight ?? DefaultRoiReferenceHeight);
        var scale = Math.Min(surfaceWidth / referenceWidth, surfaceHeight / referenceHeight);
        var width = referenceWidth * scale;
        var height = referenceHeight * scale;
        return new Rect((surfaceWidth - width) / 2, (surfaceHeight - height) / 2, width, height);
    }

    private void UpdateRoiReferenceFrame(InspectionItemPreview item, double width, double height)
    {
        if (width <= 0 || height <= 0)
        {
            return;
        }

        var previousWidth = Math.Max(1, item.RoiReferenceWidth);
        var previousHeight = Math.Max(1, item.RoiReferenceHeight);
        item.RoiRect = ScaleRoi(item.RoiRect, previousWidth, previousHeight, width, height);
        foreach (var namedRoi in item.NamedRois)
        {
            namedRoi.Rect = ScaleRoi(namedRoi.Rect, previousWidth, previousHeight, width, height);
        }

        item.RoiReferenceWidth = width;
        item.RoiReferenceHeight = height;
        if (ReferenceEquals(item, SelectedInspectionItem))
        {
            _roiLogicalRect = SelectedNamedRoi?.Rect ?? item.RoiRect;
        }
    }

    private static Rect ScaleRoi(
        Rect roi,
        double previousWidth,
        double previousHeight,
        double width,
        double height)
    {
        var left = Math.Clamp(roi.Left / previousWidth * width, 0, width);
        var top = Math.Clamp(roi.Top / previousHeight * height, 0, height);
        var right = Math.Clamp(roi.Right / previousWidth * width, left, width);
        var bottom = Math.Clamp(roi.Bottom / previousHeight * height, top, height);
        return new Rect(left, top, right - left, bottom - top);
    }

    private void UpdateRoiVisual()
    {
        if (RoiPreviewSurface.ActualWidth <= 0 || RoiPreviewSurface.ActualHeight <= 0)
        {
            return;
        }

        var referenceWidth = Math.Max(1, SelectedInspectionItem?.RoiReferenceWidth ?? DefaultRoiReferenceWidth);
        var referenceHeight = Math.Max(1, SelectedInspectionItem?.RoiReferenceHeight ?? DefaultRoiReferenceHeight);
        var viewport = GetRoiImageViewport();
        var scaleX = viewport.Width / referenceWidth;
        var scaleY = viewport.Height / referenceHeight;
        var left = viewport.Left + _roiLogicalRect.X * scaleX;
        var top = viewport.Top + _roiLogicalRect.Y * scaleY;
        var width = _roiLogicalRect.Width * scaleX;
        var height = _roiLogicalRect.Height * scaleY;

        Canvas.SetLeft(RoiSelectionRectangle, left);
        Canvas.SetTop(RoiSelectionRectangle, top);
        RoiSelectionRectangle.Width = width;
        RoiSelectionRectangle.Height = height;
        RoiSelectionRectangle.Visibility = width > 0 && height > 0 ? Visibility.Visible : Visibility.Collapsed;

        Canvas.SetLeft(RoiSelectionLabel, Math.Clamp(left + 7, viewport.Left, Math.Max(viewport.Left, viewport.Right - 110)));
        Canvas.SetTop(RoiSelectionLabel, Math.Clamp(top + 7, viewport.Top, Math.Max(viewport.Top, viewport.Bottom - 28)));
        RoiSelectionLabel.Visibility = RoiSelectionRectangle.Visibility;

        RoiCoordinatesText.Text = $"X1 {(int)_roiLogicalRect.Left}  ·  Y1 {(int)_roiLogicalRect.Top}  ·  " +
                                  $"X2 {(int)_roiLogicalRect.Right}  ·  Y2 {(int)_roiLogicalRect.Bottom}";
        UpdateRuleSummaries();
    }

    private void RequiredField_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_wizardReady)
        {
            RefreshStepCompletionStates();
            UpdateRuleEditorState();
        }
    }

    private void RequiredSelection_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_wizardReady)
        {
            RefreshStepCompletionStates();
            UpdateRuleEditorState();
        }
    }

    private void SourceSelection_Changed(object sender, RoutedEventArgs e)
    {
        if (FolderSourceRadioButton is null || VideoFolderSourceRadioButton is null)
        {
            return;
        }

        var selectedSourceKind = VideoFolderSourceRadioButton.IsChecked == true ? 3 : 0;
        UpdateSourceSettingsPanels(selectedSourceKind);

        if (!_wizardReady || _isSwitchingSourceKind)
        {
            return;
        }

        _isSwitchingSourceKind = true;
        try
        {
            var previousSourceKind = Editor.SourceKindIndex;
            if (previousSourceKind >= 0 && previousSourceKind < _sourceAddresses.Length)
            {
                _sourceAddresses[previousSourceKind] = Editor.SourceAddress;
            }

            Editor.SourceKindIndex = selectedSourceKind;
            if (previousSourceKind != selectedSourceKind)
            {
                Editor.SourceAddress = _sourceAddresses[selectedSourceKind];
            }

            FooterHintText.Text = selectedSourceKind switch
            {
                3 => "已选择视频文件夹；浏览文件夹后可继续下一步。视频读取逻辑待后续开发。",
                _ => "已选择图片文件夹；填写路径后可继续下一步。"
            };
            RefreshStepCompletionStates();
        }
        finally
        {
            _isSwitchingSourceKind = false;
        }
    }

    private void UpdateSourceSettingsPanels(int sourceKindIndex)
    {
        if (FolderSourceSettingsPanel is null || FolderSourceSettingsTitleText is null)
        {
            return;
        }

        var isVideoFolder = sourceKindIndex == 3;
        FolderSourceSettingsPanel.Visibility = Visibility.Visible;
        FolderSourceSettingsTitleText.Text = isVideoFolder ? "视频文件夹" : "图片文件夹";
        FolderSourceSettingsInfoGlyph.ToolTip = isVideoFolder
            ? "请选择包含待检测视频的文件夹；视频运行时仍保持适配器门禁。"
            : "请选择包含检测图片的文件夹；操作台每次读取下一张图片，并将图片主文件名作为序列号。";
        FolderSourceSettingsHintText.Text = isVideoFolder
            ? "当前先确认视频文件夹选择界面；视频解码、抽帧和时序逻辑留到功能开发阶段。"
            : "当前先确认图片文件夹选择界面；实际读取和检查逻辑留到功能开发阶段。";
    }

    private void TrackModel(ModelPreview model) => model.PropertyChanged += ConfigurationPropertyChanged;

    private void TrackInspectionItem(InspectionItemPreview item)
    {
        item.PropertyChanged += ConfigurationPropertyChanged;
        foreach (var poseStep in item.PoseSteps)
        {
            TrackPoseStep(poseStep);
        }

        foreach (var rule in item.AdditionalRules)
        {
            rule.PropertyChanged += ConfigurationPropertyChanged;
        }
    }

    private void BrowseSourceFolder_Click(object sender, RoutedEventArgs e)
    {
        var isVideoFolder = VideoFolderSourceRadioButton.IsChecked == true;
        var dialog = new OpenFolderDialog
        {
            Title = isVideoFolder ? "选择视频文件夹" : "选择图片文件夹",
            Multiselect = false
        };
        if (Directory.Exists(FolderPathTextBox.Text))
        {
            dialog.InitialDirectory = FolderPathTextBox.Text;
        }

        if (dialog.ShowDialog(this) == true)
        {
            FolderPathTextBox.Text = dialog.FolderName;
            FooterHintText.Text = isVideoFolder
                ? "已选择视频文件夹；本轮只保存前端路径，不执行视频解码或抽帧。"
                : "已选择图片文件夹；Folder 运行时将按图片文件名派生序列号。";
        }
    }

    private void TrackPoseStep(PoseStepPreview step) => step.PropertyChanged += ConfigurationPropertyChanged;

    private void ConfigurationPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_isRestoringPoseActionSnapshot)
        {
            return;
        }

        if (_wizardReady)
        {
            if (sender is ModelPreview model && e.PropertyName == nameof(ModelPreview.TypeIndex))
            {
                foreach (var item in InspectionItems.Where(item => ReferenceEquals(item.Model, model)))
                {
                    item.TypeIndex = ToInspectionTypeIndex(model);
                }

                RefreshCompatibleInspectionModels(repairSelection: true);
            }

            if (sender is InspectionItemPreview &&
                e.PropertyName is nameof(InspectionItemPreview.TypeIndex) or nameof(InspectionItemPreview.Model))
            {
                UpdateTypePanels();
            }

            if (ReferenceEquals(sender, SelectedInspectionItem) &&
                e.PropertyName == nameof(InspectionItemPreview.ExternalTriggerEnabled))
            {
                UpdateTriggerEditorState();
            }

            Editor.MarkDirty();
            Editor.RefreshSummaries();
            RefreshStepCompletionStates();
            UpdateRuleEditorState();
        }
    }

    private void RefreshStepCompletionStates()
    {
        if (!_wizardReady)
        {
            return;
        }

        foreach (var step in Steps)
        {
            step.IsCompleted = _confirmedStepIndexes.Contains(step.Index) &&
                               IsStepValid(step.Index, out _);
        }
    }

    private bool IsStepValid(int index, out string validationMessage)
    {
        switch (index)
        {
            case 0:
                if (HasText(ProjectNameTextBox) && HasText(StationNameTextBox) &&
                    HasText(SequenceNameTextBox) && HasText(SequenceVersionTextBox))
                {
                    validationMessage = string.Empty;
                    return true;
                }

                validationMessage = "第 1 步未完成：请填写项目名称、工位名称、型号和版本。";
                return false;

            case 1:
                if (FolderSourceRadioButton.IsChecked != true &&
                    VideoFolderSourceRadioButton.IsChecked != true)
                {
                    validationMessage = "第 2 步未完成：请选择图片文件夹或视频文件夹。";
                    return false;
                }

                if (!HasText(FolderPathTextBox))
                {
                    validationMessage = VideoFolderSourceRadioButton.IsChecked == true
                        ? "第 2 步未完成：视频图源必须填写文件夹路径。"
                        : "第 2 步未完成：图片图源必须填写文件夹路径。";
                    return false;
                }

                validationMessage = string.Empty;
                return true;

            case 2:
                var invalidModel = Models.FirstOrDefault(model =>
                    string.IsNullOrWhiteSpace(model.Name) ||
                    !IsSupportedModelFile(model.FileName) ||
                    model.TypeIndex is not (0 or 1 or 3) ||
                    model.Labels.Count == 0 ||
                    model.Labels.Any(string.IsNullOrWhiteSpace));
                if (Models.Count > 0 && invalidModel is null)
                {
                    validationMessage = string.Empty;
                    return true;
                }

                validationMessage = invalidModel is null
                    ? "第 3 步未完成：请至少添加 1 个模型。"
                    : $"第 3 步未完成：请补全模型“{invalidModel.Name}”的名称、ONNX/PT 文件、任务类型和标签。";
                return false;

            case 3:
                if (!TryGetInvalidTestBlock(out _, out _, out var testBlockMessage) && InspectionItems.Count > 0)
                {
                    validationMessage = string.Empty;
                    return true;
                }

                validationMessage = InspectionItems.Count == 0
                    ? "第 4 步未完成：请至少添加一个测试步。"
                    : $"第 4 步未完成：{testBlockMessage}";
                return false;

            case 4:
                var incompleteSteps = Steps
                    .Take(Steps.Count - 1)
                    .Where(step => !step.IsCompleted)
                    .Select(step => step.Number)
                    .ToArray();
                if (incompleteSteps.Length == 0)
                {
                    validationMessage = string.Empty;
                    return true;
                }

                validationMessage = $"尚不能确认：请先完成步骤 {string.Join("、", incompleteSteps)}。";
                return false;

            default:
                validationMessage = "当前步骤无效。";
                return false;
        }
    }

    private bool TryGetInvalidTestBlock(
        out InspectionItemPreview? invalidItem,
        out int invalidStageIndex,
        out string validationMessage)
    {
        invalidItem = null;
        invalidStageIndex = 0;
        if (InspectionItems.Count == 0)
        {
            validationMessage = "请至少添加 1 个测试步。";
            return true;
        }

        foreach (var item in InspectionItems)
        {
            if (string.IsNullOrWhiteSpace(item.Name) ||
                item.TypeIndex is < 0 or > 2 ||
                !Models.Contains(item.Model))
            {
                invalidItem = item;
                validationMessage = $"请补全测试步“{item.Name}”的名称、类型和模型绑定。";
                return true;
            }

            if (IsSingleFrameInspectionType(item.TypeIndex))
            {
                if (item.DetectionChildren.Count == 0)
                {
                    invalidItem = item;
                    invalidStageIndex = 0;
                    validationMessage = $"请为测试步“{item.Name}”至少添加 1 个检测标签。";
                    return true;
                }

                var expectedModelTypeIndex = item.TypeIndex == 2 ? 3 : 0;
                if (item.Model.TypeIndex != expectedModelTypeIndex ||
                    item.AdditionalRules.Any(rule =>
                        !Models.Contains(rule.Model) || rule.Model.TypeIndex != item.Model.TypeIndex))
                {
                    invalidItem = item;
                    invalidStageIndex = 0;
                    validationMessage = item.TypeIndex == 2
                        ? $"测试步“{item.Name}”的图像分割类型必须绑定图像分割模型。"
                        : $"测试步“{item.Name}”的目标检测类型必须绑定目标检测模型。";
                    return true;
                }

                if (string.IsNullOrWhiteSpace(item.TargetLabel) || !item.Model.Labels.Contains(item.TargetLabel))
                {
                    invalidItem = item;
                    invalidStageIndex = 0;
                    validationMessage = $"请为测试步“{item.Name}”选择有效的模型标签。";
                    return true;
                }

                if (item.UseRoi && item.RoiBackgroundImage is null)
                {
                    invalidItem = item;
                    invalidStageIndex = 0;
                    validationMessage = $"请为测试步“{item.Name}”导入标注底图后再配置 ROI。";
                    return true;
                }

                if (item.UseRoi && (item.RoiRect.Width <= 0 || item.RoiRect.Height <= 0))
                {
                    invalidItem = item;
                    invalidStageIndex = 0;
                    validationMessage = $"请为测试步“{item.Name}”框选有效 ROI，或改为检测整张图片。";
                    return true;
                }

                var targetRuleReady = item.RuleMethodIndex is >= 0 and <= 6 &&
                                      item.RuleMetricIndex is >= 0 and <= 2 &&
                                      item.RuleOutcomeIndex is >= 0 and <= 1 &&
                                      IsNonNegativeInteger(item.ExpectedCountText) &&
                                      IsConfidence(item.ConfidenceThresholdText);
                if (targetRuleReady && item.RuleMethodIndex == 1)
                {
                    targetRuleReady = IsNonNegativeInteger(item.RangeMaximumCountText) &&
                                      int.Parse(item.ExpectedCountText) <= int.Parse(item.RangeMaximumCountText);
                }

                if (!targetRuleReady)
                {
                    invalidItem = item;
                    invalidStageIndex = 0;
                    validationMessage = $"请补全测试步“{item.Name}”的数量判定条件，并确保最小值不大于最大值。";
                    return true;
                }


                var invalidAdditionalRule = item.AdditionalRules.FirstOrDefault(rule =>
                    string.IsNullOrWhiteSpace(rule.TargetLabel) ||
                    !rule.Model.Labels.Contains(rule.TargetLabel) ||
                    rule.MetricIndex is < 0 or > 2 ||
                    rule.RuleMethodIndex is < 0 or > 6 ||
                    rule.OutcomeIndex is < 0 or > 1 ||
                    !IsNonNegativeInteger(rule.ThresholdText) ||
                    !IsConfidence(rule.ConfidenceText) ||
                    (rule.RuleMethodIndex == 1 &&
                     (!IsNonNegativeInteger(rule.UpperThresholdText) ||
                      int.Parse(rule.ThresholdText) > int.Parse(rule.UpperThresholdText))));
                if (invalidAdditionalRule is not null)
                {
                    invalidItem = item;
                    invalidStageIndex = 0;
                    validationMessage = $"请补全测试步“{item.Name}”的附加规则目标、比较、阈值、Confidence 和 Outcome。";
                    return true;
                }
            }
            else
            {
                if (item.PoseSteps.Any(step => step.Model.TypeIndex != 1))
                {
                    invalidItem = item;
                    invalidStageIndex = 0;
                    validationMessage = $"测试步“{item.Name}”的 Pose/Temporal 动作必须绑定姿态或时序模型。";
                    return true;
                }

                if (item.PoseSteps.Count == 0 || item.PoseSteps.Any(step => string.IsNullOrWhiteSpace(step.Name)))
                {
                    invalidItem = item;
                    invalidStageIndex = 1;
                    validationMessage = $"请补全测试步“{item.Name}”的姿态动作顺序。";
                    return true;
                }

                if (item.PoseSteps.Any(action =>
                        string.IsNullOrWhiteSpace(action.ActionCondition) ||
                        !Models.Contains(action.Model) ||
                        !IsConfidence(action.ConfidenceThresholdText) ||
                        !IsNonNegativeInteger(action.MinimumHoldMsText) ||
                        !IsPositiveInteger(action.MaximumWaitMsText)))
                {
                    invalidItem = item;
                    invalidStageIndex = 1;
                    validationMessage = $"请为测试步“{item.Name}”选择动作，并填写有效的保持时间和最大等待时间。";
                    return true;
                }
            }

            var customFunctionReady = !string.IsNullOrWhiteSpace(item.CustomFunctionName) &&
                                      IsNonNegativeInteger(item.CustomFunctionDelayMsText) &&
                                      item.CustomFunctionTypeIndex is >= 0 and <= 2 &&
                                      (item.CustomFunctionTypeIndex != 0 ||
                                       !string.IsNullOrWhiteSpace(item.CustomFunctionFilePath));
            if (!customFunctionReady)
            {
                invalidItem = item;
                invalidStageIndex = 3;
                validationMessage = $"请补全测试步“{item.Name}”的自定义函数名称、文件与延时参数。";
                return true;
            }
        }

        validationMessage = string.Empty;
        return false;
    }

    private static bool HasText(TextBox textBox) => !string.IsNullOrWhiteSpace(textBox.Text);

    private static bool IsSupportedModelFile(string fileName) =>
        fileName.Trim().EndsWith(".onnx", StringComparison.OrdinalIgnoreCase) ||
        fileName.Trim().EndsWith(".pt", StringComparison.OrdinalIgnoreCase);

    private static int ToInspectionTypeIndex(ModelPreview model) => model.TypeIndex switch
    {
        1 => 1,
        3 => 2,
        _ => 0
    };

    private static int ToModelTypeIndex(int inspectionTypeIndex) => inspectionTypeIndex switch
    {
        1 => 1,
        2 => 3,
        _ => 0
    };

    private static bool IsSingleFrameInspectionType(int typeIndex) => typeIndex is 0 or 2;

    private static bool IsNonNegativeInteger(string value) =>
        int.TryParse(value, out var number) && number >= 0;

    private static bool IsPositiveInteger(string value) =>
        int.TryParse(value, out var number) && number > 0;

    private static bool IsConfidence(string value) =>
        string.IsNullOrWhiteSpace(value) ||
        (double.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var number) &&
         number is >= 0 and <= 1);

    private static bool IsSha256(string value) =>
        value.Length == 64 && value.All(Uri.IsHexDigit);

    private void Window_Loaded(object sender, RoutedEventArgs e) => ConfigureToolTips(this);

    private static void ConfigureToolTips(DependencyObject root)
    {
        if (root is FrameworkElement { ToolTip: not null } element)
        {
            ToolTipService.SetIsEnabled(element, true);
            ToolTipService.SetInitialShowDelay(element, 150);
            ToolTipService.SetBetweenShowDelay(element, 0);
            ToolTipService.SetShowDuration(element, 20000);
            ToolTipService.SetShowOnDisabled(element, true);
            ToolTipService.SetPlacement(
                element,
                System.Windows.Controls.Primitives.PlacementMode.MousePoint);
        }

        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            ConfigureToolTips(VisualTreeHelper.GetChild(root, index));
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && DetectionLabelEditorOverlay.Visibility == Visibility.Visible)
        {
            CancelDetectionLabelEditor();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Escape && Step5Panel.Visibility == Visibility.Visible)
        {
            ClosePoseActionEditor(restoreSnapshot: true);
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Escape && DetectionEditorOverlay.Visibility == Visibility.Visible)
        {
            CloseDetectionEditor();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Escape && _isRoiDrawing)
        {
            _isRoiDrawing = false;
            _roiDragStart = null;
            _roiLogicalRect = _roiBeforeDrag;
            if (SelectedInspectionItem is not null)
            {
                SelectedInspectionItem.RoiRect = _roiLogicalRect;
            }

            RoiPreviewSurface.ReleaseMouseCapture();
            UpdateRoiVisual();
            RefreshStepCompletionStates();
            FooterHintText.Text = "已取消本次框选，并恢复上一次 ROI。";
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Escape)
        {
            Close();
            return;
        }

        if ((Keyboard.Modifiers & ModifierKeys.Alt) == 0)
        {
            return;
        }

        if (e.Key == Key.Left)
        {
            NavigateTo(CurrentStepIndex - 1);
            e.Handled = true;
        }
        else if (e.Key == Key.Right)
        {
            NextButton_Click(NextButton, new RoutedEventArgs());
            e.Handled = true;
        }
    }

    private void NavigateTo(int index)
    {
        index = Math.Clamp(index, 0, Steps.Count - 1);
        CurrentStepIndex = index;
        RefreshStepCompletionStates();

        foreach (var panel in _wizardPanels)
        {
            panel.Visibility = Visibility.Collapsed;
        }

        foreach (var panel in _testBlockStagePanels)
        {
            panel.Visibility = Visibility.Collapsed;
        }

        TestBlockStageBar.Visibility = index == 3 ? Visibility.Visible : Visibility.Collapsed;
        if (index <= 2)
        {
            _wizardPanels[index].Visibility = Visibility.Visible;
        }
        else if (index == 3)
        {
            ShowTestBlockStage(_currentTestBlockStageIndex);
        }
        else
        {
            _wizardPanels[3].Visibility = Visibility.Visible;
            UpdateReviewSummary();
        }

        foreach (var step in Steps)
        {
            step.IsCurrent = step.Index == index;
        }

        var current = Steps[index];
        CurrentStepNumberText.Text = (index + 1).ToString();
        CurrentStepTitleText.Text = current.PageTitle;
        CurrentStepTitleText.ToolTip = current.Description;
        CurrentStepDescriptionText.Text = current.Description;
        PreviousButton.IsEnabled = index > 0;
        NextButton.IsEnabled = true;
        ExportSequenceButton.Visibility = index == Steps.Count - 1 ? Visibility.Visible : Visibility.Collapsed;
        ExportSequenceButton.IsEnabled = true;
        ExportSequenceButton.Content = "导出 Sequence 与模型";
        CompletionStatusBorder.Visibility = Visibility.Collapsed;
        NextButton.Content = index == Steps.Count - 1
            ? "应用到当前操作台"
            : $"下一步：{Steps[index + 1].Title}";
        FooterHintText.Text = $"第 {index + 1} / {Steps.Count} 步";
        FooterHintText.ToolTip = index == Steps.Count - 1
            ? "“应用”会切换当前操作台；“导出”会让你选择保存位置，两项互不依赖。"
            : "红色 * 为必填项；操作说明请悬停信息图标、标题或控件查看。";

        if (index == 3)
        {
            UpdateTypePanels();
            UpdateTriggerEditorState();
        }
    }

    private void UpdateReviewSummary()
    {
        var detectionChildCount = InspectionItems.Sum(item => item.DetectionChildren.Count);
        var actionCount = InspectionItems.Sum(item => item.TypeIndex == 1 ? item.PoseSteps.Count : 0);

        ReviewTestBlocksSummaryText.Text = $"测试步 {InspectionItems.Count} 个 · 从上到下执行 · 全部参与总判定";
        ReviewTriggerSummaryText.Text = $"自定义函数 {InspectionItems.Count} 个 · 检测标签 {detectionChildCount} 个 · 姿态动作 {actionCount} 个";
        ReviewModelsSummaryText.Text = Models.Count == 0
            ? "尚未添加模型"
            : string.Join("；", Models.Select(model => $"{model.Name}（{model.TypeLabel}）"));
        ReviewInspectionOrderText.Text = InspectionItems.Count == 0
            ? "尚未添加测试步"
            : string.Join(" → ", InspectionItems.Select(item => item.Name));
        ReviewCustomFunctionDetailsText.Text = string.Join("；", InspectionItems.Select(item => item.FunctionContractSummary));
        Editor.RefreshSummaries();
    }

    private void ApplyReviewToOperator()
    {
        try
        {
            var project = CreateCurrentProject();
            if (_returnToOperatorOnCompletion && !SuppressApplyToOperatorForSmoke)
            {
                _ = ProjectConfigurationV2CompatibilityConverter.ToV1(project, AppContext.BaseDirectory);
            }

            AppliedProject = project;
            CompletionStatusText.Text = _returnToOperatorOnCompletion
                ? "配置已通过校验，正在切换当前操作台。"
                : "配置已生成；当前预览窗口未连接操作台，仍可单独导出。";
            CompletionStatusBorder.Visibility = Visibility.Visible;
            FooterHintText.Text = _returnToOperatorOnCompletion
                ? "当前配置将直接加载到操作台；导出是独立操作。"
                : "当前配置已准备完成；请从操作台的“测试序列设置”进入以直接应用。";

            if (SuppressApplyToOperatorForSmoke || !_returnToOperatorOnCompletion)
            {
                NextButton.Content = "已应用当前配置";
                NextButton.IsEnabled = false;
                return;
            }

            DialogResult = true;
        }
        catch (Exception exception)
        {
            AppliedProject = null;
            FooterHintText.Text = $"无法应用到当前操作台：{exception.Message}";
            MessageBox.Show(
                this,
                $"当前操作台无法加载这份配置。{Environment.NewLine}{Environment.NewLine}{exception.Message}",
                "应用配置失败",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private ProjectConfigurationV2 CreateCurrentProject()
    {
        NormalizeInspectionItemsAsRequired();
        var project = V2DraftMapper.ToProject(Editor);
        var errors = ProjectConfigurationV2Validator.Validate(project)
            .Where(issue => issue.Severity == V2ValidationSeverity.Error)
            .ToArray();
        if (errors.Length > 0)
        {
            throw new InvalidDataException(
                "Sequence 配置校验失败：" +
                string.Join("；", errors.Select(error => $"{error.Code} {error.Message}")));
        }

        return project;
    }

    private void NormalizeInspectionItemsAsRequired()
    {
        foreach (var item in InspectionItems)
        {
            item.IsRequired = true;
        }
    }

    private async Task CompleteReviewAsync()
    {
        RefreshStepCompletionStates();
        if (SuppressSequenceExportForSmoke)
        {
            _ = CreateCurrentProject();
            CompletionStatusText.Text = "导出前校验已完成；当前操作台配置未发生切换。";
            CompletionStatusBorder.Visibility = Visibility.Visible;
            ExportSequenceButton.Content = "导出验证已完成";
            ExportSequenceButton.IsEnabled = false;
            FooterHintText.Text = "Sequence 导出前校验已完成；仍可点击“应用到当前操作台”。";
            return;
        }

        var safeModelName = string.Concat(Editor.SequenceName.Select(character =>
            Path.GetInvalidFileNameChars().Contains(character) ? '_' : character));
        if (string.IsNullOrWhiteSpace(safeModelName))
        {
            safeModelName = "inspection-model";
        }

        var dialog = new SaveFileDialog
        {
            Title = "导出 sequence 与对应模型",
            Filter = "Visual Inspection sequence (*.sequence.json)|*.sequence.json",
            FileName = $"{safeModelName}{PortableSequenceFile.FileSuffix}",
            AddExtension = true,
            OverwritePrompt = true
        };
        if (dialog.ShowDialog(this) != true)
        {
            FooterHintText.Text = "已取消导出；配置仍保留在当前窗口。";
            return;
        }

        var destinationPath = dialog.FileName.EndsWith(PortableSequenceFile.FileSuffix, StringComparison.OrdinalIgnoreCase)
            ? dialog.FileName
            : dialog.FileName + PortableSequenceFile.FileSuffix;
        try
        {
            ExportSequenceButton.IsEnabled = false;
            ExportSequenceButton.Content = "正在导出…";
            var project = CreateCurrentProject();
            var result = await PortableSequenceFile.ExportAsync(project, destinationPath, AppContext.BaseDirectory);
            CompletionStatusText.Text = "Sequence 与对应模型已导出；当前操作台配置未发生切换。";
            CompletionStatusBorder.Visibility = Visibility.Visible;
            ExportSequenceButton.Content = "再次导出…";
            ExportSequenceButton.IsEnabled = true;
            FooterHintText.Text = $"已导出 sequence 和 {result.ModelPaths.Count} 个对应模型：{result.SequencePath}";
            MessageBox.Show(
                this,
                $"sequence 与对应模型已导出。\n\n{result.SequencePath}\n\n请将同一目录中的 sequence 文件和模型文件一起交付。",
                "导出完成",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception exception)
        {
            ExportSequenceButton.Content = "导出 Sequence 与模型";
            ExportSequenceButton.IsEnabled = true;
            FooterHintText.Text = $"导出失败：{exception.Message}";
            MessageBox.Show(this, exception.Message, "导出失败", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void Editor_DraftApplied(object? sender, EventArgs e)
    {
        foreach (var model in Models)
        {
            TrackModel(model);
        }

        foreach (var item in InspectionItems)
        {
            item.IsRequired = true;
            TrackInspectionItem(item);
        }

        var loadedSourceKindIndex = Editor.SourceKindIndex;
        var sourceKindIndex = loadedSourceKindIndex == 3 ? 3 : 0;
        if (loadedSourceKindIndex is 1 or 2)
        {
            _sourceAddresses[loadedSourceKindIndex] = Editor.SourceAddress;
            Editor.SourceKindIndex = 0;
            Editor.SourceAddress = _sourceAddresses[0];
            FooterHintText.Text = "原草稿使用的相机图源当前待开发，请重新确认图片或视频文件夹。";
        }

        _sourceAddresses[sourceKindIndex] = Editor.SourceAddress;
        _isSwitchingSourceKind = true;
        try
        {
            FolderSourceRadioButton.IsChecked = sourceKindIndex == 0;
            VideoFolderSourceRadioButton.IsChecked = sourceKindIndex == 3;
            UsbCameraSourceRadioButton.IsChecked = false;
            IndustrialCameraSourceRadioButton.IsChecked = false;
        }
        finally
        {
            _isSwitchingSourceKind = false;
        }
        UpdateSourceSettingsPanels(sourceKindIndex);
        ModelItemsList.SelectedIndex = Models.Count > 0 ? 0 : -1;
        SelectedModel = ModelItemsList.SelectedItem as ModelPreview;
        InspectionItemsList.SelectedIndex = InspectionItems.Count > 0 ? 0 : -1;
        SelectedInspectionItem = InspectionItemsList.SelectedItem as InspectionItemPreview;
        RefreshStepCompletionStates();
        UpdateReviewSummary();
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    private sealed record PoseActionEditorSnapshot(
        InspectionItemPreview Item,
        int SelectedActionIndex,
        IReadOnlyList<PoseActionState> Actions)
    {
        public static PoseActionEditorSnapshot Capture(InspectionItemPreview item) =>
            new(
                item,
                item.PoseActionIndex,
                item.PoseSteps.Select(PoseActionState.Capture).ToArray());

        public void Restore(TestSequenceWizardV2Window owner)
        {
            foreach (var action in Item.PoseSteps)
            {
                action.PropertyChanged -= owner.ConfigurationPropertyChanged;
            }

            Item.PoseSteps.Clear();
            foreach (var state in Actions)
            {
                state.Restore();
                state.Action.PropertyChanged -= owner.ConfigurationPropertyChanged;
                state.Action.PropertyChanged += owner.ConfigurationPropertyChanged;
                Item.PoseSteps.Add(state.Action);
            }

            Item.PoseActionIndex = Actions.Count == 0
                ? -1
                : Math.Clamp(SelectedActionIndex, 0, Actions.Count - 1);
        }
    }

    private sealed record PoseActionState(
        PoseStepPreview Action,
        int Order,
        string Name,
        bool IsRequired,
        ModelPreview Model,
        string ActionCondition,
        string ConfidenceThresholdText,
        string MinimumHoldMsText,
        string MaximumWaitMsText)
    {
        public static PoseActionState Capture(PoseStepPreview action) =>
            new(
                action,
                action.Order,
                action.Name,
                action.IsRequired,
                action.Model,
                action.ActionCondition,
                action.ConfidenceThresholdText,
                action.MinimumHoldMsText,
                action.MaximumWaitMsText);

        public void Restore()
        {
            Action.Order = Order;
            Action.Name = Name;
            Action.IsRequired = IsRequired;
            Action.Model = Model;
            Action.ActionCondition = ActionCondition;
            Action.ConfidenceThresholdText = ConfidenceThresholdText;
            Action.MinimumHoldMsText = MinimumHoldMsText;
            Action.MaximumWaitMsText = MaximumWaitMsText;
        }
    }

    public sealed class WizardStepItem : INotifyPropertyChanged
    {
        private bool _isCurrent;
        private bool _isCompleted;

        public WizardStepItem(int index, string number, string title, string description)
        {
            Index = index;
            Number = number;
            Title = title;
            Description = description;
        }

        public int Index { get; }

        public string Number { get; }

        public string Title { get; }

        public string Description { get; }

        public string Badge => "待设置";

        public string PageTitle => Index switch
        {
            0 => "填写项目信息",
            1 => "选择图源",
            2 => "导入模型与标签",
            3 => "设置测试步",
            _ => "检查并完成"
        };

        public bool IsCurrent
        {
            get => _isCurrent;
            set => SetField(ref _isCurrent, value);
        }

        public bool IsCompleted
        {
            get => _isCompleted;
            set => SetField(ref _isCompleted, value);
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        private void SetField(ref bool field, bool value, [CallerMemberName] string? propertyName = null)
        {
            if (field == value)
            {
                return;
            }

            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public sealed class InspectionItemPreview : INotifyPropertyChanged
    {
        private readonly string _functionCode;
        private string _name;
        private int _typeIndex;
        private bool _isRequired;
        private ModelPreview _model;
        private string _targetLabel;
        private bool _useRoi;
        private Rect _roiRect = new(120, 80, 400, 340);
        private double _roiReferenceWidth = DefaultRoiReferenceWidth;
        private double _roiReferenceHeight = DefaultRoiReferenceHeight;
        private string _roiBackgroundPath = string.Empty;
        private ImageSource? _roiBackgroundImage;
        private int _ruleMethodIndex;
        private string _expectedCountText = "1";
        private string _rangeMaximumCountText = "2";
        private int _poseActionIndex;
        private string _poseHoldTimeText = "300";
        private string _poseMaxWaitText = "5000";
        private string _triggerSignal = string.Empty;
        private int _triggerConditionIndex;
        private string _triggerDebounceMsText = "50";
        private string _triggerDelayMsText = "200";
        private string _functionTimeoutMsText = "5000";
        private bool _allowSequenceInvocation = true;
        private bool _allowManualDebugInvocation;
        private bool _externalTriggerEnabled;
        private int _frameInputPolicyIndex;
        private int _ruleLogicalOperatorIndex;
        private int _ruleMetricIndex;
        private string _confidenceThresholdText = "0.5";
        private int _ruleOutcomeIndex;
        private string _maxConcurrencyText = "1";
        private string _queueCapacityText = "1";
        private int _overflowPolicyIndex;
        private int _customFunctionTypeIndex;
        private string _customFunctionName;
        private string _customFunctionFilePath = "functions/custom_inspection.py";
        private string _customFunctionDescription = "由 Sequence 调用的自定义函数；当前仅完成前端配置。";

        public InspectionItemPreview(
            string functionCode,
            string name,
            int typeIndex,
            bool isRequired,
            ModelPreview model,
            IEnumerable<string>? poseStepNames = null,
            Guid? stepId = null,
            Guid? modelBindingId = null,
            Guid? primaryRuleId = null,
            Guid? roiId = null,
            Guid? externalTriggerBindingId = null,
            Guid? invocationId = null)
        {
            StepId = stepId ?? Guid.NewGuid();
            InvocationId = invocationId ?? Guid.NewGuid();
            ModelBindingId = modelBindingId ?? Guid.NewGuid();
            PrimaryRuleId = primaryRuleId ?? Guid.NewGuid();
            RoiId = roiId ?? Guid.NewGuid();
            ExternalTriggerBindingId = externalTriggerBindingId ?? Guid.NewGuid();
            _functionCode = functionCode;
            _name = name;
            _typeIndex = typeIndex;
            _isRequired = isRequired;
            _model = model;
            _targetLabel = model.Labels.FirstOrDefault() ?? string.Empty;
            _customFunctionName = functionCode
                .Replace("TS-", string.Empty, StringComparison.OrdinalIgnoreCase)
                .Replace('-', '_')
                .ToLowerInvariant();
            var names = (poseStepNames ?? ["动作 1"]).ToArray();
            PoseSteps = new ObservableCollection<PoseStepPreview>(
                names.Select((stepName, index) => new PoseStepPreview(index + 1, stepName, true, model)));
            AdditionalRules = [];
            NamedRois =
            [
                new NamedRoiPreview("ROI", _roiRect)
            ];
            DetectionChildren = typeIndex != 1
                ? [new DetectionChildPreview(_targetLabel, "整张图", "数量等于 1 时 Pass")]
                : [];
        }

        public Guid StepId { get; }
        public Guid InvocationId { get; }
        public Guid ModelBindingId { get; }
        public Guid PrimaryRuleId { get; }
        public Guid RoiId { get; }
        public Guid ExternalTriggerBindingId { get; }
        public string FunctionCode => _functionCode;

        public ObservableCollection<PoseStepPreview> PoseSteps { get; }
        public ObservableCollection<ViewModels.V2.RulePreviewViewModel> AdditionalRules { get; }
        public ObservableCollection<DetectionChildPreview> DetectionChildren { get; }
        public ObservableCollection<NamedRoiPreview> NamedRois { get; }

        public string RoiBackgroundPath
        {
            get => _roiBackgroundPath;
            set => SetField(ref _roiBackgroundPath, value ?? string.Empty);
        }

        public ImageSource? RoiBackgroundImage
        {
            get => _roiBackgroundImage;
            set => SetField(ref _roiBackgroundImage, value);
        }

        public string Name
        {
            get => _name;
            set => SetField(ref _name, value);
        }

        public int TypeIndex
        {
            get => _typeIndex;
            set
            {
                if (SetField(ref _typeIndex, value))
                {
                    OnPropertyChanged(nameof(TypeLabel));
                }
            }
        }

        public string TypeLabel => TypeIndex switch
        {
            1 => "姿态动作",
            2 => "图像分割",
            _ => "目标检测"
        };

        public bool IsRequired
        {
            get => _isRequired;
            set
            {
                if (SetField(ref _isRequired, value))
                {
                    OnPropertyChanged(nameof(RequiredLabel));
                }
            }
        }

        public string RequiredLabel => IsRequired ? "必选项" : "可选项";

        public ModelPreview Model
        {
            get => _model;
            set
            {
                ArgumentNullException.ThrowIfNull(value);
                if (!SetField(ref _model, value))
                {
                    return;
                }

                var validLabels = value.TypeIndex == 1
                    ? new HashSet<string>(StringComparer.Ordinal)
                    : value.Labels.ToHashSet(StringComparer.Ordinal);
                if (!validLabels.Contains(TargetLabel))
                {
                    TargetLabel = value.Labels.FirstOrDefault() ?? string.Empty;
                }

                foreach (var child in DetectionChildren
                             .Where(child => !validLabels.Contains(child.Label))
                             .ToArray())
                {
                    DetectionChildren.Remove(child);
                }

                foreach (var rule in AdditionalRules.ToArray())
                {
                    if (!validLabels.Contains(rule.TargetLabel))
                    {
                        AdditionalRules.Remove(rule);
                        continue;
                    }

                    rule.Model = value;
                }
            }
        }

        public string TargetLabel
        {
            get => _targetLabel;
            set => SetField(ref _targetLabel, value ?? string.Empty);
        }

        public bool UseRoi
        {
            get => _useRoi;
            set
            {
                if (SetField(ref _useRoi, value))
                {
                    OnPropertyChanged(nameof(UseFullImage));
                }
            }
        }

        public bool UseFullImage
        {
            get => !UseRoi;
            set
            {
                if (value)
                {
                    UseRoi = false;
                }
            }
        }

        public Rect RoiRect
        {
            get => _roiRect;
            set => SetField(ref _roiRect, value);
        }

        public double RoiReferenceWidth
        {
            get => _roiReferenceWidth;
            set => SetField(ref _roiReferenceWidth, value > 0 ? value : DefaultRoiReferenceWidth);
        }

        public double RoiReferenceHeight
        {
            get => _roiReferenceHeight;
            set => SetField(ref _roiReferenceHeight, value > 0 ? value : DefaultRoiReferenceHeight);
        }

        public int RuleMethodIndex
        {
            get => _ruleMethodIndex;
            set => SetField(ref _ruleMethodIndex, value);
        }

        public string ExpectedCountText
        {
            get => _expectedCountText;
            set => SetField(ref _expectedCountText, value ?? string.Empty);
        }

        public string RangeMaximumCountText
        {
            get => _rangeMaximumCountText;
            set => SetField(ref _rangeMaximumCountText, value ?? string.Empty);
        }

        public int PoseActionIndex
        {
            get => _poseActionIndex;
            set
            {
                if (SetField(ref _poseActionIndex, value))
                {
                    OnPropertyChanged(nameof(SelectedPoseAction));
                    OnPropertyChanged(nameof(PoseHoldTimeText));
                    OnPropertyChanged(nameof(PoseMaxWaitText));
                }
            }
        }

        public string PoseHoldTimeText
        {
            get => SelectedPoseAction?.MinimumHoldMsText ?? _poseHoldTimeText;
            set
            {
                _poseHoldTimeText = value ?? string.Empty;
                if (SelectedPoseAction is not null)
                {
                    SelectedPoseAction.MinimumHoldMsText = _poseHoldTimeText;
                }

                OnPropertyChanged();
            }
        }

        public string PoseMaxWaitText
        {
            get => SelectedPoseAction?.MaximumWaitMsText ?? _poseMaxWaitText;
            set
            {
                _poseMaxWaitText = value ?? string.Empty;
                if (SelectedPoseAction is not null)
                {
                    SelectedPoseAction.MaximumWaitMsText = _poseMaxWaitText;
                }

                OnPropertyChanged();
            }
        }

        public PoseStepPreview? SelectedPoseAction => PoseActionIndex >= 0 && PoseActionIndex < PoseSteps.Count
            ? PoseSteps[PoseActionIndex]
            : null;

        public bool AllowSequenceInvocation
        {
            get => _allowSequenceInvocation;
            set
            {
                if (SetField(ref _allowSequenceInvocation, value))
                {
                    NotifyFunctionContractChanged();
                }
            }
        }

        public bool AllowManualDebugInvocation
        {
            get => _allowManualDebugInvocation;
            set
            {
                if (SetField(ref _allowManualDebugInvocation, value))
                {
                    NotifyFunctionContractChanged();
                }
            }
        }

        public bool ExternalTriggerEnabled
        {
            get => _externalTriggerEnabled;
            set
            {
                if (SetField(ref _externalTriggerEnabled, value))
                {
                    NotifyFunctionContractChanged();
                }
            }
        }

        public string TriggerSignal
        {
            get => _triggerSignal;
            set
            {
                if (SetField(ref _triggerSignal, value ?? string.Empty))
                {
                    NotifyFunctionContractChanged();
                }
            }
        }

        public int TriggerConditionIndex
        {
            get => _triggerConditionIndex;
            set
            {
                if (SetField(ref _triggerConditionIndex, value))
                {
                    NotifyFunctionContractChanged();
                }
            }
        }

        public string TriggerDebounceMsText
        {
            get => _triggerDebounceMsText;
            set
            {
                if (SetField(ref _triggerDebounceMsText, value ?? string.Empty))
                {
                    NotifyFunctionContractChanged();
                }
            }
        }

        public string TriggerDelayMsText
        {
            get => _triggerDelayMsText;
            set
            {
                if (SetField(ref _triggerDelayMsText, value ?? string.Empty))
                {
                    NotifyFunctionContractChanged();
                }
            }
        }

        public int CustomFunctionTypeIndex
        {
            get => _customFunctionTypeIndex;
            set
            {
                if (SetField(ref _customFunctionTypeIndex, value))
                {
                    NotifyFunctionContractChanged();
                }
            }
        }

        public string CustomFunctionName
        {
            get => _customFunctionName;
            set
            {
                if (SetField(ref _customFunctionName, value ?? string.Empty))
                {
                    NotifyFunctionContractChanged();
                }
            }
        }

        public string CustomFunctionFilePath
        {
            get => _customFunctionFilePath;
            set
            {
                if (SetField(ref _customFunctionFilePath, value ?? string.Empty))
                {
                    NotifyFunctionContractChanged();
                }
            }
        }

        public string CustomFunctionDelayMsText
        {
            get => TriggerDelayMsText;
            set
            {
                TriggerDelayMsText = value;
                OnPropertyChanged();
            }
        }

        public string CustomFunctionDescription
        {
            get => _customFunctionDescription;
            set => SetField(ref _customFunctionDescription, value ?? string.Empty);
        }

        public string FunctionTimeoutMsText
        {
            get => _functionTimeoutMsText;
            set
            {
                if (SetField(ref _functionTimeoutMsText, value ?? string.Empty))
                {
                    NotifyFunctionContractChanged();
                }
            }
        }

        public int FrameInputPolicyIndex
        {
            get => _frameInputPolicyIndex;
            set
            {
                if (SetField(ref _frameInputPolicyIndex, value))
                {
                    OnPropertyChanged(nameof(FrameInputPolicyLabel));
                    NotifyFunctionContractChanged();
                }
            }
        }

        public string FrameInputPolicyLabel => FrameInputPolicyIndex switch
        {
            1 => "每次调用采集",
            2 => "连续帧",
            3 => "外部上下文帧",
            4 => "调试时选择图源",
            _ => "每个工件采集一次"
        };

        public int RuleLogicalOperatorIndex
        {
            get => _ruleLogicalOperatorIndex;
            set => SetField(ref _ruleLogicalOperatorIndex, value);
        }

        public int RuleMetricIndex
        {
            get => _ruleMetricIndex;
            set => SetField(ref _ruleMetricIndex, value);
        }

        public string ConfidenceThresholdText
        {
            get => _confidenceThresholdText;
            set => SetField(ref _confidenceThresholdText, value ?? string.Empty);
        }

        public int RuleOutcomeIndex
        {
            get => _ruleOutcomeIndex;
            set => SetField(ref _ruleOutcomeIndex, value);
        }

        public string MaxConcurrencyText
        {
            get => _maxConcurrencyText;
            set => SetField(ref _maxConcurrencyText, value ?? string.Empty);
        }

        public string QueueCapacityText
        {
            get => _queueCapacityText;
            set => SetField(ref _queueCapacityText, value ?? string.Empty);
        }

        public int OverflowPolicyIndex
        {
            get => _overflowPolicyIndex;
            set => SetField(ref _overflowPolicyIndex, value);
        }

        public string InvocationChannelsLabel
        {
            get
            {
                var entries = new List<string>();
                if (AllowSequenceInvocation)
                {
                    entries.Add("序列调用");
                }

                if (ExternalTriggerEnabled)
                {
                    entries.Add("外部信号");
                }

                if (AllowManualDebugInvocation)
                {
                    entries.Add("手动调试");
                }

                return entries.Count == 0 ? "未配置调用入口" : string.Join(" + ", entries);
            }
        }

        public string TriggerConditionLabel => TriggerConditionIndex switch
        {
            1 => "下降沿",
            2 => "高电平",
            3 => "低电平",
            _ => "上升沿"
        };

        public string TriggerSummaryLabel => $"{CustomFunctionName} · 延时 {CustomFunctionDelayMsText} ms";

        public string FunctionContractSummary
        {
            get
            {
                var type = CustomFunctionTypeIndex switch
                {
                    1 => "内置函数",
                    2 => "预留函数",
                    _ => "Python 文件"
                };
                return $"{Name} · {type} · {CustomFunctionName} · 延时 {CustomFunctionDelayMsText} ms";
            }
        }

        public void RepairLabels()
        {
            if (TypeIndex == 1)
            {
                return;
            }

            var validLabels = Model.Labels.ToHashSet(StringComparer.Ordinal);
            if (!validLabels.Contains(TargetLabel))
            {
                TargetLabel = Model.Labels.FirstOrDefault() ?? string.Empty;
            }

            foreach (var child in DetectionChildren.Where(child => !validLabels.Contains(child.Label)).ToArray())
            {
                DetectionChildren.Remove(child);
            }

            foreach (var rule in AdditionalRules.Where(rule => !validLabels.Contains(rule.TargetLabel)).ToArray())
            {
                AdditionalRules.Remove(rule);
            }
        }

        public void RenameLabel(string oldLabel, string newLabel)
        {
            if (string.Equals(TargetLabel, oldLabel, StringComparison.Ordinal))
            {
                TargetLabel = newLabel;
            }

            for (var index = 0; index < DetectionChildren.Count; index++)
            {
                var child = DetectionChildren[index];
                if (string.Equals(child.Label, oldLabel, StringComparison.Ordinal))
                {
                    DetectionChildren[index] = new DetectionChildPreview(newLabel, child.ScopeSummary, child.RuleSummary);
                }
            }

            foreach (var rule in AdditionalRules.Where(rule => string.Equals(rule.TargetLabel, oldLabel, StringComparison.Ordinal)))
            {
                rule.TargetLabel = newLabel;
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value))
            {
                return false;
            }

            field = value;
            OnPropertyChanged(propertyName);
            return true;
        }

        private void NotifyFunctionContractChanged()
        {
            OnPropertyChanged(nameof(InvocationChannelsLabel));
            OnPropertyChanged(nameof(TriggerConditionLabel));
            OnPropertyChanged(nameof(TriggerSummaryLabel));
            OnPropertyChanged(nameof(FunctionContractSummary));
            OnPropertyChanged(nameof(CustomFunctionDelayMsText));
        }

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    public sealed class ModelPreview : INotifyPropertyChanged
    {
        private string _name;
        private string _fileName;
        private int _typeIndex;
        private bool _autoImportLabels;
        private string _version = "1.0.0";
        private string _sha256 = string.Empty;
        private string _adapterId;
        private string _labelSetVersion = "1";
        private string _manualLabelsText;

        public ModelPreview(
            string name,
            string fileName,
            int typeIndex,
            bool autoImportLabels,
            IEnumerable<string> labels,
            Guid? modelArtifactId = null,
            Guid? runtimeProfileId = null,
            IEnumerable<int>? labelIds = null)
        {
            ModelArtifactId = modelArtifactId ?? Guid.NewGuid();
            RuntimeProfileId = runtimeProfileId ?? Guid.NewGuid();
            _name = name;
            _fileName = fileName;
            _typeIndex = typeIndex;
            _autoImportLabels = autoImportLabels;
            _adapterId = typeIndex == 0
                ? Core.V2.Configuration.KnownAdapterIds.YoloEndToEndDetection
                : Core.V2.Configuration.KnownAdapterIds.TemporalAction;
            var labelNames = labels.ToArray();
            _manualLabelsText = string.Join(", ", labelNames);
            var stableIds = labelIds?.ToArray() ?? Enumerable.Range(0, labelNames.Length).ToArray();
            if (stableIds.Length != labelNames.Length)
            {
                throw new ArgumentException("Label ID 数量必须与标签名称数量一致。", nameof(labelIds));
            }

            Labels = new ObservableCollection<string>(labelNames);
            LabelIds = new ObservableCollection<int>(stableIds);
            LabelDisplayEntries = new ObservableCollection<string>(
                labelNames.Select((label, index) => $"ID {stableIds[index]} · {label}"));
        }

        public Guid ModelArtifactId { get; }
        public Guid RuntimeProfileId { get; }

        public string Name
        {
            get => _name;
            set => SetField(ref _name, value);
        }

        public string FileName
        {
            get => _fileName;
            set
            {
                if (SetField(ref _fileName, value))
                {
                    OnPropertyChanged(nameof(FormatLabel));
                    OnPropertyChanged(nameof(DetailsLabel));
                }
            }
        }

        public int TypeIndex
        {
            get => _typeIndex;
            set
            {
                if (SetField(ref _typeIndex, value))
                {
                    AdapterId = value switch
                    {
                        1 => Core.V2.Configuration.KnownAdapterIds.TemporalAction,
                        2 => Core.V2.Configuration.KnownAdapterIds.Classification,
                        3 => Core.V2.Configuration.KnownAdapterIds.Segmentation,
                        _ => Core.V2.Configuration.KnownAdapterIds.YoloEndToEndDetection
                    };
                    OnPropertyChanged(nameof(TypeLabel));
                    OnPropertyChanged(nameof(UiTypeIndex));
                    OnPropertyChanged(nameof(DetailsLabel));
                    OnPropertyChanged(nameof(LabelPreviewTitle));
                }
            }
        }

        public string TypeLabel => TypeIndex switch
        {
            1 => "姿态 / 时序",
            2 => "图像分类",
            3 => "图像分割",
            _ => "目标检测"
        };

        public int UiTypeIndex
        {
            get => TypeIndex switch
            {
                1 => 1,
                3 => 2,
                _ => 0
            };
            set => TypeIndex = value switch
            {
                1 => 1,
                2 => 3,
                _ => 0
            };
        }

        public string FormatLabel
        {
            get
            {
                var extension = Path.GetExtension(FileName);
                return string.IsNullOrWhiteSpace(extension)
                    ? "待选择文件"
                    : extension.TrimStart('.').ToUpperInvariant();
            }
        }

        public string DetailsLabel => $"{FormatLabel} · {TypeLabel} · {Labels.Count} 项";

        public string LabelPreviewTitle => TypeIndex == 1
            ? $"已配置 {Labels.Count} 个动作名称"
            : $"已识别 {Labels.Count} 个标签";

        public string Version
        {
            get => _version;
            set
            {
                if (SetField(ref _version, value ?? string.Empty))
                {
                    OnPropertyChanged(nameof(ContractStatus));
                }
            }
        }

        public string Sha256
        {
            get => _sha256;
            set
            {
                if (SetField(ref _sha256, value ?? string.Empty))
                {
                    OnPropertyChanged(nameof(ContractStatus));
                }
            }
        }

        public string AdapterId
        {
            get => _adapterId;
            set
            {
                if (SetField(ref _adapterId, value ?? string.Empty))
                {
                    OnPropertyChanged(nameof(ContractStatus));
                }
            }
        }

        public string LabelSetVersion
        {
            get => _labelSetVersion;
            set => SetField(ref _labelSetVersion, value ?? string.Empty);
        }

        public string RuntimeProfileLabel => "CPU · 1× 并发 · 1× 预热";

        public string ContractStatus => string.IsNullOrWhiteSpace(Version) ||
                                        Sha256.Length != 64 ||
                                        string.IsNullOrWhiteSpace(AdapterId)
            ? "契约待补全"
            : "契约字段已填写（待运行校验）";

        public bool AutoImportLabels
        {
            get => _autoImportLabels;
            set
            {
                if (SetField(ref _autoImportLabels, value))
                {
                    OnPropertyChanged(nameof(UseManualLabels));
                    OnPropertyChanged(nameof(LabelSourceIndex));
                }
            }
        }

        public bool UseManualLabels
        {
            get => !AutoImportLabels;
            set
            {
                if (value)
                {
                    AutoImportLabels = false;
                }
            }
        }

        public ObservableCollection<string> Labels { get; }
        public ObservableCollection<int> LabelIds { get; }
        public ObservableCollection<string> LabelDisplayEntries { get; }
        public bool HasValidLabelIds => LabelIds.Count == Labels.Count &&
                                        LabelIds.All(id => id >= 0) &&
                                        LabelIds.Distinct().Count() == LabelIds.Count;

        public int ResolveLabelId(string label)
        {
            var index = Labels.IndexOf(label);
            return index >= 0 && index < LabelIds.Count ? LabelIds[index] : -1;
        }

        public int LabelSourceIndex
        {
            get => AutoImportLabels ? 0 : 1;
            set => AutoImportLabels = value != 1;
        }

        public string ManualLabelsText
        {
            get => _manualLabelsText;
            set => SetField(ref _manualLabelsText, value ?? string.Empty);
        }

        public void ReplaceLabels(IEnumerable<ModelLabelDefinition> labels)
        {
            var values = labels.ToArray();
            Labels.Clear();
            LabelIds.Clear();
            foreach (var label in values)
            {
                Labels.Add(label.Name.Trim());
                LabelIds.Add(label.Id);
            }

            AutoImportLabels = true;
            SynchronizeLabelMetadata();
        }

        public bool TryAddLabel(string label, out string error)
        {
            label = label.Trim();
            if (string.IsNullOrWhiteSpace(label))
            {
                error = "标签名称不能为空。";
                return false;
            }

            if (Labels.Contains(label, StringComparer.Ordinal))
            {
                error = $"标签“{label}”已经存在。";
                return false;
            }

            var nextId = LabelIds.Count == 0 ? 0 : checked(LabelIds.Max() + 1);
            Labels.Add(label);
            LabelIds.Add(nextId);
            AutoImportLabels = false;
            SynchronizeLabelMetadata();
            error = string.Empty;
            return true;
        }

        public bool TryRemoveLabel(string label)
        {
            var index = Labels.IndexOf(label);
            if (index < 0)
            {
                return false;
            }

            Labels.RemoveAt(index);
            LabelIds.RemoveAt(index);
            AutoImportLabels = false;
            SynchronizeLabelMetadata();
            return true;
        }

        public bool TryRenameLabel(string oldLabel, string newLabel, out string error)
        {
            newLabel = newLabel.Trim();
            var index = Labels.IndexOf(oldLabel);
            if (index < 0)
            {
                error = "原标签已经不存在，请重新选择。";
                return false;
            }

            if (string.IsNullOrWhiteSpace(newLabel))
            {
                error = "标签名称不能为空。";
                return false;
            }

            if (!string.Equals(oldLabel, newLabel, StringComparison.Ordinal) && Labels.Contains(newLabel, StringComparer.Ordinal))
            {
                error = $"标签“{newLabel}”已经存在。";
                return false;
            }

            Labels[index] = newLabel;
            AutoImportLabels = false;
            SynchronizeLabelMetadata();
            error = string.Empty;
            return true;
        }

        private void SynchronizeLabelMetadata()
        {
            ManualLabelsText = string.Join(", ", Labels);
            LabelDisplayEntries.Clear();
            for (var index = 0; index < Labels.Count; index++)
            {
                LabelDisplayEntries.Add($"ID {LabelIds[index]} · {Labels[index]}");
            }

            OnPropertyChanged(nameof(DetailsLabel));
            OnPropertyChanged(nameof(LabelPreviewTitle));
            OnPropertyChanged(nameof(HasValidLabelIds));
        }

        public string? ResolveLabelName(int labelId)
        {
            for (var index = 0; index < LabelIds.Count; index++)
            {
                if (LabelIds[index] == labelId)
                {
                    return Labels[index];
                }
            }

            return null;
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value))
            {
                return false;
            }

            field = value;
            OnPropertyChanged(propertyName);
            return true;
        }

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    public sealed class DetectionEditorPreview : ViewModels.ObservableObject
    {
        private ModelPreview? _model;
        private int _metricIndex;
        private int _ruleMethodIndex;
        private string _thresholdText = "1";
        private string _upperThresholdText = "2";
        private string _confidenceText = "0.5";
        private int _outcomeIndex;
        private DetectionLabelOptionPreview? _activeLabel;

        public ObservableCollection<DetectionLabelOptionPreview> LabelOptions { get; } = [];

        public ModelPreview? Model
        {
            get => _model;
            private set => SetProperty(ref _model, value);
        }

        public DetectionLabelOptionPreview? ActiveLabel
        {
            get => _activeLabel;
            private set => SetProperty(ref _activeLabel, value);
        }

        public int MetricIndex
        {
            get => _metricIndex;
            set
            {
                if (SetProperty(ref _metricIndex, value))
                {
                    OnPropertyChanged(nameof(RuleSummary));
                }
            }
        }

        public int RuleMethodIndex
        {
            get => _ruleMethodIndex;
            set
            {
                if (SetProperty(ref _ruleMethodIndex, value))
                {
                    OnPropertyChanged(nameof(RuleSummary));
                }
            }
        }

        public string ThresholdText
        {
            get => _thresholdText;
            set
            {
                if (SetProperty(ref _thresholdText, value ?? string.Empty))
                {
                    OnPropertyChanged(nameof(RuleSummary));
                }
            }
        }

        public string UpperThresholdText
        {
            get => _upperThresholdText;
            set
            {
                if (SetProperty(ref _upperThresholdText, value ?? string.Empty))
                {
                    OnPropertyChanged(nameof(RuleSummary));
                }
            }
        }

        public string ConfidenceText
        {
            get => _confidenceText;
            set
            {
                if (SetProperty(ref _confidenceText, value ?? string.Empty))
                {
                    OnPropertyChanged(nameof(RuleSummary));
                }
            }
        }

        public int OutcomeIndex
        {
            get => _outcomeIndex;
            set
            {
                if (SetProperty(ref _outcomeIndex, value))
                {
                    OnPropertyChanged(nameof(RuleSummary));
                }
            }
        }

        public int SelectedLabelCount => LabelOptions.Count(option => option.IsConfigured);

        public string SelectedLabelSummary => SelectedLabelCount == 0
            ? "尚未配置检测标签"
            : $"已配置 {SelectedLabelCount} 个检测标签";

        public string RuleSummary
        {
            get
            {
                var metric = MetricIndex switch
                {
                    1 => "缺失数量",
                    2 => "是否存在",
                    _ => "识别数量"
                };
                var method = RuleMethodIndex switch
                {
                    1 => $"在 {ThresholdText} 到 {UpperThresholdText} 之间",
                    2 => $"> {ThresholdText}",
                    3 => $"!= {ThresholdText}",
                    4 => $">= {ThresholdText}",
                    5 => $"< {ThresholdText}",
                    6 => $"<= {ThresholdText}",
                    _ => $"= {ThresholdText}"
                };
                var outcome = OutcomeIndex == 1 ? "Fail" : "Pass";
                var confidence = string.IsNullOrWhiteSpace(ConfidenceText) ? "0.5（默认）" : ConfidenceText;
                return $"{metric} {method} 时 {outcome} · 置信度阈值 {confidence}";
            }
        }

        public void Load(InspectionItemPreview item)
        {
            Model = item.Model;
            MetricIndex = item.RuleMetricIndex;
            RuleMethodIndex = item.RuleMethodIndex;
            ThresholdText = item.ExpectedCountText;
            UpperThresholdText = item.RangeMaximumCountText;
            ConfidenceText = item.ConfidenceThresholdText;
            OutcomeIndex = item.RuleOutcomeIndex;
            RebuildLabelOptions(item);
        }

        public void BeginLabelEdit(DetectionLabelOptionPreview option)
        {
            ActiveLabel = option;
            MetricIndex = option.MetricIndex;
            RuleMethodIndex = option.RuleMethodIndex;
            ThresholdText = option.ThresholdText;
            UpperThresholdText = option.UpperThresholdText;
            ConfidenceText = option.ConfidenceText;
            OutcomeIndex = option.OutcomeIndex;
        }

        public void CommitActiveLabel()
        {
            if (ActiveLabel is not { } option)
            {
                return;
            }

            option.MetricIndex = MetricIndex;
            option.RuleMethodIndex = RuleMethodIndex;
            option.ThresholdText = ThresholdText;
            option.UpperThresholdText = UpperThresholdText;
            option.ConfidenceText = ConfidenceText;
            option.OutcomeIndex = OutcomeIndex;
            option.IsConfigured = true;
            option.RefreshSummaries();
            RefreshSelectionSummary();
        }

        public void EndLabelEdit() => ActiveLabel = null;

        public void AddRoiOption(string name)
        {
            foreach (var label in LabelOptions)
            {
                label.RoiOptions.Add(new RoiSelectionOptionPreview(name));
            }
        }

        public void RemoveRoiOption(string name)
        {
            foreach (var label in LabelOptions)
            {
                var option = label.RoiOptions.FirstOrDefault(candidate =>
                    string.Equals(candidate.Name, name, StringComparison.Ordinal));
                if (option is not null)
                {
                    label.RoiOptions.Remove(option);
                }
            }
        }

        public void RefreshSelectionSummary()
        {
            OnPropertyChanged(nameof(SelectedLabelCount));
            OnPropertyChanged(nameof(SelectedLabelSummary));
        }

        private void RebuildLabelOptions(InspectionItemPreview item)
        {
            var existing = item.DetectionChildren.ToDictionary(child => child.Label, StringComparer.Ordinal);
            ActiveLabel = null;
            LabelOptions.Clear();
            foreach (var label in Model?.Labels ?? [])
            {
                var child = existing.GetValueOrDefault(label);
                var additionalRule = item.AdditionalRules.FirstOrDefault(rule =>
                    string.Equals(rule.TargetLabel, label, StringComparison.Ordinal));
                var option = new DetectionLabelOptionPreview(label)
                {
                    IsConfigured = child is not null,
                    UseRoi = child?.ScopeSummary.StartsWith("ROI", StringComparison.Ordinal) == true,
                    MetricIndex = additionalRule?.MetricIndex ?? item.RuleMetricIndex,
                    RuleMethodIndex = additionalRule?.RuleMethodIndex ?? item.RuleMethodIndex,
                    ThresholdText = additionalRule?.ThresholdText ?? item.ExpectedCountText,
                    UpperThresholdText = additionalRule?.UpperThresholdText ?? item.RangeMaximumCountText,
                    ConfidenceText = additionalRule?.ConfidenceText ?? item.ConfidenceThresholdText,
                    OutcomeIndex = additionalRule?.OutcomeIndex ?? item.RuleOutcomeIndex
                };
                foreach (var roi in item.NamedRois)
                {
                    option.RoiOptions.Add(new RoiSelectionOptionPreview(roi.Name)
                    {
                        IsSelected = child?.ScopeSummary.Contains(roi.Name, StringComparison.Ordinal) == true
                    });
                }

                LabelOptions.Add(option);
            }

            RefreshSelectionSummary();
        }
    }

    public sealed class DetectionLabelOptionPreview : ViewModels.ObservableObject
    {
        private bool _isConfigured;
        private bool _useRoi;
        private int _metricIndex;
        private int _ruleMethodIndex;
        private string _thresholdText = "1";
        private string _upperThresholdText = "2";
        private string _confidenceText = "0.5";
        private int _outcomeIndex;

        public DetectionLabelOptionPreview(string label)
        {
            Label = label;
        }

        public string Label { get; }
        public ObservableCollection<RoiSelectionOptionPreview> RoiOptions { get; } = [];

        public bool IsConfigured
        {
            get => _isConfigured;
            set
            {
                if (SetProperty(ref _isConfigured, value))
                {
                    OnPropertyChanged(nameof(ConfigurationStatus));
                    OnPropertyChanged(nameof(ConfigurationAction));
                }
            }
        }

        public string ConfigurationStatus => IsConfigured ? "已配置" : "未配置";

        public string ConfigurationAction => IsConfigured ? "重新编辑" : "开始配置";

        public bool UseRoi
        {
            get => _useRoi;
            set
            {
                if (SetProperty(ref _useRoi, value))
                {
                    OnPropertyChanged(nameof(UseFullImage));
                    OnPropertyChanged(nameof(ScopeSummary));
                }
            }
        }

        public bool UseFullImage
        {
            get => !UseRoi;
            set
            {
                if (value)
                {
                    UseRoi = false;
                }
            }
        }

        public int MetricIndex
        {
            get => _metricIndex;
            set
            {
                if (SetProperty(ref _metricIndex, value))
                {
                    OnPropertyChanged(nameof(RuleSummary));
                }
            }
        }

        public int RuleMethodIndex
        {
            get => _ruleMethodIndex;
            set
            {
                if (SetProperty(ref _ruleMethodIndex, value))
                {
                    OnPropertyChanged(nameof(RuleSummary));
                }
            }
        }

        public string ThresholdText
        {
            get => _thresholdText;
            set
            {
                if (SetProperty(ref _thresholdText, value ?? string.Empty))
                {
                    OnPropertyChanged(nameof(RuleSummary));
                }
            }
        }

        public string UpperThresholdText
        {
            get => _upperThresholdText;
            set
            {
                if (SetProperty(ref _upperThresholdText, value ?? string.Empty))
                {
                    OnPropertyChanged(nameof(RuleSummary));
                }
            }
        }

        public string ConfidenceText
        {
            get => _confidenceText;
            set
            {
                if (SetProperty(ref _confidenceText, value ?? string.Empty))
                {
                    OnPropertyChanged(nameof(RuleSummary));
                }
            }
        }

        public int OutcomeIndex
        {
            get => _outcomeIndex;
            set
            {
                if (SetProperty(ref _outcomeIndex, value))
                {
                    OnPropertyChanged(nameof(RuleSummary));
                }
            }
        }

        public string ScopeSummary
        {
            get
            {
                if (UseFullImage)
                {
                    return "整张图";
                }

                var selectedRois = RoiOptions.Where(roi => roi.IsSelected).Select(roi => roi.Name).ToArray();
                return selectedRois.Length == 0
                    ? "ROI · 尚未选择区域"
                    : $"ROI · {string.Join(" + ", selectedRois)}";
            }
        }

        public string RuleSummary
        {
            get
            {
                var metric = MetricIndex switch
                {
                    1 => "缺失数量",
                    2 => "是否存在",
                    _ => "识别数量"
                };
                var method = RuleMethodIndex switch
                {
                    1 => $"在 {ThresholdText} 到 {UpperThresholdText} 之间",
                    2 => $"> {ThresholdText}",
                    3 => $"!= {ThresholdText}",
                    4 => $">= {ThresholdText}",
                    5 => $"< {ThresholdText}",
                    6 => $"<= {ThresholdText}",
                    _ => $"= {ThresholdText}"
                };
                var outcome = OutcomeIndex == 1 ? "Fail" : "Pass";
                var confidence = string.IsNullOrWhiteSpace(ConfidenceText) ? "0.5（默认）" : ConfidenceText;
                return $"{metric} {method} 时 {outcome} · 置信度阈值 {confidence}";
            }
        }

        public void RefreshSummaries()
        {
            OnPropertyChanged(nameof(ScopeSummary));
            OnPropertyChanged(nameof(RuleSummary));
        }
    }

    public sealed class RoiSelectionOptionPreview : ViewModels.ObservableObject
    {
        private bool _isSelected;

        public RoiSelectionOptionPreview(string name)
        {
            Name = name;
        }

        public string Name { get; }

        public bool IsSelected
        {
            get => _isSelected;
            set => SetProperty(ref _isSelected, value);
        }
    }

    public sealed class DetectionChildPreview
    {
        public DetectionChildPreview(string label, string scopeSummary, string ruleSummary)
        {
            Label = label;
            ScopeSummary = scopeSummary;
            RuleSummary = ruleSummary;
        }

        public string Label { get; }
        public string ScopeSummary { get; }
        public string RuleSummary { get; }
    }

    public sealed class NamedRoiPreview : ViewModels.ObservableObject
    {
        private string _name;
        private Rect _rect;

        public NamedRoiPreview(string name, Rect rect)
        {
            _name = name;
            _rect = rect;
        }

        public string Name
        {
            get => _name;
            set => SetProperty(ref _name, value ?? string.Empty);
        }

        public Rect Rect
        {
            get => _rect;
            set
            {
                if (SetProperty(ref _rect, value))
                {
                    OnPropertyChanged(nameof(CoordinateSummary));
                }
            }
        }

        public string CoordinateSummary =>
            $"X1 {(int)Rect.Left} · Y1 {(int)Rect.Top} · X2 {(int)Rect.Right} · Y2 {(int)Rect.Bottom}";
    }

    public sealed class PoseStepPreview : INotifyPropertyChanged
    {
        private int _order;
        private string _name;
        private bool _isRequired;
        private ModelPreview _model;
        private string _actionCondition;
        private string _confidenceThresholdText = "0.5";
        private string _minimumHoldMsText = "300";
        private string _maximumWaitMsText = "5000";

        public PoseStepPreview(
            int order,
            string name,
            bool isRequired,
            ModelPreview model,
            Guid? actionId = null,
            Guid? modelBindingId = null)
        {
            ActionId = actionId ?? Guid.NewGuid();
            ModelBindingId = modelBindingId ?? Guid.NewGuid();
            _order = order;
            _name = name;
            _isRequired = isRequired;
            _model = model;
            _actionCondition = name;
        }

        public Guid ActionId { get; }
        public Guid ModelBindingId { get; }

        public int Order
        {
            get => _order;
            set
            {
                if (SetField(ref _order, value))
                {
                    OnPropertyChanged(nameof(OrderText));
                    OnPropertyChanged(nameof(DisplayLabel));
                }
            }
        }

        public string OrderText => Order.ToString("00");

        public string DisplayLabel => $"{OrderText} · {Name}";

        public string Name
        {
            get => _name;
            set
            {
                if (SetField(ref _name, value))
                {
                    OnPropertyChanged(nameof(DisplayLabel));
                }
            }
        }

        public bool IsRequired
        {
            get => _isRequired;
            set
            {
                if (SetField(ref _isRequired, value))
                {
                    OnPropertyChanged(nameof(RequiredLabel));
                }
            }
        }

        public string RequiredLabel => IsRequired ? "必选动作" : "可选动作";

        public ModelPreview Model
        {
            get => _model;
            set => SetField(ref _model, value);
        }

        public string ActionCondition
        {
            get => _actionCondition;
            set => SetField(ref _actionCondition, value ?? string.Empty);
        }

        public string ConfidenceThresholdText
        {
            get => _confidenceThresholdText;
            set => SetField(ref _confidenceThresholdText, value ?? string.Empty);
        }

        public string MinimumHoldMsText
        {
            get => _minimumHoldMsText;
            set => SetField(ref _minimumHoldMsText, value ?? string.Empty);
        }

        public string MaximumWaitMsText
        {
            get => _maximumWaitMsText;
            set => SetField(ref _maximumWaitMsText, value ?? string.Empty);
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value))
            {
                return false;
            }

            field = value;
            OnPropertyChanged(propertyName);
            return true;
        }

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
