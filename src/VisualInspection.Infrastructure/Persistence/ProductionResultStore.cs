using System.Text;
using SkiaSharp;
using VisualInspection.Core.Domain;
using VisualInspection.Core.Imaging;

namespace VisualInspection.Infrastructure.Persistence;

public sealed record ProductionResultRecord
{
    public required string Machine { get; init; }
    public required DateTimeOffset CompletedAt { get; init; }
    public required string Workstation { get; init; }
    public required string ProductModel { get; init; }
    public required string EmployeeNumber { get; init; }
    public required string SerialNumber { get; init; }
    public required InspectionVerdict Verdict { get; init; }
    public ImageFrame? Frame { get; init; }
}

public sealed record ProductionResultWriteResult(string LogPath, string? ImagePath);

public sealed class ProductionResultStore(string rootDirectory)
{
    public const string Header = "机台|日期|时间|工站|型号|员工号|序列号|测试结果|图片地址|";
    private readonly string _rootDirectory = Path.GetFullPath(rootDirectory);
    private readonly SemaphoreSlim _gate = new(1, 1);

    public string GetCurrentLogPath() =>
        Path.Combine(_rootDirectory, "logs", $"inspection-results-{DateTime.Now:yyyyMMdd}.txt");

    public async Task<ProductionResultWriteResult> AppendAsync(
        ProductionResultRecord record,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);
        var imagePath = record.Frame is not null && record.Verdict is InspectionVerdict.Pass or InspectionVerdict.Fail
            ? await SaveImageAsync(record, cancellationToken)
            : null;
        var logPath = Path.Combine(
            _rootDirectory,
            "logs",
            $"inspection-results-{record.CompletedAt.LocalDateTime:yyyyMMdd}.txt");

        Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var writeHeader = !File.Exists(logPath) || new FileInfo(logPath).Length == 0;
            await using var stream = new FileStream(
                logPath,
                FileMode.Append,
                FileAccess.Write,
                FileShare.Read,
                16 * 1024,
                FileOptions.Asynchronous);
            await using var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
            if (writeHeader)
            {
                await writer.WriteLineAsync(Header);
            }

            var local = record.CompletedAt.LocalDateTime;
            var result = record.Verdict switch
            {
                InspectionVerdict.Pass => "PASS",
                InspectionVerdict.Fail => "FAIL",
                _ => "ERROR"
            };
            var fields = new[]
            {
                record.Machine,
                local.ToString("yyyy-MM-dd"),
                local.ToString("HH:mm:ss.fff"),
                record.Workstation,
                record.ProductModel,
                record.EmployeeNumber,
                record.SerialNumber,
                result,
                imagePath ?? string.Empty
            };
            await writer.WriteLineAsync(string.Join('|', fields.Select(NormalizeField)) + "|");
            await writer.FlushAsync(cancellationToken);
        }
        finally
        {
            _gate.Release();
        }

        return new ProductionResultWriteResult(logPath, imagePath);
    }

    private async Task<string> SaveImageAsync(
        ProductionResultRecord record,
        CancellationToken cancellationToken)
    {
        var result = record.Verdict == InspectionVerdict.Pass ? "PASS" : "FAIL";
        var resultDirectory = record.Verdict == InspectionVerdict.Pass ? "pass" : "fail";
        var safeSerial = SanitizeFileName(record.SerialNumber);
        var extension = record.Frame!.DataFormat switch
        {
            ImageFrameDataFormat.EncodedJpeg => ".jpg",
            ImageFrameDataFormat.EncodedPng => ".png",
            ImageFrameDataFormat.EncodedBmp => ".bmp",
            _ => ".png"
        };
        var directory = Path.Combine(_rootDirectory, "images", resultDirectory);
        Directory.CreateDirectory(directory);
        var path = Path.Combine(
            directory,
            $"{safeSerial}_{record.CompletedAt.LocalDateTime:yyyyMMdd-HHmmssfff}_{result}{extension}");
        var payload = IsEncoded(record.Frame.DataFormat)
            ? record.Frame.Data.ToArray()
            : EncodeRawPng(record.Frame);
        await File.WriteAllBytesAsync(path, payload, cancellationToken);
        return path;
    }

    private static bool IsEncoded(ImageFrameDataFormat format) => format is
        ImageFrameDataFormat.EncodedJpeg or
        ImageFrameDataFormat.EncodedPng or
        ImageFrameDataFormat.EncodedBmp;

    private static byte[] EncodeRawPng(ImageFrame frame)
    {
        var bytesPerPixel = frame.DataFormat switch
        {
            ImageFrameDataFormat.Gray8 => 1,
            ImageFrameDataFormat.Bgr24 => 3,
            ImageFrameDataFormat.Bgra32 => 4,
            _ => throw new NotSupportedException($"不支持归档图像格式 {frame.DataFormat}。")
        };
        var minimumStride = checked(frame.Width * bytesPerPixel);
        var stride = frame.Stride ?? minimumStride;
        if (Math.Abs(stride) < minimumStride || frame.Data.Length < checked(Math.Abs(stride) * frame.Height))
        {
            throw new InvalidDataException("原始图像 Buffer 或 Stride 无效，无法归档结果图片。");
        }

        using var bitmap = new SKBitmap(new SKImageInfo(frame.Width, frame.Height, SKColorType.Rgba8888, SKAlphaType.Unpremul));
        var destination = bitmap.GetPixelSpan();
        var source = frame.Data.Span;
        for (var y = 0; y < frame.Height; y++)
        {
            var sourceY = stride > 0 ? y : frame.Height - 1 - y;
            var sourceRow = sourceY * Math.Abs(stride);
            var destinationRow = y * bitmap.RowBytes;
            for (var x = 0; x < frame.Width; x++)
            {
                var sourceOffset = sourceRow + x * bytesPerPixel;
                var destinationOffset = destinationRow + x * 4;
                if (frame.DataFormat == ImageFrameDataFormat.Gray8)
                {
                    destination[destinationOffset] = source[sourceOffset];
                    destination[destinationOffset + 1] = source[sourceOffset];
                    destination[destinationOffset + 2] = source[sourceOffset];
                }
                else
                {
                    destination[destinationOffset] = source[sourceOffset + 2];
                    destination[destinationOffset + 1] = source[sourceOffset + 1];
                    destination[destinationOffset + 2] = source[sourceOffset];
                }

                destination[destinationOffset + 3] = 255;
            }
        }

        using var image = SKImage.FromBitmap(bitmap);
        using var encoded = image.Encode(SKEncodedImageFormat.Png, 100)
            ?? throw new InvalidDataException("无法将原始图像编码为 PNG。");
        return encoded.ToArray();
    }

    private static string NormalizeField(string? value) =>
        (value ?? string.Empty)
            .Replace('|', ' ')
            .Replace('\r', ' ')
            .Replace('\n', ' ')
            .Trim();

    private static string SanitizeFileName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars().ToHashSet();
        var safe = new string(value.Trim().Select(character => invalid.Contains(character) ? '_' : character).ToArray());
        return string.IsNullOrWhiteSpace(safe) ? "UNKNOWN" : safe;
    }
}
