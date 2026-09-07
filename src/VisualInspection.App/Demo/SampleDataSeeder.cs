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
        "fan-pass");

    public static string FailureDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "VisualInspectionTestDeployment",
        "acceptance-data",
        "fan-fail");

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
            var path = Path.Combine(directory, $"fan-{index:00}.bmp");
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
            frames.Add(new
            {
                fileName = $"fan-{index:00}.bmp",
                targetCounts = new Dictionary<string, int>
                {
                    ["标签"] = includeFailure && index == 3 ? 2 : 3,
                    ["黑线"] = 3,
                    ["白线"] = 1,
                    ["反向标签"] = includeFailure && index == 3 ? 1 : 0,
                    ["反向黑线"] = 0,
                    ["反向白线"] = 0
                },
                detections = CreateDetections(index, includeFailure),
                actions = Array.Empty<string>()
            });
        }

        return new
        {
            schemaVersion = 1,
            mode = includeFailure ? "deterministic-fail-scenario" : "deterministic-pass-scenario",
            frames
        };
    }

    private static IReadOnlyList<object> CreateDetections(int frameIndex, bool includeFailure)
    {
        var detections = new List<object>
        {
            Detection("标签", 212, 82, 276, 118, 0.97),
            Detection("标签", 288, 82, 352, 118, 0.96),
            Detection("标签", 364, 82, 428, 118, 0.95),
            Detection("黑线", 206, 130, 246, 276, 0.94),
            Detection("黑线", 300, 130, 340, 276, 0.93),
            Detection("黑线", 394, 130, 434, 276, 0.92),
            Detection("白线", 348, 128, 380, 278, 0.91)
        };
        if (includeFailure && frameIndex == 3)
        {
            detections.RemoveAt(2);
            detections.Add(Detection("反向标签", 364, 82, 428, 118, 0.94));
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
                var isPanel = x is >= 132 and <= 508 && y is >= 18 and <= 342;
                var (red, green, blue) = isPanel
                    ? ((byte)221, (byte)228, (byte)224)
                    : ((byte)235, (byte)241, (byte)238);

                var dx = x - 320;
                var dy = y - 180;
                var radiusSquared = dx * dx + dy * dy;
                if (radiusSquared is >= 118 * 118 and <= 132 * 132)
                {
                    red = 62;
                    green = 82;
                    blue = 73;
                }

                if (IsFanBlade(dx, dy))
                {
                    red = 102;
                    green = 137;
                    blue = 121;
                }

                if (radiusSquared <= 34 * 34)
                {
                    red = 0;
                    green = 145;
                    blue = 95;
                }

                if (includeFailure && frameIndex == 3 && x is >= 222 and <= 276 && y is >= 138 and <= 190)
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

    private static bool IsFanBlade(int x, int y)
    {
        for (var index = 0; index < 5; index++)
        {
            var angle = (-Math.PI / 2) + index * (2 * Math.PI / 5);
            var centerX = Math.Cos(angle) * 78;
            var centerY = Math.Sin(angle) * 78;
            var localX = x - centerX;
            var localY = y - centerY;
            var radial = localX * Math.Cos(angle) + localY * Math.Sin(angle);
            var tangent = -localX * Math.Sin(angle) + localY * Math.Cos(angle);
            if ((radial * radial) / (52d * 52d) + (tangent * tangent) / (25d * 25d) <= 1)
            {
                return true;
            }
        }

        return false;
    }

    private static void WriteInt16(byte[] buffer, int offset, short value) =>
        BitConverter.GetBytes(value).CopyTo(buffer, offset);

    private static void WriteInt32(byte[] buffer, int offset, int value) =>
        BitConverter.GetBytes(value).CopyTo(buffer, offset);
}
