namespace VisualInspection.Core.Imaging;

public interface IImageSource : IAsyncDisposable
{
    Guid Id { get; }
    string Name { get; }
    ImageSourceState State { get; }
    ImageSourceProgress Progress { get; }

    Task OpenAsync(CancellationToken cancellationToken = default);

    Task<ImageFrame?> ReadAsync(CancellationToken cancellationToken = default);

    Task ResetAsync(CancellationToken cancellationToken = default);

    Task CloseAsync(CancellationToken cancellationToken = default);
}
