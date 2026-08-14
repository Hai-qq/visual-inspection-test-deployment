using System.Text.Json;
using System.Text.Json.Serialization;
using VisualInspection.Core.Security;

namespace VisualInspection.Infrastructure.Persistence;

public sealed class JsonUserAccountStore(string filePath) : IUserAccountStore
{
    private readonly string _filePath = Path.GetFullPath(filePath);
    private readonly JsonSerializerOptions _options = CreateOptions();

    public async Task<IReadOnlyList<UserAccount>> ListAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_filePath))
        {
            return [];
        }

        try
        {
            await using var stream = File.OpenRead(_filePath);
            return await JsonSerializer.DeserializeAsync<List<UserAccount>>(stream, _options, cancellationToken) ?? [];
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException($"用户账户文件不是有效的 JSON：{_filePath}", exception);
        }
    }

    public async Task SaveAsync(IReadOnlyCollection<UserAccount> accounts, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(accounts);
        var duplicate = accounts.GroupBy(account => account.Username, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null)
        {
            throw new InvalidDataException($"用户名重复：{duplicate.Key}");
        }

        var directory = Path.GetDirectoryName(_filePath) ?? throw new InvalidOperationException("用户账户文件缺少目录。");
        Directory.CreateDirectory(directory);
        var temporaryPath = Path.Combine(directory, $".{Path.GetFileName(_filePath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            await using (var stream = new FileStream(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                16 * 1024,
                useAsync: true))
            {
                await JsonSerializer.SerializeAsync(stream, accounts, _options, cancellationToken);
                await stream.FlushAsync(cancellationToken);
            }

            File.Move(temporaryPath, _filePath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            WriteIndented = true
        };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        return options;
    }
}
