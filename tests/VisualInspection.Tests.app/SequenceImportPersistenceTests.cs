using System.IO;
using VisualInspection.App.Demo;
using VisualInspection.App.Services;
using VisualInspection.Core.V2.Configuration;
using VisualInspection.Infrastructure.Persistence;
using VisualInspection.Infrastructure.V2.Persistence;

namespace VisualInspection.App.Tests;

public sealed class SequenceImportPersistenceTests
{
    [Fact]
    public async Task Import_PersistsLatestProjectWithoutDemoRefresh_AndReadOnlyVerificationDoesNotReplaceIt()
    {
        var directory = Path.Combine(Path.GetTempPath(), "visual-inspection-import-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var storeDirectory = Path.Combine(directory, "projects");
            var modelPath = Path.Combine(directory, "probe.onnx");
            await File.WriteAllBytesAsync(modelPath, [1, 2, 3]); // Persistence test, never inference evidence.
            var legacy = SampleProjectFactory.Create(directory) with { Name = "ZZZ configured" };
            var configured = ProjectConfigurationV1Migrator.Migrate(legacy);
            configured = configured with
            {
                TestStepCatalog = configured.TestStepCatalog.Select(step => step with
                { CustomFunction = new CustomFunctionConfiguration { Kind = CustomFunctionKind.BuiltIn, Name = "preserve_me", FilePath = "functions/check.py", Description = "metadata only" } }).ToList(),
                ModelArtifacts = configured.ModelArtifacts.Select(model => model with
                { FilePath = modelPath, Sha256 = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(new byte[] { 1, 2, 3 })),
                    AdapterId = KnownAdapterIds.YoloEndToEndDetection }).ToList()
            };
            var sequencePath = Path.Combine(directory, "delivery", "test.sequence.json");
            await PortableSequenceFile.ExportAsync(configured, sequencePath);
            var store = new JsonProjectConfigurationStore(storeDirectory);
            await store.SaveAsync(legacy with { Id = Guid.NewGuid(), Name = "AAA older project" });
            var imported = await ApplicationBootstrapper.LoadPortableSequenceAsync(sequencePath,
                persist: true, storageDirectory: storeDirectory);
            Assert.False(imported.IsRuntimeReady);
            Assert.True(imported.Project.IsUserConfigured);
            var restarted = await ApplicationBootstrapper.LoadOrCreateProjectAsync(storageDirectory: storeDirectory);
            Assert.Equal("ZZZ configured", restarted.Project.Name);
            Assert.Equal(configured.TestStepCatalog.Single().CustomFunction,
                restarted.Project.TestSequences.Single().Items.Single().CustomFunction);
            Assert.Equal(imported.Project.Models.Single().FilePath, restarted.Project.Models.Single().FilePath);
            Assert.Equal(imported.Project.InputSources.Single().Folder!.FolderPath,
                restarted.Project.InputSources.Single().Folder!.FolderPath);
            var savedPath = Path.Combine(storeDirectory, imported.Project.Id.ToString("N") + ".json");
            var saved = await File.ReadAllBytesAsync(savedPath);
            await ApplicationBootstrapper.LoadPortableSequenceAsync(sequencePath, storageDirectory: storeDirectory);
            Assert.Equal(saved, await File.ReadAllBytesAsync(savedPath));
            await File.WriteAllTextAsync(sequencePath, "invalid json");
            await Assert.ThrowsAnyAsync<Exception>(() => ApplicationBootstrapper.LoadPortableSequenceAsync(sequencePath,
                persist: true, storageDirectory: storeDirectory));
            Assert.Equal(saved, await File.ReadAllBytesAsync(savedPath));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
