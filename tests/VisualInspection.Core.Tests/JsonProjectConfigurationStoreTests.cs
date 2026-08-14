using VisualInspection.Infrastructure.Persistence;

namespace VisualInspection.Core.Tests;

public sealed class JsonProjectConfigurationStoreTests
{
    [Fact]
    public async Task SaveAndLoad_RoundTripsNestedConfiguration()
    {
        using var directory = new TemporaryDirectory();
        var store = new JsonProjectConfigurationStore(directory.Path);
        var project = ConfigurationTestFactory.Create();

        await store.SaveAsync(project);
        var loaded = await store.LoadAsync(project.Id);

        Assert.NotNull(loaded);
        Assert.Equal(project.Name, loaded.Name);
        Assert.Equal(project.Models[0].TaskType, loaded.Models[0].TaskType);
        Assert.Equal(project.Targets[0].ModelBindings[0].Id, loaded.Targets[0].ModelBindings[0].Id);
        Assert.Equal(project.TestSequences[0].Items[0].Rules[0].Metric, loaded.TestSequences[0].Items[0].Rules[0].Metric);
    }

    [Fact]
    public async Task Save_AtomicallyOverwritesSameProject()
    {
        using var directory = new TemporaryDirectory();
        var store = new JsonProjectConfigurationStore(directory.Path);
        var project = ConfigurationTestFactory.Create();

        await store.SaveAsync(project);
        await store.SaveAsync(project with { Name = "Renamed Project" });
        var loaded = await store.LoadAsync(project.Id);

        Assert.Equal("Renamed Project", loaded?.Name);
        Assert.Single(Directory.GetFiles(directory.Path, "*.json"));
        Assert.Empty(Directory.GetFiles(directory.Path, "*.tmp"));
    }

    [Fact]
    public async Task List_ReturnsSavedProjectMetadata()
    {
        using var directory = new TemporaryDirectory();
        var store = new JsonProjectConfigurationStore(directory.Path);
        var first = ConfigurationTestFactory.Create();
        var second = first with { Id = Guid.NewGuid(), Name = "Another Project" };

        await store.SaveAsync(first);
        await store.SaveAsync(second);
        var summaries = await store.ListAsync();

        Assert.Equal(2, summaries.Count);
        Assert.Contains(summaries, summary => summary.ProjectId == first.Id && summary.SchemaVersion == 1);
        Assert.Contains(summaries, summary => summary.ProjectId == second.Id);
    }

    [Fact]
    public async Task Load_ReturnsNullWhenProjectDoesNotExist()
    {
        using var directory = new TemporaryDirectory();
        var store = new JsonProjectConfigurationStore(directory.Path);

        var loaded = await store.LoadAsync(Guid.NewGuid());

        Assert.Null(loaded);
    }

    [Fact]
    public async Task Load_RejectsInvalidJson()
    {
        using var directory = new TemporaryDirectory();
        var projectId = Guid.NewGuid();
        Directory.CreateDirectory(directory.Path);
        await File.WriteAllTextAsync(Path.Combine(directory.Path, $"{projectId:N}.json"), "not-json");
        var store = new JsonProjectConfigurationStore(directory.Path);

        await Assert.ThrowsAsync<InvalidDataException>(() => store.LoadAsync(projectId));
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "VisualInspectionTests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
