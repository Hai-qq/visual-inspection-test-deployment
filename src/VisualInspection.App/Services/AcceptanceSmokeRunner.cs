using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using VisualInspection.App.Demo;
using VisualInspection.Core.Configuration;
using VisualInspection.Core.Domain;
using VisualInspection.Core.Execution;
using VisualInspection.Infrastructure.Analysis;
using VisualInspection.Infrastructure.Imaging;

namespace VisualInspection.App.Services;

public static class AcceptanceSmokeRunner
{
    public static string ReceiptPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "VisualInspectionTestDeployment",
        "acceptance-smoke-result.json");

    public static async Task<int> RunAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var demoDirectory = await SampleDataSeeder.EnsureAsync(cancellationToken);
            var project = SampleProjectFactory.Create(demoDirectory);
            var configurationErrors = ProjectConfigurationValidator.Validate(project)
                .Where(issue => issue.Severity == ConfigurationValidationSeverity.Error)
                .ToArray();
            if (configurationErrors.Length > 0)
            {
                await WriteReceiptAsync(new SmokeReceipt
                {
                    CompletedAtUtc = DateTimeOffset.UtcNow,
                    Verdict = InspectionVerdict.Error,
                    ExitCode = 3,
                    Summary = string.Join("; ", configurationErrors.Select(issue => $"{issue.Code}: {issue.Message}"))
                }, cancellationToken);
                return 3;
            }

            var sequence = project.TestSequences.OrderByDescending(item => item.IsPublished).First();
            var sourceDefinition = project.InputSources.First(item => item.Id == sequence.InputSourceId);
            var folderPath = ApplicationBootstrapper.ResolveFolderPath(sourceDefinition.Folder!.FolderPath);
            await using var source = ImageSourceFactory.Create(sourceDefinition, AppContext.BaseDirectory);
            var provider = await ManifestInspectionProvider.LoadAsync(folderPath, project, cancellationToken);
            var result = await new TestSequenceRunner().RunAsync(
                project,
                sequence,
                source,
                provider,
                cancellationToken: cancellationToken);
            var exitCode = result.Verdict switch
            {
                InspectionVerdict.Pass => 0,
                InspectionVerdict.Fail => 2,
                _ => 3
            };
            await WriteReceiptAsync(new SmokeReceipt
            {
                CompletedAtUtc = DateTimeOffset.UtcNow,
                Verdict = result.Verdict,
                ExitCode = exitCode,
                Summary = result.Summary,
                ItemResults = result.Items.Select(item => new SmokeItemReceipt
                {
                    Order = item.ItemOrder,
                    Name = item.ItemName,
                    IsRequired = item.IsRequired,
                    Verdict = item.Verdict,
                    Measured = item.Measured
                }).ToList()
            }, cancellationToken);
            return exitCode;
        }
        catch (Exception exception)
        {
            await WriteReceiptAsync(new SmokeReceipt
            {
                CompletedAtUtc = DateTimeOffset.UtcNow,
                Verdict = InspectionVerdict.Error,
                ExitCode = 3,
                Summary = exception.ToString()
            }, CancellationToken.None);
            return 3;
        }
    }

    private static async Task WriteReceiptAsync(SmokeReceipt receipt, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(ReceiptPath)!);
        var options = new JsonSerializerOptions { WriteIndented = true };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        await File.WriteAllTextAsync(
            ReceiptPath,
            JsonSerializer.Serialize(receipt, options),
            cancellationToken);
    }

    private sealed record SmokeReceipt
    {
        public DateTimeOffset CompletedAtUtc { get; init; }
        public InspectionVerdict Verdict { get; init; }
        public int ExitCode { get; init; }
        public string Summary { get; init; } = string.Empty;
        public List<SmokeItemReceipt> ItemResults { get; init; } = [];
    }

    private sealed record SmokeItemReceipt
    {
        public int Order { get; init; }
        public string Name { get; init; } = string.Empty;
        public bool IsRequired { get; init; }
        public InspectionVerdict Verdict { get; init; }
        public string Measured { get; init; } = string.Empty;
    }
}
