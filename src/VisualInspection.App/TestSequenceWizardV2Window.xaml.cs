using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace VisualInspection.App;

public partial class TestSequenceWizardV2Window : Window, INotifyPropertyChanged
{
    private const double RoiReferenceWidth = 640;
    private const double RoiReferenceHeight = 480;
    private readonly FrameworkElement[] _stepPanels;
    private readonly HashSet<int> _confirmedStepIndexes = [];
    private bool _wizardReady;
    private bool _isRoiDrawing;
    private Point? _roiDragStart;
    private Rect _roiBeforeDrag;
    private Rect _roiLogicalRect = new(120, 80, 400, 340);
    private ModelPreview? _selectedModel;
    private InspectionItemPreview? _selectedInspectionItem;

    public TestSequenceWizardV2Window()
    {
        InitializeComponent();

        Steps =
        [
            new(0, "01", "项目信息", "填写项目、工位、测试序列和版本。"),
            new(1, "02", "选择图源", "选择图片文件夹、USB 摄像头或工业相机。"),
            new(2, "03", "导入模型", "建立项目模型库，可连续导入多个 ONNX / PT 模型。"),
            new(3, "04", "编排检测项", "使用加号、减号和排序按钮，建立需要执行的检测项。"),
            new(4, "05", "检测内容", "按检测类型填写目标区域或姿态动作顺序。"),
            new(5, "06", "判定条件", "按检测类型填写数量规则或姿态时序条件。"),
            new(6, "07", "运行参数", "设置毫秒延时、图源策略和发布方式。"),
            new(7, "08", "检查完成", "汇总检查全部必填内容，再进入保存与校验。")
        ];

        Models =
        [
            new("Fan 主体检测", "fan.onnx", 0, true, ["fan", "hub", "housing"]),
            new("叶片缺陷检测", "blade-defect.onnx", 0, true, ["blade_defect", "crack"]),
            new("装配姿态时序", "assembly-pose.onnx", 1, false, ["取件", "放置", "按压到位"])
        ];

        InspectionItems =
        [
            new(1, "风扇到位", 0, true, Models[0]),
            new(2, "叶片缺陷", 0, true, Models[1]),
            new(3, "拿取与放置", 1, false, Models[2])
        ];

        PoseSteps =
        [
            new(1, "取件", true),
            new(2, "放置", true),
            new(3, "按压到位", true)
        ];

        _stepPanels =
        [
            Step1Panel,
            Step2Panel,
            Step3Panel,
            Step4Panel,
            Step5Panel,
            Step6Panel,
            Step8Panel,
            Step9Panel
        ];

        foreach (var model in Models)
        {
            TrackModel(model);
        }

        foreach (var item in InspectionItems)
        {
            TrackInspectionItem(item);
        }

        foreach (var step in PoseSteps)
        {
            TrackPoseStep(step);
        }

        DataContext = this;
        ModelItemsList.SelectedIndex = 0;
        SelectedModel = Models[0];
        InspectionItemsList.SelectedIndex = 0;
        SelectedInspectionItem = InspectionItems[0];
        _wizardReady = true;
        UpdateRuleEditorState();
        NavigateTo(0);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<WizardStepItem> Steps { get; }

    public ObservableCollection<ModelPreview> Models { get; }

    public ObservableCollection<InspectionItemPreview> InspectionItems { get; }

    public ObservableCollection<PoseStepPreview> PoseSteps { get; }

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

            _selectedInspectionItem = value;
            OnPropertyChanged();
            UpdateTypePanels();
        }
    }

    public int CurrentStepIndex { get; private set; }

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

    public void SaveSnapshot() => SaveSnapshot(SnapshotPath);

    public void SavePoseSnapshot() => SaveSnapshot(PoseSnapshotPath);

    public void SaveSourceSnapshot() => SaveSnapshot(SourceSnapshotPath);

    public void SaveModelsSnapshot() => SaveSnapshot(ModelsSnapshotPath);

    public void SaveRoiSnapshot() => SaveSnapshot(RoiSnapshotPath);

    public void SaveRuleSnapshot() => SaveSnapshot(RuleSnapshotPath);

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
        UpdateLayout();
    }

    public void ShowModelsStepForPreview()
    {
        NavigateTo(2);
        UpdateLayout();
    }

    public void ShowUsbSourceStepForPreview()
    {
        UsbCameraSourceRadioButton.IsChecked = true;
        NavigateTo(1);
        UpdateLayout();
    }

    public void ShowPoseContentStepForPreview()
    {
        var poseItem = InspectionItems.First(item => item.TypeIndex == 1);
        InspectionItemsList.SelectedItem = poseItem;
        SelectedInspectionItem = poseItem;
        NavigateTo(4);
        UpdateLayout();
    }

    public void ShowTargetContentStepForPreview()
    {
        var targetItem = InspectionItems.First(item => item.TypeIndex == 0);
        InspectionItemsList.SelectedItem = targetItem;
        SelectedInspectionItem = targetItem;
        NavigateTo(4);
        UpdateLayout();
        UpdateRoiVisual();
    }

    public void ShowTargetRuleStepForPreview()
    {
        var targetItem = InspectionItems.First(item => item.TypeIndex == 0);
        InspectionItemsList.SelectedItem = targetItem;
        SelectedInspectionItem = targetItem;
        NavigateTo(5);
        UpdateLayout();
        UpdateRuleEditorState();
    }

    public void ApplyRoiSelectionForSmoke(Point start, Point end)
    {
        UpdateLayout();
        ApplyRoiFromPreviewPoints(start, end);
    }

    public Rect RoiLogicalRect => _roiLogicalRect;

    private void StepButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { CommandParameter: int index })
        {
            NavigateTo(index);
        }
    }

    private void PreviousButton_Click(object sender, RoutedEventArgs e) => NavigateTo(CurrentStepIndex - 1);

    private void NextButton_Click(object sender, RoutedEventArgs e)
    {
        if (!IsStepValid(CurrentStepIndex, out var validationMessage))
        {
            RefreshStepCompletionStates();
            FooterHintText.Text = validationMessage;
            return;
        }

        _confirmedStepIndexes.Add(CurrentStepIndex);
        RefreshStepCompletionStates();

        if (CurrentStepIndex == Steps.Count - 1)
        {
            CompletePrototype();
            return;
        }

        NavigateTo(CurrentStepIndex + 1);
    }

    private void AddInspectionItem_Click(object sender, RoutedEventArgs e)
    {
        var defaultModel = Models.FirstOrDefault(model => model.TypeIndex == 0) ?? Models[0];
        var item = new InspectionItemPreview(
            InspectionItems.Count + 1,
            $"新检测项 {InspectionItems.Count + 1}",
            0,
            true,
            defaultModel);
        TrackInspectionItem(item);
        InspectionItems.Add(item);
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
        ModelItemsList.SelectedItem = model;
        SelectedModel = model;
        RefreshStepCompletionStates();
        FooterHintText.Text = "已新增模型卡片；请在右侧依次填写带红色 * 的内容。";
    }

    private void RemoveSelectedModel_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedModel is null)
        {
            return;
        }

        if (Models.Count <= 1)
        {
            FooterHintText.Text = "项目模型库至少保留 1 个模型；最后一个模型不能删除。";
            return;
        }

        if (InspectionItems.Any(item => ReferenceEquals(item.Model, SelectedModel)))
        {
            FooterHintText.Text = "该模型已被检测项绑定；请先在第 4 步改绑，再删除模型。";
            return;
        }

        var previousIndex = Models.IndexOf(SelectedModel);
        SelectedModel.PropertyChanged -= ConfigurationPropertyChanged;
        Models.Remove(SelectedModel);
        ModelItemsList.SelectedIndex = Math.Clamp(previousIndex, 0, Models.Count - 1);
        SelectedModel = ModelItemsList.SelectedItem as ModelPreview;
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
            FooterHintText.Text = "至少保留 1 个检测项；最后一个检测项不能删除。";
            return;
        }

        var previousIndex = InspectionItems.IndexOf(item);
        item.PropertyChanged -= ConfigurationPropertyChanged;
        InspectionItems.Remove(item);
        RenumberInspectionItems();
        InspectionItemsList.SelectedIndex = Math.Clamp(previousIndex, 0, InspectionItems.Count - 1);
        SelectedInspectionItem = InspectionItemsList.SelectedItem as InspectionItemPreview;
        RefreshStepCompletionStates();
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

    private void MoveInspectionItem(InspectionItemPreview item, int offset)
    {
        var currentIndex = InspectionItems.IndexOf(item);
        var destinationIndex = currentIndex + offset;
        if (currentIndex < 0 || destinationIndex < 0 || destinationIndex >= InspectionItems.Count)
        {
            return;
        }

        InspectionItems.Move(currentIndex, destinationIndex);
        RenumberInspectionItems();
        InspectionItemsList.SelectedItem = item;
    }

    private void RenumberInspectionItems()
    {
        for (var index = 0; index < InspectionItems.Count; index++)
        {
            InspectionItems[index].Order = index + 1;
        }
    }

    private void InspectionItemsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        SelectedInspectionItem = InspectionItemsList.SelectedItem as InspectionItemPreview;
        RefreshStepCompletionStates();
    }

    private void InspectionTypeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (SelectedInspectionItem is null || InspectionTypeComboBox.SelectedIndex < 0)
        {
            return;
        }

        SelectedInspectionItem.TypeIndex = InspectionTypeComboBox.SelectedIndex;
        UpdateTypePanels();
        RefreshStepCompletionStates();
    }

    private void AddPoseStep_Click(object sender, RoutedEventArgs e)
    {
        var step = new PoseStepPreview(PoseSteps.Count + 1, $"新动作 {PoseSteps.Count + 1}", true);
        TrackPoseStep(step);
        PoseSteps.Add(step);
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
    }

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
        TargetContentPanel.Visibility = isPose ? Visibility.Collapsed : Visibility.Visible;
        PoseContentPanel.Visibility = isPose ? Visibility.Visible : Visibility.Collapsed;
        TargetRulePanel.Visibility = isPose ? Visibility.Collapsed : Visibility.Visible;
        PoseRulePanel.Visibility = isPose ? Visibility.Visible : Visibility.Collapsed;
        UpdateRuleEditorState();
    }

    private void UpdateRuleEditorState()
    {
        if (!_wizardReady || !IsInitialized)
        {
            return;
        }

        if (SelectedInspectionItem?.TypeIndex == 0 &&
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
            switch (TargetRuleMethodComboBox.SelectedIndex)
            {
                case 0:
                    TargetRuleSummaryText.Text = $"在{region}内统计目标“{target}”，识别数量等于 {count} 时，当前检测项通过。";
                    break;
                case 1 when IsNonNegativeInteger(RangeMaximumCountTextBox.Text):
                    var maximum = int.Parse(RangeMaximumCountTextBox.Text);
                    TargetRuleSummaryText.Text = count <= maximum
                        ? $"在{region}内统计目标“{target}”，识别数量在 {count} 到 {maximum} 之间（含边界）时，当前检测项通过。"
                        : "数量范围无效：最小数量不能大于最大数量。";
                    break;
                case 1:
                    TargetRuleSummaryText.Text = "请填写有效的最大数量，系统会在这里生成数量范围判定。";
                    break;
                case 2:
                    TargetRuleSummaryText.Text = $"在{region}内统计目标“{target}”，识别数量大于 {count} 时，当前检测项通过。";
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
            "系统按画布顺序检查全部必选动作，全部满足时当前检测项通过。";
    }

    private void RedrawRoiButton_Click(object sender, RoutedEventArgs e)
    {
        RoiRegionRadioButton.IsChecked = true;
        RoiPreviewSurface.Focus();
        FooterHintText.Text = "请在右侧图像预览中按住鼠标左键拖动；松开后会更新 ROI 坐标。";
    }

    private void RoiPreviewSurface_Loaded(object sender, RoutedEventArgs e) => UpdateRoiVisual();

    private void RoiPreviewSurface_SizeChanged(object sender, SizeChangedEventArgs e) => UpdateRoiVisual();

    private void RoiPreviewSurface_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        RoiRegionRadioButton.IsChecked = true;
        _roiBeforeDrag = _roiLogicalRect;
        _roiDragStart = ClampToRoiSurface(e.GetPosition(RoiPreviewSurface));
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
            UpdateRoiVisual();
            FooterHintText.Text = "框选范围过小，已保留上一次 ROI；请按住鼠标拖出一个矩形区域。";
        }
        else
        {
            FooterHintText.Text = $"ROI 已更新：{RoiCoordinatesText.Text}。当前仍是前端预览，不会保存配置。";
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

    private void ApplyRoiFromPreviewPoints(Point start, Point end)
    {
        var surfaceWidth = Math.Max(1, RoiPreviewSurface.ActualWidth);
        var surfaceHeight = Math.Max(1, RoiPreviewSurface.ActualHeight);
        var left = Math.Min(start.X, end.X);
        var top = Math.Min(start.Y, end.Y);
        var right = Math.Max(start.X, end.X);
        var bottom = Math.Max(start.Y, end.Y);

        _roiLogicalRect = new Rect(
            Math.Round(left / surfaceWidth * RoiReferenceWidth),
            Math.Round(top / surfaceHeight * RoiReferenceHeight),
            Math.Round((right - left) / surfaceWidth * RoiReferenceWidth),
            Math.Round((bottom - top) / surfaceHeight * RoiReferenceHeight));
        UpdateRoiVisual();
        RefreshStepCompletionStates();
    }

    private Point ClampToRoiSurface(Point point) => new(
        Math.Clamp(point.X, 0, Math.Max(0, RoiPreviewSurface.ActualWidth)),
        Math.Clamp(point.Y, 0, Math.Max(0, RoiPreviewSurface.ActualHeight)));

    private void UpdateRoiVisual()
    {
        if (RoiPreviewSurface.ActualWidth <= 0 || RoiPreviewSurface.ActualHeight <= 0)
        {
            return;
        }

        var scaleX = RoiPreviewSurface.ActualWidth / RoiReferenceWidth;
        var scaleY = RoiPreviewSurface.ActualHeight / RoiReferenceHeight;
        var left = _roiLogicalRect.X * scaleX;
        var top = _roiLogicalRect.Y * scaleY;
        var width = _roiLogicalRect.Width * scaleX;
        var height = _roiLogicalRect.Height * scaleY;

        Canvas.SetLeft(RoiSelectionRectangle, left);
        Canvas.SetTop(RoiSelectionRectangle, top);
        RoiSelectionRectangle.Width = width;
        RoiSelectionRectangle.Height = height;
        RoiSelectionRectangle.Visibility = width > 0 && height > 0 ? Visibility.Visible : Visibility.Collapsed;

        Canvas.SetLeft(RoiSelectionLabel, Math.Min(left + 7, Math.Max(0, RoiPreviewSurface.ActualWidth - 110)));
        Canvas.SetTop(RoiSelectionLabel, Math.Min(top + 7, Math.Max(0, RoiPreviewSurface.ActualHeight - 28)));
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
        if (_wizardReady)
        {
            RefreshStepCompletionStates();
        }
    }

    private void TrackModel(ModelPreview model) => model.PropertyChanged += ConfigurationPropertyChanged;

    private void TrackInspectionItem(InspectionItemPreview item) =>
        item.PropertyChanged += ConfigurationPropertyChanged;

    private void TrackPoseStep(PoseStepPreview step) => step.PropertyChanged += ConfigurationPropertyChanged;

    private void ConfigurationPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_wizardReady)
        {
            if (sender is InspectionItemPreview &&
                e.PropertyName is nameof(InspectionItemPreview.TypeIndex) or nameof(InspectionItemPreview.Model))
            {
                UpdateTypePanels();
            }

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
                if (HasText(ProjectNameTextBox) && HasText(StationNameTextBox) && HasText(SequenceNameTextBox))
                {
                    validationMessage = string.Empty;
                    return true;
                }

                validationMessage = "第 1 步未完成：请填写项目名称、工位名称和测试序列名称。";
                return false;

            case 1:
                if (FolderSourceRadioButton.IsChecked != true &&
                    UsbCameraSourceRadioButton.IsChecked != true &&
                    IndustrialCameraSourceRadioButton.IsChecked != true)
                {
                    validationMessage = "第 2 步未完成：请选择一种图源。";
                    return false;
                }

                if (FolderSourceRadioButton.IsChecked == true && !HasText(FolderPathTextBox))
                {
                    validationMessage = "第 2 步未完成：图片文件夹图源必须填写文件夹路径。";
                    return false;
                }

                validationMessage = string.Empty;
                return true;

            case 2:
                var invalidModel = Models.FirstOrDefault(model =>
                    string.IsNullOrWhiteSpace(model.Name) ||
                    !IsSupportedModelFile(model.FileName) ||
                    model.TypeIndex is < 0 or > 3 ||
                    model.Labels.Count == 0 ||
                    model.Labels.Any(string.IsNullOrWhiteSpace));
                if (Models.Count > 0 && invalidModel is null)
                {
                    validationMessage = string.Empty;
                    return true;
                }

                validationMessage = invalidModel is null
                    ? "第 3 步未完成：请至少添加 1 个模型。"
                    : $"第 3 步未完成：请补全模型“{invalidModel.Name}”的名称、ONNX/PT 文件和标签。";
                return false;

            case 3:
                var invalidItem = InspectionItems.FirstOrDefault(item =>
                    string.IsNullOrWhiteSpace(item.Name) ||
                    item.TypeIndex is < 0 or > 1 ||
                    !Models.Contains(item.Model));
                if (InspectionItems.Count > 0 && invalidItem is null)
                {
                    validationMessage = string.Empty;
                    return true;
                }

                validationMessage = invalidItem is null
                    ? "第 4 步未完成：请至少添加 1 个检测项。"
                    : $"第 4 步未完成：请补全检测项“{invalidItem.Name}”的名称、类型和模型绑定。";
                return false;

            case 4:
                var targetContentReady = InspectionItems
                    .Where(item => item.TypeIndex == 0)
                    .All(item => item.Model.Labels.Any(label => !string.IsNullOrWhiteSpace(label)));
                var targetRegionReady = !InspectionItems.Any(item => item.TypeIndex == 0) ||
                                        FullImageRegionRadioButton.IsChecked == true ||
                                        (RoiRegionRadioButton.IsChecked == true &&
                                         _roiLogicalRect.Width > 0 &&
                                         _roiLogicalRect.Height > 0);
                var poseContentReady = !InspectionItems.Any(item => item.TypeIndex == 1) ||
                                       (PoseSteps.Count > 0 && PoseSteps.All(step => !string.IsNullOrWhiteSpace(step.Name)));
                if (targetContentReady && targetRegionReady && poseContentReady)
                {
                    validationMessage = string.Empty;
                    return true;
                }

                validationMessage = "第 5 步未完成：请为目标选择有效标签，并补全姿态动作顺序。";
                return false;

            case 5:
                if (SelectedInspectionItem?.TypeIndex == 1)
                {
                    if (PoseActionComboBox.SelectedIndex >= 0 &&
                        IsNonNegativeInteger(PoseHoldTimeTextBox.Text) &&
                        IsPositiveInteger(PoseMaxWaitTextBox.Text))
                    {
                        validationMessage = string.Empty;
                        return true;
                    }

                    validationMessage = "第 6 步未完成：请选择动作，并填写有效的保持时间和最大等待时间。";
                    return false;
                }

                var targetRuleReady = TargetRuleTargetComboBox.SelectedIndex >= 0 &&
                                      TargetRuleMethodComboBox.SelectedIndex >= 0 &&
                                      IsNonNegativeInteger(ExpectedCountTextBox.Text);
                if (targetRuleReady && TargetRuleMethodComboBox.SelectedIndex == 1)
                {
                    targetRuleReady = IsNonNegativeInteger(RangeMaximumCountTextBox.Text) &&
                                      int.Parse(ExpectedCountTextBox.Text) <= int.Parse(RangeMaximumCountTextBox.Text);
                }

                if (targetRuleReady)
                {
                    validationMessage = string.Empty;
                    return true;
                }

                validationMessage = "第 6 步未完成：请选择检测目标和判断方式，并填写有效且顺序正确的数量条件。";
                return false;

            case 6:
                if (IsNonNegativeInteger(DefaultDelayTextBox.Text) && RuntimeSourceComboBox.SelectedIndex >= 0)
                {
                    validationMessage = string.Empty;
                    return true;
                }

                validationMessage = "第 7 步未完成：请填写非负延时并选择运行时图源策略。";
                return false;

            case 7:
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

    private static bool HasText(TextBox textBox) => !string.IsNullOrWhiteSpace(textBox.Text);

    private static bool IsSupportedModelFile(string fileName) =>
        fileName.Trim().EndsWith(".onnx", StringComparison.OrdinalIgnoreCase) ||
        fileName.Trim().EndsWith(".pt", StringComparison.OrdinalIgnoreCase);

    private static bool IsNonNegativeInteger(string value) =>
        int.TryParse(value, out var number) && number >= 0;

    private static bool IsPositiveInteger(string value) =>
        int.TryParse(value, out var number) && number > 0;

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && _isRoiDrawing)
        {
            _isRoiDrawing = false;
            _roiDragStart = null;
            _roiLogicalRect = _roiBeforeDrag;
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

        for (var panelIndex = 0; panelIndex < _stepPanels.Length; panelIndex++)
        {
            _stepPanels[panelIndex].Visibility = panelIndex == index ? Visibility.Visible : Visibility.Collapsed;
        }

        foreach (var step in Steps)
        {
            step.IsCurrent = step.Index == index;
        }

        var current = Steps[index];
        CurrentStepNumberText.Text = (index + 1).ToString();
        CurrentStepTitleText.Text = current.PageTitle;
        CurrentStepDescriptionText.Text = current.Description;
        PreviousButton.IsEnabled = index > 0;
        NextButton.IsEnabled = true;
        PrototypeStatusBorder.Visibility = Visibility.Collapsed;
        NextButton.Content = index == Steps.Count - 1
            ? "确认界面流程"
            : $"下一步：{Steps[index + 1].Title}";
        FooterHintText.Text = index == Steps.Count - 1
            ? "前端确认阶段：点击确认只显示完成状态，不会保存配置。"
            : $"第 {index + 1} / {Steps.Count} 步 · 红色 * 为必填项，悬停信息图标可查看说明。";

        if (index is 4 or 5)
        {
            UpdateTypePanels();
        }
    }

    private void CompletePrototype()
    {
        RefreshStepCompletionStates();
        PrototypeStatusBorder.Visibility = Visibility.Visible;
        NextButton.Content = "界面流程已确认";
        NextButton.IsEnabled = false;
        FooterHintText.Text = "本次仅完成 V2 前端交互确认；保存、校验和模型业务尚未接入。";
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

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
            1 => "选择图片来源",
            2 => "导入模型与标签",
            3 => "编排检测项顺序",
            4 => "填写检测内容",
            5 => "设置通过条件",
            6 => "设置运行参数",
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
        private int _order;
        private string _name;
        private int _typeIndex;
        private bool _isRequired;
        private ModelPreview _model;

        public InspectionItemPreview(int order, string name, int typeIndex, bool isRequired, ModelPreview model)
        {
            _order = order;
            _name = name;
            _typeIndex = typeIndex;
            _isRequired = isRequired;
            _model = model;
        }

        public int Order
        {
            get => _order;
            set
            {
                if (SetField(ref _order, value))
                {
                    OnPropertyChanged(nameof(OrderText));
                }
            }
        }

        public string OrderText => Order.ToString("00");

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

        public string TypeLabel => TypeIndex == 1 ? "姿态动作" : "目标检测";

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
            set => SetField(ref _model, value);
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

    public sealed class ModelPreview : INotifyPropertyChanged
    {
        private string _name;
        private string _fileName;
        private int _typeIndex;
        private bool _autoImportLabels;

        public ModelPreview(
            string name,
            string fileName,
            int typeIndex,
            bool autoImportLabels,
            IEnumerable<string> labels)
        {
            _name = name;
            _fileName = fileName;
            _typeIndex = typeIndex;
            _autoImportLabels = autoImportLabels;
            Labels = new ObservableCollection<string>(labels);
        }

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
                    OnPropertyChanged(nameof(TypeLabel));
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

        public bool AutoImportLabels
        {
            get => _autoImportLabels;
            set
            {
                if (SetField(ref _autoImportLabels, value))
                {
                    OnPropertyChanged(nameof(UseManualLabels));
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

    public sealed class PoseStepPreview : INotifyPropertyChanged
    {
        private int _order;
        private string _name;
        private bool _isRequired;

        public PoseStepPreview(int order, string name, bool isRequired)
        {
            _order = order;
            _name = name;
            _isRequired = isRequired;
        }

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
