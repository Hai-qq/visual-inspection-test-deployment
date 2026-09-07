using System.Text.Json.Nodes;
using VisualInspection.Core.V2.Configuration;
using VisualInspection.Infrastructure.V2.Persistence;

namespace VisualInspection.V2.Tests;

public sealed class PortableSequenceFileTests
{
    [Fact]
    public async Task ExportAndLoad_CopiesReferencedModelAndVerifiesHash()
    {
        using var temporary = new TemporaryDirectory();
        var sourceModel = Path.Combine(temporary.Path, "source.onnx");
        await File.WriteAllBytesAsync(sourceModel, [1, 3, 5, 7, 9]);
        var project = V2TestFactory.CreateProject(
            adapterId: KnownAdapterIds.YoloEndToEndDetection,
            modelPath: sourceModel,
            modelHash: V2TestFactory.Sha256(sourceModel));
        var deliveryDirectory = Path.Combine(temporary.Path, "delivery");
        var sequencePath = Path.Combine(deliveryDirectory, "Demo.sequence.json");

        var exported = await PortableSequenceFile.ExportAsync(project, sequencePath);
        var loaded = await PortableSequenceFile.LoadAsync(sequencePath);

        Assert.Equal(sequencePath, exported.SequencePath);
        var copiedModel = Assert.Single(exported.ModelPaths);
        Assert.True(File.Exists(copiedModel));
        Assert.Equal(Path.GetFileName(copiedModel), loaded.ModelArtifacts.Single().FilePath is { } resolved
            ? Path.GetFileName(resolved)
            : null);
        Assert.Equal(V2TestFactory.Sha256(sourceModel), loaded.ModelArtifacts.Single().Sha256);
        Assert.Empty(loaded.DeploymentBindings);
    }

    [Fact]
    public async Task Load_RejectsModelWhoseContentChanged()
    {
        using var temporary = new TemporaryDirectory();
        var sourceModel = Path.Combine(temporary.Path, "source.onnx");
        await File.WriteAllBytesAsync(sourceModel, [2, 4, 6, 8]);
        var project = V2TestFactory.CreateProject(
            adapterId: KnownAdapterIds.YoloEndToEndDetection,
            modelPath: sourceModel,
            modelHash: V2TestFactory.Sha256(sourceModel));
        var sequencePath = Path.Combine(temporary.Path, "delivery", "Demo.sequence.json");
        var exported = await PortableSequenceFile.ExportAsync(project, sequencePath);
        await File.AppendAllTextAsync(exported.ModelPaths.Single(), "changed");

        var exception = await Assert.ThrowsAsync<InvalidDataException>(() =>
            PortableSequenceFile.LoadAsync(sequencePath));

        Assert.Contains("SHA-256", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Load_RejectsModelPathOutsideSequenceDirectory()
    {
        using var temporary = new TemporaryDirectory();
        var sourceModel = Path.Combine(temporary.Path, "source.onnx");
        await File.WriteAllBytesAsync(sourceModel, [9, 7, 5, 3, 1]);
        var project = V2TestFactory.CreateProject(
            adapterId: KnownAdapterIds.YoloEndToEndDetection,
            modelPath: sourceModel,
            modelHash: V2TestFactory.Sha256(sourceModel));
        var sequencePath = Path.Combine(temporary.Path, "delivery", "Demo.sequence.json");
        await PortableSequenceFile.ExportAsync(project, sequencePath);
        var root = JsonNode.Parse(await File.ReadAllTextAsync(sequencePath))!.AsObject();
        root["modelArtifacts"]![0]!["filePath"] = "../source.onnx";
        await File.WriteAllTextAsync(sequencePath, root.ToJsonString());

        var exception = await Assert.ThrowsAsync<InvalidDataException>(() =>
            PortableSequenceFile.LoadAsync(sequencePath));

        Assert.Contains("同目录文件名", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void CompatibilityConverter_MapsDetectionRulesAndProductModel()
    {
        var project = V2TestFactory.CreateProject(
            adapterId: KnownAdapterIds.YoloEndToEndDetection,
            modelPath: "fan.onnx");

        var converted = ProjectConfigurationV2CompatibilityConverter.ToV1(project, Directory.GetCurrentDirectory());

        Assert.Equal(project.TestSequenceVersions.Single().Name, converted.TestSequences.Single().Name);
        Assert.Single(converted.TestSequences.Single().Items);
        Assert.Single(converted.TestSequences.Single().Items.Single().Rules);
        Assert.Equal("defect", converted.Targets.Single().Name);
        Assert.Equal(0.5, converted.TestSequences.Single().Items.Single().Rules.Single().ConfidenceThreshold);
    }

    [Fact]
    public void CompatibilityConverter_UsesSiblingInputFolderWhenDeploymentIsNotIncluded()
    {
        var sourceProject = V2TestFactory.CreateProject(
            adapterId: KnownAdapterIds.YoloEndToEndDetection,
            modelPath: "fan.onnx");
        var folderSource = sourceProject.InputSourceDefinitions.Single() with
        {
            Name = "图片文件夹",
            Kind = InputSourceKind.Folder
        };
        var project = sourceProject with
        {
            InputSourceDefinitions = [folderSource],
            DeploymentBindings = []
        };
        var sequenceDirectory = Path.Combine(Path.GetTempPath(), "portable-sequence");

        var converted = ProjectConfigurationV2CompatibilityConverter.ToV1(project, sequenceDirectory);

        Assert.Equal(
            Path.GetFullPath(Path.Combine(sequenceDirectory, "input")),
            converted.InputSources.Single().Folder?.FolderPath);
    }
}
