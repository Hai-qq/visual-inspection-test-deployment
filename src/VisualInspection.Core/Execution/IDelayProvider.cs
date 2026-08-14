namespace VisualInspection.Core.Execution;

public interface IDelayProvider
{
    Task DelayAsync(int milliseconds, CancellationToken cancellationToken = default);
}

public sealed class SystemDelayProvider : IDelayProvider
{
    public Task DelayAsync(int milliseconds, CancellationToken cancellationToken = default) =>
        milliseconds <= 0 ? Task.CompletedTask : Task.Delay(milliseconds, cancellationToken);
}
