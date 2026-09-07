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

    public static async Task<ApplicationBootstrapResult> LoadOrCreateProjectAsync(
        CancellationToken cancellationToken = default)
    {
        var bundledFan = await FrontendDemoAssetSeeder.EnsureAsync(cancellationToken);
        var demoDirectory = bundledFan?.ImageDirectory ?? await SampleDataSeeder.EnsureAsync(cancellationToken);
        var modelPath = bundledFan?.ModelPath;
        IProjectConfigurationStore store = new JsonProjectConfigurationStore(ProjectStorageDirectory);
        var summaries = await store.ListAsync(cancellationToken);
        ProjectConfiguration project;

        if (summaries.Count == 0)
        {
            project = SampleProjectFactory.Create(demoDirectory, modelPath);
            await store.SaveAsync(project, cancellationToken);
        }
        else
        {
            project = await store.LoadAsync(summaries[0].ProjectId, cancellationToken)
                ?? throw new InvalidDataException("所选项目配置已不存在。");

            if (ShouldRestoreBuiltInSource(project))
            {
                project = ReplaceActiveFolder(project, demoDirectory);
                await store.SaveAsync(project, cancellationToken);
            }

            if (NeedsBuiltInFanRefresh(project, demoDirectory, modelPath))
            {
                project = SampleProjectFactory.Create(demoDirectory, modelPath);
                await store.SaveAsync(project, cancellationToken);
            }
        }

        return await CreateBootstrapResultAsync(project, store, demoDirectory, cancellationToken);
    }

    public static async Task<ApplicationBootstrapResult> LoadPortableSequenceAsync(
        string sequencePath,
        CancellationToken cancellationToken = default)
    {
        var absolutePath = Path.GetFullPath(sequencePath);
        var portable = await PortableSequenceFile.LoadAsync(absolutePath, cancellationToken);
        var project = ProjectConfigurationV2CompatibilityConverter.ToV1(
            portable,
            Path.GetDirectoryName(absolutePath)!);
        IProjectConfigurationStore store = new JsonProjectConfigurationStore(ProjectStorageDirectory);
        var source = project.InputSources.Single();
        var dataDirectory = source.Folder?.FolderPath ?? Path.GetDirectoryName(absolutePath)!;
        return await CreateBootstrapResultAsync(project, store, dataDirectory, cancellationToken);
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

    private static bool ShouldRestoreBuiltInSource(ProjectConfiguration project)
    {
        if (project.Id != SampleProjectFactory.SampleProjectId)
        {
            return false;
        }

        var sequence = project.TestSequences.OrderByDescending(item => item.IsPublished).FirstOrDefault();
        var source = sequence is null
            ? null
            : project.InputSources.FirstOrDefault(item => item.Id == sequence.InputSourceId);
        return source?.Type == InputSourceType.Folder &&
            source.Folder is not null &&
            !Directory.Exists(ResolveFolderPath(source.Folder.FolderPath));
    }

    private static ProjectConfiguration ReplaceActiveFolder(ProjectConfiguration project, string demoDirectory)
    {
        var sequence = project.TestSequences.OrderByDescending(item => item.IsPublished).First();
        var sources = project.InputSources.Select(source =>
            source.Id != sequence.InputSourceId
                ? source
                : source with
                {
                    Name = "内置验收数据",
                    Type = InputSourceType.Folder,
                    Folder = (source.Folder ?? new FolderInputOptions()) with
                    {
                        FolderPath = demoDirectory,
                        IncludeSubfolders = false,
                        SortOrder = FolderSortOrder.NaturalFileName,
                        InvalidFileBehavior = InvalidFileBehavior.Skip,
                        LoopPlayback = false,
                        PoseFrameIntervalMs = 100
                    },
                    Camera = null
                }).ToList();
        return project with { InputSources = sources };
    }

    private static bool NeedsBuiltInFanRefresh(
        ProjectConfiguration project,
        string demoDirectory,
        string? modelPath)
    {
        if (project.Id != SampleProjectFactory.SampleProjectId)
        {
            return false;
        }

        var sequence = project.TestSequences.FirstOrDefault();
        var source = sequence is null
            ? null
            : project.InputSources.FirstOrDefault(item => item.Id == sequence.InputSourceId);
        var fanModel = project.Models.FirstOrDefault(item => item.Name == SampleProjectFactory.SampleModelName);
        var fanItem = sequence?.Items.Count == 1 ? sequence.Items[0] : null;
        return project.Name != SampleProjectFactory.SampleProjectName ||
            sequence?.Name != SampleProjectFactory.SampleProductModel ||
            fanItem?.Name != "风扇检测" ||
            fanItem?.RuleOperator != RuleLogicalOperator.And ||
            fanItem?.Rules.Count != 6 ||
            source?.Folder?.FolderPath != demoDirectory ||
            fanModel?.FilePath != (modelPath ?? "models/fan.onnx") ||
            fanModel?.Sha256 != (modelPath is null ? null : SampleProjectFactory.BundledFanModelSha256) ||
            sequence?.IsPublished != (modelPath is not null);
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
