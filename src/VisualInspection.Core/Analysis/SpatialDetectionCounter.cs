using VisualInspection.Core.Configuration;

namespace VisualInspection.Core.Analysis;

public static class SpatialDetectionCounter
{
    public static int Count(
        IEnumerable<TargetDetection> detections,
        TargetRuleDefinition rule,
        int frameWidth,
        int frameHeight)
    {
        ArgumentNullException.ThrowIfNull(detections);
        ArgumentNullException.ThrowIfNull(rule);
        if (frameWidth <= 0 || frameHeight <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(frameWidth), "图像宽高必须大于 0。");
        }

        var candidates = detections.Where(detection =>
        {
            ValidateDetection(detection, frameWidth, frameHeight);
            return detection.TargetId == rule.TargetId
                && detection.ModelBindingId == rule.ModelBindingId
                && detection.Confidence >= rule.ConfidenceThreshold;
        });

        if (rule.Scope.Type == RegionType.FullImage)
        {
            return candidates.Count();
        }

        return candidates.Count(detection => rule.Scope.Regions.Any(region =>
            ContainsCenter(region, detection, frameWidth, frameHeight)));
    }

    private static bool ContainsCenter(
        RegionOfInterestDefinition region,
        TargetDetection detection,
        int frameWidth,
        int frameHeight)
    {
        if (region.ReferenceWidth <= 0 || region.ReferenceHeight <= 0)
        {
            throw new InvalidDataException($"ROI“{region.Name}”的参考图像宽高无效。");
        }

        var centerX = detection.CenterX * region.ReferenceWidth / frameWidth;
        var centerY = detection.CenterY * region.ReferenceHeight / frameHeight;
        return centerX >= region.X1
            && centerX <= region.X2
            && centerY >= region.Y1
            && centerY <= region.Y2;
    }

    private static void ValidateDetection(TargetDetection detection, int frameWidth, int frameHeight)
    {
        if (!double.IsFinite(detection.X1)
            || !double.IsFinite(detection.Y1)
            || !double.IsFinite(detection.X2)
            || !double.IsFinite(detection.Y2)
            || detection.X1 < 0
            || detection.Y1 < 0
            || detection.X2 <= detection.X1
            || detection.Y2 <= detection.Y1
            || detection.X2 > frameWidth
            || detection.Y2 > frameHeight)
        {
            throw new InvalidDataException("检测框坐标无效或超出当前图像范围。");
        }

        if (!double.IsFinite(detection.Confidence) || detection.Confidence is < 0 or > 1)
        {
            throw new InvalidDataException("检测置信度必须在 0 到 1 之间。");
        }
    }
}
