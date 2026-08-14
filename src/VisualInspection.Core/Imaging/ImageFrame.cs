namespace VisualInspection.Core.Imaging;

public sealed record ImageFrame
{
    public required Guid SourceId { get; init; }
    public required long SequenceNumber { get; init; }
    public required DateTimeOffset CapturedAtUtc { get; init; }
    public required int Width { get; init; }
    public required int Height { get; init; }
    public required ImageFrameDataFormat DataFormat { get; init; }
    public required ReadOnlyMemory<byte> Data { get; init; }
    public int? Stride { get; init; }
    public string? Origin { get; init; }
}
