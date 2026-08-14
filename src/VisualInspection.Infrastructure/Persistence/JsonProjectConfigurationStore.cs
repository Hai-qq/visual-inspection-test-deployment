using System.Text.Json;
using System.Text.Json.Serialization;
using VisualInspection.Core.Configuration;

namespace VisualInspection.Infrastructure.Persistence;

public sealed class JsonProjectConfigurationStore : IProjectConfigurationStore
{
    private readonly string _rootDirectory;
    private readonly JsonSerializerOptions _serializerOptions;

    public JsonProjectConfigurationStore(string rootDirectory)
    {
        if (string.IsNullOrWhiteSpace(rootDirectory))
        {
            throw new ArgumentException("必须提供项目存储目录。", nameof(rootDirectory));
        }

        _rootDirectory = Path.GetFullPath(rootDirectory);
        _serializerOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            WriteIndented = true
        };
        _serializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
    }

    public async Task SaveAsync(
        ProjectConfiguration configuration,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        if (configuration.Id == Guid.Empty)
        {
            throw new ArgumentException("项目标识不能为空。", nameof(configuration));
        }

        Directory.CreateDirectory(_rootDirectory);
        var destinationPath = GetProjectPath(configuration.Id);
        var temporaryPath = Path.Combine(
            _rootDirectory,
            $".{configuration.Id:N}.{Guid.NewGuid():N}.tmp");
        var envelope = new ProjectConfigurationEnvelope
        {
            SchemaVersion = ConfigurationSchema.CurrentVersion,
            SavedAtUtc = DateTimeOffset.UtcNow,
            Project = configuration
        };

        try
        {
            await using (var stream = new FileStream(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 16 * 1024,
                useAsync: true))
            {
                await JsonSerializer.SerializeAsync(stream, envelope, _serializerOptions, cancellationToken);
                await stream.FlushAsync(cancellationToken);
            }

            File.Move(temporaryPath, destinationPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    public async Task<ProjectConfiguration?> LoadAsync(
        Guid projectId,
        CancellationToken cancellationToken = default)
    {
        if (projectId == Guid.Empty)
        {
            throw new ArgumentException("项目标识不能为空。", nameof(projectId));
        }

        var path = GetProjectPath(projectId);
        if (!File.Exists(path))
        {
            return null;
        }

        return (await ReadEnvelopeAsync(path, cancellationToken)).Project;
    }

    public async Task<IReadOnlyList<ProjectConfigurationSummary>> ListAsync(
        CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(_rootDirectory))
        {
            return [];
        }

        var summaries = new List<ProjectConfigurationSummary>();
        foreach (var path in Directory.EnumerateFiles(_rootDirectory, "*.json").OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var envelope = await ReadEnvelopeAsync(path, cancellationToken);
            summaries.Add(new ProjectConfigurationSummary(
                envelope.Project.Id,
                envelope.Project.Name,
                envelope.SchemaVersion,
                envelope.SavedAtUtc));
        }

        return summaries.OrderBy(summary => summary.ProjectName, StringComparer.CurrentCultureIgnoreCase).ToArray();
    }

    private string GetProjectPath(Guid projectId) => Path.Combine(_rootDirectory, $"{projectId:N}.json");

    private async Task<ProjectConfigurationEnvelope> ReadEnvelopeAsync(
        string path,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 16 * 1024,
                useAsync: true);
            var envelope = await JsonSerializer.DeserializeAsync<ProjectConfigurationEnvelope>(
                stream,
                _serializerOptions,
                cancellationToken);
            if (envelope?.Project is null)
            {
                throw new InvalidDataException($"项目配置为空：{path}");
            }

            if (envelope.SchemaVersion != ConfigurationSchema.CurrentVersion)
            {
                throw new NotSupportedException(
                    $"不支持项目架构版本 {envelope.SchemaVersion}，当前需要版本 {ConfigurationSchema.CurrentVersion}。");
            }

            return envelope;
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException($"项目配置不是有效的 JSON 文件：{path}", exception);
        }
    }

    private sealed record ProjectConfigurationEnvelope
    {
        public int SchemaVersion { get; init; }
        public DateTimeOffset SavedAtUtc { get; init; }
        public ProjectConfiguration Project { get; init; } = new();
    }
}
