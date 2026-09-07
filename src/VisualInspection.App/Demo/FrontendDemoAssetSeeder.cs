using System.IO;
using System.Reflection;
using System.Security.Cryptography;

namespace VisualInspection.App.Demo;

public static class FrontendDemoAssetSeeder
{
    private const string ModelResourceName = "VisualInspection.FrontendDemo.Assets.fan.onnx";
    private const string ImageResourceName = "VisualInspection.FrontendDemo.Assets.IMG_1533.JPG";
    public const string BundledFanImageSha256 = "1aae7ead89bf4f0b956e48ec35924dfa2bed08208950a4108481cfb46a4487dd";

    private static string AssetRoot => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "VisualInspectionTestDeployment",
        "bundled-fan");

    public static bool HasEmbeddedAssets
    {
        get
        {
            var assembly = typeof(FrontendDemoAssetSeeder).Assembly;
            return assembly.GetManifestResourceInfo(ModelResourceName) is not null &&
                assembly.GetManifestResourceInfo(ImageResourceName) is not null;
        }
    }

    public static async Task<FrontendDemoAssets?> EnsureAsync(CancellationToken cancellationToken = default)
    {
        var assembly = typeof(FrontendDemoAssetSeeder).Assembly;
        await using var modelResource = assembly.GetManifestResourceStream(ModelResourceName);
        await using var imageResource = assembly.GetManifestResourceStream(ImageResourceName);
        if (modelResource is null || imageResource is null)
        {
            return null;
        }

        var modelPath = Path.Combine(AssetRoot, "models", "fan.onnx");
        var imageDirectory = Path.Combine(AssetRoot, "input");
        var imagePath = Path.Combine(imageDirectory, "IMG_1533.JPG");
        await EnsureFileAsync(
            modelResource,
            modelPath,
            SampleProjectFactory.BundledFanModelSha256,
            cancellationToken);
        await EnsureFileAsync(imageResource, imagePath, BundledFanImageSha256, cancellationToken);
        return new FrontendDemoAssets(modelPath, imageDirectory, imagePath);
    }

    private static async Task EnsureFileAsync(
        Stream resource,
        string destination,
        string expectedSha256,
        CancellationToken cancellationToken)
    {
        if (File.Exists(destination) &&
            new FileInfo(destination).Length == resource.Length &&
            await HasExpectedHashAsync(destination, expectedSha256, cancellationToken))
        {
            return;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        var temporaryPath = $"{destination}.{Guid.NewGuid():N}.tmp";
        try
        {
            await using (var output = new FileStream(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                1024 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                await resource.CopyToAsync(output, cancellationToken);
                await output.FlushAsync(cancellationToken);
            }

            if (!await HasExpectedHashAsync(temporaryPath, expectedSha256, cancellationToken))
            {
                throw new InvalidDataException($"内置 Fan 资源校验失败：{Path.GetFileName(destination)} 的 SHA-256 不匹配。");
            }

            File.Move(temporaryPath, destination, true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private static async Task<bool> HasExpectedHashAsync(
        string path,
        string expectedSha256,
        CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            1024 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken);
        return Convert.ToHexString(hash).Equals(expectedSha256, StringComparison.OrdinalIgnoreCase);
    }
}

public sealed record FrontendDemoAssets(string ModelPath, string ImageDirectory, string ImagePath);
