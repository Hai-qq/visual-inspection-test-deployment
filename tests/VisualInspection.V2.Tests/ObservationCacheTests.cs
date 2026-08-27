using VisualInspection.Core.V2.Execution;
using VisualInspection.Runner;

namespace VisualInspection.V2.Tests;

public sealed class ObservationCacheTests
{
    [Fact]
    public async Task SameFrameArtifactProfileAndAdapter_SharesObservationAcrossRuleBindings()
    {
        var cache = new RunObservationCache();
        var captureId = Guid.NewGuid();
        var artifactId = Guid.NewGuid();
        var stepId = Guid.NewGuid();
        var profileId = Guid.NewGuid();
        var firstBindingId = Guid.NewGuid();
        var secondBindingId = Guid.NewGuid();
        var executions = 0;

        Task<ModelObservation> Execute()
        {
            executions++;
            return Task.FromResult(new ModelObservation
            {
                CaptureId = captureId,
                ModelBindingId = firstBindingId,
                ObservedAtUtc = DateTimeOffset.UtcNow,
                Counts = new Dictionary<ModelOutputKey, int>
                {
                    [new ModelOutputKey(firstBindingId, 0)] = 1
                }
            });
        }

        var first = await cache.GetOrExecuteAsync(captureId, stepId, artifactId, profileId, "adapter", firstBindingId, Execute);
        var second = await cache.GetOrExecuteAsync(captureId, stepId, artifactId, profileId, "adapter", secondBindingId, Execute);

        Assert.Equal(1, first.GetCount(firstBindingId, 0));
        Assert.Equal(1, second.GetCount(secondBindingId, 0));
        Assert.Equal(1, executions);
    }
}
