using VisualInspection.Core.V2.Configuration;
using VisualInspection.Core.V2.Execution;

namespace VisualInspection.Runner;

public sealed class ResultPublicationCoordinator
{
    private readonly ILineResultAdapterRegistry _lineAdapters;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private Guid? _activeRunId;
    private long _lastResultSequenceNumber;

    public ResultPublicationCoordinator(ILineResultAdapterRegistry lineAdapters)
    {
        _lineAdapters = lineAdapters ?? throw new ArgumentNullException(nameof(lineAdapters));
    }

    public async Task SetReadyAsync(
        DeploymentBinding deployment,
        bool ready,
        CancellationToken cancellationToken = default)
    {
        var adapter = Resolve(deployment);
        await adapter.SetReadyAsync(ready, cancellationToken);
    }

    public async Task BeginAsync(
        DeploymentBinding deployment,
        Guid runId,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var adapter = Resolve(deployment);
            await adapter.BeginRunAsync(runId, cancellationToken);
            _activeRunId = runId;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<bool> PublishAsync(
        DeploymentBinding deployment,
        SequenceRunResult result,
        long requestedSequenceNumber,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_activeRunId != result.RunId)
            {
                return false;
            }

            var sequenceNumber = Math.Max(requestedSequenceNumber, _lastResultSequenceNumber + 1);
            await Resolve(deployment).LatchResultAsync(new LineResultPayload
            {
                RunId = result.RunId,
                ResultSequenceNumber = sequenceNumber,
                RunState = result.State,
                Verdict = result.Verdict,
                Error = result.Error
            }, cancellationToken);
            _lastResultSequenceNumber = sequenceNumber;
            _activeRunId = null;
            return true;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task FaultAndNotReadyAsync(
        DeploymentBinding deployment,
        StructuredError error,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            _activeRunId = null;
            await Resolve(deployment).FaultAsync(error, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    private ILineResultAdapter Resolve(DeploymentBinding deployment)
    {
        var adapterId = deployment.LineResultBinding?.AdapterId
            ?? throw RunnerExecutionException.Configuration("LINE_BINDING_MISSING", "Deployment 缺少 Line Result Binding。");
        return _lineAdapters.TryGet(adapterId, out var adapter)
            ? adapter
            : throw RunnerExecutionException.Configuration("LINE_ADAPTER_MISSING", $"Line Result Adapter“{adapterId}”未注册。");
    }
}
