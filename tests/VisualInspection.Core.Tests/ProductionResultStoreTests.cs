using VisualInspection.Core.Domain;
using VisualInspection.Core.Imaging;
using VisualInspection.Infrastructure.Persistence;

namespace VisualInspection.Core.Tests;

public sealed class ProductionResultStoreTests
{
    [Theory]
    [InlineData(InspectionVerdict.Pass, "pass", "PASS")]
    [InlineData(InspectionVerdict.Fail, "fail", "FAIL")]
    public async Task AppendAsync_WritesRequiredTxtColumnsAndResultImage(
        InspectionVerdict verdict,
        string resultDirectory,
        string resultText)
    {
        var directory = Path.Combine(Path.GetTempPath(), "VisualInspectionTests", Guid.NewGuid().ToString("N"));
        try
        {
            var store = new ProductionResultStore(directory);
            var completedAt = new DateTimeOffset(2026, 8, 27, 14, 5, 6, 789, TimeSpan.FromHours(8));
            var result = await store.AppendAsync(new ProductionResultRecord
            {
                Machine = "MACHINE-01",
                CompletedAt = completedAt,
                Workstation = "工站-A",
                ProductModel = "EV-100",
                EmployeeNumber = "E001",
                SerialNumber = "SN:001",
                Verdict = verdict,
                Frame = new ImageFrame
                {
                    SourceId = Guid.NewGuid(),
                    SequenceNumber = 1,
                    CapturedAtUtc = completedAt,
                    Width = 1,
                    Height = 1,
                    DataFormat = ImageFrameDataFormat.EncodedPng,
                    Data = new byte[] { 1, 2, 3 },
                    Origin = "SN:001.png"
                }
            });

            var lines = await File.ReadAllLinesAsync(result.LogPath);
            Assert.Equal(ProductionResultStore.Header, lines[0]);
            Assert.Contains($"MACHINE-01|2026-08-27|14:05:06.789|工站-A|EV-100|E001|SN:001|{resultText}|", lines[1]);
            Assert.NotNull(result.ImagePath);
            Assert.Contains($"images{Path.DirectorySeparatorChar}{resultDirectory}", result.ImagePath, StringComparison.OrdinalIgnoreCase);
            Assert.Contains($"SN_001_20260827-140506789_{resultText}.png", result.ImagePath, StringComparison.OrdinalIgnoreCase);
            Assert.True(File.Exists(result.ImagePath));
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Fact]
    public async Task AppendAsync_ErrorWritesTxtWithoutCreatingResultImage()
    {
        var directory = Path.Combine(Path.GetTempPath(), "VisualInspectionTests", Guid.NewGuid().ToString("N"));
        try
        {
            var store = new ProductionResultStore(directory);
            var result = await store.AppendAsync(new ProductionResultRecord
            {
                Machine = "MACHINE-01",
                CompletedAt = DateTimeOffset.Now,
                Workstation = "工站-A",
                ProductModel = "EV-100",
                EmployeeNumber = "E001",
                SerialNumber = "SN-ERROR",
                Verdict = InspectionVerdict.Error
            });

            Assert.Null(result.ImagePath);
            Assert.Contains("|ERROR||", (await File.ReadAllLinesAsync(result.LogPath))[1]);
            Assert.False(Directory.Exists(Path.Combine(directory, "images")));
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }
}
