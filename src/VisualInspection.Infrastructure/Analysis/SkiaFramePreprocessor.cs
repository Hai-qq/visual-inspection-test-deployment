using SkiaSharp;
using VisualInspection.Core.Imaging;

namespace VisualInspection.Infrastructure.Analysis;

public sealed class SkiaFramePreprocessor : IFramePreprocessor
{
    public PreprocessedImage Preprocess(ImageFrame frame, int inputWidth, int inputHeight)
    {
        ArgumentNullException.ThrowIfNull(frame);
        if (frame.Width <= 0 || frame.Height <= 0 || inputWidth <= 0 || inputHeight <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(frame), "源图像和模型输入宽高必须大于 0。");
        }

        using var source = frame.DataFormat switch
        {
            ImageFrameDataFormat.EncodedJpeg or
            ImageFrameDataFormat.EncodedPng or
            ImageFrameDataFormat.EncodedBmp => DecodeEncoded(frame),
            ImageFrameDataFormat.Gray8 or
            ImageFrameDataFormat.Bgr24 or
            ImageFrameDataFormat.Bgra32 => DecodeRaw(frame),
            _ => throw new NotSupportedException($"不支持图像格式 {frame.DataFormat}。")
        };
        return Letterbox(source, inputWidth, inputHeight);
    }

    private static SKBitmap DecodeEncoded(ImageFrame frame)
    {
        var bitmap = SKBitmap.Decode(frame.Data.ToArray())
            ?? throw new InvalidDataException($"无法解码输入图像：{frame.Origin ?? "未知来源"}。");
        if (bitmap.Width != frame.Width || bitmap.Height != frame.Height)
        {
            bitmap.Dispose();
            throw new InvalidDataException(
                $"图像头尺寸 {frame.Width}×{frame.Height} 与解码尺寸 {bitmap.Width}×{bitmap.Height} 不一致。");
        }

        return bitmap;
    }

    private static SKBitmap DecodeRaw(ImageFrame frame)
    {
        var bytesPerPixel = frame.DataFormat switch
        {
            ImageFrameDataFormat.Gray8 => 1,
            ImageFrameDataFormat.Bgr24 => 3,
            ImageFrameDataFormat.Bgra32 => 4,
            _ => throw new NotSupportedException($"{frame.DataFormat} 不是原始像素格式。")
        };
        var minimumStride = checked(frame.Width * bytesPerPixel);
        var configuredStride = frame.Stride ?? minimumStride;
        if (configuredStride == 0 || Math.Abs(configuredStride) < minimumStride)
        {
            throw new InvalidDataException($"Stride {configuredStride} 小于每行有效字节数 {minimumStride}。");
        }

        var requiredBytes = checked(Math.Abs(configuredStride) * frame.Height);
        if (frame.Data.Length < requiredBytes)
        {
            throw new InvalidDataException($"原始相机 Buffer 长度不足：期望至少 {requiredBytes}，实际 {frame.Data.Length}。");
        }

        var bitmap = new SKBitmap(new SKImageInfo(
            frame.Width,
            frame.Height,
            SKColorType.Rgba8888,
            SKAlphaType.Unpremul));
        var destination = bitmap.GetPixelSpan();
        var source = frame.Data.Span;
        for (var y = 0; y < frame.Height; y++)
        {
            var sourceY = configuredStride > 0 ? y : frame.Height - 1 - y;
            var sourceRow = sourceY * Math.Abs(configuredStride);
            var destinationRow = y * bitmap.RowBytes;
            for (var x = 0; x < frame.Width; x++)
            {
                var sourceOffset = sourceRow + x * bytesPerPixel;
                var destinationOffset = destinationRow + x * 4;
                switch (frame.DataFormat)
                {
                    case ImageFrameDataFormat.Gray8:
                        destination[destinationOffset] = source[sourceOffset];
                        destination[destinationOffset + 1] = source[sourceOffset];
                        destination[destinationOffset + 2] = source[sourceOffset];
                        destination[destinationOffset + 3] = 255;
                        break;
                    case ImageFrameDataFormat.Bgr24:
                        destination[destinationOffset] = source[sourceOffset + 2];
                        destination[destinationOffset + 1] = source[sourceOffset + 1];
                        destination[destinationOffset + 2] = source[sourceOffset];
                        destination[destinationOffset + 3] = 255;
                        break;
                    case ImageFrameDataFormat.Bgra32:
                        destination[destinationOffset] = source[sourceOffset + 2];
                        destination[destinationOffset + 1] = source[sourceOffset + 1];
                        destination[destinationOffset + 2] = source[sourceOffset];
                        // Alpha is not a model input channel. Force opaque pixels so camera-buffer
                        // alpha (often undefined) cannot blend RGB against the letterbox background.
                        destination[destinationOffset + 3] = 255;
                        break;
                }
            }
        }

        return bitmap;
    }

    private static PreprocessedImage Letterbox(SKBitmap source, int inputWidth, int inputHeight)
    {
        var scale = Math.Min((double)inputWidth / source.Width, (double)inputHeight / source.Height);
        var resizedWidth = Math.Max(1, (int)Math.Round(source.Width * scale));
        var resizedHeight = Math.Max(1, (int)Math.Round(source.Height * scale));
        var padLeft = (int)Math.Round((inputWidth - resizedWidth) / 2d - 0.1d);
        var padTop = (int)Math.Round((inputHeight - resizedHeight) / 2d - 0.1d);

        using var letterboxed = new SKBitmap(new SKImageInfo(
            inputWidth,
            inputHeight,
            SKColorType.Rgba8888,
            SKAlphaType.Unpremul));
        using (var canvas = new SKCanvas(letterboxed))
        using (var paint = new SKPaint { IsAntialias = true })
        {
            canvas.Clear(new SKColor(114, 114, 114, 255));
            canvas.DrawBitmap(
                source,
                new SKRect(padLeft, padTop, padLeft + resizedWidth, padTop + resizedHeight),
                new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.None),
                paint);
            canvas.Flush();
        }

        var tensor = new float[checked(3 * inputHeight * inputWidth)];
        var pixels = letterboxed.GetPixelSpan();
        var planeSize = inputWidth * inputHeight;
        for (var y = 0; y < inputHeight; y++)
        {
            var sourceRow = y * letterboxed.RowBytes;
            var destinationRow = y * inputWidth;
            for (var x = 0; x < inputWidth; x++)
            {
                var sourceOffset = sourceRow + x * 4;
                var destinationOffset = destinationRow + x;
                tensor[destinationOffset] = pixels[sourceOffset] / 255f;
                tensor[planeSize + destinationOffset] = pixels[sourceOffset + 1] / 255f;
                tensor[planeSize * 2 + destinationOffset] = pixels[sourceOffset + 2] / 255f;
            }
        }

        return new PreprocessedImage
        {
            NchwRgb01 = tensor,
            InputWidth = inputWidth,
            InputHeight = inputHeight,
            Transform = new LetterboxTransform(
                source.Width,
                source.Height,
                inputWidth,
                inputHeight,
                scale,
                padLeft,
                padTop)
        };
    }
}
