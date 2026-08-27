using VisualInspection.Core.V2.Execution;

namespace VisualInspection.Infrastructure.V2.Runtime;

public sealed class ModelRuntimeAdapterRegistry : IModelRuntimeAdapterRegistry
{
    private readonly IReadOnlyDictionary<string, IModelRuntimeAdapter> _adapters;

    public ModelRuntimeAdapterRegistry(IEnumerable<IModelRuntimeAdapter> adapters)
    {
        _adapters = CreateLookup(adapters, adapter => adapter.AdapterId);
    }

    public IReadOnlyCollection<IModelRuntimeAdapter> All => _adapters.Values.ToArray();

    public bool TryGet(string adapterId, out IModelRuntimeAdapter adapter) =>
        _adapters.TryGetValue(adapterId, out adapter!);

    private static IReadOnlyDictionary<string, T> CreateLookup<T>(IEnumerable<T> values, Func<T, string> getId) =>
        values.GroupBy(getId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.Count() == 1
                    ? group.Single()
                    : throw new InvalidOperationException($"AdapterId“{group.Key}”重复注册。"),
                StringComparer.OrdinalIgnoreCase);
}

public sealed class CameraAdapterRegistry : ICameraAdapterRegistry
{
    private readonly IReadOnlyDictionary<string, ICameraAdapter> _adapters;

    public CameraAdapterRegistry(IEnumerable<ICameraAdapter> adapters)
    {
        _adapters = adapters.GroupBy(adapter => adapter.AdapterId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.Count() == 1
                    ? group.Single()
                    : throw new InvalidOperationException($"Camera AdapterId“{group.Key}”重复注册。"),
                StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyCollection<ICameraAdapter> All => _adapters.Values.ToArray();

    public bool TryGet(string adapterId, out ICameraAdapter adapter) =>
        _adapters.TryGetValue(adapterId, out adapter!);
}

public sealed class TriggerAdapterRegistry : ITriggerAdapterRegistry
{
    private readonly IReadOnlyDictionary<string, ITriggerAdapter> _adapters;

    public TriggerAdapterRegistry(IEnumerable<ITriggerAdapter> adapters)
    {
        _adapters = adapters.GroupBy(adapter => adapter.AdapterId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.Count() == 1
                    ? group.Single()
                    : throw new InvalidOperationException($"Trigger AdapterId“{group.Key}”重复注册。"),
                StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyCollection<ITriggerAdapter> All => _adapters.Values.ToArray();

    public bool TryGet(string adapterId, out ITriggerAdapter adapter) =>
        _adapters.TryGetValue(adapterId, out adapter!);
}

public sealed class LineResultAdapterRegistry : ILineResultAdapterRegistry
{
    private readonly IReadOnlyDictionary<string, ILineResultAdapter> _adapters;

    public LineResultAdapterRegistry(IEnumerable<ILineResultAdapter> adapters)
    {
        _adapters = adapters.GroupBy(adapter => adapter.AdapterId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.Count() == 1
                    ? group.Single()
                    : throw new InvalidOperationException($"Line AdapterId“{group.Key}”重复注册。"),
                StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyCollection<ILineResultAdapter> All => _adapters.Values.ToArray();

    public bool TryGet(string adapterId, out ILineResultAdapter adapter) =>
        _adapters.TryGetValue(adapterId, out adapter!);
}
