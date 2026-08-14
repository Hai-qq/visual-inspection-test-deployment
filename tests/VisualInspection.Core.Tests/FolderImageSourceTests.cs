using System.Buffers.Binary;
using VisualInspection.Core.Configuration;
using VisualInspection.Core.Imaging;
using VisualInspection.Infrastructure.Imaging;

namespace VisualInspection.Core.Tests;

public sealed class FolderImageSourceTests
{
    [Fact]
    public async Task Read_UsesNaturalFileNameOrderAndIgnoresUnsupportedFiles()
    {
        using var directory = new TemporaryImageDirectory();
        directory.WriteImage("image10.png", CreatePng(10, 10));
        directory.WriteImage("image2.png", CreatePng(2, 2));
        directory.WriteImage("image1.bmp", CreateBmp(1, 1));
        File.WriteAllText(Path.Combine(directory.Path, "notes.txt"), "ignored");
        await using var source = CreateSource(directory.Path);

        await source.OpenAsync();
        var frames = await ReadAllAsync(source);

        Assert.Equal(3, source.Progress.TotalCount);
        Assert.Equal(["image1.bmp", "image2.png", "image10.png"],
            frames.Select(frame => Path.GetFileName(frame.Origin)).ToArray());
    }

    [Theory]
    [InlineData(".png", ImageFrameDataFormat.EncodedPng)]
    [InlineData(".bmp", ImageFrameDataFormat.EncodedBmp)]
    [InlineData(".jpg", ImageFrameDataFormat.EncodedJpeg)]
    [InlineData(".jpeg", ImageFrameDataFormat.EncodedJpeg)]
    public async Task Read_ReturnsDimensionsAndEncodedFormat(
        string extension,
        ImageFrameDataFormat expectedFormat)
    {
        using var directory = new TemporaryImageDirectory();
        directory.WriteImage($"frame{extension}", CreateImage(extension, 23, 17));
        await using var source = CreateSource(directory.Path);

        await source.OpenAsync();
        var frame = await source.ReadAsync();

        Assert.NotNull(frame);
        Assert.Equal(23, frame.Width);
        Assert.Equal(17, frame.Height);
        Assert.Equal(expectedFormat, frame.DataFormat);
        Assert.Equal(1, frame.SequenceNumber);
        Assert.Equal(source.Id, frame.SourceId);
        Assert.False(frame.Data.IsEmpty);
    }

    [Fact]
    public async Task Open_RespectsIncludeSubfolders()
    {
        using var directory = new TemporaryImageDirectory();
        directory.WriteImage("root.png", CreatePng(1, 1));
        directory.WriteImage(Path.Combine("nested", "child.png"), CreatePng(1, 1));

        await using var topOnly = CreateSource(directory.Path, includeSubfolders: false);
        await topOnly.OpenAsync();
        Assert.Equal(1, topOnly.Progress.TotalCount);

        await using var recursive = CreateSource(directory.Path, includeSubfolders: true);
        await recursive.OpenAsync();
        Assert.Equal(2, recursive.Progress.TotalCount);
    }

    [Fact]
    public async Task Read_SkipsCorruptFileWhenConfigured()
    {
        using var directory = new TemporaryImageDirectory();
        directory.WriteImage("image1.png", [1, 2, 3]);
        directory.WriteImage("image2.png", CreatePng(20, 10));
        await using var source = CreateSource(directory.Path, invalidBehavior: InvalidFileBehavior.Skip);

        await source.OpenAsync();
        var frame = await source.ReadAsync();

        Assert.NotNull(frame);
        Assert.Equal("image2.png", Path.GetFileName(frame.Origin));
        Assert.Equal(1, source.Progress.FailedCount);
        Assert.Equal(ImageSourceState.Completed, source.State);
    }

    [Fact]
    public async Task Read_StopsOnCorruptFileWhenConfigured()
    {
        using var directory = new TemporaryImageDirectory();
        directory.WriteImage("image1.png", [1, 2, 3]);
        await using var source = CreateSource(directory.Path, invalidBehavior: InvalidFileBehavior.Stop);
        await source.OpenAsync();

        await Assert.ThrowsAsync<ImageSourceException>(() => source.ReadAsync());

        Assert.Equal(ImageSourceState.Error, source.State);
        Assert.Equal(1, source.Progress.FailedCount);
    }

    [Fact]
    public async Task Read_LoopsWhenConfigured()
    {
        using var directory = new TemporaryImageDirectory();
        directory.WriteImage("only.png", CreatePng(8, 6));
        await using var source = CreateSource(directory.Path, loopPlayback: true);
        await source.OpenAsync();

        var first = await source.ReadAsync();
        var second = await source.ReadAsync();

        Assert.Equal(1, first?.SequenceNumber);
        Assert.Equal(2, second?.SequenceNumber);
        Assert.Equal("only.png", Path.GetFileName(second?.Origin));
        Assert.Equal(ImageSourceState.Streaming, source.State);
    }

    [Fact]
    public async Task Read_LoopingSourceFailsInsteadOfSpinningWhenEveryFileIsCorrupt()
    {
        using var directory = new TemporaryImageDirectory();
        directory.WriteImage("bad1.png", [1, 2, 3]);
        directory.WriteImage("bad2.jpg", [4, 5, 6]);
        await using var source = CreateSource(
            directory.Path,
            loopPlayback: true,
            invalidBehavior: InvalidFileBehavior.Skip);
        await source.OpenAsync();

        await Assert.ThrowsAsync<ImageSourceException>(() => source.ReadAsync());

        Assert.Equal(ImageSourceState.Error, source.State);
        Assert.Equal(2, source.Progress.FailedCount);
    }

    [Fact]
    public async Task Reset_RewindsProgressAndSequenceNumber()
    {
        using var directory = new TemporaryImageDirectory();
        directory.WriteImage("only.png", CreatePng(8, 6));
        await using var source = CreateSource(directory.Path);
        await source.OpenAsync();
        await source.ReadAsync();

        await source.ResetAsync();
        var replay = await source.ReadAsync();

        Assert.Equal(1, replay?.SequenceNumber);
        Assert.Equal(1, source.Progress.CurrentIndex);
    }

    [Fact]
    public async Task Open_EmptyFolderCompletesWithoutFrame()
    {
        using var directory = new TemporaryImageDirectory();
        await using var source = CreateSource(directory.Path);

        await source.OpenAsync();
        var frame = await source.ReadAsync();

        Assert.Null(frame);
        Assert.Equal(ImageSourceState.Completed, source.State);
        Assert.Equal(0, source.Progress.TotalCount);
    }

    [Fact]
    public async Task Open_MissingFolderReportsSourceError()
    {
        using var directory = new TemporaryImageDirectory();
        var missingPath = Path.Combine(directory.Path, "missing");
        await using var source = CreateSource(missingPath);

        await Assert.ThrowsAsync<ImageSourceException>(() => source.OpenAsync());

        Assert.Equal(ImageSourceState.Error, source.State);
    }

    [Fact]
    public async Task Open_CanSortByLastWriteTime()
    {
        using var directory = new TemporaryImageDirectory();
        var later = directory.WriteImage("a.png", CreatePng(1, 1));
        var earlier = directory.WriteImage("z.png", CreatePng(1, 1));
        File.SetLastWriteTimeUtc(earlier, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        File.SetLastWriteTimeUtc(later, new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc));
        await using var source = CreateSource(directory.Path, sortOrder: FolderSortOrder.LastWriteTime);

        await source.OpenAsync();
        var frames = await ReadAllAsync(source);

        Assert.Equal(["z.png", "a.png"], frames.Select(frame => Path.GetFileName(frame.Origin)).ToArray());
    }

    [Fact]
    public void Factory_RejectsCameraUntilAdapterIsImplemented()
    {
        var definition = new InputSourceDefinition
        {
            Name = "Camera 01",
            Type = InputSourceType.DirectShowCamera,
            Camera = new CameraInputOptions { AdapterId = "directshow", DeviceId = "camera-1" }
        };

        Assert.Throws<NotSupportedException>(() => ImageSourceFactory.Create(definition, Environment.CurrentDirectory));
    }

    private static FolderImageSource CreateSource(
        string folderPath,
        bool includeSubfolders = false,
        bool loopPlayback = false,
        InvalidFileBehavior invalidBehavior = InvalidFileBehavior.Skip,
        FolderSortOrder sortOrder = FolderSortOrder.NaturalFileName)
    {
        var definition = new InputSourceDefinition
        {
            Name = "Test Folder",
            Type = InputSourceType.Folder,
            Folder = new FolderInputOptions
            {
                FolderPath = folderPath,
                IncludeSubfolders = includeSubfolders,
                LoopPlayback = loopPlayback,
                InvalidFileBehavior = invalidBehavior,
                SortOrder = sortOrder
            }
        };
        return new FolderImageSource(definition, Environment.CurrentDirectory);
    }

    private static async Task<List<ImageFrame>> ReadAllAsync(IImageSource source)
    {
        var frames = new List<ImageFrame>();
        ImageFrame? frame;
        while ((frame = await source.ReadAsync()) is not null)
        {
            frames.Add(frame);
        }

        return frames;
    }

    private static byte[] CreateImage(string extension, int width, int height) => extension switch
    {
        ".png" => CreatePng(width, height),
        ".bmp" => CreateBmp(width, height),
        ".jpg" or ".jpeg" => CreateJpeg(width, height),
        _ => throw new ArgumentOutOfRangeException(nameof(extension))
    };

    private static byte[] CreatePng(int width, int height)
    {
        var data = new byte[24];
        new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }.CopyTo(data, 0);
        BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(8, 4), 13);
        "IHDR"u8.CopyTo(data.AsSpan(12, 4));
        BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(16, 4), (uint)width);
        BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(20, 4), (uint)height);
        return data;
    }

    private static byte[] CreateBmp(int width, int height)
    {
        var data = new byte[54];
        data[0] = (byte)'B';
        data[1] = (byte)'M';
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(18, 4), width);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(22, 4), height);
        return data;
    }

    private static byte[] CreateJpeg(int width, int height)
    {
        var data = new byte[23];
        data[0] = 0xff;
        data[1] = 0xd8;
        data[2] = 0xff;
        data[3] = 0xc0;
        BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(4, 2), 17);
        data[6] = 8;
        BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(7, 2), (ushort)height);
        BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(9, 2), (ushort)width);
        data[21] = 0xff;
        data[22] = 0xd9;
        return data;
    }

    private sealed class TemporaryImageDirectory : IDisposable
    {
        public TemporaryImageDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "VisualInspectionTests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public string WriteImage(string relativePath, byte[] data)
        {
            var path = System.IO.Path.Combine(Path, relativePath);
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, data);
            return path;
        }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
