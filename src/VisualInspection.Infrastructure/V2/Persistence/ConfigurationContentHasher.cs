using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using VisualInspection.Core.V2.Configuration;

namespace VisualInspection.Infrastructure.V2.Persistence;

public static class ConfigurationContentHasher
{
    private static readonly JsonSerializerOptions Options = V2Json.CreateOptions(writeIndented: false);

    public static string Compute(
        ProjectConfigurationV2 project,
        TestSequenceVersion sequence,
        IReadOnlyList<ModelArtifactReference> references)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(sequence);
        ArgumentNullException.ThrowIfNull(references);
        var normalizedProject = Normalize(project);
        var material = new PackageHashMaterial(
            normalizedProject,
            sequence with { ContentHash = null },
            references.OrderBy(reference => reference.ModelArtifactId).ToArray());
        var json = JsonSerializer.Serialize(material, Options);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json))).ToLowerInvariant();
    }

    public static ProjectConfigurationV2 DeepClone(ProjectConfigurationV2 project)
    {
        var json = JsonSerializer.Serialize(project, Options);
        return JsonSerializer.Deserialize<ProjectConfigurationV2>(json, Options)
            ?? throw new InvalidDataException("无法克隆 V2 Project Snapshot。");
    }

    private static ProjectConfigurationV2 Normalize(ProjectConfigurationV2 project) => project with
    {
        ModelArtifacts = project.ModelArtifacts.OrderBy(value => value.ModelArtifactId).Select(value => value with
        {
            LabelSet = value.LabelSet.OrderBy(label => label.Id).ToList()
        }).ToList(),
        RuntimeProfiles = project.RuntimeProfiles.OrderBy(value => value.RuntimeProfileId).ToList(),
        InputSourceDefinitions = project.InputSourceDefinitions.OrderBy(value => value.InputSourceId).ToList(),
        TestStepCatalog = project.TestStepCatalog.OrderBy(value => value.StepId).Select(value => value with
        {
            ModelBindings = value.ModelBindings.OrderBy(binding => binding.ModelBindingId).ToList(),
            RuleSet = value.RuleSet is null ? null : value.RuleSet with
            {
                Rules = value.RuleSet.Rules.OrderBy(rule => rule.RuleId).ToList()
            },
            PoseProgram = value.PoseProgram is null ? null : value.PoseProgram with
            {
                Actions = value.PoseProgram.Actions.OrderBy(action => action.Order).ToList()
            },
            InvocationPolicy = value.InvocationPolicy with
            {
                ExternalTriggerBindings = value.InvocationPolicy.ExternalTriggerBindings
                    .OrderBy(binding => binding.BindingId)
                    .ToList()
            }
        }).ToList(),
        TestSequenceVersions = project.TestSequenceVersions
            .OrderBy(value => value.SequenceId)
            .ThenBy(value => value.Version, StringComparer.Ordinal)
            .Select(value => value with
            {
                ContentHash = null,
                OrderedInvocations = value.OrderedInvocations.OrderBy(invocation => invocation.Order).ToList()
            })
            .ToList(),
        DeploymentBindings = project.DeploymentBindings.OrderBy(value => value.DeploymentBindingId).Select(value => value with
        {
            TriggerSignalBindings = value.TriggerSignalBindings.OrderBy(binding => binding.BindingId).ToList(),
            InputSourceBindings = value.InputSourceBindings.OrderBy(binding => binding.SourceBindingId).ToList(),
            AdapterAllowlist = value.AdapterAllowlist.Order(StringComparer.OrdinalIgnoreCase).ToList()
        }).ToList(),
        RetiredFunctionCodes = project.RetiredFunctionCodes.Order(StringComparer.OrdinalIgnoreCase).ToList()
    };

    private sealed record PackageHashMaterial(
        ProjectConfigurationV2 Project,
        TestSequenceVersion Sequence,
        IReadOnlyList<ModelArtifactReference> ModelReferences);
}
