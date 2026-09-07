using System.IO;
using VisualInspection.App.Demo;
using VisualInspection.App.Services;
using VisualInspection.App.ViewModels;
using VisualInspection.Core.V2.Configuration;
using VisualInspection.Infrastructure.Persistence;
using VisualInspection.Infrastructure.V2.Persistence;

namespace VisualInspection.App.Tests;

public sealed class SequenceImportPersistenceTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Startup_IsEmpty_WithoutCreatingDataOrReadingDamagedSavedProjects(bool existingStorage)
    {
        var directory = Path.Combine(Path.GetTempPath(), "visual-inspection-empty-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            var savedPath = Path.Combine(directory, "old-project.json");
            if (existingStorage)
            {
                Directory.CreateDirectory(directory);
                await File.WriteAllTextAsync(savedPath, "damaged old configuration");
            }
            var bootstrap = await ApplicationBootstrapper.CreateUnloadedAsync(storageDirectory: directory);
            Assert.Empty(bootstrap.Project.TestSequences);
            Assert.Empty(bootstrap.Project.Models);
            Assert.Empty(bootstrap.Project.InputSources);
            Assert.Empty(bootstrap.Project.Targets);
            Assert.False(bootstrap.IsInputSourceReady);
            Assert.False(bootstrap.IsRuntimeReady);
            Assert.Null(bootstrap.PreviewFrame);
            Assert.Empty(bootstrap.DemoDataDirectory);
            if (existingStorage)
            {
                Assert.Equal("damaged old configuration", await File.ReadAllTextAsync(savedPath));
                Assert.Single(Directory.GetFiles(directory));
            }
            else Assert.False(Directory.Exists(directory));
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task Import_PreservesSavedProject_WhileRestartRemainsUnloaded_AndFailedImportDoesNotReplaceIt()
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
            var loadedViewModel = new MainWindowViewModel(imported);
            Assert.True(loadedViewModel.HasLoadedSequence);
            Assert.Single(loadedViewModel.Sequence);
            Assert.Equal(6, loadedViewModel.DetectionSummary.Count);
            Assert.Contains("FAN-A01", loadedViewModel.SequenceName);
            Assert.False(loadedViewModel.StartCommand.CanExecute(null)); // Invalid model bytes must not enable runtime.
            loadedViewModel.IsLoadingSequence = true;
            Assert.False(loadedViewModel.CanImportSequence);
            Assert.False(loadedViewModel.CanOpenSettings);
            Assert.False(loadedViewModel.ResetCommand.CanExecute(null));
            loadedViewModel.IsLoadingSequence = false;
            Assert.True(loadedViewModel.CanImportSequence);
            Assert.True(loadedViewModel.ResetCommand.CanExecute(null));
            var restarted = await ApplicationBootstrapper.CreateUnloadedAsync(storageDirectory: storeDirectory);
            Assert.Empty(restarted.Project.TestSequences);
            Assert.Empty(restarted.Project.Models);
            Assert.False(restarted.IsRuntimeReady);
            var savedProject = await store.LoadAsync(imported.Project.Id);
            Assert.NotNull(savedProject);
            Assert.Equal("ZZZ configured", savedProject.Name);
            Assert.Equal(configured.TestStepCatalog.Single().CustomFunction,
                savedProject.TestSequences.Single().Items.Single().CustomFunction);
            Assert.Equal(imported.Project.Models.Single().FilePath, savedProject.Models.Single().FilePath);
            Assert.Equal(imported.Project.InputSources.Single().Folder!.FolderPath,
                savedProject.InputSources.Single().Folder!.FolderPath);
            var savedPath = Path.Combine(storeDirectory, imported.Project.Id.ToString("N") + ".json");
            var saved = await File.ReadAllBytesAsync(savedPath);
            await ApplicationBootstrapper.LoadPortableSequenceAsync(sequencePath, storageDirectory: storeDirectory);
            Assert.Equal(saved, await File.ReadAllBytesAsync(savedPath));
            Assert.Single(loadedViewModel.Sequence);
            Assert.Equal(6, loadedViewModel.DetectionSummary.Count);
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
