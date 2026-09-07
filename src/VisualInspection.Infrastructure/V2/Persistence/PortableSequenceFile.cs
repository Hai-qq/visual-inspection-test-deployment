using System.Security.Cryptography;
using System.Text.Json;
using VisualInspection.Core.V2.Configuration;

namespace VisualInspection.Infrastructure.V2.Persistence;

public sealed record PortableSequenceExportResult(
    string SequencePath,
    IReadOnlyList<string> ModelPaths);

public static class PortableSequenceFile
{
    public const string FileSuffix = ".sequence.json";

    public static async Task<PortableSequenceExportResult> ExportAsync(
        ProjectConfigurationV2 project,
        string destinationPath,
        string? sourceBaseDirectory = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);
        if (project.TestSequenceVersions.Count != 1)
        {
            throw new InvalidDataException("每个 sequence 文件必须且只能包含一个型号。");
        }

        var schemaErrors = ProjectConfigurationV2Validator.Validate(project)
            .Where(issue => issue.Severity == V2ValidationSeverity.Error)
            .ToArray();
        if (schemaErrors.Length > 0)
        {
            throw new InvalidDataException(
                "sequence 配置校验失败：" +
                string.Join("；", schemaErrors.Select(issue => $"{issue.Code} {issue.Message}")));
        }

        var sequencePath = Path.GetFullPath(destinationPath);
        var destinationDirectory = Path.GetDirectoryName(sequencePath)!;
        var sourceRoot = Path.GetFullPath(sourceBaseDirectory ?? Directory.GetCurrentDirectory());
        Directory.CreateDirectory(destinationDirectory);

        var referencedModelIds = project.TestStepCatalog
            .SelectMany(step => step.ModelBindings)
            .Select(binding => binding.ModelArtifactId)
            .ToHashSet();
        var portableModels = new List<ModelArtifact>();
        var exportedModelPaths = new List<string>();
        var usedFileNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var model in project.ModelArtifacts.Where(model => referencedModelIds.Contains(model.ModelArtifactId)))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var configuredPath = model.FilePath ?? model.ArtifactUri;
            if (string.IsNullOrWhiteSpace(configuredPath))
            {
                throw new InvalidDataException($"模型“{model.Name}”没有文件路径。");
            }

            var sourcePath = Path.IsPathRooted(configuredPath)
                ? Path.GetFullPath(configuredPath)
                : Path.GetFullPath(Path.Combine(sourceRoot, configuredPath));
            if (!File.Exists(sourcePath))
            {
                throw new FileNotFoundException($"模型“{model.Name}”不存在。", sourcePath);
            }

            var fileName = Path.GetFileName(sourcePath);
            if (!usedFileNames.Add(fileName))
            {
                throw new InvalidDataException($"多个模型使用了相同文件名“{fileName}”，无法生成可移植交付物。");
            }

            var hash = await ComputeSha256Async(sourcePath, cancellationToken);
            if (!string.IsNullOrWhiteSpace(model.Sha256) &&
                !hash.Equals(model.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException($"模型“{model.Name}”的 SHA-256 与配置不一致。");
            }

            var modelDestination = Path.Combine(destinationDirectory, fileName);
            await CopyVerifiedAsync(sourcePath, modelDestination, hash, cancellationToken);
            exportedModelPaths.Add(modelDestination);
            portableModels.Add(model with
            {
                FilePath = fileName,
                ArtifactUri = null,
                Sha256 = hash
            });
        }

        if (portableModels.Count != referencedModelIds.Count)
        {
            throw new InvalidDataException("测试步引用了未导入的模型，不能导出 sequence。");
        }

        // Deployment bindings contain machine-local device or folder addresses. They are
        // deliberately excluded from the portable handoff; the operator loader resolves
        // folder input to the package-adjacent "input" directory and fails closed when it
        // is not ready.
        var portable = project with
        {
            ModelArtifacts = portableModels,
            DeploymentBindings = []
        };
        await WriteAtomicAsync(sequencePath, portable, cancellationToken);
        return new PortableSequenceExportResult(sequencePath, exportedModelPaths);
    }

    public static async Task<ProjectConfigurationV2> LoadAsync(
        string sequencePath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sequencePath);
        var absolutePath = Path.GetFullPath(sequencePath);
        if (!File.Exists(absolutePath))
        {
            throw new FileNotFoundException("sequence 文件不存在。", absolutePath);
        }

        ProjectConfigurationV2 project;
        try
        {
            await using var stream = new FileStream(
                absolutePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                64 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            project = await JsonSerializer.DeserializeAsync<ProjectConfigurationV2>(
                stream,
                V2Json.CreateOptions(),
                cancellationToken)
                ?? throw new InvalidDataException("sequence 文件内容为空。");
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("sequence 文件不是有效 JSON。", exception);
        }

        if (project.SchemaVersion != ConfigurationSchemaV2.CurrentVersion ||
            project.TestSequenceVersions.Count != 1)
        {
            throw new NotSupportedException(
                $"只支持包含一个型号的 schema v{ConfigurationSchemaV2.CurrentVersion} sequence。");
        }

        var schemaErrors = ProjectConfigurationV2Validator.Validate(project)
            .Where(issue => issue.Severity == V2ValidationSeverity.Error)
            .ToArray();
        if (schemaErrors.Length > 0)
        {
            throw new InvalidDataException(
                "sequence 配置校验失败：" +
                string.Join("；", schemaErrors.Select(issue => $"{issue.Code} {issue.Message}")));
        }

        var directory = Path.GetDirectoryName(absolutePath)!;
        var resolvedModels = new List<ModelArtifact>();
        foreach (var model in project.ModelArtifacts)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var configuredPath = model.FilePath ?? model.ArtifactUri;
            if (string.IsNullOrWhiteSpace(configuredPath))
            {
                throw new InvalidDataException($"模型“{model.Name}”没有文件路径。");
            }

            if (Path.IsPathRooted(configuredPath) ||
                !string.Equals(configuredPath, Path.GetFileName(configuredPath), StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    $"sequence 模型“{model.Name}”必须使用同目录文件名，不能引用绝对路径或上级目录。");
            }

            var modelPath = Path.GetFullPath(Path.Combine(directory, configuredPath));
            if (!File.Exists(modelPath))
            {
                throw new FileNotFoundException($"sequence 对应模型“{model.Name}”不存在。", modelPath);
            }

            var actualHash = await ComputeSha256Async(modelPath, cancellationToken);
            if (string.IsNullOrWhiteSpace(model.Sha256) ||
                !actualHash.Equals(model.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException($"模型“{model.Name}”的 SHA-256 校验失败。");
            }

            resolvedModels.Add(model with { FilePath = modelPath, ArtifactUri = null });
        }

        return project with { ModelArtifacts = resolvedModels };
    }

    private static async Task CopyVerifiedAsync(
        string sourcePath,
        string destinationPath,
        string expectedHash,
        CancellationToken cancellationToken)
    {
        if (Path.GetFullPath(sourcePath).Equals(Path.GetFullPath(destinationPath), StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (File.Exists(destinationPath))
        {
            var existingHash = await ComputeSha256Async(destinationPath, cancellationToken);
            if (existingHash.Equals(expectedHash, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            throw new IOException($"目标目录已有不同内容的模型文件：{destinationPath}");
        }

        var temporaryPath = $"{destinationPath}.{Guid.NewGuid():N}.tmp";
        try
        {
            await using (var source = new FileStream(
                             sourcePath,
                             FileMode.Open,
                             FileAccess.Read,
                             FileShare.Read,
                             1024 * 1024,
                             FileOptions.Asynchronous | FileOptions.SequentialScan))
            await using (var destination = new FileStream(
                             temporaryPath,
                             FileMode.CreateNew,
                             FileAccess.Write,
                             FileShare.None,
                             1024 * 1024,
                             FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                await source.CopyToAsync(destination, cancellationToken);
                await destination.FlushAsync(cancellationToken);
            }

            File.Move(temporaryPath, destinationPath);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private static async Task WriteAtomicAsync(
        string destinationPath,
        ProjectConfigurationV2 project,
        CancellationToken cancellationToken)
    {
        var temporaryPath = $"{destinationPath}.{Guid.NewGuid():N}.tmp";
        try
        {
            await using (var stream = new FileStream(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                64 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                await JsonSerializer.SerializeAsync(stream, project, V2Json.CreateOptions(), cancellationToken);
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

    private static async Task<string> ComputeSha256Async(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            1024 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken)).ToLowerInvariant();
    }
}
