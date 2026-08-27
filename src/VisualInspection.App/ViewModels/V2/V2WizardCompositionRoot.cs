using System.IO;
using VisualInspection.Core.V2.Configuration;
using VisualInspection.Core.V2.Execution;
using VisualInspection.Infrastructure.V2.Adapters;
using VisualInspection.Infrastructure.V2.Persistence;
using VisualInspection.Infrastructure.V2.Runtime;

namespace VisualInspection.App.ViewModels.V2;

internal sealed record V2WizardServices(
    ConfigurationPublicationService Publication,
    IConfigurationLifecycleStore Store,
    string StorageRoot,
    string BaseDirectory);

internal static class V2WizardCompositionRoot
{
    public static V2WizardServices Create()
    {
        var storageRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "VisualInspectionTestDeployment",
            "v2-configuration");
        var store = new JsonConfigurationLifecycleStore(storageRoot);

        IModelRuntimeAdapter[] models =
        [
            new OnnxYoloEndToEndModelAdapter(),
            new UnconfiguredModelRuntimeAdapter(KnownAdapterIds.YoloRawDetection, TestStepKind.Detection),
            new UnconfiguredModelRuntimeAdapter(KnownAdapterIds.Classification, TestStepKind.Classification),
            new UnconfiguredModelRuntimeAdapter(KnownAdapterIds.Segmentation, TestStepKind.Segmentation),
            new UnconfiguredModelRuntimeAdapter(KnownAdapterIds.PoseKeypoint, TestStepKind.Pose),
            new UnconfiguredModelRuntimeAdapter(KnownAdapterIds.TemporalAction, TestStepKind.Temporal)
        ];
        ICameraAdapter[] cameras = [new SimulatedCameraAdapter(), new UnconfiguredCameraAdapter()];
        ITriggerAdapter[] triggers = [new SimulatedTriggerAdapter(), new UnconfiguredTriggerAdapter()];
        ILineResultAdapter[] lines = [new SimulatedLineResultAdapter(), new UnconfiguredLineResultAdapter()];
        var runtimeValidator = new RuntimeConfigurationValidator(
            new ModelRuntimeAdapterRegistry(models),
            new CameraAdapterRegistry(cameras),
            new TriggerAdapterRegistry(triggers),
            new LineResultAdapterRegistry(lines));

        return new V2WizardServices(
            new ConfigurationPublicationService(store, runtimeValidator),
            store,
            storageRoot,
            Directory.GetCurrentDirectory());
    }
}
