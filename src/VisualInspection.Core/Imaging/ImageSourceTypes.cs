namespace VisualInspection.Core.Imaging;

public enum ImageSourceState
{
    Closed,
    Ready,
    Streaming,
    Completed,
    Error
}

public enum ImageFrameDataFormat
{
    EncodedJpeg,
    EncodedPng,
    EncodedBmp,
    Gray8,
    Bgr24,
    Bgra32
}
