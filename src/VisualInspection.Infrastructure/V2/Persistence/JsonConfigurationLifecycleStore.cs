using System.Text.Json;
using VisualInspection.Core.Configuration;
using VisualInspection.Core.V2.Configuration;

namespace VisualInspection.Infrastructure.V2.Persistence;

public sealed class JsonConfigurationLifecycleStore : IConfigurationLifecycleStore
{
    private readonly string _rootDirectory;
    private readonly JsonSerializerOptions _options = V2Json.CreateOptions();

    public JsonConfigurationLifecycleStore(string rootDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectory);
        _rootDirectory = Path.GetFullPath(rootDirectory);
    }

    public Task SaveDraftAsync(ConfigurationDraftV2 draft, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(draft);
        RequireId(draft.DraftId, nameof(draft.DraftId));
        return WriteAtomicAsync(GetDraftPath(draft.DraftId), draft, overwrite: true, cancellationToken);
    }

    public Task<ConfigurationDraftV2?> LoadDraftAsync(Guid draftId, CancellationToken cancellationToken = default)
    {
        RequireId(draftId, nameof(draftId));
        return ReadAsync<ConfigurationDraftV2>(GetDraftPath(draftId), cancellationToken);
    }

    public Task SavePublishedPackageAsync(
        PublishedConfigurationPackage package,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(package);
        RequireId(package.PackageId, nameof(package.PackageId));
        return WriteAtomicAsync(GetPackagePath(package.PackageId), package, overwrite: false, cancellationToken);
    }

    public Task<PublishedConfigurationPackage?> LoadPublishedPackageAsync(
        Guid packageId,
        CancellationToken cancellationToken = default)
    {
        RequireId(packageId, nameof(packageId));
        return ReadAsync<PublishedConfigurationPackage>(GetPackagePath(packageId), cancellationToken);
    }

    public Task SaveAssignmentAsync(DeploymentAssignment assignment, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(assignment);
        RequireId(assignment.DeploymentBindingId, nameof(assignment.DeploymentBindingId));
        RequireId(assignment.PackageId, nameof(assignment.PackageId));
        return WriteAtomicAsync(GetAssignmentPath(assignment.DeploymentBindingId), assignment, overwrite: true, cancellationToken);
    }

    public Task<DeploymentAssignment?> LoadAssignmentAsync(
        Guid deploymentBindingId,
        CancellationToken cancellationToken = default)
    {
        RequireId(deploymentBindingId, nameof(deploymentBindingId));
        return ReadAsync<DeploymentAssignment>(GetAssignmentPath(deploymentBindingId), cancellationToken);
    }

    public Task SaveActivePointerAsync(
        ActiveDeploymentPointer pointer,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(pointer);
        RequireId(pointer.DeploymentBindingId, nameof(pointer.DeploymentBindingId));
        RequireId(pointer.PackageId, nameof(pointer.PackageId));
        return WriteAtomicAsync(GetActivePath(pointer.DeploymentBindingId), pointer, overwrite: true, cancellationToken);
    }

    public Task<ActiveDeploymentPointer?> LoadActivePointerAsync(
        Guid deploymentBindingId,
        CancellationToken cancellationToken = default)
    {
        RequireId(deploymentBindingId, nameof(deploymentBindingId));
        return ReadAsync<ActiveDeploymentPointer>(GetActivePath(deploymentBindingId), cancellationToken);
    }

    public async Task<ProjectConfigurationV2> LoadPortableProjectAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var absolutePath = Path.GetFullPath(path);
        try
        {
            await using var stream = OpenRead(absolutePath);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            var root = document.RootElement;
            var schemaVersion = GetSchemaVersion(root);
            var projectElement = root.TryGetProperty("project", out var envelopeProject) ? envelopeProject : root;
            return schemaVersion switch
            {
                ConfigurationSchemaV2.CurrentVersion =>
                    projectElement.Deserialize<ProjectConfigurationV2>(_options)
                    ?? throw new InvalidDataException($"V2 项目配置为空：{absolutePath}"),
                ConfigurationSchema.CurrentVersion => ProjectConfigurationV1Migrator.Migrate(
                    projectElement.Deserialize<ProjectConfiguration>(_options)
                    ?? throw new InvalidDataException($"v1 项目配置为空：{absolutePath}")),
                _ => throw new NotSupportedException($"不支持项目配置 schema v{schemaVersion}。")
            };
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException($"项目配置不是有效 JSON：{absolutePath}", exception);
        }
    }

    private async Task WriteAtomicAsync<T>(
        string destinationPath,
        T value,
        bool overwrite,
        CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(destinationPath)!;
        Directory.CreateDirectory(directory);
        if (!overwrite && File.Exists(destinationPath))
        {
            throw new IOException($"不可变发布对象已存在，禁止覆盖：{destinationPath}");
        }

        var temporaryPath = Path.Combine(directory, $".{Path.GetFileName(destinationPath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            await using (var stream = new FileStream(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                16 * 1024,
                useAsync: true))
            {
                await JsonSerializer.SerializeAsync(stream, value, _options, cancellationToken);
                await stream.FlushAsync(cancellationToken);
            }

            File.Move(temporaryPath, destinationPath, overwrite);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private async Task<T?> ReadAsync<T>(string path, CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
        {
            return default;
        }

        try
        {
            await using var stream = OpenRead(path);
            return await JsonSerializer.DeserializeAsync<T>(stream, _options, cancellationToken)
                ?? throw new InvalidDataException($"JSON 对象为空：{path}");
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException($"JSON 文件无效：{path}", exception);
        }
    }

    private static FileStream OpenRead(string path) => new(
        path,
        FileMode.Open,
        FileAccess.Read,
        FileShare.Read,
        16 * 1024,
        useAsync: true);

    private static int GetSchemaVersion(JsonElement root)
    {
        if (root.TryGetProperty("schemaVersion", out var schema) && schema.TryGetInt32(out var version))
        {
            return version;
        }

        return ConfigurationSchema.CurrentVersion;
    }

    private string GetDraftPath(Guid id) => Path.Combine(_rootDirectory, "drafts", $"{id:N}.json");
    private string GetPackagePath(Guid id) => Path.Combine(_rootDirectory, "packages", $"{id:N}.json");
    private string GetAssignmentPath(Guid id) => Path.Combine(_rootDirectory, "assignments", $"{id:N}.json");
    private string GetActivePath(Guid id) => Path.Combine(_rootDirectory, "active", $"{id:N}.json");

    private static void RequireId(Guid value, string name)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("ID 不能为空。", name);
        }
    }
}
