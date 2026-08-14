using VisualInspection.Core.Analysis;
using VisualInspection.Core.Configuration;

namespace VisualInspection.Core.Tests;

public sealed class SpatialDetectionCounterTests
{
    private static readonly Guid TargetId = Guid.Parse("10000000-0000-0000-0000-000000000001");
    private static readonly Guid BindingId = Guid.Parse("20000000-0000-0000-0000-000000000001");

    [Fact]
    public void Count_FullImageFiltersTargetBindingAndConfidence()
    {
        var rule = Rule(new RegionScopeDefinition { Type = RegionType.FullImage }, confidence: 0.6);
        var detections = new[]
        {
            Detection(TargetId, BindingId, 10, 10, 20, 20, 0.9),
            Detection(TargetId, BindingId, 30, 10, 40, 20, 0.59),
            Detection(Guid.NewGuid(), BindingId, 50, 10, 60, 20, 0.9),
            Detection(TargetId, Guid.NewGuid(), 70, 10, 80, 20, 0.9)
        };

        var count = SpatialDetectionCounter.Count(detections, rule, 100, 100);

        Assert.Equal(1, count);
    }

    [Fact]
    public void Count_RoiUsesBoxCenterAndScalesReferenceCoordinates()
    {
        var rule = Rule(new RegionScopeDefinition
        {
            Type = RegionType.Roi,
            Regions =
            [
                new RegionOfInterestDefinition
                {
                    Name = "中心区",
                    X1 = 25,
                    Y1 = 25,
                    X2 = 75,
                    Y2 = 75,
                    ReferenceWidth = 100,
                    ReferenceHeight = 100
                }
            ]
        });
        var detections = new[]
        {
            Detection(TargetId, BindingId, 90, 90, 110, 110, 0.9),
            Detection(TargetId, BindingId, 10, 10, 30, 30, 0.9),
            Detection(TargetId, BindingId, 140, 140, 160, 160, 0.9)
        };

        var count = SpatialDetectionCounter.Count(detections, rule, 200, 200);

        Assert.Equal(2, count);
    }

    [Fact]
    public void Count_MultipleOverlappingRoisCountsEachDetectionOnce()
    {
        var rule = Rule(new RegionScopeDefinition
        {
            Type = RegionType.Roi,
            Regions =
            [
                Roi("A", 0, 0, 60, 60),
                Roi("B", 40, 40, 100, 100)
            ]
        });
        var detections = new[] { Detection(TargetId, BindingId, 45, 45, 55, 55, 0.9) };

        var count = SpatialDetectionCounter.Count(detections, rule, 100, 100);

        Assert.Equal(1, count);
    }

    [Fact]
    public void Count_RejectsDetectionOutsideFrame()
    {
        var rule = Rule(new RegionScopeDefinition { Type = RegionType.FullImage });
        var detections = new[] { Detection(TargetId, BindingId, 90, 90, 110, 110, 0.9) };

        Assert.Throws<InvalidDataException>(() => SpatialDetectionCounter.Count(detections, rule, 100, 100));
    }

    private static TargetRuleDefinition Rule(RegionScopeDefinition scope, double confidence = 0.5) => new()
    {
        TargetId = TargetId,
        ModelBindingId = BindingId,
        Scope = scope,
        ConfidenceThreshold = confidence
    };

    private static TargetDetection Detection(
        Guid targetId,
        Guid bindingId,
        double x1,
        double y1,
        double x2,
        double y2,
        double confidence) => new()
        {
            TargetId = targetId,
            ModelBindingId = bindingId,
            X1 = x1,
            Y1 = y1,
            X2 = x2,
            Y2 = y2,
            Confidence = confidence
        };

    private static RegionOfInterestDefinition Roi(string name, int x1, int y1, int x2, int y2) => new()
    {
        Name = name,
        X1 = x1,
        Y1 = y1,
        X2 = x2,
        Y2 = y2,
        ReferenceWidth = 100,
        ReferenceHeight = 100
    };
}
