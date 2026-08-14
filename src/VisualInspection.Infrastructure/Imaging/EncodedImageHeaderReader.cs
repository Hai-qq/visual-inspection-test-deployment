using System.Buffers.Binary;
using VisualInspection.Core.Imaging;

namespace VisualInspection.Infrastructure.Imaging;

internal static class EncodedImageHeaderReader
{
    public static (int Width, int Height) ReadDimensions(
        ReadOnlySpan<byte> data,
        ImageFrameDataFormat format) => format switch
        {
            ImageFrameDataFormat.EncodedPng => ReadPng(data),
            ImageFrameDataFormat.EncodedBmp => ReadBmp(data),
            ImageFrameDataFormat.EncodedJpeg => ReadJpeg(data),
            _ => throw new InvalidDataException($"不支持此编码图像格式：{format}。")
        };

    private static (int Width, int Height) ReadPng(ReadOnlySpan<byte> data)
    {
        ReadOnlySpan<byte> signature = [137, 80, 78, 71, 13, 10, 26, 10];
        if (data.Length < 24 || !data[..8].SequenceEqual(signature) ||
            !data.Slice(12, 4).SequenceEqual("IHDR"u8))
        {
            throw new InvalidDataException("PNG 文件头无效或不完整。");
        }

        var width = BinaryPrimitives.ReadUInt32BigEndian(data.Slice(16, 4));
        var height = BinaryPrimitives.ReadUInt32BigEndian(data.Slice(20, 4));
        return CheckedDimensions(width, height);
    }

    private static (int Width, int Height) ReadBmp(ReadOnlySpan<byte> data)
    {
        if (data.Length < 26 || data[0] != 'B' || data[1] != 'M')
        {
            throw new InvalidDataException("BMP 文件头无效或不完整。");
        }

        var width = BinaryPrimitives.ReadInt32LittleEndian(data.Slice(18, 4));
        var signedHeight = BinaryPrimitives.ReadInt32LittleEndian(data.Slice(22, 4));
        if (width <= 0 || signedHeight is 0 or int.MinValue)
        {
            throw new InvalidDataException("BMP 图像尺寸无效。");
        }

        return (width, Math.Abs(signedHeight));
    }

    private static (int Width, int Height) ReadJpeg(ReadOnlySpan<byte> data)
    {
        if (data.Length < 4 || data[0] != 0xff || data[1] != 0xd8)
        {
            throw new InvalidDataException("JPEG 起始标记缺失。");
        }

        var index = 2;
        while (index < data.Length)
        {
            while (index < data.Length && data[index] != 0xff) index++;
            while (index < data.Length && data[index] == 0xff) index++;
            if (index >= data.Length) break;

            var marker = data[index++];
            if (marker is 0xd8 or 0xd9) continue;
            if (marker is >= 0xd0 and <= 0xd7 || marker == 0x01) continue;
            if (index + 2 > data.Length) break;

            var segmentLength = BinaryPrimitives.ReadUInt16BigEndian(data.Slice(index, 2));
            if (segmentLength < 2 || index + segmentLength > data.Length)
            {
                throw new InvalidDataException("JPEG 数据段无效或不完整。");
            }

            if (IsStartOfFrame(marker))
            {
                if (segmentLength < 7)
                {
                    throw new InvalidDataException("JPEG 帧头不完整。");
                }

                var height = BinaryPrimitives.ReadUInt16BigEndian(data.Slice(index + 3, 2));
                var width = BinaryPrimitives.ReadUInt16BigEndian(data.Slice(index + 5, 2));
                return CheckedDimensions(width, height);
            }

            index += segmentLength;
        }

        throw new InvalidDataException("未找到受支持的 JPEG 帧头。");
    }

    private static bool IsStartOfFrame(byte marker) =>
        marker is >= 0xc0 and <= 0xcf && marker is not (0xc4 or 0xc8 or 0xcc);

    private static (int Width, int Height) CheckedDimensions(uint width, uint height)
    {
        if (width is 0 || height is 0 || width > int.MaxValue || height > int.MaxValue)
        {
            throw new InvalidDataException("编码图像尺寸无效。");
        }

        return ((int)width, (int)height);
    }
}
