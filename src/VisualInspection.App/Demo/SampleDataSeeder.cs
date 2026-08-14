using System.IO;
using System.Text;
using System.Text.Json;

namespace VisualInspection.App.Demo;

public static class SampleDataSeeder
{
    private const int Width = 640;
    private const int Height = 360;

    public static string DefaultDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "VisualInspectionTestDeployment",
        "acceptance-data",
        "sample-set-01");

    public static string FailureDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "VisualInspectionTestDeployment",
        "acceptance-data",
        "sample-set-fail");

    public static async Task<string> EnsureAsync(CancellationToken cancellationToken = default)
    {
        await EnsureDirectoryAsync(DefaultDirectory, includeFailure: false, cancellationToken);
        await EnsureDirectoryAsync(FailureDirectory, includeFailure: true, cancellationToken);
        return DefaultDirectory;
    }

    private static async Task EnsureDirectoryAsync(
        string directory,
        bool includeFailure,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(directory);
        for (var index = 1; index <= 12; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var path = Path.Combine(directory, $"frame-{index:00}.bmp");
            if (!File.Exists(path))
            {
                await File.WriteAllBytesAsync(path, CreateBitmap(index, includeFailure), cancellationToken);
            }
        }

        var manifestPath = Path.Combine(directory, "detections.json");
        var manifest = CreateManifest(includeFailure);
        await File.WriteAllTextAsync(
            manifestPath,
            JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }),
            Encoding.UTF8,
            cancellationToken);
    }

    private static object CreateManifest(bool includeFailure)
    {
        var frames = new List<object>();
        for (var index = 1; index <= 12; index++)
        {
            var action = index switch
            {
                >= 4 and <= 6 => "hand_near_part",
                >= 7 and <= 9 => "hand_near_fixture",
                >= 10 and <= 12 => "hands_clear",
                _ => null
            };
            frames.Add(new
            {
                fileName = $"frame-{index:00}.bmp",
                targetCounts = new Dictionary<string, int>
                {
                    ["螺钉"] = 4,
                    ["标签"] = 1,
                    ["表面瑕疵"] = includeFailure && index == 3 ? 1 : 0,
                    ["操作员动作"] = action is null ? 0 : 1
                },
                detections = CreateDetections(index, includeFailure, action is not null),
                actions = action is null ? Array.Empty<string>() : new[] { action }
            });
        }

        return new
        {
            schemaVersion = 1,
            mode = includeFailure ? "deterministic-fail-scenario" : "deterministic-pass-scenario",
            frames
        };
    }

    private static IReadOnlyList<object> CreateDetections(int frameIndex, bool includeFailure, bool includeAction)
    {
        var detections = new List<object>
        {
            Detection("螺钉", 193, 108, 217, 132, 0.99),
            Detection("螺钉", 423, 108, 447, 132, 0.98),
            Detection("螺钉", 193, 228, 217, 252, 0.97),
            Detection("螺钉", 423, 228, 447, 252, 0.96),
            Detection("标签", 460, 20, 600, 56, 0.95)
        };
        if (includeFailure && frameIndex == 3)
        {
            detections.Add(Detection("表面瑕疵", 300, 105, 340, 145, 0.94));
        }

        if (includeAction)
        {
            detections.Add(Detection("操作员动作", 36, 120, 118, 240, 0.93));
        }

        return detections;
    }

    private static object Detection(
        string targetName,
        double x1,
        double y1,
        double x2,
        double y2,
        double confidence) =>
        new { targetName, x1, y1, x2, y2, confidence };

    private static byte[] CreateBitmap(int frameIndex, bool includeFailure)
    {
        const int bytesPerPixel = 3;
        var rowSize = (Width * bytesPerPixel + 3) & ~3;
        var pixelBytes = rowSize * Height;
        var bytes = new byte[54 + pixelBytes];

        bytes[0] = (byte)'B';
        bytes[1] = (byte)'M';
        WriteInt32(bytes, 2, bytes.Length);
        WriteInt32(bytes, 10, 54);
        WriteInt32(bytes, 14, 40);
        WriteInt32(bytes, 18, Width);
        WriteInt32(bytes, 22, Height);
        WriteInt16(bytes, 26, 1);
        WriteInt16(bytes, 28, 24);
        WriteInt32(bytes, 34, pixelBytes);
        WriteInt32(bytes, 38, 3780);
        WriteInt32(bytes, 42, 3780);

        for (var y = 0; y < Height; y++)
        {
            for (var x = 0; x < Width; x++)
            {
                var offset = 54 + (Height - 1 - y) * rowSize + x * bytesPerPixel;
                var isPanel = x is >= 145 and <= 495 && y is >= 75 and <= 285;
                var (red, green, blue) = isPanel
                    ? ((byte)205, (byte)216, (byte)211)
                    : ((byte)235, (byte)241, (byte)238);

                if (IsScrew(x, y) || x is >= 272 and <= 368 && y is >= 160 and <= 200)
                {
                    red = 90;
                    green = 112;
                    blue = 102;
                }

                if (x is >= 460 and <= 600 && y is >= 20 and <= 56)
                {
                    red = 0;
                    green = 145;
                    blue = 95;
                }

                if (frameIndex >= 4 && x is >= 36 and <= 118 && y is >= 120 and <= 240)
                {
                    red = frameIndex <= 6 ? (byte)68 : frameIndex <= 9 ? (byte)85 : (byte)118;
                    green = frameIndex <= 6 ? (byte)132 : frameIndex <= 9 ? (byte)155 : (byte)171;
                    blue = frameIndex <= 6 ? (byte)180 : frameIndex <= 9 ? (byte)112 : (byte)144;
                }

                if (includeFailure && frameIndex == 3 && x is >= 300 and <= 340 && y is >= 105 and <= 145)
                {
                    red = 190;
                    green = 64;
                    blue = 58;
                }

                bytes[offset] = blue;
                bytes[offset + 1] = green;
                bytes[offset + 2] = red;
            }
        }

        return bytes;
    }

    private static bool IsScrew(int x, int y)
    {
        var centers = new[] { (205, 120), (435, 120), (205, 240), (435, 240) };
        return centers.Any(center =>
        {
            var dx = x - center.Item1;
            var dy = y - center.Item2;
            return dx * dx + dy * dy <= 12 * 12;
        });
    }

    private static void WriteInt16(byte[] buffer, int offset, short value) =>
        BitConverter.GetBytes(value).CopyTo(buffer, offset);

    private static void WriteInt32(byte[] buffer, int offset, int value) =>
        BitConverter.GetBytes(value).CopyTo(buffer, offset);
}
