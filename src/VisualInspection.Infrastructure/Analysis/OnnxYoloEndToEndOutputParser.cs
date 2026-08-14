namespace VisualInspection.Infrastructure.Analysis;

public static class OnnxYoloEndToEndOutputParser
{
    public static IReadOnlyList<YoloEndToEndDetection> Parse(
        ReadOnlySpan<float> output,
        IReadOnlyList<int> dimensions,
        LetterboxTransform transform,
        IReadOnlyDictionary<int, double> minimumConfidenceByClass)
    {
        ArgumentNullException.ThrowIfNull(dimensions);
        ArgumentNullException.ThrowIfNull(minimumConfidenceByClass);
        if (dimensions.Count != 3 || dimensions[0] != 1 || dimensions[1] <= 0 || dimensions[2] != 6)
        {
            throw new NotSupportedException(
                $"当前仅支持 YOLO 端到端检测输出 [1,N,6]，实际为 [{string.Join(',', dimensions)}]。");
        }

        var expectedLength = checked(dimensions[0] * dimensions[1] * dimensions[2]);
        if (output.Length != expectedLength)
        {
            throw new InvalidDataException($"ONNX 输出元素数量不匹配：期望 {expectedLength}，实际 {output.Length}。");
        }

        if (transform.Scale <= 0 || transform.SourceWidth <= 0 || transform.SourceHeight <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(transform), "图像缩放参数无效。");
        }

        var detections = new List<YoloEndToEndDetection>();
        for (var index = 0; index < dimensions[1]; index++)
        {
            var offset = index * 6;
            var confidence = output[offset + 4];
            var rawClassId = output[offset + 5];
            if (!float.IsFinite(confidence) || !float.IsFinite(rawClassId))
            {
                throw new InvalidDataException("YOLO 输出包含非有限置信度或类别编号。");
            }

            if (confidence <= 0)
            {
                continue;
            }

            if (confidence > 1.0001f)
            {
                throw new InvalidDataException($"YOLO 输出置信度超出 0 到 1：{confidence}。");
            }

            var classId = checked((int)MathF.Round(rawClassId));
            if (classId < 0 || MathF.Abs(rawClassId - classId) > 0.001f ||
                !minimumConfidenceByClass.TryGetValue(classId, out var minimumConfidence) ||
                confidence < minimumConfidence)
            {
                continue;
            }

            var rawX1 = output[offset];
            var rawY1 = output[offset + 1];
            var rawX2 = output[offset + 2];
            var rawY2 = output[offset + 3];
            if (!float.IsFinite(rawX1) || !float.IsFinite(rawY1) ||
                !float.IsFinite(rawX2) || !float.IsFinite(rawY2))
            {
                throw new InvalidDataException("YOLO 输出包含非有限检测框坐标。");
            }

            var x1 = Math.Clamp((rawX1 - transform.PadLeft) / transform.Scale, 0, transform.SourceWidth);
            var y1 = Math.Clamp((rawY1 - transform.PadTop) / transform.Scale, 0, transform.SourceHeight);
            var x2 = Math.Clamp((rawX2 - transform.PadLeft) / transform.Scale, 0, transform.SourceWidth);
            var y2 = Math.Clamp((rawY2 - transform.PadTop) / transform.Scale, 0, transform.SourceHeight);
            if (x2 <= x1 || y2 <= y1)
            {
                continue;
            }

            detections.Add(new YoloEndToEndDetection(classId, x1, y1, x2, y2, confidence));
        }

        return detections;
    }
}

public readonly record struct LetterboxTransform(
    int SourceWidth,
    int SourceHeight,
    int InputWidth,
    int InputHeight,
    double Scale,
    int PadLeft,
    int PadTop);

public readonly record struct YoloEndToEndDetection(
    int ClassId,
    double X1,
    double Y1,
    double X2,
    double Y2,
    double Confidence);
