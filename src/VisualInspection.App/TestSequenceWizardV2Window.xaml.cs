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

public partial class TestSequenceWizardV2Window : Window
{
    private readonly FrameworkElement[] _stepPanels;
    private int _furthestStepIndex;

    public TestSequenceWizardV2Window()
    {
        InitializeComponent();

        Steps =
        [
            new(0, "01", "项目信息", "填写项目、工位、测试序列和版本。"),
            new(1, "02", "选择图源", "选择图片文件夹、USB 摄像头或工业相机。"),
            new(2, "03", "导入模型", "导入 ONNX / PT 模型并确认标签来源。"),
            new(3, "04", "选择目标", "选择要检测的标签并绑定对应模型。"),
            new(4, "05", "设置区域", "为每个目标选择全图或手工框定 ROI。"),
            new(5, "06", "判定规则", "设置数量等于、数量范围或异常数量规则。"),
            new(6, "07", "姿态动作", "按顺序设置时序动作；普通项目可以跳过。", isOptional: true),
            new(7, "08", "运行参数", "设置毫秒延时、图源策略和发布方式。"),
            new(8, "09", "检查完成", "汇总检查全部设置，再进入保存与校验。")
        ];

        _stepPanels =
        [
            Step1Panel,
            Step2Panel,
            Step3Panel,
            Step4Panel,
            Step5Panel,
            Step6Panel,
            Step7Panel,
            Step8Panel,
            Step9Panel
        ];

        DataContext = this;
        NavigateTo(0);
    }

    public ObservableCollection<WizardStepItem> Steps { get; }

    public int CurrentStepIndex { get; private set; }

    public static string SnapshotPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "VisualInspectionTestDeployment",
        "v2-wizard-preview.png");

    public void SaveSnapshot()
    {
        UpdateLayout();
        var width = Math.Max(1, (int)Math.Ceiling(ActualWidth));
        var height = Math.Max(1, (int)Math.Ceiling(ActualHeight));
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(this);

        Directory.CreateDirectory(Path.GetDirectoryName(SnapshotPath)!);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(SnapshotPath);
        encoder.Save(stream);
    }

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
        if (CurrentStepIndex == Steps.Count - 1)
        {
            CompletePrototype();
            return;
        }

        _furthestStepIndex = Math.Max(_furthestStepIndex, CurrentStepIndex + 1);
        NavigateTo(CurrentStepIndex + 1);
    }

    private void SkipPose_Click(object sender, RoutedEventArgs e)
    {
        _furthestStepIndex = Math.Max(_furthestStepIndex, 7);
        NavigateTo(7);
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
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
        _furthestStepIndex = Math.Max(_furthestStepIndex, index);

        for (var panelIndex = 0; panelIndex < _stepPanels.Length; panelIndex++)
        {
            _stepPanels[panelIndex].Visibility = panelIndex == index ? Visibility.Visible : Visibility.Collapsed;
        }

        foreach (var step in Steps)
        {
            step.IsCurrent = step.Index == index;
            step.IsCompleted = step.Index < index || step.Index < _furthestStepIndex;
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
            : $"第 {index + 1} / {Steps.Count} 步 · 可点击顶部步骤返回检查。";
    }

    private void CompletePrototype()
    {
        _furthestStepIndex = Steps.Count;
        foreach (var step in Steps)
        {
            step.IsCompleted = true;
            step.IsCurrent = step.Index == CurrentStepIndex;
        }

        PrototypeStatusBorder.Visibility = Visibility.Visible;
        NextButton.Content = "界面流程已确认";
        NextButton.IsEnabled = false;
        FooterHintText.Text = "本次仅完成 V2 前端交互确认；保存、校验和模型业务尚未接入。";
    }

    public sealed class WizardStepItem : INotifyPropertyChanged
    {
        private bool _isCurrent;
        private bool _isCompleted;

        public WizardStepItem(
            int index,
            string number,
            string title,
            string description,
            bool isOptional = false)
        {
            Index = index;
            Number = number;
            Title = title;
            Description = description;
            IsOptional = isOptional;
        }

        public int Index { get; }

        public string Number { get; }

        public string Title { get; }

        public string Description { get; }

        public bool IsOptional { get; }

        public string Badge => IsOptional ? "可跳过" : "待设置";

        public string PageTitle => Index switch
        {
            0 => "填写项目信息",
            1 => "选择图片来源",
            2 => "导入模型与标签",
            3 => "选择检测目标",
            4 => "设置目标区域",
            5 => "设置判定规则",
            6 => "设置姿态动作",
            7 => "设置运行参数",
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
}
