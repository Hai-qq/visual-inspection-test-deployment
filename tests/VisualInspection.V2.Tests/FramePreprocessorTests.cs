using SkiaSharp;
using VisualInspection.Core.Imaging;
using VisualInspection.Infrastructure.Analysis;

namespace VisualInspection.V2.Tests;

public sealed class FramePreprocessorTests
{
    private readonly SkiaFramePreprocessor _preprocessor = new();

    [Fact]
    public void Gray8_UsesStrideAndExpandsToRgb()
    {
        var frame = RawFrame(ImageFrameDataFormat.Gray8, 2, 1, 4, [0, 255, 99, 99]);

        var result = _preprocessor.Preprocess(frame, 2, 1);

        Assert.Equal([0f, 1f, 0f, 1f, 0f, 1f], result.NchwRgb01, FloatComparer.Instance);
    }

    [Fact]
    public void Bgr24_ConvertsChannelOrderAndSkipsRowPadding()
    {
        var frame = RawFrame(ImageFrameDataFormat.Bgr24, 2, 1, 8, [10, 20, 30, 40, 50, 60, 99, 99]);

        var result = _preprocessor.Preprocess(frame, 2, 1);

        AssertClose(30 / 255f, result.NchwRgb01[0]);
        AssertClose(60 / 255f, result.NchwRgb01[1]);
        AssertClose(20 / 255f, result.NchwRgb01[2]);
        AssertClose(50 / 255f, result.NchwRgb01[3]);
        AssertClose(10 / 255f, result.NchwRgb01[4]);
        AssertClose(40 / 255f, result.NchwRgb01[5]);
    }

    [Fact]
    public void Bgra32_ConvertsBgraWithoutEncodedDecode()
    {
        var frame = RawFrame(ImageFrameDataFormat.Bgra32, 1, 1, 4, [5, 10, 20, 128]);

        var result = _preprocessor.Preprocess(frame, 1, 1);

        AssertClose(20 / 255f, result.NchwRgb01[0]);
        AssertClose(10 / 255f, result.NchwRgb01[1]);
        AssertClose(5 / 255f, result.NchwRgb01[2]);
    }

    [Fact]
    public void NegativeStride_HandlesBottomUpBuffer()
    {
        var frame = RawFrame(ImageFrameDataFormat.Gray8, 1, 2, -1, [255, 0]);

        var result = _preprocessor.Preprocess(frame, 1, 2);

        Assert.Equal(0f, result.NchwRgb01[0]);
        Assert.Equal(1f, result.NchwRgb01[1]);
    }

    [Fact]
    public void EncodedPng_IsDecodedAndLetterboxed()
    {
        using var bitmap = new SKBitmap(new SKImageInfo(2, 1, SKColorType.Rgba8888, SKAlphaType.Unpremul));
        bitmap.SetPixel(0, 0, new SKColor(255, 0, 0));
        bitmap.SetPixel(1, 0, new SKColor(0, 255, 0));
        using var image = SKImage.FromBitmap(bitmap);
        using var encoded = image.Encode(SKEncodedImageFormat.Png, 100);
        var frame = new ImageFrame
        {
            SourceId = Guid.NewGuid(),
            SequenceNumber = 1,
            CapturedAtUtc = DateTimeOffset.UtcNow,
            Width = 2,
            Height = 1,
            DataFormat = ImageFrameDataFormat.EncodedPng,
            Data = encoded.ToArray()
        };

        var result = _preprocessor.Preprocess(frame, 2, 1);

        AssertClose(1, result.NchwRgb01[0]);
        AssertClose(0, result.NchwRgb01[1]);
        AssertClose(0, result.NchwRgb01[2]);
        AssertClose(1, result.NchwRgb01[3]);
    }

    [Fact]
    public void RawBuffer_WithInvalidStrideIsRejected()
    {
        var frame = RawFrame(ImageFrameDataFormat.Bgr24, 2, 1, 5, [0, 0, 0, 0, 0]);

        Assert.Throws<InvalidDataException>(() => _preprocessor.Preprocess(frame, 2, 1));
    }

    private static ImageFrame RawFrame(
        ImageFrameDataFormat format,
        int width,
        int height,
        int stride,
        byte[] data) => new()
        {
            SourceId = Guid.NewGuid(),
            SequenceNumber = 1,
            CapturedAtUtc = DateTimeOffset.UtcNow,
            Width = width,
            Height = height,
            DataFormat = format,
            Data = data,
            Stride = stride
        };

    private static void AssertClose(float expected, float actual) =>
        Assert.InRange(actual, expected - 0.0001f, expected + 0.0001f);

    private sealed class FloatComparer : IEqualityComparer<float>
    {
        public static FloatComparer Instance { get; } = new();

        public bool Equals(float x, float y) => Math.Abs(x - y) < 0.0001f;
        public int GetHashCode(float obj) => 0;
    }
}
