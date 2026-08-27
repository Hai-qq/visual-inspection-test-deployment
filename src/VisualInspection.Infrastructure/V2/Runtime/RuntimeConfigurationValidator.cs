using System.Security.Cryptography;
using VisualInspection.Core.V2.Configuration;
using VisualInspection.Core.V2.Execution;

namespace VisualInspection.Infrastructure.V2.Runtime;

public sealed class RuntimeConfigurationValidator(
    IModelRuntimeAdapterRegistry modelAdapters,
    ICameraAdapterRegistry cameraAdapters,
    ITriggerAdapterRegistry triggerAdapters,
    ILineResultAdapterRegistry lineAdapters) : IRuntimeConfigurationValidator
{
    private readonly IModelRuntimeAdapterRegistry _modelAdapters =
        modelAdapters ?? throw new ArgumentNullException(nameof(modelAdapters));
    private readonly ICameraAdapterRegistry _cameraAdapters =
        cameraAdapters ?? throw new ArgumentNullException(nameof(cameraAdapters));
    private readonly ITriggerAdapterRegistry _triggerAdapters =
        triggerAdapters ?? throw new ArgumentNullException(nameof(triggerAdapters));
    private readonly ILineResultAdapterRegistry _lineAdapters =
        lineAdapters ?? throw new ArgumentNullException(nameof(lineAdapters));

    public async Task<IReadOnlyList<V2ValidationIssue>> ValidateAsync(
        ProjectConfigurationV2 project,
        TestSequenceVersion sequence,
        RuntimeValidationContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(sequence);
        ArgumentNullException.ThrowIfNull(context);
        var issues = new List<V2ValidationIssue>();
        var deployment = context.DeploymentBindingId is { } deploymentId
            ? project.DeploymentBindings.FirstOrDefault(binding => binding.DeploymentBindingId == deploymentId)
            : null;
        if (deployment is null)
        {
            Error(issues, "V2-RUNTIME-DEPLOYMENT", "deploymentBindingId", "Runtime Validation 需要存在的 Deployment Binding。");
            return issues;
        }

        var baseDirectory = string.IsNullOrWhiteSpace(context.BaseDirectory)
            ? Directory.GetCurrentDirectory()
            : Path.GetFullPath(context.BaseDirectory);
        var steps = project.TestStepCatalog.ToDictionary(step => step.StepId);
        var artifacts = project.ModelArtifacts.ToDictionary(artifact => artifact.ModelArtifactId);
        var profiles = project.RuntimeProfiles.ToDictionary(profile => profile.RuntimeProfileId);
        var usedSteps = sequence.OrderedInvocations
            .Select(invocation => steps.GetValueOrDefault(invocation.StepId))
            .Where(step => step is not null)
            .Cast<TestStepDefinition>()
            .DistinctBy(step => step.StepId)
            .ToArray();

        if (context.EnvironmentMode == RuntimeEnvironmentMode.Production &&
            usedSteps.Any(step => step.DefaultFrameInputPolicy == FrameInputPolicy.OperatorDebugSelection))
        {
            Error(issues, "V2-PROD-DEBUG-SOURCE", "testStepCatalog", "Production 禁止“调试时选择图源”。");
        }

        foreach (var binding in usedSteps.SelectMany(step => step.ModelBindings).DistinctBy(binding => binding.ModelBindingId))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var path = $"modelBindings[{binding.ModelBindingId}]";
            if (!artifacts.TryGetValue(binding.ModelArtifactId, out var artifact) ||
                !profiles.TryGetValue(binding.AdapterProfileId, out var profile))
            {
                Error(issues, "V2-RUNTIME-MODEL-REF", path, "模型或 Runtime Profile 引用不完整。");
                continue;
            }

            if (!_modelAdapters.TryGet(binding.AdapterId, out var adapter))
            {
                Error(issues, "V2-RUNTIME-ADAPTER-MISSING", $"{path}.adapterId", $"Model Adapter“{binding.AdapterId}”未注册。");
                continue;
            }

            if (adapter.TaskType != artifact.TaskType)
            {
                Error(issues, "V2-RUNTIME-ADAPTER-CONTRACT", path, "Model Adapter TaskType 与 Artifact Contract 不一致。");
            }

            ValidateProductionAdapterPolicy(
                context.EnvironmentMode,
                deployment,
                adapter.AdapterId,
                adapter.ProviderKind == RuntimeProviderKind.OnnxYoloEndToEnd,
                path,
                issues);

            var artifactPath = ResolveArtifactPath(artifact, baseDirectory, issues, path);
            if (artifactPath is not null)
            {
                if (!File.Exists(artifactPath))
                {
                    Error(issues, "V2-RUNTIME-MODEL-FILE", $"{path}.filePath", $"模型文件不存在：{artifactPath}");
                }
                else
                {
                    var actualHash = await ComputeSha256Async(artifactPath, cancellationToken);
                    if (!string.Equals(actualHash, artifact.Sha256, StringComparison.OrdinalIgnoreCase))
                    {
                        Error(issues, "V2-RUNTIME-MODEL-HASH", $"{path}.sha256", "模型文件 SHA-256 与配置不一致。");
                    }
                }
            }

            try
            {
                var probe = await adapter.ProbeAsync(artifact, profile, baseDirectory, cancellationToken);
                if (!probe.IsReady)
                {
                    Error(issues, "V2-RUNTIME-MODEL-PROBE", path, probe.Status);
                }
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                Error(issues, "V2-RUNTIME-MODEL-PROBE", path, $"Model Adapter Probe 失败：{exception.Message}");
            }
        }

        await ValidateSourcesAsync(project, sequence, deployment, context.EnvironmentMode, issues, cancellationToken);
        await ValidateTriggersAsync(usedSteps, deployment, context.EnvironmentMode, issues, cancellationToken);
        await ValidateLineAsync(deployment, context.EnvironmentMode, issues, cancellationToken);
        return issues;
    }

    private async Task ValidateSourcesAsync(
        ProjectConfigurationV2 project,
        TestSequenceVersion sequence,
        DeploymentBinding deployment,
        RuntimeEnvironmentMode environmentMode,
        ICollection<V2ValidationIssue> issues,
        CancellationToken cancellationToken)
    {
        var requiredBindings = sequence.OrderedInvocations
            .Select(invocation => invocation.SourceBindingId ?? sequence.DefaultSourceBindingId)
            .Append(sequence.DefaultSourceBindingId)
            .Distinct();
        foreach (var sourceBindingId in requiredBindings)
        {
            var path = $"deployment.inputSourceBindings[{sourceBindingId}]";
            var source = project.InputSourceDefinitions.FirstOrDefault(value => value.SourceBindingId == sourceBindingId);
            var binding = deployment.InputSourceBindings.FirstOrDefault(value => value.SourceBindingId == sourceBindingId);
            if (source is null || binding is null)
            {
                Error(issues, "V2-RUNTIME-CAPTURE-BINDING", path, "Sequence 使用的 Source Binding 未映射到 Deployment。");
                continue;
            }

            if (!_cameraAdapters.TryGet(binding.CameraAdapterId, out var adapter))
            {
                Error(issues, "V2-RUNTIME-CAMERA-MISSING", path, $"Camera Adapter“{binding.CameraAdapterId}”未注册。");
                continue;
            }

            ValidateProductionAdapterPolicy(
                environmentMode,
                deployment,
                adapter.AdapterId,
                adapter.IsProductionCapable,
                path,
                issues);
            var probe = await adapter.ProbeAsync(binding, cancellationToken);
            if (!probe.IsReady)
            {
                Error(issues, "V2-RUNTIME-CAMERA-PROBE", path, probe.Status);
            }
        }
    }

    private async Task ValidateTriggersAsync(
        IEnumerable<TestStepDefinition> usedSteps,
        DeploymentBinding deployment,
        RuntimeEnvironmentMode environmentMode,
        ICollection<V2ValidationIssue> issues,
        CancellationToken cancellationToken)
    {
        var tags = usedSteps.SelectMany(step => step.InvocationPolicy.ExternalTriggerBindings)
            .Select(binding => binding.LogicalSignalTag)
            .Distinct(StringComparer.OrdinalIgnoreCase);
        foreach (var tag in tags)
        {
            var path = $"deployment.triggerSignalBindings[{tag}]";
            var binding = deployment.TriggerSignalBindings.FirstOrDefault(value =>
                string.Equals(value.LogicalSignalTag, tag, StringComparison.OrdinalIgnoreCase));
            if (binding is null)
            {
                Error(issues, "V2-RUNTIME-TRIGGER-BINDING", path, "Signal Tag 没有 Deployment 地址映射。");
                continue;
            }

            if (!_triggerAdapters.TryGet(binding.TriggerAdapterId, out var adapter))
            {
                Error(issues, "V2-RUNTIME-TRIGGER-MISSING", path, $"Trigger Adapter“{binding.TriggerAdapterId}”未注册。");
                continue;
            }

            ValidateProductionAdapterPolicy(
                environmentMode,
                deployment,
                adapter.AdapterId,
                adapter.IsProductionCapable,
                path,
                issues);
            var probe = await adapter.ProbeAsync(binding, cancellationToken);
            if (!probe.IsReady)
            {
                Error(issues, "V2-RUNTIME-TRIGGER-PROBE", path, probe.Status);
            }
        }
    }

    private async Task ValidateLineAsync(
        DeploymentBinding deployment,
        RuntimeEnvironmentMode environmentMode,
        ICollection<V2ValidationIssue> issues,
        CancellationToken cancellationToken)
    {
        const string path = "deployment.lineResultBinding";
        var binding = deployment.LineResultBinding;
        if (binding is null || !_lineAdapters.TryGet(binding.AdapterId, out var adapter))
        {
            Error(issues, "V2-RUNTIME-LINE-MISSING", path, "Line Result Adapter 未配置或未注册。");
            return;
        }

        ValidateProductionAdapterPolicy(
            environmentMode,
            deployment,
            adapter.AdapterId,
            adapter.IsProductionCapable,
            path,
            issues);
        var probe = await adapter.ProbeAsync(binding, cancellationToken);
        if (!probe.IsReady)
        {
            Error(issues, "V2-RUNTIME-LINE-PROBE", path, probe.Status);
        }
    }

    private static void ValidateProductionAdapterPolicy(
        RuntimeEnvironmentMode environmentMode,
        DeploymentBinding deployment,
        string adapterId,
        bool productionCapable,
        string path,
        ICollection<V2ValidationIssue> issues)
    {
        if (environmentMode != RuntimeEnvironmentMode.Production)
        {
            return;
        }

        if (!deployment.AdapterAllowlist.Contains(adapterId, StringComparer.OrdinalIgnoreCase))
        {
            Error(issues, "V2-PROD-ALLOWLIST", path, $"Production Adapter“{adapterId}”不在 Deployment Allowlist。");
        }

        if (!productionCapable || string.Equals(adapterId, KnownAdapterIds.Manifest, StringComparison.OrdinalIgnoreCase))
        {
            Error(issues, "V2-PROD-ADAPTER", path, $"Adapter“{adapterId}”不是可用的生产 Adapter。");
        }
    }

    private static string? ResolveArtifactPath(
        ModelArtifact artifact,
        string baseDirectory,
        ICollection<V2ValidationIssue> issues,
        string path)
    {
        if (!string.IsNullOrWhiteSpace(artifact.FilePath))
        {
            var expanded = Environment.ExpandEnvironmentVariables(artifact.FilePath);
            return Path.IsPathRooted(expanded)
                ? Path.GetFullPath(expanded)
                : Path.GetFullPath(Path.Combine(baseDirectory, expanded));
        }

        if (Uri.TryCreate(artifact.ArtifactUri, UriKind.Absolute, out var uri) && uri.IsFile)
        {
            return uri.LocalPath;
        }

        Error(issues, "V2-RUNTIME-ARTIFACT-URI", $"{path}.artifactUri", "当前 Runtime 只支持本地 FilePath 或 file:// Artifact URI。");
        return null;
    }

    private static async Task<string> ComputeSha256Async(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, true);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static void Error(ICollection<V2ValidationIssue> issues, string code, string path, string message) =>
        issues.Add(new V2ValidationIssue(V2ValidationSeverity.Error, code, path, message));
}
