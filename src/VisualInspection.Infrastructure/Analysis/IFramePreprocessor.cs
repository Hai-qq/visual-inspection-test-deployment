using VisualInspection.Core.Imaging;

namespace VisualInspection.Infrastructure.Analysis;

public interface IFramePreprocessor
{
    PreprocessedImage Preprocess(ImageFrame frame, int inputWidth, int inputHeight);
}

public sealed record PreprocessedImage
{
    public required float[] NchwRgb01 { get; init; }
    public required int InputWidth { get; init; }
    public required int InputHeight { get; init; }
    public required LetterboxTransform Transform { get; init; }
}
