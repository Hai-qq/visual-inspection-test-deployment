using System.Text.Json;
using VisualInspection.Core.Domain;
using VisualInspection.Core.Execution;
using VisualInspection.Infrastructure.Persistence;

namespace VisualInspection.Core.Tests;

public sealed class JsonLineExecutionLogStoreTests
{
    [Fact]
    public async Task AppendAsync_PersistsSerialNumberWithInspectionResult()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "VisualInspectionTests",
            Guid.NewGuid().ToString("N"));
        try
        {
            var store = new JsonLineExecutionLogStore(directory);
            await store.AppendAsync([
                new ExecutionAuditEntry
                {
                    TimestampUtc = DateTimeOffset.UtcNow,
                    Level = "INFO",
                    Event = "serial-image-completed",
                    Message = "序列号 SN-001 · image1.png · 通过",
                    SerialNumber = "SN-001",
                    Verdict = InspectionVerdict.Pass
                }
            ]);

            var line = Assert.Single(await File.ReadAllLinesAsync(store.GetCurrentLogPath()));
            using var document = JsonDocument.Parse(line);
            Assert.Equal("SN-001", document.RootElement.GetProperty("SerialNumber").GetString());
            Assert.Equal("serial-image-completed", document.RootElement.GetProperty("Event").GetString());
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }
}
