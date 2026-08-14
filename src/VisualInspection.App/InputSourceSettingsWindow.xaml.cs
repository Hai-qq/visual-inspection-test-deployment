using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using VisualInspection.App.Services;
using VisualInspection.Core.Configuration;
using VisualInspection.Core.Imaging;
using VisualInspection.Infrastructure.Analysis;
using VisualInspection.Infrastructure.Imaging;

namespace VisualInspection.App;

public partial class InputSourceSettingsWindow : Window
{
    private readonly ProjectConfiguration _project;
    private readonly IProjectConfigurationStore _store;
    private readonly string _demoDataDirectory;
    private readonly TestSequenceDefinition _sequence;
    private readonly InputSourceDefinition _source;

    public InputSourceSettingsWindow(
        ProjectConfiguration project,
        IProjectConfigurationStore store,
        string demoDataDirectory)
    {
        InitializeComponent();
        _project = project;
        _store = store;
        _demoDataDirectory = demoDataDirectory;
        _sequence = project.TestSequences.OrderByDescending(item => item.IsPublished).First();
        _source = project.InputSources.First(item => item.Id == _sequence.InputSourceId);

        SortOrderCombo.ItemsSource = new[]
        {
            new LocalizedOption<FolderSortOrder>(FolderSortOrder.NaturalFileName, "按文件名自然排序"),
            new LocalizedOption<FolderSortOrder>(FolderSortOrder.LastWriteTime, "按最后修改时间排序")
        };
        InvalidBehaviorCombo.ItemsSource = new[]
        {
            new LocalizedOption<InvalidFileBehavior>(InvalidFileBehavior.Skip, "跳过并记录"),
            new LocalizedOption<InvalidFileBehavior>(InvalidFileBehavior.Stop, "停止测试")
        };
        CameraTypeCombo.ItemsSource = new[]
        {
            new LocalizedOption<InputSourceType>(InputSourceType.DirectShowCamera, "USB 相机（DirectShow）"),
            new LocalizedOption<InputSourceType>(InputSourceType.VendorCamera, "工业相机（厂商适配器）")
        };
        LoadValues();
    }

    private void LoadValues()
    {
        var folder = _source.Folder ?? new FolderInputOptions
        {
            FolderPath = _demoDataDirectory,
            PoseFrameIntervalMs = 100
        };
        FolderPathBox.Text = folder.FolderPath;
        IncludeSubfoldersCheck.IsChecked = folder.IncludeSubfolders;
        LoopPlaybackCheck.IsChecked = folder.LoopPlayback;
        SelectOption<FolderSortOrder>(SortOrderCombo, folder.SortOrder);
        SelectOption<InvalidFileBehavior>(InvalidBehaviorCombo, folder.InvalidFileBehavior);
        PoseIntervalBox.Text = folder.PoseFrameIntervalMs.ToString();

        var camera = _source.Camera;
        SelectOption(
            CameraTypeCombo,
            _source.Type == InputSourceType.VendorCamera
                ? InputSourceType.VendorCamera
                : InputSourceType.DirectShowCamera);
        if (camera is not null)
        {
            AdapterIdBox.Text = camera.AdapterId;
            DeviceIdBox.Text = camera.DeviceId;
            CameraWidthBox.Text = camera.Width.ToString();
            CameraHeightBox.Text = camera.Height.ToString();
            FrameRateBox.Text = camera.FrameRate.ToString("0.##");
            GrabTimeoutBox.Text = camera.GrabTimeoutMs.ToString();
        }

        SourceTabs.SelectedIndex = _source.Type == InputSourceType.Folder ? 0 : 1;
    }

    private void BrowseFolder_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "选择图像源文件夹",
            Multiselect = false
        };
        if (Directory.Exists(FolderPathBox.Text))
        {
            dialog.InitialDirectory = FolderPathBox.Text;
        }

        if (dialog.ShowDialog(this) == true)
        {
            FolderPathBox.Text = dialog.FolderName;
            ValidationText.Text = "文件夹已更改，请在保存前重新校验。";
            PreviewImage.Source = null;
            PreviewPlaceholder.Visibility = Visibility.Visible;
        }
    }

    private void UseBuiltIn_Click(object sender, RoutedEventArgs e)
    {
        SelectBuiltInDirectory(_demoDataDirectory, "通过");
    }

    private void UseBuiltInFail_Click(object sender, RoutedEventArgs e)
    {
        SelectBuiltInDirectory(Demo.SampleDataSeeder.FailureDirectory, "不通过");
    }

    private void SelectBuiltInDirectory(string directory, string scenario)
    {
        FolderPathBox.Text = directory;
        IncludeSubfoldersCheck.IsChecked = false;
        LoopPlaybackCheck.IsChecked = false;
        SelectOption<FolderSortOrder>(SortOrderCombo, FolderSortOrder.NaturalFileName);
        SelectOption<InvalidFileBehavior>(InvalidBehaviorCombo, InvalidFileBehavior.Skip);
        PoseIntervalBox.Text = "100";
        ValidationText.Text = $"已选择内置{scenario}验收数据，请校验后预览。";
    }

    private async void ValidateFolder_Click(object sender, RoutedEventArgs e)
    {
        await ValidateFolderAsync(showDialogOnFailure: false);
    }

    private async Task<bool> ValidateFolderAsync(bool showDialogOnFailure)
    {
        try
        {
            var source = BuildFolderSource();
            await using var imageSource = ImageSourceFactory.Create(source, AppContext.BaseDirectory);
            await imageSource.OpenAsync();
            var frame = await imageSource.ReadAsync();
            if (frame is null)
            {
                throw new InvalidDataException("所选文件夹中没有可读取的 JPG、JPEG、PNG 或 BMP 图像。");
            }

            PreviewImage.Source = CreatePreview(frame.Data);
            PreviewPlaceholder.Visibility = Visibility.Collapsed;
            var folderPath = ApplicationBootstrapper.ResolveFolderPath(source.Folder!.FolderPath);
            var sequence = _project.TestSequences
                .OrderByDescending(candidate => candidate.IsPublished)
                .First();
            var onnxProbe = OnnxYoloInspectionProvider.Probe(_project, sequence, AppContext.BaseDirectory);
            var manifestState = onnxProbe.IsReady
                ? "真实 ONNX Runtime 已就绪 · 可以执行模型推理"
                : ManifestInspectionProvider.IsAvailable(folderPath)
                    ? "已找到 detections.json · 验收模式下可以开始测试"
                    : $"未找到 detections.json · {onnxProbe.Status}";
            ValidationText.Text =
                $"校验通过 · {imageSource.Progress.TotalCount} 个文件 · 首帧 {frame.Width} × {frame.Height} · {manifestState}";
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
                                           ArgumentException or InvalidDataException or ImageSourceException)
        {
            ValidationText.Text = $"校验失败 · {exception.Message}";
            PreviewImage.Source = null;
            PreviewPlaceholder.Visibility = Visibility.Visible;
            if (showDialogOnFailure)
            {
                MessageBox.Show(this, exception.Message, "图源校验失败", MessageBoxButton.OK, MessageBoxImage.Warning);
            }

            return false;
        }
    }

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            InputSourceDefinition updatedSource;
            if (SourceTabs.SelectedIndex == 0)
            {
                if (!await ValidateFolderAsync(showDialogOnFailure: true))
                {
                    return;
                }

                updatedSource = BuildFolderSource();
            }
            else
            {
                updatedSource = BuildCameraSource();
            }

            var updatedProject = _project with
            {
                InputSources = _project.InputSources
                    .Select(source => source.Id == _source.Id ? updatedSource : source)
                    .ToList()
            };
            var errors = ProjectConfigurationValidator.Validate(updatedProject)
                .Where(issue => issue.Severity == ConfigurationValidationSeverity.Error)
                .ToArray();
            if (errors.Length > 0)
            {
                throw new InvalidDataException(string.Join(Environment.NewLine, errors.Select(issue => issue.Message)));
            }

            await _store.SaveAsync(updatedProject);
            DialogResult = true;
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidDataException or IOException)
        {
            MessageBox.Show(this, exception.Message, "无法保存图源", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private InputSourceDefinition BuildFolderSource()
    {
        if (string.IsNullOrWhiteSpace(FolderPathBox.Text))
        {
            throw new ArgumentException("必须选择图像文件夹。");
        }

        if (!int.TryParse(PoseIntervalBox.Text, out var interval) || interval <= 0)
        {
            throw new ArgumentException("姿态帧间隔必须是正整数。");
        }

        return _source with
        {
            Type = InputSourceType.Folder,
            Name = Path.GetFileName(Path.TrimEndingDirectorySeparator(FolderPathBox.Text)) is { Length: > 0 } name
                ? name
                : "文件夹图源",
            Folder = new FolderInputOptions
            {
                FolderPath = FolderPathBox.Text.Trim(),
                IncludeSubfolders = IncludeSubfoldersCheck.IsChecked == true,
                SortOrder = SortOrderCombo.SelectedItem is LocalizedOption<FolderSortOrder> sort
                    ? sort.Value
                    : FolderSortOrder.NaturalFileName,
                InvalidFileBehavior = InvalidBehaviorCombo.SelectedItem is LocalizedOption<InvalidFileBehavior> behavior
                    ? behavior.Value
                    : InvalidFileBehavior.Skip,
                LoopPlayback = LoopPlaybackCheck.IsChecked == true,
                PoseFrameIntervalMs = interval
            },
            Camera = null
        };
    }

    private InputSourceDefinition BuildCameraSource()
    {
        if (!int.TryParse(CameraWidthBox.Text, out var width) || width <= 0 ||
            !int.TryParse(CameraHeightBox.Text, out var height) || height <= 0 ||
            !double.TryParse(FrameRateBox.Text, out var frameRate) || frameRate <= 0 ||
            !int.TryParse(GrabTimeoutBox.Text, out var timeout) || timeout <= 0)
        {
            throw new ArgumentException("相机宽度、高度、帧率和超时时间必须为正数。");
        }

        if (string.IsNullOrWhiteSpace(AdapterIdBox.Text) || string.IsNullOrWhiteSpace(DeviceIdBox.Text))
        {
            throw new ArgumentException("必须填写相机适配器标识和设备标识。");
        }

        var type = CameraTypeCombo.SelectedItem is LocalizedOption<InputSourceType> selected
            ? selected.Value
            : InputSourceType.DirectShowCamera;
        return _source with
        {
            Type = type,
            Name = type == InputSourceType.DirectShowCamera ? "USB 相机" : "工业相机",
            Folder = null,
            Camera = new CameraInputOptions
            {
                AdapterId = AdapterIdBox.Text.Trim(),
                DeviceId = DeviceIdBox.Text.Trim(),
                Width = width,
                Height = height,
                FrameRate = frameRate,
                PixelFormat = "BGR24",
                TriggerMode = "Continuous",
                GrabTimeoutMs = timeout
            }
        };
    }

    private static BitmapImage CreatePreview(ReadOnlyMemory<byte> data)
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

    private static void SelectOption<T>(ComboBox comboBox, T value)
        where T : struct, Enum
    {
        comboBox.SelectedItem = comboBox.ItemsSource
            .Cast<LocalizedOption<T>>()
            .First(option => EqualityComparer<T>.Default.Equals(option.Value, value));
    }

    private sealed record LocalizedOption<T>(T Value, string Display)
        where T : struct, Enum
    {
        public override string ToString() => Display;
    }
}
