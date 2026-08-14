using System.Text.Json;
using VisualInspection.Core.Execution;

namespace VisualInspection.Infrastructure.Persistence;

public sealed class JsonLineExecutionLogStore(string directory)
{
    private readonly string _directory = Path.GetFullPath(directory);
    private readonly SemaphoreSlim _gate = new(1, 1);

    public string GetCurrentLogPath() =>
        Path.Combine(_directory, $"inspection-{DateTime.Now:yyyyMMdd}.jsonl");

    public async Task AppendAsync(
        IEnumerable<ExecutionAuditEntry> entries,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entries);
        var materialized = entries.ToArray();
        if (materialized.Length == 0)
        {
            return;
        }

        Directory.CreateDirectory(_directory);
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await using var stream = new FileStream(
                GetCurrentLogPath(),
                FileMode.Append,
                FileAccess.Write,
                FileShare.Read,
                16 * 1024,
                useAsync: true);
            await using var writer = new StreamWriter(stream);
            foreach (var entry in materialized)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await writer.WriteLineAsync(JsonSerializer.Serialize(entry));
            }

            await writer.FlushAsync(cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }
}
