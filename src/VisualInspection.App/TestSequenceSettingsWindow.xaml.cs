using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Microsoft.Win32;
using VisualInspection.Core.Configuration;
using VisualInspection.Core.Domain;
using VisualInspection.Core.Imaging;
using VisualInspection.Core.Rules;
using VisualInspection.Infrastructure.Analysis;

namespace VisualInspection.App;

public partial class TestSequenceSettingsWindow : Window
{
    private readonly IProjectConfigurationStore _store;
    private readonly Guid _sequenceId;
    private readonly ImageFrame? _previewFrame;
    private ProjectConfiguration _workingProject;
    private bool _loading;
    private Point? _roiStart;

    public TestSequenceSettingsWindow(
        ProjectConfiguration project,
        IProjectConfigurationStore store,
        ImageFrame? previewFrame)
    {
        InitializeComponent();
        _workingProject = project;
        _store = store;
        _previewFrame = previewFrame;
        _sequenceId = project.TestSequences.OrderByDescending(sequence => sequence.IsPublished).First().Id;

        ConfigureOptions();
        LoadGeneral();
        LoadPreview();
        RefreshModels();
        RefreshTargets();
        RefreshNormalItems();
        RefreshPoseItems();
        UpdateInputSourceSummary();
    }

    private TestSequenceDefinition CurrentSequence =>
        _workingProject.TestSequences.First(sequence => sequence.Id == _sequenceId);

    private void ConfigureOptions()
    {
        ModelFormatCombo.ItemsSource = new[]
        {
            new LocalizedOption<ModelFormat>(ModelFormat.Onnx, "ONNX"),
            new LocalizedOption<ModelFormat>(ModelFormat.Pt, "PT")
        };
        ModelTaskCombo.ItemsSource = new[]
        {
            new LocalizedOption<ModelTaskType>(ModelTaskType.Detection, "目标检测"),
            new LocalizedOption<ModelTaskType>(ModelTaskType.Classification, "图像分类"),
            new LocalizedOption<ModelTaskType>(ModelTaskType.Segmentation, "图像分割"),
            new LocalizedOption<ModelTaskType>(ModelTaskType.Pose, "姿态识别"),
            new LocalizedOption<ModelTaskType>(ModelTaskType.Temporal, "时序动作")
        };
        LabelSourceCombo.ItemsSource = new[]
        {
            new LocalizedOption<LabelSourceMode>(LabelSourceMode.Manual, "手工填写"),
            new LocalizedOption<LabelSourceMode>(LabelSourceMode.ImportedFromModel, "模型导入")
        };
        SourcePolicyCombo.ItemsSource = new[]
        {
            new LocalizedOption<RuntimeSourcePolicy>(RuntimeSourcePolicy.Fixed, "固定绑定"),
            new LocalizedOption<RuntimeSourcePolicy>(RuntimeSourcePolicy.OperatorSelectable, "操作员可选")
        };
        RuleLogicalCombo.ItemsSource = new[]
        {
            new LocalizedOption<RuleLogicalOperator>(RuleLogicalOperator.And, "全部满足（与）"),
            new LocalizedOption<RuleLogicalOperator>(RuleLogicalOperator.Or, "任一满足（或）")
        };
        RuleMetricCombo.ItemsSource = new[]
        {
            new LocalizedOption<QuantityMetric>(QuantityMetric.PresentCount, "在场数量"),
            new LocalizedOption<QuantityMetric>(QuantityMetric.MissingCount, "缺失数量"),
            new LocalizedOption<QuantityMetric>(QuantityMetric.Presence, "是否存在")
        };
        RuleOperatorCombo.ItemsSource = new[]
        {
            new LocalizedOption<ComparisonOperator>(ComparisonOperator.Equal, "等于"),
            new LocalizedOption<ComparisonOperator>(ComparisonOperator.NotEqual, "不等于"),
            new LocalizedOption<ComparisonOperator>(ComparisonOperator.GreaterThan, "大于"),
            new LocalizedOption<ComparisonOperator>(ComparisonOperator.GreaterThanOrEqual, "大于等于"),
            new LocalizedOption<ComparisonOperator>(ComparisonOperator.LessThan, "小于"),
            new LocalizedOption<ComparisonOperator>(ComparisonOperator.LessThanOrEqual, "小于等于"),
            new LocalizedOption<ComparisonOperator>(ComparisonOperator.BetweenInclusive, "闭区间")
        };
        RuleOutcomeCombo.ItemsSource = new[]
        {
            new LocalizedOption<InspectionVerdict>(InspectionVerdict.Pass, "通过"),
            new LocalizedOption<InspectionVerdict>(InspectionVerdict.Fail, "不通过")
        };
        RuleScopeCombo.ItemsSource = new[]
        {
            new LocalizedOption<RegionType>(RegionType.FullImage, "全图"),
            new LocalizedOption<RegionType>(RegionType.Roi, "ROI 区域")
        };
    }

    private void LoadGeneral()
    {
        _loading = true;
        try
        {
            var sequence = CurrentSequence;
            ProjectNameBox.Text = _workingProject.Name;
            WorkstationBox.Text = _workingProject.Workstation;
            SequenceNameBox.Text = sequence.Name;
            SequenceVersionBox.Text = sequence.Version;
            DefaultDelayBox.Text = sequence.DefaultDelayMs.ToString(CultureInfo.CurrentCulture);
            InputSourceCombo.ItemsSource = _workingProject.InputSources;
            InputSourceCombo.SelectedItem = _workingProject.InputSources.FirstOrDefault(source => source.Id == sequence.InputSourceId);
            SelectOption(SourcePolicyCombo, sequence.SourcePolicy);
            PublishedCheck.IsChecked = sequence.IsPublished;
        }
        finally
        {
            _loading = false;
        }
    }

    private void LoadPreview()
    {
        if (_previewFrame is null || _previewFrame.Data.IsEmpty)
        {
            return;
        }

        using var stream = new MemoryStream(_previewFrame.Data.ToArray());
        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.StreamSource = stream;
        image.EndInit();
        image.Freeze();
        RulePreviewImage.Source = image;
        RulePreviewPlaceholder.Visibility = Visibility.Collapsed;
    }

    private void RefreshModels(Guid? selectedId = null)
    {
        _loading = true;
        try
        {
            ModelsList.ItemsSource = null;
            ModelsList.ItemsSource = _workingProject.Models;
            ModelsList.SelectedItem = _workingProject.Models.FirstOrDefault(model => model.Id == selectedId)
                ?? _workingProject.Models.FirstOrDefault();
        }
        finally
        {
            _loading = false;
        }

        if (ModelsList.SelectedItem is ModelDefinition model)
        {
            LoadModel(model);
        }
    }

    private void ModelsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_loading && ModelsList.SelectedItem is ModelDefinition model)
        {
            LoadModel(model);
        }
    }

    private void LoadModel(ModelDefinition model)
    {
        _loading = true;
        try
        {
            ModelNameBox.Text = model.Name;
            ModelVersionBox.Text = model.Version;
            ModelPathBox.Text = model.FilePath;
            SelectOption(ModelFormatCombo, model.Format);
            SelectOption(ModelTaskCombo, model.TaskType);
            SelectOption(LabelSourceCombo, model.LabelSource);
            LabelsBox.Text = string.Join(Environment.NewLine, model.Labels.OrderBy(label => label.Id).Select(label => $"{label.Id}={label.Name}"));
        }
        finally
        {
            _loading = false;
        }
    }

    private void AddModel_Click(object sender, RoutedEventArgs e)
    {
        var model = new ModelDefinition
        {
            Name = $"新模型 {_workingProject.Models.Count + 1}",
            Version = "1.0.0",
            Format = ModelFormat.Onnx,
            TaskType = ModelTaskType.Detection,
            FilePath = "models/new-model.onnx",
            LabelSource = LabelSourceMode.Manual,
            Labels = [new ModelLabelDefinition { Id = 0, Name = "新标签" }]
        };
        _workingProject = _workingProject with { Models = [.. _workingProject.Models, model] };
        RefreshModels(model.Id);
        RefreshTargets();
        SetStatus("已新增模型草稿；保存前请完善模型和标签信息。", false);
    }

    private void DeleteModel_Click(object sender, RoutedEventArgs e)
    {
        RunEditorAction(() =>
        {
            var model = RequireSelected<ModelDefinition>(ModelsList, "请先选择模型。");
            if (_workingProject.Targets.SelectMany(target => target.ModelBindings).Any(binding => binding.ModelId == model.Id))
            {
                throw new InvalidOperationException("该模型仍被目标绑定引用，不能删除。");
            }

            _workingProject = _workingProject with
            {
                Models = _workingProject.Models.Where(candidate => candidate.Id != model.Id).ToList()
            };
            RefreshModels();
            RefreshTargets();
        }, "模型已从配置草稿中删除。");
    }

    private void BrowseModel_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "选择模型文件",
            Filter = "模型文件 (*.onnx;*.pt)|*.onnx;*.pt|ONNX 模型 (*.onnx)|*.onnx|PT 模型 (*.pt)|*.pt|所有文件 (*.*)|*.*"
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        ModelPathBox.Text = dialog.FileName;
        SelectOption(ModelFormatCombo, System.IO.Path.GetExtension(dialog.FileName).Equals(".pt", StringComparison.OrdinalIgnoreCase)
            ? ModelFormat.Pt
            : ModelFormat.Onnx);
    }

    private void ImportModelLabels_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var configuredPath = RequireText(ModelPathBox.Text, "请先选择模型文件。");
            var modelPath = System.IO.Path.IsPathRooted(configuredPath)
                ? configuredPath
                : System.IO.Path.GetFullPath(configuredPath, AppContext.BaseDirectory);
            if (!File.Exists(modelPath))
            {
                throw new FileNotFoundException("模型文件不存在。", modelPath);
            }

            var labels = OnnxModelLabelImporter.Import(modelPath);
            var contract = OnnxModelContractInspector.Inspect(modelPath);
            LabelsBox.Text = string.Join(Environment.NewLine, labels.Select(label => $"{label.Id}={label.Name}"));
            SelectOption(LabelSourceCombo, LabelSourceMode.ImportedFromModel);
            SelectOption(ModelFormatCombo, ModelFormat.Onnx);
            SetStatus(
                $"已读取 {labels.Count} 个标签 · 输入 {string.Join("; ", contract.Inputs)} · 输出 {string.Join("; ", contract.Outputs)}；请检查后应用并保存。",
                false);
        }
        catch (Exception exception) when (exception is ArgumentException or IOException or InvalidDataException or
                                           NotSupportedException or Microsoft.ML.OnnxRuntime.OnnxRuntimeException)
        {
            SetStatus(exception.Message, true);
            MessageBox.Show(this, exception.Message, "标签读取失败", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void ApplyModel_Click(object sender, RoutedEventArgs e) =>
        RunEditorAction(() => ApplySelectedModel(refresh: true), "模型与标签修改已应用到配置草稿。");

    private void ApplySelectedModel(bool refresh)
    {
        if (ModelsList.SelectedItem is not ModelDefinition selected)
        {
            return;
        }

        var name = RequireText(ModelNameBox.Text, "模型名称不能为空。");
        var version = RequireText(ModelVersionBox.Text, "模型版本不能为空。");
        var filePath = RequireText(ModelPathBox.Text, "模型文件路径不能为空。");
        var labels = ParseLabels(LabelsBox.Text);
        var updated = selected with
        {
            Name = name,
            Version = version,
            FilePath = filePath,
            Format = GetSelectedValue<ModelFormat>(ModelFormatCombo),
            TaskType = GetSelectedValue<ModelTaskType>(ModelTaskCombo),
            LabelSource = GetSelectedValue<LabelSourceMode>(LabelSourceCombo),
            Labels = labels
        };
        var models = _workingProject.Models.Select(model => model.Id == selected.Id ? updated : model).ToList();
        var targets = _workingProject.Targets.Select(target => target with
        {
            ModelBindings = target.ModelBindings.Select(binding => binding.ModelId == selected.Id
                ? binding with { ModelVersion = version }
                : binding).ToList()
        }).ToList();
        _workingProject = _workingProject with { Models = models, Targets = targets };
        if (refresh)
        {
            RefreshModels(selected.Id);
            RefreshTargets();
            RefreshNormalItems();
            RefreshPoseItems();
        }
    }

    private static List<ModelLabelDefinition> ParseLabels(string text)
    {
        var labels = new List<ModelLabelDefinition>();
        foreach (var rawLine in text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var separator = rawLine.IndexOfAny(['=', ':']);
            if (separator <= 0 || !int.TryParse(rawLine[..separator].Trim(), out var id) || id < 0)
            {
                throw new ArgumentException($"标签行格式无效：{rawLine}。请使用“编号=名称”。");
            }

            var name = rawLine[(separator + 1)..].Trim();
            if (string.IsNullOrWhiteSpace(name))
            {
                throw new ArgumentException($"标签 {id} 的名称不能为空。");
            }

            labels.Add(new ModelLabelDefinition { Id = id, Name = name });
        }

        if (labels.Count == 0)
        {
            throw new ArgumentException("模型至少需要一个标签。");
        }

        if (labels.GroupBy(label => label.Id).Any(group => group.Count() > 1))
        {
            throw new ArgumentException("标签编号不能重复。");
        }

        return labels.OrderBy(label => label.Id).ToList();
    }

    private void RefreshTargets(Guid? selectedId = null)
    {
        _loading = true;
        try
        {
            TargetsList.ItemsSource = null;
            TargetsList.ItemsSource = _workingProject.Targets;
            TargetsList.SelectedItem = _workingProject.Targets.FirstOrDefault(target => target.Id == selectedId)
                ?? _workingProject.Targets.FirstOrDefault();
        }
        finally
        {
            _loading = false;
        }

        if (TargetsList.SelectedItem is TargetDefinition target)
        {
            LoadTarget(target);
        }
    }

    private void TargetsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_loading && TargetsList.SelectedItem is TargetDefinition target)
        {
            LoadTarget(target);
        }
    }

    private void LoadTarget(TargetDefinition target)
    {
        _loading = true;
        try
        {
            TargetNameBox.Text = target.Name;
            BindingModelCombo.ItemsSource = _workingProject.Models;
            var binding = target.ModelBindings.FirstOrDefault();
            BindingModelCombo.SelectedItem = binding is null
                ? _workingProject.Models.FirstOrDefault()
                : _workingProject.Models.FirstOrDefault(model => model.Id == binding.ModelId);
            RefreshBindingLabels(binding?.OutputLabelId);
        }
        finally
        {
            _loading = false;
        }
    }

    private void BindingModelCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_loading)
        {
            RefreshBindingLabels();
        }
    }

    private void RefreshBindingLabels(int? selectedLabelId = null)
    {
        if (BindingModelCombo.SelectedItem is not ModelDefinition model)
        {
            BindingLabelCombo.ItemsSource = null;
            return;
        }

        var options = model.Labels.Select(label => new LocalizedOption<int>(label.Id, $"{label.Id} · {label.Name}")).ToArray();
        BindingLabelCombo.ItemsSource = options;
        BindingLabelCombo.SelectedItem = options.FirstOrDefault(option => option.Value == selectedLabelId) ?? options.FirstOrDefault();
    }

    private void AddTarget_Click(object sender, RoutedEventArgs e)
    {
        RunEditorAction(() =>
        {
            var model = _workingProject.Models.FirstOrDefault() ?? throw new InvalidOperationException("请先创建模型。");
            var label = model.Labels.FirstOrDefault() ?? throw new InvalidOperationException("请先为模型配置标签。");
            var target = new TargetDefinition
            {
                Name = $"新目标 {_workingProject.Targets.Count + 1}",
                ModelBindings =
                [
                    new ModelBindingDefinition
                    {
                        ModelId = model.Id,
                        ModelVersion = model.Version,
                        OutputLabelId = label.Id
                    }
                ]
            };
            _workingProject = _workingProject with { Targets = [.. _workingProject.Targets, target] };
            RefreshTargets(target.Id);
            RefreshNormalItems();
            RefreshPoseItems();
        }, "已新增目标草稿。");
    }

    private void DeleteTarget_Click(object sender, RoutedEventArgs e)
    {
        RunEditorAction(() =>
        {
            var target = RequireSelected<TargetDefinition>(TargetsList, "请先选择目标。");
            var bindingIds = target.ModelBindings.Select(binding => binding.Id).ToHashSet();
            var referenced = _workingProject.TestSequences.SelectMany(sequence => sequence.Items).Any(item =>
                item.Rules.Any(rule => rule.TargetId == target.Id || bindingIds.Contains(rule.ModelBindingId)) ||
                item.PoseSteps.Any(step => bindingIds.Contains(step.ModelBindingId)));
            if (referenced)
            {
                throw new InvalidOperationException("该目标或其模型绑定仍被规则/姿态步骤引用，不能删除。");
            }

            _workingProject = _workingProject with
            {
                Targets = _workingProject.Targets.Where(candidate => candidate.Id != target.Id).ToList()
            };
            RefreshTargets();
            RefreshNormalItems();
            RefreshPoseItems();
        }, "目标已从配置草稿中删除。");
    }

    private void ApplyTarget_Click(object sender, RoutedEventArgs e) =>
        RunEditorAction(() => ApplySelectedTarget(refresh: true), "目标与模型绑定修改已应用到配置草稿。");

    private void ApplySelectedTarget(bool refresh)
    {
        if (TargetsList.SelectedItem is not TargetDefinition selected)
        {
            return;
        }

        var model = RequireSelected<ModelDefinition>(BindingModelCombo, "请选择绑定模型。");
        var labelId = GetSelectedValue<int>(BindingLabelCombo);
        var existing = selected.ModelBindings.FirstOrDefault();
        var binding = (existing ?? new ModelBindingDefinition()) with
        {
            ModelId = model.Id,
            ModelVersion = model.Version,
            OutputLabelId = labelId
        };
        var updated = selected with
        {
            Name = RequireText(TargetNameBox.Text, "目标名称不能为空。"),
            ModelBindings = existing is null
                ? [binding]
                : [binding, .. selected.ModelBindings.Skip(1)]
        };
        _workingProject = _workingProject with
        {
            Targets = _workingProject.Targets.Select(target => target.Id == selected.Id ? updated : target).ToList()
        };
        if (refresh)
        {
            RefreshTargets(selected.Id);
            RefreshNormalItems();
            RefreshPoseItems();
        }
    }

    private void RefreshNormalItems(Guid? itemId = null, Guid? ruleId = null)
    {
        var items = CurrentSequence.Items.Where(item => item.Type == TestItemType.Normal).OrderBy(item => item.Order).ToList();
        _loading = true;
        try
        {
            NormalItemsList.ItemsSource = items;
            NormalItemsList.SelectedItem = items.FirstOrDefault(item => item.Id == itemId) ?? items.FirstOrDefault();
        }
        finally
        {
            _loading = false;
        }

        if (NormalItemsList.SelectedItem is TestItemDefinition item)
        {
            LoadNormalItem(item, ruleId);
        }
        else
        {
            RulesList.ItemsSource = null;
        }

        UpdateNormalEditorAvailability();
    }

    private void NormalItemsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_loading && NormalItemsList.SelectedItem is TestItemDefinition item)
        {
            LoadNormalItem(item);
        }

        UpdateNormalEditorAvailability();
    }

    private void LoadNormalItem(TestItemDefinition item, Guid? ruleId = null)
    {
        _loading = true;
        try
        {
            NormalItemNameBox.Text = item.Name;
            NormalItemDelayBox.Text = item.DelayMs?.ToString(CultureInfo.CurrentCulture) ?? string.Empty;
            NormalItemEnabledCheck.IsChecked = item.Enabled;
            NormalItemRequiredCheck.IsChecked = item.IsRequired;
            SelectOption(RuleLogicalCombo, item.RuleOperator);
            var rules = item.Rules.Select(rule => new RuleDisplay(rule, RuleStandardFormatter.Format(rule, _workingProject))).ToList();
            RulesList.ItemsSource = rules;
            RulesList.SelectedItem = rules.FirstOrDefault(candidate => candidate.Rule.Id == ruleId) ?? rules.FirstOrDefault();
        }
        finally
        {
            _loading = false;
        }

        if (RulesList.SelectedItem is RuleDisplay display)
        {
            LoadRule(display.Rule);
        }

        UpdateNormalEditorAvailability();
    }

    private void RulesList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_loading && RulesList.SelectedItem is RuleDisplay display)
        {
            LoadRule(display.Rule);
        }

        UpdateNormalEditorAvailability();
    }

    private void UpdateNormalEditorAvailability()
    {
        var hasItem = NormalItemsList.SelectedItem is TestItemDefinition;
        var hasRule = RulesList.SelectedItem is RuleDisplay;
        DeleteNormalItemButton.IsEnabled = hasItem;
        NormalRuleEditor.IsEnabled = hasItem;
        AddRuleButton.IsEnabled = hasItem;
        ApplyRuleButton.IsEnabled = hasRule;
        DeleteRuleButton.IsEnabled = hasRule;
        RoiEditor.IsEnabled = hasRule;
    }

    private void AddNormalItem_Click(object sender, RoutedEventArgs e)
    {
        RunEditorAction(() =>
        {
            ApplySelectedRule(refresh: false);
            var edit = TestSequenceEditor.AddNormalItem(_workingProject, CurrentSequence, CreateNormalItemName());
            ReplaceSequence(edit.Sequence);
            RefreshNormalItems(edit.Item.Id, edit.Item.Rules[0].Id);
        }, "已新增普通检测项草稿；保存前可继续修改默认规则。");
    }

    private void DeleteNormalItem_Click(object sender, RoutedEventArgs e)
    {
        RunEditorAction(() =>
        {
            var item = RequireSelected<TestItemDefinition>(NormalItemsList, "请先选择普通检测项。");
            ReplaceSequence(TestSequenceEditor.RemoveItem(CurrentSequence, item.Id));
            RefreshNormalItems();
            RefreshPoseItems();
        }, "普通检测项及其规则已从配置草稿中删除。");
    }

    private string CreateNormalItemName()
    {
        var existingNames = CurrentSequence.Items
            .Select(item => item.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var suffix = 1;
        while (existingNames.Contains($"普通检测项 {suffix}"))
        {
            suffix++;
        }

        return $"普通检测项 {suffix}";
    }

    private void LoadRule(TargetRuleDefinition rule)
    {
        _loading = true;
        try
        {
            RuleTargetCombo.ItemsSource = _workingProject.Targets;
            RuleTargetCombo.SelectedItem = _workingProject.Targets.FirstOrDefault(target => target.Id == rule.TargetId);
            RefreshRuleBindings(rule.ModelBindingId);
            SelectOption(RuleMetricCombo, rule.Metric);
            SelectOption(RuleOperatorCombo, rule.Operator);
            SelectOption(RuleOutcomeCombo, rule.OutcomeWhenMatched);
            SelectOption(RuleScopeCombo, rule.Scope.Type);
            RuleThresholdBox.Text = rule.Threshold.ToString(CultureInfo.CurrentCulture);
            RuleUpperBox.Text = rule.UpperThreshold?.ToString(CultureInfo.CurrentCulture) ?? string.Empty;
            RuleExpectedBox.Text = rule.ExpectedCount?.ToString(CultureInfo.CurrentCulture) ?? string.Empty;
            RuleConfidenceBox.Text = rule.ConfidenceThreshold.ToString("0.##", CultureInfo.CurrentCulture);
            var region = rule.Scope.Regions.FirstOrDefault();
            RoiNameBox.Text = region?.Name ?? "区域-1";
            RoiX1Box.Text = (region?.X1 ?? 0).ToString(CultureInfo.CurrentCulture);
            RoiY1Box.Text = (region?.Y1 ?? 0).ToString(CultureInfo.CurrentCulture);
            RoiX2Box.Text = (region?.X2 ?? _previewFrame?.Width ?? 640).ToString(CultureInfo.CurrentCulture);
            RoiY2Box.Text = (region?.Y2 ?? _previewFrame?.Height ?? 360).ToString(CultureInfo.CurrentCulture);
            RoiReferenceWidthBox.Text = (region?.ReferenceWidth ?? _previewFrame?.Width ?? 640).ToString(CultureInfo.CurrentCulture);
            RoiReferenceHeightBox.Text = (region?.ReferenceHeight ?? _previewFrame?.Height ?? 360).ToString(CultureInfo.CurrentCulture);
        }
        finally
        {
            _loading = false;
        }

        UpdateRoiEditorState();
        DrawRoiFromFields();
    }

    private void RuleTargetCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_loading)
        {
            RefreshRuleBindings();
        }
    }

    private void RefreshRuleBindings(Guid? selectedBindingId = null)
    {
        if (RuleTargetCombo.SelectedItem is not TargetDefinition target)
        {
            RuleBindingCombo.ItemsSource = null;
            return;
        }

        var options = target.ModelBindings.Select(CreateBindingOption).ToList();
        RuleBindingCombo.ItemsSource = options;
        RuleBindingCombo.SelectedItem = options.FirstOrDefault(option => option.Binding.Id == selectedBindingId) ?? options.FirstOrDefault();
    }

    private BindingOption CreateBindingOption(ModelBindingDefinition binding)
    {
        var model = _workingProject.Models.FirstOrDefault(candidate => candidate.Id == binding.ModelId);
        var label = model?.Labels.FirstOrDefault(candidate => candidate.Id == binding.OutputLabelId);
        return new BindingOption(binding, $"{model?.Name ?? "未知模型"} · {label?.Name ?? $"标签 {binding.OutputLabelId}"}");
    }

    private void ApplyRule_Click(object sender, RoutedEventArgs e) =>
        RunEditorAction(() => ApplySelectedRule(refresh: true), "检测项与规则修改已应用到配置草稿。");

    private void ApplySelectedRule(bool refresh)
    {
        if (NormalItemsList.SelectedItem is not TestItemDefinition selectedItem || RulesList.SelectedItem is not RuleDisplay selectedDisplay)
        {
            return;
        }

        var targetSelection = RequireSelected<TargetDefinition>(RuleTargetCombo, "请选择检测目标。");
        var target = _workingProject.Targets.First(candidate => candidate.Id == targetSelection.Id);
        var bindingSelection = RequireSelected<BindingOption>(RuleBindingCombo, "请选择模型绑定。");
        var binding = target.ModelBindings.FirstOrDefault(candidate => candidate.Id == bindingSelection.Binding.Id)
            ?? throw new InvalidOperationException("所选模型绑定已不存在。");
        var scopeType = GetSelectedValue<RegionType>(RuleScopeCombo);
        var scope = scopeType == RegionType.FullImage
            ? new RegionScopeDefinition { Type = RegionType.FullImage }
            : new RegionScopeDefinition
            {
                Type = RegionType.Roi,
                Regions =
                [
                    new RegionOfInterestDefinition
                    {
                        Id = selectedDisplay.Rule.Scope.Regions.FirstOrDefault()?.Id ?? Guid.NewGuid(),
                        Name = RequireText(RoiNameBox.Text, "ROI 名称不能为空。"),
                        X1 = ParseNonNegativeInt(RoiX1Box.Text, "ROI X1"),
                        Y1 = ParseNonNegativeInt(RoiY1Box.Text, "ROI Y1"),
                        X2 = ParseNonNegativeInt(RoiX2Box.Text, "ROI X2"),
                        Y2 = ParseNonNegativeInt(RoiY2Box.Text, "ROI Y2"),
                        ReferenceWidth = ParsePositiveInt(RoiReferenceWidthBox.Text, "ROI 参考宽度"),
                        ReferenceHeight = ParsePositiveInt(RoiReferenceHeightBox.Text, "ROI 参考高度")
                    }
                ]
            };
        var updatedRule = selectedDisplay.Rule with
        {
            TargetId = target.Id,
            ModelBindingId = binding.Id,
            Scope = scope,
            Metric = GetSelectedValue<QuantityMetric>(RuleMetricCombo),
            Operator = GetSelectedValue<ComparisonOperator>(RuleOperatorCombo),
            Threshold = ParseNonNegativeInt(RuleThresholdBox.Text, "规则阈值"),
            UpperThreshold = ParseOptionalNonNegativeInt(RuleUpperBox.Text, "规则上限"),
            ExpectedCount = ParseOptionalNonNegativeInt(RuleExpectedBox.Text, "预期数量"),
            ConfidenceThreshold = ParseProbability(RuleConfidenceBox.Text, "规则置信度"),
            OutcomeWhenMatched = GetSelectedValue<InspectionVerdict>(RuleOutcomeCombo)
        };
        var updatedItem = selectedItem with
        {
            Name = RequireText(NormalItemNameBox.Text, "检测项名称不能为空。"),
            DelayMs = ParseOptionalNonNegativeInt(NormalItemDelayBox.Text, "单项延时"),
            Enabled = NormalItemEnabledCheck.IsChecked == true,
            IsRequired = NormalItemRequiredCheck.IsChecked == true,
            RuleOperator = GetSelectedValue<RuleLogicalOperator>(RuleLogicalCombo),
            Rules = selectedItem.Rules.Select(rule => rule.Id == updatedRule.Id ? updatedRule : rule).ToList()
        };
        ReplaceSequenceItem(updatedItem);
        if (refresh)
        {
            RefreshNormalItems(updatedItem.Id, updatedRule.Id);
        }
    }

    private void AddRule_Click(object sender, RoutedEventArgs e)
    {
        RunEditorAction(() =>
        {
            var item = RequireSelected<TestItemDefinition>(NormalItemsList, "请先选择普通检测项。");
            var target = _workingProject.Targets.FirstOrDefault(candidate => candidate.ModelBindings.Count > 0)
                ?? throw new InvalidOperationException("请先创建带模型绑定的目标。");
            var rule = new TargetRuleDefinition
            {
                TargetId = target.Id,
                ModelBindingId = target.ModelBindings[0].Id,
                Metric = QuantityMetric.PresentCount,
                Operator = ComparisonOperator.Equal,
                Threshold = 1,
                OutcomeWhenMatched = InspectionVerdict.Pass
            };
            var updated = item with { Rules = [.. item.Rules, rule] };
            ReplaceSequenceItem(updated);
            RefreshNormalItems(updated.Id, rule.Id);
        }, "已新增规则草稿。");
    }

    private void DeleteRule_Click(object sender, RoutedEventArgs e)
    {
        RunEditorAction(() =>
        {
            var item = RequireSelected<TestItemDefinition>(NormalItemsList, "请先选择普通检测项。");
            var display = RequireSelected<RuleDisplay>(RulesList, "请先选择规则。");
            if (item.Rules.Count <= 1)
            {
                throw new InvalidOperationException("普通检测项至少需要一条规则。");
            }

            var updated = item with { Rules = item.Rules.Where(rule => rule.Id != display.Rule.Id).ToList() };
            ReplaceSequenceItem(updated);
            RefreshNormalItems(updated.Id);
        }, "规则已从配置草稿中删除。");
    }

    private void RuleScopeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_loading)
        {
            UpdateRoiEditorState();
            DrawRoiFromFields();
        }
    }

    private void UpdateRoiEditorState()
    {
        var enabled = RuleScopeCombo.SelectedItem is LocalizedOption<RegionType> option && option.Value == RegionType.Roi;
        RoiNameBox.IsEnabled = enabled;
        RoiX1Box.IsEnabled = enabled;
        RoiY1Box.IsEnabled = enabled;
        RoiX2Box.IsEnabled = enabled;
        RoiY2Box.IsEnabled = enabled;
        RoiReferenceWidthBox.IsEnabled = enabled;
        RoiReferenceHeightBox.IsEnabled = enabled;
        RuleRoiCanvas.IsEnabled = enabled;
        RuleRoiRectangle.Visibility = enabled ? Visibility.Visible : Visibility.Collapsed;
    }

    private void RoiCoordinate_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_loading)
        {
            DrawRoiFromFields();
        }
    }

    private void DrawRoiFromFields()
    {
        if (!RuleRoiCanvas.IsEnabled ||
            !TryReadRoi(out var x1, out var y1, out var x2, out var y2, out var width, out var height) ||
            width <= 0 || height <= 0)
        {
            RuleRoiRectangle.Visibility = Visibility.Collapsed;
            return;
        }

        var left = 480d * x1 / width;
        var top = 270d * y1 / height;
        var right = 480d * x2 / width;
        var bottom = 270d * y2 / height;
        Canvas.SetLeft(RuleRoiRectangle, Math.Clamp(left, 0, 480));
        Canvas.SetTop(RuleRoiRectangle, Math.Clamp(top, 0, 270));
        RuleRoiRectangle.Width = Math.Max(0, Math.Clamp(right, 0, 480) - Math.Clamp(left, 0, 480));
        RuleRoiRectangle.Height = Math.Max(0, Math.Clamp(bottom, 0, 270) - Math.Clamp(top, 0, 270));
        RuleRoiRectangle.Visibility = Visibility.Visible;
    }

    private bool TryReadRoi(out int x1, out int y1, out int x2, out int y2, out int width, out int height)
    {
        x1 = y1 = x2 = y2 = width = height = 0;
        return int.TryParse(RoiX1Box.Text, out x1) && int.TryParse(RoiY1Box.Text, out y1) &&
            int.TryParse(RoiX2Box.Text, out x2) && int.TryParse(RoiY2Box.Text, out y2) &&
            int.TryParse(RoiReferenceWidthBox.Text, out width) && int.TryParse(RoiReferenceHeightBox.Text, out height);
    }

    private void RuleRoiCanvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (!RuleRoiCanvas.IsEnabled)
        {
            return;
        }

        _roiStart = e.GetPosition(RuleRoiCanvas);
        RuleRoiCanvas.CaptureMouse();
        UpdateDragRectangle(_roiStart.Value, _roiStart.Value);
    }

    private void RuleRoiCanvas_MouseMove(object sender, MouseEventArgs e)
    {
        if (_roiStart is not null && e.LeftButton == MouseButtonState.Pressed)
        {
            UpdateDragRectangle(_roiStart.Value, e.GetPosition(RuleRoiCanvas));
        }
    }

    private void RuleRoiCanvas_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_roiStart is null)
        {
            return;
        }

        var end = e.GetPosition(RuleRoiCanvas);
        var start = _roiStart.Value;
        _roiStart = null;
        RuleRoiCanvas.ReleaseMouseCapture();
        var referenceWidth = int.TryParse(RoiReferenceWidthBox.Text, out var width) && width > 0 ? width : _previewFrame?.Width ?? 640;
        var referenceHeight = int.TryParse(RoiReferenceHeightBox.Text, out var height) && height > 0 ? height : _previewFrame?.Height ?? 360;
        var left = Math.Clamp(Math.Min(start.X, end.X), 0, 480);
        var top = Math.Clamp(Math.Min(start.Y, end.Y), 0, 270);
        var right = Math.Clamp(Math.Max(start.X, end.X), 0, 480);
        var bottom = Math.Clamp(Math.Max(start.Y, end.Y), 0, 270);
        _loading = true;
        try
        {
            RoiX1Box.Text = Math.Round(left * referenceWidth / 480).ToString(CultureInfo.CurrentCulture);
            RoiY1Box.Text = Math.Round(top * referenceHeight / 270).ToString(CultureInfo.CurrentCulture);
            RoiX2Box.Text = Math.Round(right * referenceWidth / 480).ToString(CultureInfo.CurrentCulture);
            RoiY2Box.Text = Math.Round(bottom * referenceHeight / 270).ToString(CultureInfo.CurrentCulture);
            RoiReferenceWidthBox.Text = referenceWidth.ToString(CultureInfo.CurrentCulture);
            RoiReferenceHeightBox.Text = referenceHeight.ToString(CultureInfo.CurrentCulture);
        }
        finally
        {
            _loading = false;
        }

        DrawRoiFromFields();
    }

    private void UpdateDragRectangle(Point start, Point end)
    {
        var left = Math.Clamp(Math.Min(start.X, end.X), 0, 480);
        var top = Math.Clamp(Math.Min(start.Y, end.Y), 0, 270);
        var right = Math.Clamp(Math.Max(start.X, end.X), 0, 480);
        var bottom = Math.Clamp(Math.Max(start.Y, end.Y), 0, 270);
        Canvas.SetLeft(RuleRoiRectangle, left);
        Canvas.SetTop(RuleRoiRectangle, top);
        RuleRoiRectangle.Width = right - left;
        RuleRoiRectangle.Height = bottom - top;
        RuleRoiRectangle.Visibility = Visibility.Visible;
    }

    private void RefreshPoseItems(Guid? itemId = null, Guid? stepId = null)
    {
        var items = CurrentSequence.Items.Where(item => item.Type == TestItemType.PoseSequence).OrderBy(item => item.Order).ToList();
        _loading = true;
        try
        {
            PoseItemsList.ItemsSource = items;
            PoseItemsList.SelectedItem = items.FirstOrDefault(item => item.Id == itemId) ?? items.FirstOrDefault();
        }
        finally
        {
            _loading = false;
        }

        if (PoseItemsList.SelectedItem is TestItemDefinition item)
        {
            LoadPoseItem(item, stepId);
        }
        else
        {
            PoseStepsList.ItemsSource = null;
            PoseCanvasPanel.Children.Clear();
        }
    }

    private void PoseItemsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_loading && PoseItemsList.SelectedItem is TestItemDefinition item)
        {
            LoadPoseItem(item);
        }
    }

    private void LoadPoseItem(TestItemDefinition item, Guid? stepId = null)
    {
        _loading = true;
        try
        {
            PoseItemNameBox.Text = item.Name;
            PoseItemDelayBox.Text = item.DelayMs?.ToString(CultureInfo.CurrentCulture) ?? string.Empty;
            var steps = item.PoseSteps.OrderBy(step => step.Order).Select(step => new PoseStepDisplay(step)).ToList();
            PoseStepsList.ItemsSource = steps;
            PoseStepsList.SelectedItem = steps.FirstOrDefault(display => display.Step.Id == stepId) ?? steps.FirstOrDefault();
            PoseBindingCombo.ItemsSource = GetPoseBindingOptions();
        }
        finally
        {
            _loading = false;
        }

        DrawPoseCanvas(item.PoseSteps);
        if (PoseStepsList.SelectedItem is PoseStepDisplay display)
        {
            LoadPoseStep(display.Step);
        }
    }

    private void PoseStepsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_loading && PoseStepsList.SelectedItem is PoseStepDisplay display)
        {
            LoadPoseStep(display.Step);
        }
    }

    private List<BindingOption> GetPoseBindingOptions()
    {
        return _workingProject.Targets.SelectMany(target => target.ModelBindings)
            .Where(binding => _workingProject.Models.Any(model => model.Id == binding.ModelId &&
                model.TaskType is ModelTaskType.Pose or ModelTaskType.Temporal))
            .Select(CreateBindingOption)
            .ToList();
    }

    private void LoadPoseStep(PoseStepDefinition step)
    {
        _loading = true;
        try
        {
            PoseStepNameBox.Text = step.Name;
            PoseActionBox.Text = step.ActionCondition;
            PoseConfidenceBox.Text = step.ConfidenceThreshold.ToString("0.##", CultureInfo.CurrentCulture);
            PoseHoldBox.Text = step.MinimumHoldMs.ToString(CultureInfo.CurrentCulture);
            PoseWaitBox.Text = step.MaximumWaitMs.ToString(CultureInfo.CurrentCulture);
            PoseRequiredCheck.IsChecked = step.IsRequired;
            var options = GetPoseBindingOptions();
            PoseBindingCombo.ItemsSource = options;
            PoseBindingCombo.SelectedItem = options.FirstOrDefault(option => option.Binding.Id == step.ModelBindingId) ?? options.FirstOrDefault();
        }
        finally
        {
            _loading = false;
        }
    }

    private void DrawPoseCanvas(IEnumerable<PoseStepDefinition> steps)
    {
        PoseCanvasPanel.Children.Clear();
        var ordered = steps.OrderBy(step => step.Order).ToArray();
        for (var index = 0; index < ordered.Length; index++)
        {
            if (index > 0)
            {
                PoseCanvasPanel.Children.Add(new TextBlock
                {
                    Text = "→",
                    Margin = new Thickness(8, 11, 8, 0),
                    FontSize = 18,
                    Foreground = (Brush)FindResource("GreenDarkBrush")
                });
            }

            var border = new Border
            {
                Background = (Brush)FindResource("GreenPaleBrush"),
                BorderBrush = (Brush)FindResource("GreenBrush"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(3),
                Padding = new Thickness(12, 8, 12, 8),
                Margin = new Thickness(0, 4, 0, 4)
            };
            border.Child = new TextBlock { Text = $"{ordered[index].Order}. {ordered[index].Name}", FontWeight = FontWeights.SemiBold };
            PoseCanvasPanel.Children.Add(border);
        }
    }

    private void ApplyPoseStep_Click(object sender, RoutedEventArgs e) =>
        RunEditorAction(() => ApplySelectedPoseStep(refresh: true), "姿态步骤修改已应用到配置草稿。");

    private void ApplySelectedPoseStep(bool refresh)
    {
        if (PoseItemsList.SelectedItem is not TestItemDefinition selectedItem || PoseStepsList.SelectedItem is not PoseStepDisplay selectedDisplay)
        {
            return;
        }

        var binding = RequireSelected<BindingOption>(PoseBindingCombo, "请选择姿态或时序模型绑定。");
        var updatedStep = selectedDisplay.Step with
        {
            Name = RequireText(PoseStepNameBox.Text, "步骤名称不能为空。"),
            ActionCondition = RequireText(PoseActionBox.Text, "动作条件不能为空。"),
            ModelBindingId = binding.Binding.Id,
            ConfidenceThreshold = ParseProbability(PoseConfidenceBox.Text, "姿态置信度"),
            MinimumHoldMs = ParseNonNegativeInt(PoseHoldBox.Text, "动作保持时间"),
            MaximumWaitMs = ParsePositiveInt(PoseWaitBox.Text, "最大等待时间"),
            IsRequired = PoseRequiredCheck.IsChecked == true
        };
        var updatedItem = selectedItem with
        {
            Name = RequireText(PoseItemNameBox.Text, "姿态项名称不能为空。"),
            DelayMs = ParseOptionalNonNegativeInt(PoseItemDelayBox.Text, "姿态项延时"),
            PoseSteps = selectedItem.PoseSteps.Select(step => step.Id == updatedStep.Id ? updatedStep : step).ToList()
        };
        ReplaceSequenceItem(updatedItem);
        if (refresh)
        {
            RefreshPoseItems(updatedItem.Id, updatedStep.Id);
        }
    }

    private void AddPoseStep_Click(object sender, RoutedEventArgs e)
    {
        RunEditorAction(() =>
        {
            var item = RequireSelected<TestItemDefinition>(PoseItemsList, "请先选择姿态测试项。");
            var binding = GetPoseBindingOptions().FirstOrDefault()
                ?? throw new InvalidOperationException("请先创建姿态或时序模型绑定。");
            var step = new PoseStepDefinition
            {
                Order = item.PoseSteps.Count + 1,
                Name = $"新步骤 {item.PoseSteps.Count + 1}",
                ActionCondition = "new_action",
                ModelBindingId = binding.Binding.Id,
                ConfidenceThreshold = 0.5,
                MinimumHoldMs = 200,
                MaximumWaitMs = 5000
            };
            var updated = item with { PoseSteps = [.. item.PoseSteps, step] };
            ReplaceSequenceItem(updated);
            RefreshPoseItems(updated.Id, step.Id);
        }, "已新增姿态步骤草稿。");
    }

    private void DeletePoseStep_Click(object sender, RoutedEventArgs e)
    {
        RunEditorAction(() =>
        {
            var item = RequireSelected<TestItemDefinition>(PoseItemsList, "请先选择姿态测试项。");
            var display = RequireSelected<PoseStepDisplay>(PoseStepsList, "请先选择姿态步骤。");
            if (item.PoseSteps.Count <= 1)
            {
                throw new InvalidOperationException("姿态测试项至少需要一个动作步骤。");
            }

            var steps = item.PoseSteps.Where(step => step.Id != display.Step.Id).OrderBy(step => step.Order)
                .Select((step, index) => step with { Order = index + 1 }).ToList();
            var updated = item with { PoseSteps = steps };
            ReplaceSequenceItem(updated);
            RefreshPoseItems(updated.Id);
        }, "姿态步骤已从配置草稿中删除。");
    }

    private void MovePoseStepUp_Click(object sender, RoutedEventArgs e) => MoveSelectedPoseStep(-1);

    private void MovePoseStepDown_Click(object sender, RoutedEventArgs e) => MoveSelectedPoseStep(1);

    private void MoveSelectedPoseStep(int offset)
    {
        RunEditorAction(() =>
        {
            var item = RequireSelected<TestItemDefinition>(PoseItemsList, "请先选择姿态测试项。");
            var display = RequireSelected<PoseStepDisplay>(PoseStepsList, "请先选择姿态步骤。");
            var steps = item.PoseSteps.OrderBy(step => step.Order).ToList();
            var index = steps.FindIndex(step => step.Id == display.Step.Id);
            var targetIndex = index + offset;
            if (index < 0 || targetIndex < 0 || targetIndex >= steps.Count)
            {
                return;
            }

            (steps[index], steps[targetIndex]) = (steps[targetIndex], steps[index]);
            steps = steps.Select((step, order) => step with { Order = order + 1 }).ToList();
            var updated = item with { PoseSteps = steps };
            ReplaceSequenceItem(updated);
            RefreshPoseItems(updated.Id, display.Step.Id);
        }, offset < 0 ? "姿态步骤已上移。" : "姿态步骤已下移。");
    }

    private void UpdateInputSourceSummary()
    {
        var sequence = CurrentSequence;
        var source = _workingProject.InputSources.FirstOrDefault(candidate => candidate.Id == sequence.InputSourceId);
        InputSourceSummaryText.Text = source is null
            ? "当前测试序列没有有效图源绑定。"
            : $"当前绑定：{source.Name} · {FormatSourceType(source.Type)}\n运行策略：{FormatSourcePolicy(sequence.SourcePolicy)}";
    }

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            ApplyGeneral();
            ApplySelectedModel(refresh: false);
            ApplySelectedTarget(refresh: false);
            ApplySelectedRule(refresh: false);
            ApplySelectedPoseStep(refresh: false);

            var issues = ProjectConfigurationValidator.Validate(_workingProject);
            var errors = issues.Where(issue => issue.Severity == ConfigurationValidationSeverity.Error).ToArray();
            if (errors.Length > 0)
            {
                var details = string.Join(Environment.NewLine, errors.Take(10).Select(issue => $"{issue.Code}：{issue.Message}"));
                throw new InvalidDataException($"配置校验未通过：{Environment.NewLine}{details}");
            }

            await _store.SaveAsync(_workingProject);
            ValidationText.Text = issues.Count == 0
                ? "配置校验通过并已保存。"
                : $"配置已保存，同时保留 {issues.Count} 条非阻塞警告。";
            DialogResult = true;
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or InvalidDataException or IOException)
        {
            SetStatus(exception.Message, true);
            MessageBox.Show(this, exception.Message, "无法保存测试序列", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void ApplyGeneral()
    {
        var sequence = CurrentSequence;
        var source = RequireSelected<InputSourceDefinition>(InputSourceCombo, "请选择测试序列绑定图源。");
        var published = PublishedCheck.IsChecked == true;
        var updated = sequence with
        {
            Name = RequireText(SequenceNameBox.Text, "测试序列名称不能为空。"),
            Version = RequireText(SequenceVersionBox.Text, "测试序列版本不能为空。"),
            DefaultDelayMs = ParseNonNegativeInt(DefaultDelayBox.Text, "默认延时"),
            InputSourceId = source.Id,
            SourcePolicy = GetSelectedValue<RuntimeSourcePolicy>(SourcePolicyCombo),
            IsPublished = published,
            PublishedAtUtc = published ? sequence.PublishedAtUtc ?? DateTimeOffset.UtcNow : null
        };
        _workingProject = _workingProject with
        {
            Name = RequireText(ProjectNameBox.Text, "项目名称不能为空。"),
            Workstation = RequireText(WorkstationBox.Text, "工位不能为空。")
        };
        ReplaceSequence(updated);
        UpdateInputSourceSummary();
    }

    private void ReplaceSequenceItem(TestItemDefinition item)
    {
        var sequence = CurrentSequence with
        {
            Items = CurrentSequence.Items.Select(candidate => candidate.Id == item.Id ? item : candidate).ToList()
        };
        ReplaceSequence(sequence);
    }

    private void ReplaceSequence(TestSequenceDefinition sequence)
    {
        _workingProject = _workingProject with
        {
            TestSequences = _workingProject.TestSequences.Select(candidate => candidate.Id == sequence.Id ? sequence : candidate).ToList()
        };
    }

    private void RunEditorAction(Action action, string successMessage)
    {
        try
        {
            action();
            SetStatus(successMessage, false);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            SetStatus(exception.Message, true);
        }
    }

    private void SetStatus(string message, bool isError)
    {
        ValidationText.Text = message;
        ValidationText.Foreground = isError
            ? (Brush)FindResource("FailBrush")
            : (Brush)FindResource("GreenDarkBrush");
    }

    private static T RequireSelected<T>(Selector selector, string message) where T : class =>
        selector.SelectedItem as T ?? throw new InvalidOperationException(message);

    private static string RequireText(string? value, string message) =>
        string.IsNullOrWhiteSpace(value) ? throw new ArgumentException(message) : value.Trim();

    private static int ParsePositiveInt(string text, string fieldName) =>
        int.TryParse(text, out var value) && value > 0
            ? value
            : throw new ArgumentException($"{fieldName}必须是正整数。");

    private static int ParseNonNegativeInt(string text, string fieldName) =>
        int.TryParse(text, out var value) && value >= 0
            ? value
            : throw new ArgumentException($"{fieldName}必须是非负整数。");

    private static int? ParseOptionalNonNegativeInt(string text, string fieldName) =>
        string.IsNullOrWhiteSpace(text) ? null : ParseNonNegativeInt(text, fieldName);

    private static double ParseProbability(string text, string fieldName)
    {
        if (!double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out var value) &&
            !double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value) || value is < 0 or > 1)
        {
            throw new ArgumentException($"{fieldName}必须位于 0 到 1 之间。");
        }

        return value;
    }

    private static T GetSelectedValue<T>(Selector selector) where T : struct
    {
        return selector.SelectedItem switch
        {
            LocalizedOption<T> option => option.Value,
            _ => throw new InvalidOperationException("请选择有效选项。")
        };
    }

    private static void SelectOption<T>(Selector selector, T value) where T : struct
    {
        selector.SelectedItem = selector.ItemsSource.Cast<LocalizedOption<T>>()
            .FirstOrDefault(option => EqualityComparer<T>.Default.Equals(option.Value, value));
    }

    private static string FormatSourceType(InputSourceType type) => type switch
    {
        InputSourceType.Folder => "文件夹图源",
        InputSourceType.DirectShowCamera => "USB 相机（DirectShow）",
        _ => "工业相机（厂商适配器）"
    };

    private static string FormatSourcePolicy(RuntimeSourcePolicy policy) => policy == RuntimeSourcePolicy.Fixed
        ? "固定绑定"
        : "操作员可选";

    private sealed record LocalizedOption<T>(T Value, string Display)
    {
        public override string ToString() => Display;
    }

    private sealed record BindingOption(ModelBindingDefinition Binding, string Display)
    {
        public override string ToString() => Display;
    }

    private sealed record RuleDisplay(TargetRuleDefinition Rule, string Display);

    private sealed record PoseStepDisplay(PoseStepDefinition Step)
    {
        public string Display => $"{Step.Order:00} · {Step.Name}";
    }
}
