using System.Text;
using VisualInspection.Infrastructure.Analysis;

namespace VisualInspection.Core.Tests;

public sealed class OnnxModelLabelImporterTests
{
    [Fact]
    public void Import_ReadsJsonObjectFromNamesMetadata()
    {
        WithOnnxMetadata("names", "{\"2\":\"螺钉\",\"0\":\"工件\",\"1\":\"标签\"}", path =>
        {
            var labels = OnnxModelLabelImporter.Import(path);

            Assert.Collection(
                labels,
                label => { Assert.Equal(0, label.Id); Assert.Equal("工件", label.Name); },
                label => { Assert.Equal(1, label.Id); Assert.Equal("标签", label.Name); },
                label => { Assert.Equal(2, label.Id); Assert.Equal("螺钉", label.Name); });
        });
    }

    [Fact]
    public void Import_ReadsPythonStyleListFromLabelsMetadata()
    {
        WithOnnxMetadata("labels", "['person', 'safety helmet', 'glove']", path =>
        {
            var labels = OnnxModelLabelImporter.Import(path);

            Assert.Equal(["person", "safety helmet", "glove"], labels.Select(label => label.Name));
            Assert.Equal([0, 1, 2], labels.Select(label => label.Id));
        });
    }

    [Fact]
    public void Import_RejectsOnnxWithoutSupportedLabelMetadata()
    {
        WithOnnxMetadata("author", "test", path =>
        {
            var exception = Assert.Throws<InvalidDataException>(() => OnnxModelLabelImporter.Import(path));

            Assert.Contains("未找到", exception.Message, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void Import_RejectsPtWithoutUnsafeDeserialization()
    {
        var path = Path.Combine(Path.GetTempPath(), $"visual-inspection-{Guid.NewGuid():N}.pt");
        try
        {
            File.WriteAllBytes(path, [0x50, 0x4B]);

            var exception = Assert.Throws<NotSupportedException>(() => OnnxModelLabelImporter.Import(path));

            Assert.Contains("pickle", exception.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static void WithOnnxMetadata(string key, string value, Action<string> assertion)
    {
        var path = Path.Combine(Path.GetTempPath(), $"visual-inspection-{Guid.NewGuid():N}.onnx");
        try
        {
            using (var file = File.Create(path))
            {
                WriteLengthDelimited(file, 1, Encoding.UTF8.GetBytes("test-model"));
                using var entry = new MemoryStream();
                WriteLengthDelimited(entry, 1, Encoding.UTF8.GetBytes(key));
                WriteLengthDelimited(entry, 2, Encoding.UTF8.GetBytes(value));
                WriteLengthDelimited(file, 14, entry.ToArray());
            }

            assertion(path);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static void WriteLengthDelimited(Stream stream, int fieldNumber, byte[] value)
    {
        WriteVarint(stream, (ulong)((fieldNumber << 3) | 2));
        WriteVarint(stream, (ulong)value.Length);
        stream.Write(value);
    }

    private static void WriteVarint(Stream stream, ulong value)
    {
        while (value >= 0x80)
        {
            stream.WriteByte((byte)(value | 0x80));
            value >>= 7;
        }

        stream.WriteByte((byte)value);
    }
}
