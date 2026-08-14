using System.Globalization;
using System.Text;
using System.Text.Json;
using VisualInspection.Core.Configuration;

namespace VisualInspection.Infrastructure.Analysis;

public static class OnnxModelLabelImporter
{
    private const int MetadataPropertiesFieldNumber = 14;
    private const int MaximumMetadataEntryBytes = 1024 * 1024;

    private static readonly HashSet<string> SupportedKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "names",
        "labels",
        "class_names",
        "classes"
    };

    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    public static IReadOnlyList<ModelLabelDefinition> Import(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        var extension = Path.GetExtension(filePath);
        if (extension.Equals(".pt", StringComparison.OrdinalIgnoreCase))
        {
            throw new NotSupportedException("当前版本不会直接反序列化 PT 文件。PT 可能包含可执行的 pickle 内容，需先确定安全的模型运行时与标签元数据契约。");
        }

        if (!extension.Equals(".onnx", StringComparison.OrdinalIgnoreCase))
        {
            throw new NotSupportedException("标签自动读取当前仅支持 ONNX 文件；PT 文件需等待安全运行时契约。");
        }

        using var stream = File.OpenRead(filePath);
        while (stream.Position < stream.Length)
        {
            var tag = ReadRequiredVarint(stream, "ONNX 字段标签");
            var fieldNumber = checked((int)(tag >> 3));
            var wireType = checked((int)(tag & 0x07));
            if (fieldNumber <= 0)
            {
                throw new InvalidDataException("ONNX 文件包含无效的 protobuf 字段编号。");
            }

            if (fieldNumber == MetadataPropertiesFieldNumber && wireType == 2)
            {
                var entryLength = ReadLength(stream, "ONNX 元数据项");
                if (entryLength > MaximumMetadataEntryBytes)
                {
                    throw new InvalidDataException($"ONNX 元数据项超过 {MaximumMetadataEntryBytes / 1024} KB 安全上限。");
                }

                var entryBytes = ReadBytes(stream, checked((int)entryLength), "ONNX 元数据项");
                var (key, value) = ReadMetadataEntry(entryBytes);
                if (key is not null && value is not null && SupportedKeys.Contains(key))
                {
                    return ParseLabels(value);
                }

                continue;
            }

            SkipField(stream, wireType);
        }

        throw new InvalidDataException("ONNX 模型中未找到可识别的标签元数据。支持的键为 names、labels、class_names、classes。");
    }

    internal static IReadOnlyList<ModelLabelDefinition> ParseLabels(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidDataException("ONNX 标签元数据为空。");
        }

        var text = value.Trim();
        if (TryParseJson(text, out var jsonLabels))
        {
            return ValidateLabels(jsonLabels);
        }

        if (text.StartsWith('{') && text.EndsWith('}'))
        {
            return ValidateLabels(ParseDictionary(text[1..^1]));
        }

        if (text.StartsWith('[') && text.EndsWith(']'))
        {
            return ValidateLabels(ParseList(text[1..^1]));
        }

        var lines = text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (lines.Length > 1 || (lines.Length == 1 && FindUnquotedSeparator(lines[0], ['=', ':']) > 0))
        {
            return ValidateLabels(ParseKeyValueParts(lines));
        }

        return ValidateLabels(ParseList(text));
    }

    private static bool TryParseJson(string text, out List<ModelLabelDefinition> labels)
    {
        labels = [];
        try
        {
            using var document = JsonDocument.Parse(text);
            if (document.RootElement.ValueKind == JsonValueKind.Array)
            {
                var id = 0;
                foreach (var element in document.RootElement.EnumerateArray())
                {
                    labels.Add(new ModelLabelDefinition { Id = id++, Name = JsonLabelName(element) });
                }

                return true;
            }

            if (document.RootElement.ValueKind == JsonValueKind.Object)
            {
                foreach (var property in document.RootElement.EnumerateObject())
                {
                    if (!int.TryParse(property.Name, NumberStyles.None, CultureInfo.InvariantCulture, out var id))
                    {
                        throw new InvalidDataException($"标签编号“{property.Name}”不是非负整数。");
                    }

                    labels.Add(new ModelLabelDefinition { Id = id, Name = JsonLabelName(property.Value) });
                }

                return true;
            }

            return false;
        }
        catch (JsonException)
        {
            labels = [];
            return false;
        }
    }

    private static string JsonLabelName(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.String => element.GetString() ?? string.Empty,
        JsonValueKind.Number => element.GetRawText(),
        _ => throw new InvalidDataException("标签名称必须是字符串或数字。")
    };

    private static List<ModelLabelDefinition> ParseDictionary(string content)
    {
        var labels = new List<ModelLabelDefinition>();
        foreach (var part in SplitUnquoted(content, ','))
        {
            var separator = FindUnquotedSeparator(part, [':', '=']);
            if (separator <= 0)
            {
                throw new InvalidDataException($"无法解析标签映射项“{part.Trim()}”。");
            }

            var rawId = Unquote(part[..separator].Trim());
            if (!int.TryParse(rawId, NumberStyles.None, CultureInfo.InvariantCulture, out var id))
            {
                throw new InvalidDataException($"标签编号“{rawId}”不是非负整数。");
            }

            labels.Add(new ModelLabelDefinition { Id = id, Name = Unquote(part[(separator + 1)..].Trim()) });
        }

        return labels;
    }

    private static List<ModelLabelDefinition> ParseKeyValueParts(IEnumerable<string> parts)
    {
        var labels = new List<ModelLabelDefinition>();
        foreach (var part in parts)
        {
            var separator = FindUnquotedSeparator(part, ['=', ':']);
            if (separator <= 0 || !int.TryParse(part[..separator].Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var id))
            {
                throw new InvalidDataException($"无法解析标签行“{part.Trim()}”。请使用“编号=名称”。");
            }

            labels.Add(new ModelLabelDefinition { Id = id, Name = Unquote(part[(separator + 1)..].Trim()) });
        }

        return labels;
    }

    private static List<ModelLabelDefinition> ParseList(string content)
    {
        var parts = SplitUnquoted(content, ',')
            .Select(part => Unquote(part.Trim()))
            .Where(part => !string.IsNullOrWhiteSpace(part))
            .ToList();
        return parts.Select((name, id) => new ModelLabelDefinition { Id = id, Name = name }).ToList();
    }

    private static IReadOnlyList<ModelLabelDefinition> ValidateLabels(List<ModelLabelDefinition> labels)
    {
        if (labels.Count == 0)
        {
            throw new InvalidDataException("ONNX 标签元数据中没有有效标签。");
        }

        if (labels.Any(label => label.Id < 0))
        {
            throw new InvalidDataException("标签编号不能为负数。");
        }

        if (labels.Any(label => string.IsNullOrWhiteSpace(label.Name)))
        {
            throw new InvalidDataException("标签名称不能为空。");
        }

        if (labels.GroupBy(label => label.Id).Any(group => group.Count() > 1))
        {
            throw new InvalidDataException("ONNX 标签元数据包含重复编号。");
        }

        return labels
            .Select(label => label with { Name = label.Name.Trim() })
            .OrderBy(label => label.Id)
            .ToList();
    }

    private static IReadOnlyList<string> SplitUnquoted(string text, char separator)
    {
        var parts = new List<string>();
        var start = 0;
        char? quote = null;
        var escaped = false;
        for (var index = 0; index < text.Length; index++)
        {
            var character = text[index];
            if (escaped)
            {
                escaped = false;
                continue;
            }

            if (character == '\\' && quote is not null)
            {
                escaped = true;
                continue;
            }

            if (character is '\'' or '"')
            {
                quote = quote == character ? null : quote ?? character;
                continue;
            }

            if (character == separator && quote is null)
            {
                parts.Add(text[start..index]);
                start = index + 1;
            }
        }

        if (quote is not null)
        {
            throw new InvalidDataException("标签元数据包含未闭合的引号。");
        }

        parts.Add(text[start..]);
        return parts;
    }

    private static int FindUnquotedSeparator(string text, IReadOnlyCollection<char> separators)
    {
        char? quote = null;
        var escaped = false;
        for (var index = 0; index < text.Length; index++)
        {
            var character = text[index];
            if (escaped)
            {
                escaped = false;
                continue;
            }

            if (character == '\\' && quote is not null)
            {
                escaped = true;
                continue;
            }

            if (character is '\'' or '"')
            {
                quote = quote == character ? null : quote ?? character;
                continue;
            }

            if (quote is null && separators.Contains(character))
            {
                return index;
            }
        }

        return -1;
    }

    private static string Unquote(string value)
    {
        if (value.Length < 2 || value[0] is not ('\'' or '"') || value[^1] != value[0])
        {
            return value;
        }

        if (value[0] == '"')
        {
            try
            {
                return JsonSerializer.Deserialize<string>(value) ?? string.Empty;
            }
            catch (JsonException exception)
            {
                throw new InvalidDataException("标签名称中的转义字符无效。", exception);
            }
        }

        return value[1..^1]
            .Replace("\\'", "'", StringComparison.Ordinal)
            .Replace("\\\\", "\\", StringComparison.Ordinal);
    }

    private static (string? Key, string? Value) ReadMetadataEntry(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes, writable: false);
        string? key = null;
        string? value = null;
        while (stream.Position < stream.Length)
        {
            var tag = ReadRequiredVarint(stream, "ONNX 元数据字段标签");
            var fieldNumber = checked((int)(tag >> 3));
            var wireType = checked((int)(tag & 0x07));
            if (wireType == 2 && fieldNumber is 1 or 2)
            {
                var length = ReadLength(stream, "ONNX 元数据字符串");
                if (length > MaximumMetadataEntryBytes)
                {
                    throw new InvalidDataException("ONNX 元数据字符串超过安全上限。");
                }

                var decoded = DecodeUtf8(ReadBytes(stream, checked((int)length), "ONNX 元数据字符串"));
                if (fieldNumber == 1)
                {
                    key = decoded;
                }
                else
                {
                    value = decoded;
                }

                continue;
            }

            SkipField(stream, wireType);
        }

        return (key, value);
    }

    private static string DecodeUtf8(byte[] bytes)
    {
        try
        {
            return StrictUtf8.GetString(bytes);
        }
        catch (DecoderFallbackException exception)
        {
            throw new InvalidDataException("ONNX 标签元数据不是有效的 UTF-8 文本。", exception);
        }
    }

    private static ulong ReadLength(Stream stream, string description)
    {
        var length = ReadRequiredVarint(stream, description);
        if (length > long.MaxValue || length > (ulong)(stream.Length - stream.Position))
        {
            throw new InvalidDataException($"{description}长度无效或文件已截断。");
        }

        return length;
    }

    private static byte[] ReadBytes(Stream stream, int length, string description)
    {
        var buffer = new byte[length];
        stream.ReadExactly(buffer);
        if (buffer.Length != length)
        {
            throw new InvalidDataException($"{description}读取不完整。");
        }

        return buffer;
    }

    private static ulong ReadRequiredVarint(Stream stream, string description)
    {
        ulong value = 0;
        for (var shift = 0; shift < 64; shift += 7)
        {
            var next = stream.ReadByte();
            if (next < 0)
            {
                throw new InvalidDataException($"读取{description}时文件意外结束。");
            }

            value |= (ulong)(next & 0x7F) << shift;
            if ((next & 0x80) == 0)
            {
                return value;
            }
        }

        throw new InvalidDataException($"{description}使用了无效的 varint 编码。");
    }

    private static void SkipField(Stream stream, int wireType)
    {
        switch (wireType)
        {
            case 0:
                _ = ReadRequiredVarint(stream, "ONNX varint 字段");
                return;
            case 1:
                SkipBytes(stream, 8);
                return;
            case 2:
                SkipBytes(stream, ReadLength(stream, "ONNX 长度字段"));
                return;
            case 5:
                SkipBytes(stream, 4);
                return;
            default:
                throw new InvalidDataException($"ONNX 文件包含不支持的 protobuf wire type：{wireType}。");
        }
    }

    private static void SkipBytes(Stream stream, ulong count)
    {
        if (count > long.MaxValue || count > (ulong)(stream.Length - stream.Position))
        {
            throw new InvalidDataException("ONNX 文件字段长度无效或文件已截断。");
        }

        stream.Seek(checked((long)count), SeekOrigin.Current);
    }
}
