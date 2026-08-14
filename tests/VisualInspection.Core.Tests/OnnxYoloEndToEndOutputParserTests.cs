using VisualInspection.Infrastructure.Analysis;

namespace VisualInspection.Core.Tests;

public sealed class OnnxYoloEndToEndOutputParserTests
{
    [Fact]
    public void Parse_MapsLetterboxCoordinatesAndFiltersUnboundOrLowConfidenceRows()
    {
        float[] output =
        [
            64, 192, 320, 416, 0.90f, 1,
            10, 10, 20, 20, 0.99f, 2,
            64, 192, 320, 416, 0.20f, 1,
            0, 0, 0, 0, 0, 0
        ];
        var transform = new LetterboxTransform(
            SourceWidth: 1000,
            SourceHeight: 500,
            InputWidth: 640,
            InputHeight: 640,
            Scale: 0.64,
            PadLeft: 0,
            PadTop: 160);

        var detections = OnnxYoloEndToEndOutputParser.Parse(
            output,
            [1, 4, 6],
            transform,
            new Dictionary<int, double> { [1] = 0.25 });

        var detection = Assert.Single(detections);
        Assert.Equal(1, detection.ClassId);
        Assert.Equal(100, detection.X1, 3);
        Assert.Equal(50, detection.Y1, 3);
        Assert.Equal(500, detection.X2, 3);
        Assert.Equal(400, detection.Y2, 3);
        Assert.Equal(0.9, detection.Confidence, 3);
    }

    [Fact]
    public void Parse_RejectsNonEndToEndOutputShape()
    {
        var exception = Assert.Throws<NotSupportedException>(() =>
            OnnxYoloEndToEndOutputParser.Parse(
                new float[10 * 8400],
                [1, 10, 8400],
                new LetterboxTransform(640, 640, 640, 640, 1, 0, 0),
                new Dictionary<int, double> { [0] = 0.25 }));

        Assert.Contains("[1,N,6]", exception.Message, StringComparison.Ordinal);
    }
}
