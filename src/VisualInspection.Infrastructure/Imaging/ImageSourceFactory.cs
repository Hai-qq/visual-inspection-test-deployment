using VisualInspection.Core.Configuration;
using VisualInspection.Core.Imaging;

namespace VisualInspection.Infrastructure.Imaging;

public static class ImageSourceFactory
{
    public static IImageSource Create(InputSourceDefinition definition, string baseDirectory)
    {
        ArgumentNullException.ThrowIfNull(definition);
        return definition.Type switch
        {
            InputSourceType.Folder => new FolderImageSource(definition, baseDirectory),
            InputSourceType.DirectShowCamera => throw new NotSupportedException(
                "尚未安装 DirectShow 图像源适配器。"),
            InputSourceType.VendorCamera => throw new NotSupportedException(
                "尚未为此图像源安装厂商相机适配器。"),
            _ => throw new ArgumentOutOfRangeException(nameof(definition), definition.Type, "不支持此图像源类型。")
        };
    }
}
