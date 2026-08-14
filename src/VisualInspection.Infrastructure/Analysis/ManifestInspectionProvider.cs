using System.Text.Json;
using System.Text.Json.Serialization;
using VisualInspection.Core.Analysis;
using VisualInspection.Core.Configuration;
using VisualInspection.Core.Imaging;

namespace VisualInspection.Infrastructure.Analysis;

/// <summary>
/// Deterministic acceptance adapter. It reads precomputed observations from
/// detections.json beside the source images; it is not a model inference runtime.
/// </summary>
public sealed class ManifestInspectionProvider : IInspectionProvider
{
    public const string ManifestFileName = "detections.json";

    private readonly IReadOnlyDictionary<string, ManifestFrame> _frames;
    private readonly IReadOnlyDictionary<string, TargetLookup> _targetsByName;

    private ManifestInspectionProvider(
        IReadOnlyDictionary<string, ManifestFrame> frames,
        IReadOnlyDictionary<string, TargetLookup> targetsByName)
    {
        _frames = frames;
        _targetsByName = targetsByName;
    }

    public string Name => "验收清单适配器";

    public static string GetManifestPath(string imageFolder) =>
        Path.Combine(Path.GetFullPath(imageFolder), ManifestFileName);

    public static bool IsAvailable(string imageFolder) =>
        Directory.Exists(imageFolder) && File.Exists(GetManifestPath(imageFolder));

    public static async Task<ManifestInspectionProvider> LoadAsync(
        string imageFolder,
        ProjectConfiguration project,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(project);
        var path = GetManifestPath(imageFolder);
        if (!File.Exists(path))
        {
            throw new FileNotFoundException(
                "未找到 detections.json 验收清单。请安装模型运行环境，或使用内置验收数据。",
                path);
        }

        await using var stream = File.OpenRead(path);
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        var manifest = await JsonSerializer.DeserializeAsync<InspectionManifest>(stream, options, cancellationToken)
            ?? throw new InvalidDataException($"验收清单为空：{path}");
        if (manifest.SchemaVersion != 1)
        {
            throw new NotSupportedException($"不支持验收清单架构版本 {manifest.SchemaVersion}。");
        }

        var frames = manifest.Frames
            .ToDictionary(frame => frame.FileName, StringComparer.OrdinalIgnoreCase);
        var targets = project.Targets.ToDictionary(
            target => target.Name,
            target => new TargetLookup(
                target.Id,
                target.ModelBindings.FirstOrDefault()?.Id
                    ?? throw new InvalidDataException($"目标“{target.Name}”没有模型绑定。")),
            StringComparer.OrdinalIgnoreCase);
        return new ManifestInspectionProvider(frames, targets);
    }

    public Task<FrameInspectionObservation> AnalyzeAsync(
        ImageFrame frame,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var fileName = Path.GetFileName(frame.Origin);
        if (string.IsNullOrWhiteSpace(fileName) || !_frames.TryGetValue(fileName, out var manifestFrame))
        {
            throw new InvalidDataException($"帧“{fileName ?? "未知"}”没有对应的验收观测数据。");
        }

        var counts = new Dictionary<Guid, int>();
        foreach (var pair in manifestFrame.TargetCounts)
        {
            if (!_targetsByName.TryGetValue(pair.Key, out var target))
            {
                throw new InvalidDataException($"验收清单引用了未知目标“{pair.Key}”。");
            }

            if (pair.Value < 0)
            {
                throw new InvalidDataException($"验收目标数量不能为负数：{pair.Key}。");
            }

            counts[target.TargetId] = pair.Value;
        }

        var detections = manifestFrame.Detections?.Select(detection =>
        {
            if (!_targetsByName.TryGetValue(detection.TargetName, out var target))
            {
                throw new InvalidDataException($"验收检测框引用了未知目标“{detection.TargetName}”。");
            }

            return new TargetDetection
            {
                TargetId = target.TargetId,
                ModelBindingId = target.ModelBindingId,
                X1 = detection.X1,
                Y1 = detection.Y1,
                X2 = detection.X2,
                Y2 = detection.Y2,
                Confidence = detection.Confidence
            };
        }).ToList();

        return Task.FromResult(new FrameInspectionObservation
        {
            TargetCounts = counts,
            Detections = detections,
            Actions = new HashSet<string>(manifestFrame.Actions, StringComparer.OrdinalIgnoreCase),
            ProviderDetails = detections is null
                ? $"{ManifestFileName}:{fileName}:预计算数量"
                : $"{ManifestFileName}:{fileName}:空间检测框"
        });
    }

    private sealed record InspectionManifest
    {
        public int SchemaVersion { get; init; }
        public List<ManifestFrame> Frames { get; init; } = [];
    }

    private sealed record ManifestFrame
    {
        public string FileName { get; init; } = string.Empty;
        public Dictionary<string, int> TargetCounts { get; init; } = new(StringComparer.OrdinalIgnoreCase);
        public List<ManifestDetection>? Detections { get; init; }
        public List<string> Actions { get; init; } = [];
    }

    private sealed record ManifestDetection
    {
        public string TargetName { get; init; } = string.Empty;
        public double X1 { get; init; }
        public double Y1 { get; init; }
        public double X2 { get; init; }
        public double Y2 { get; init; }
        public double Confidence { get; init; }
    }

    private sealed record TargetLookup(Guid TargetId, Guid ModelBindingId);
}
