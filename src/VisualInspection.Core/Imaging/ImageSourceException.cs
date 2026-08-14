namespace VisualInspection.Core.Imaging;

public sealed class ImageSourceException : Exception
{
    public ImageSourceException(string message)
        : base(message)
    {
    }

    public ImageSourceException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
