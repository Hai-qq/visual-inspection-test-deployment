using VisualInspection.Core.Configuration;
using VisualInspection.Core.Imaging;

namespace VisualInspection.Infrastructure.Imaging;

public sealed class FolderImageSource : IImageSource
{
    private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".bmp"
    };

    private readonly FolderInputOptions _options;
    private readonly string _baseDirectory;
    private readonly List<string> _files = [];
    private int _nextFileIndex;
    private int _failedCount;
    private long _sequenceNumber;

    public FolderImageSource(InputSourceDefinition definition, string baseDirectory)
    {
        ArgumentNullException.ThrowIfNull(definition);
        if (definition.Type != InputSourceType.Folder || definition.Folder is null)
        {
            throw new ArgumentException("图像源必须包含文件夹配置。", nameof(definition));
        }

        if (definition.Id == Guid.Empty)
        {
            throw new ArgumentException("图像源标识不能为空。", nameof(definition));
        }

        if (string.IsNullOrWhiteSpace(baseDirectory))
        {
            throw new ArgumentException("必须提供基础目录。", nameof(baseDirectory));
        }

        Id = definition.Id;
        Name = definition.Name;
        _options = definition.Folder;
        _baseDirectory = Path.GetFullPath(baseDirectory);
    }

    public Guid Id { get; }
    public string Name { get; }
    public ImageSourceState State { get; private set; } = ImageSourceState.Closed;
    public ImageSourceProgress Progress { get; private set; } = new(0, 0, 0, null);
    public string? ResolvedFolderPath { get; private set; }

    public Task OpenAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _files.Clear();
        _nextFileIndex = 0;
        _failedCount = 0;
        _sequenceNumber = 0;

        try
        {
            var configuredPath = Environment.ExpandEnvironmentVariables(_options.FolderPath);
            ResolvedFolderPath = Path.IsPathRooted(configuredPath)
                ? Path.GetFullPath(configuredPath)
                : Path.GetFullPath(Path.Combine(_baseDirectory, configuredPath));

            if (!Directory.Exists(ResolvedFolderPath))
            {
                throw new DirectoryNotFoundException($"图像源文件夹不存在：{ResolvedFolderPath}");
            }

            var searchOption = _options.IncludeSubfolders
                ? SearchOption.AllDirectories
                : SearchOption.TopDirectoryOnly;
            _files.AddRange(Directory
                .EnumerateFiles(ResolvedFolderPath, "*", searchOption)
                .Where(path => SupportedExtensions.Contains(Path.GetExtension(path))));

            SortFiles(_files, _options.SortOrder);
            State = _files.Count == 0 ? ImageSourceState.Completed : ImageSourceState.Ready;
            Progress = new ImageSourceProgress(0, _files.Count, 0, null);
            return Task.CompletedTask;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            State = ImageSourceState.Error;
            throw new ImageSourceException($"无法打开文件夹图像源“{Name}”。", exception);
        }
    }

    public async Task<ImageFrame?> ReadAsync(CancellationToken cancellationToken = default)
    {
        if (State == ImageSourceState.Closed)
        {
            throw new InvalidOperationException("读取前必须先打开文件夹图像源。");
        }

        if (State == ImageSourceState.Error)
        {
            throw new InvalidOperationException("文件夹图像源处于错误状态，请复位或重新打开后再读取。");
        }

        if (_files.Count == 0 || State == ImageSourceState.Completed && !_options.LoopPlayback)
        {
            return null;
        }

        var attempts = 0;
        while (attempts < _files.Count)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_nextFileIndex >= _files.Count)
            {
                if (!_options.LoopPlayback)
                {
                    State = ImageSourceState.Completed;
                    return null;
                }

                _nextFileIndex = 0;
            }

            var fileIndex = _nextFileIndex++;
            var path = _files[fileIndex];
            attempts++;
            Progress = new ImageSourceProgress(fileIndex + 1, _files.Count, _failedCount, path);

            try
            {
                var data = await File.ReadAllBytesAsync(path, cancellationToken);
                var format = GetDataFormat(path);
                var dimensions = EncodedImageHeaderReader.ReadDimensions(data, format);
                _sequenceNumber++;
                State = !_options.LoopPlayback && _nextFileIndex >= _files.Count
                    ? ImageSourceState.Completed
                    : ImageSourceState.Streaming;
                return new ImageFrame
                {
                    SourceId = Id,
                    SequenceNumber = _sequenceNumber,
                    CapturedAtUtc = DateTimeOffset.UtcNow,
                    Width = dimensions.Width,
                    Height = dimensions.Height,
                    DataFormat = format,
                    Data = data,
                    Origin = path
                };
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException)
            {
                _failedCount++;
                Progress = Progress with { FailedCount = _failedCount };
                if (_options.InvalidFileBehavior == InvalidFileBehavior.Stop)
                {
                    State = ImageSourceState.Error;
                    throw new ImageSourceException($"无法读取图像文件：{path}", exception);
                }
            }
        }

        if (_options.LoopPlayback)
        {
            State = ImageSourceState.Error;
            throw new ImageSourceException($"文件夹图像源“{Name}”中没有可读取的图像。");
        }

        State = ImageSourceState.Completed;
        return null;
    }

    public Task ResetAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (State == ImageSourceState.Closed)
        {
            throw new InvalidOperationException("文件夹图像源已关闭。");
        }

        _nextFileIndex = 0;
        _failedCount = 0;
        _sequenceNumber = 0;
        State = _files.Count == 0 ? ImageSourceState.Completed : ImageSourceState.Ready;
        Progress = new ImageSourceProgress(0, _files.Count, 0, null);
        return Task.CompletedTask;
    }

    public Task CloseAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _files.Clear();
        _nextFileIndex = 0;
        _failedCount = 0;
        _sequenceNumber = 0;
        State = ImageSourceState.Closed;
        Progress = new ImageSourceProgress(0, 0, 0, null);
        return Task.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        await CloseAsync();
    }

    private static ImageFrameDataFormat GetDataFormat(string path) =>
        Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".jpg" or ".jpeg" => ImageFrameDataFormat.EncodedJpeg,
            ".png" => ImageFrameDataFormat.EncodedPng,
            ".bmp" => ImageFrameDataFormat.EncodedBmp,
            _ => throw new InvalidDataException($"不支持此图像扩展名：{Path.GetExtension(path)}")
        };

    private static void SortFiles(List<string> files, FolderSortOrder sortOrder)
    {
        if (sortOrder == FolderSortOrder.LastWriteTime)
        {
            files.Sort((left, right) =>
            {
                var comparison = File.GetLastWriteTimeUtc(left).CompareTo(File.GetLastWriteTimeUtc(right));
                return comparison != 0 ? comparison : NaturalPathComparer.Instance.Compare(left, right);
            });
            return;
        }

        files.Sort(NaturalPathComparer.Instance);
    }
}
