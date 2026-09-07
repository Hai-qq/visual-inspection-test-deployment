using System.IO;
using VisualInspection.App.Demo;
using VisualInspection.Core.Configuration;
using VisualInspection.Core.Imaging;
using VisualInspection.Core.Rules;
using VisualInspection.Core.V2.Configuration;
using VisualInspection.Infrastructure.Analysis;
using VisualInspection.Infrastructure.Imaging;
using VisualInspection.Infrastructure.Persistence;
using VisualInspection.Infrastructure.V2.Persistence;

namespace VisualInspection.App.Services;

public static class ApplicationBootstrapper
{
    public static string ProjectStorageDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "VisualInspectionTestDeployment",
        "projects");

    // Normal startup never reads, creates or restores a saved sequence.
    public static Task<ApplicationBootstrapResult> CreateUnloadedAsync(
        CancellationToken cancellationToken = default,
        string? storageDirectory = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IProjectConfigurationStore store = new JsonProjectConfigurationStore(storageDirectory ?? ProjectStorageDirectory);
        return Task.FromResult(new ApplicationBootstrapResult(
            new ProjectConfiguration(), false, "尚未加载图源", false, "请先导入测试序列。",
            store, string.Empty, null));
    }

    // Explicit acceptance fixtures only; never read or overwrite the operator's saved projects.
    internal static async Task<ApplicationBootstrapResult> LoadAcceptanceProjectAsync(
        CancellationToken cancellationToken = default)
    {
        var bundledFan = await FrontendDemoAssetSeeder.EnsureAsync(cancellationToken);
        var demoDirectory = bundledFan?.ImageDirectory ?? await SampleDataSeeder.EnsureAsync(cancellationToken);
        var project = SampleProjectFactory.Create(demoDirectory, bundledFan?.ModelPath);
        IProjectConfigurationStore store = new JsonProjectConfigurationStore(ProjectStorageDirectory);
        return await CreateBootstrapResultAsync(project, store, demoDirectory, cancellationToken);
    }
    public static async Task<ApplicationBootstrapResult> LoadPortableSequenceAsync(
        string sequencePath,
        CancellationToken cancellationToken = default,
        bool persist = false,
        string? storageDirectory = null)
    {
        var absolutePath = Path.GetFullPath(sequencePath);
        var portable = await PortableSequenceFile.LoadAsync(absolutePath, cancellationToken);
        var project = ProjectConfigurationV2CompatibilityConverter.ToV1(
            portable,
            Path.GetDirectoryName(absolutePath)!);
        IProjectConfigurationStore store = new JsonProjectConfigurationStore(storageDirectory ?? ProjectStorageDirectory);
        var source = project.InputSources.Single();
        var dataDirectory = source.Folder?.FolderPath ?? Path.GetDirectoryName(absolutePath)!;
        var result = await CreateBootstrapResultAsync(project, store, dataDirectory, cancellationToken);
        if (persist) await store.SaveAsync(project, cancellationToken);
        return result;
    }

    public static async Task<ApplicationBootstrapResult> LoadConfiguredSequenceAsync(
        ProjectConfigurationV2 configuredSequence,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(configuredSequence);
        var project = ProjectConfigurationV2CompatibilityConverter.ToV1(
            configuredSequence,
            AppContext.BaseDirectory);
        IProjectConfigurationStore store = new JsonProjectConfigurationStore(ProjectStorageDirectory);
        var source = project.InputSources.Single();
        var dataDirectory = source.Folder?.FolderPath ?? AppContext.BaseDirectory;
        var result = await CreateBootstrapResultAsync(project, store, dataDirectory, cancellationToken);
        await store.SaveAsync(project, cancellationToken);
        return result;
    }

    private static async Task<ApplicationBootstrapResult> CreateBootstrapResultAsync(
        ProjectConfiguration project,
        IProjectConfigurationStore store,
        string dataDirectory,
        CancellationToken cancellationToken)
    {
        var issues = ProjectConfigurationValidator.Validate(project);
        var errors = issues.Where(issue => issue.Severity == ConfigurationValidationSeverity.Error).ToArray();
        if (errors.Length > 0)
        {
            var details = string.Join(Environment.NewLine, errors.Select(error => $"{error.Code}: {error.Message}"));
            throw new InvalidDataException($"项目配置校验失败：{Environment.NewLine}{details}");
        }

        var activeSequence = project.TestSequences
            .OrderByDescending(sequence => sequence.IsPublished)
            .First();
        var inputSource = project.InputSources.First(source => source.Id == activeSequence.InputSourceId);
        var probe = await ProbeInputSourceAsync(inputSource, cancellationToken);

        var resolvedFolder = inputSource.Type == InputSourceType.Folder && inputSource.Folder is not null
            ? ResolveFolderPath(inputSource.Folder.FolderPath)
            : null;
        var manifestReady = resolvedFolder is not null && ManifestInspectionProvider.IsAvailable(resolvedFolder);
        var onnxProbe = OnnxYoloInspectionProvider.Probe(project, activeSequence, AppContext.BaseDirectory);
        var runtimeReady = onnxProbe.IsReady || manifestReady;
        var runtimeStatus = onnxProbe.IsReady
            ? onnxProbe.Status
            : manifestReady
                ? $"验收清单适配器已就绪（确定性数据，并非 PT/ONNX 推理） · {onnxProbe.Status}"
                : $"{onnxProbe.Status} · 未找到 detections.json 验收清单";

        return new ApplicationBootstrapResult(
            project,
            probe.IsReady,
            probe.Status,
            probe.IsReady && runtimeReady,
            runtimeStatus,
            store,
            dataDirectory,
            probe.PreviewFrame);
    }

    public static async Task<InputSourceProbeResult> ProbeInputSourceAsync(
        InputSourceDefinition definition,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var source = ImageSourceFactory.Create(definition, AppContext.BaseDirectory);
            await source.OpenAsync(cancellationToken);
            var frame = await source.ReadAsync(cancellationToken);
            if (frame is null)
            {
                return new InputSourceProbeResult(false, $"{definition.Name}：没有可读取的图像", null);
            }

            var skipped = source.Progress.FailedCount > 0
                ? $" · 已跳过 {source.Progress.FailedCount} 个文件"
                : string.Empty;
            return new InputSourceProbeResult(
                true,
                $"{definition.Name}：就绪 · {source.Progress.TotalCount} 个文件 · {frame.Width} × {frame.Height}{skipped}",
                frame);
        }
        catch (Exception exception) when (exception is ImageSourceException or NotSupportedException or ArgumentException)
        {
            return new InputSourceProbeResult(false, $"{definition.Name}：{exception.Message}", null);
        }
    }

    public static string ResolveFolderPath(string configuredPath)
    {
        var expanded = Environment.ExpandEnvironmentVariables(configuredPath);
        return Path.IsPathRooted(expanded)
            ? Path.GetFullPath(expanded)
            : Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, expanded));
    }

}

public sealed record InputSourceProbeResult(bool IsReady, string Status, ImageFrame? PreviewFrame);

public sealed record ApplicationBootstrapResult(
    ProjectConfiguration Project,
    bool IsInputSourceReady,
    string InputSourceStatus,
    bool IsRuntimeReady,
    string RuntimeStatus,
    IProjectConfigurationStore Store,
    string DemoDataDirectory,
    ImageFrame? PreviewFrame);
