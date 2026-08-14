namespace VisualInspection.Core.Imaging;

public sealed record ImageSourceProgress(
    int CurrentIndex,
    int TotalCount,
    int FailedCount,
    string? CurrentItem);
