using System.IO;
using VisualInspection.App.Demo;
using VisualInspection.Core.Configuration;
using VisualInspection.Core.Imaging;
using VisualInspection.Infrastructure.Analysis;
using VisualInspection.Infrastructure.Imaging;
using VisualInspection.Infrastructure.Persistence;

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
        var demoDirectory = await SampleDataSeeder.EnsureAsync(cancellationToken);
        IProjectConfigurationStore store = new JsonProjectConfigurationStore(ProjectStorageDirectory);
        var summaries = await store.ListAsync(cancellationToken);
        ProjectConfiguration project;

        if (summaries.Count == 0)
        {
            project = SampleProjectFactory.Create(demoDirectory);
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

            if (NeedsSampleLocalization(project))
            {
                project = LocalizeSampleProject(project);
                await store.SaveAsync(project, cancellationToken);
            }
        }

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
            demoDirectory,
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

    private static ProjectConfiguration LocalizeSampleProject(ProjectConfiguration project)
    {
        var models = project.Models.Select(model => model.Id.ToString("N") switch
        {
            "10000000000000000000000000000001" => model with
            {
                Name = "检测模型 A",
                Labels = model.Labels.Select(label => label with
                {
                    Name = label.Id switch { 0 => "螺钉", 1 => "标签", 2 => "瑕疵", _ => label.Name }
                }).ToList()
            },
            "10000000000000000000000000000002" => model with
            {
                Name = "动作姿态模型",
                Labels = model.Labels.Select(label => label with { Name = label.Id == 0 ? "操作员" : label.Name }).ToList()
            },
            _ => model
        }).ToList();
        var targets = project.Targets.Select(target => target.Id.ToString("N") switch
        {
            "20000000000000000000000000000001" => target with { Name = "螺钉" },
            "20000000000000000000000000000002" => target with { Name = "标签" },
            "20000000000000000000000000000003" => target with { Name = "表面瑕疵" },
            "20000000000000000000000000000004" => target with { Name = "操作员动作" },
            _ => target
        }).ToList();
        var sequences = project.TestSequences.Select(sequence => sequence with
        {
            Name = sequence.Id == Guid.Parse("50000000-0000-0000-0000-000000000001")
                ? "终检测试序列"
                : sequence.Name,
            Items = sequence.Items.Select(item => item with
            {
                Name = item.Order switch
                {
                    1 => "螺钉在位",
                    2 => "标签对齐",
                    3 => "表面瑕疵",
                    4 => "动作序列",
                    _ => item.Name
                },
                Rules = item.Rules.Select(rule => rule with
                {
                    Scope = rule.Scope with
                    {
                        Regions = rule.Scope.Regions.Select(region => region with
                        {
                            Name = region.Name == "ROI-A" ? "区域-A" : region.Name
                        }).ToList()
                    }
                }).ToList(),
                PoseSteps = item.PoseSteps.Select(step => step with
                {
                    Name = step.Order switch { 1 => "拿取", 2 => "放置", 3 => "确认", _ => step.Name }
                }).ToList()
            }).ToList()
        }).ToList();

        return project with
        {
            Name = "A 线 · 开关装配",
            Workstation = "工位 01",
            Models = models,
            Targets = targets,
            TestSequences = sequences,
            InputSources = project.InputSources.Select(source =>
                source.Name is "Sample Set 01" or "Built-in Acceptance Set"
                    ? source with { Name = "内置验收数据" }
                    : source).ToList()
        };
    }

    private static bool NeedsSampleLocalization(ProjectConfiguration project)
        => project.Id == SampleProjectFactory.SampleProjectId && project.Name != "A 线 · 开关装配";
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
