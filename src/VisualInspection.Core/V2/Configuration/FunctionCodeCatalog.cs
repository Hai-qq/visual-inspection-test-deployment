using System.Globalization;
using System.Text;

namespace VisualInspection.Core.V2.Configuration;

public sealed class FunctionCodeCatalog(ProjectConfigurationV2 project)
{
    private readonly ProjectConfigurationV2 _project = project ?? throw new ArgumentNullException(nameof(project));

    public TestStepDefinition Add(
        string requestedFunctionCode,
        string name,
        TestStepKind kind,
        Guid? stepId = null)
    {
        var functionCode = Normalize(requestedFunctionCode);
        EnsureAvailable(functionCode);
        var step = new TestStepDefinition
        {
            StepId = stepId ?? Guid.NewGuid(),
            FunctionCode = functionCode,
            Name = name,
            Kind = kind
        };
        _project.TestStepCatalog.Add(step);
        return step;
    }

    public TestStepDefinition Rename(Guid stepId, string newName)
    {
        var index = _project.TestStepCatalog.FindIndex(step => step.StepId == stepId);
        if (index < 0)
        {
            throw new KeyNotFoundException($"Test Step {stepId} 不存在。");
        }

        var renamed = _project.TestStepCatalog[index] with { Name = newName };
        _project.TestStepCatalog[index] = renamed;
        return renamed;
    }

    public void ChangeFunctionCode(Guid stepId, string requestedFunctionCode)
    {
        var index = _project.TestStepCatalog.FindIndex(step => step.StepId == stepId);
        if (index < 0)
        {
            throw new KeyNotFoundException($"Test Step {stepId} 不存在。");
        }

        var current = _project.TestStepCatalog[index];
        var functionCode = Normalize(requestedFunctionCode);
        if (string.Equals(current.FunctionCode, functionCode, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (_project.TestSequenceVersions.Any(sequence =>
                sequence.IsPublished && sequence.OrderedInvocations.Any(invocation => invocation.StepId == stepId)))
        {
            throw new InvalidOperationException("已被发布 Sequence 引用的 FunctionCode 不能静默修改。");
        }

        EnsureAvailable(functionCode);
        _project.RetiredFunctionCodes.Add(current.FunctionCode);
        _project.TestStepCatalog[index] = current with { FunctionCode = functionCode };
    }

    public void Remove(Guid stepId)
    {
        var step = _project.TestStepCatalog.FirstOrDefault(candidate => candidate.StepId == stepId)
            ?? throw new KeyNotFoundException($"Test Step {stepId} 不存在。");
        if (_project.TestSequenceVersions.Any(sequence =>
                sequence.IsPublished && sequence.OrderedInvocations.Any(invocation => invocation.StepId == stepId)))
        {
            throw new InvalidOperationException("已发布 Package 使用的 Test Step 不能从当前项目静默删除。");
        }

        _project.TestStepCatalog.Remove(step);
        if (!_project.RetiredFunctionCodes.Contains(step.FunctionCode, StringComparer.OrdinalIgnoreCase))
        {
            _project.RetiredFunctionCodes.Add(step.FunctionCode);
        }
    }

    public static string CreateStableCode(string name, Guid stepId)
    {
        var normalized = Normalize(name);
        var prefix = normalized.Length > 48 ? normalized[..48].TrimEnd('_', '-') : normalized;
        var stableSuffix = stepId.ToString("N").ToUpperInvariant();
        return $"{prefix}_{stableSuffix}"[..Math.Min(prefix.Length + 9, 64)];
    }

    public static string Normalize(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        var decomposed = value.Trim().Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder();
        foreach (var character in decomposed)
        {
            var category = CharUnicodeInfo.GetUnicodeCategory(character);
            if (category == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            if (char.IsAsciiLetterOrDigit(character))
            {
                builder.Append(char.ToUpperInvariant(character));
            }
            else if (character is '-' or '_' or ' ')
            {
                if (builder.Length > 0 && builder[^1] != '_')
                {
                    builder.Append('_');
                }
            }
        }

        var normalized = builder.ToString().Trim('_');
        if (normalized.Length < 3 || !char.IsAsciiLetter(normalized[0]))
        {
            normalized = $"STEP_{normalized}".TrimEnd('_');
        }

        return normalized.Length <= 64 ? normalized : normalized[..64];
    }

    private void EnsureAvailable(string functionCode)
    {
        if (_project.TestStepCatalog.Any(step =>
                string.Equals(step.FunctionCode, functionCode, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException($"FunctionCode“{functionCode}”已存在。");
        }

        if (_project.RetiredFunctionCodes.Contains(functionCode, StringComparer.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"FunctionCode“{functionCode}”已退役，不能静默复用。");
        }
    }
}
