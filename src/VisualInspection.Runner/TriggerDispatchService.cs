using System.Threading.Channels;
using VisualInspection.Core.V2.Configuration;
using VisualInspection.Core.V2.Execution;

namespace VisualInspection.Runner;

public sealed class TriggerDispatchService : IAsyncDisposable
{
    private readonly Channel<DispatchWorkItem> _channel;
    private readonly QueueOverflowPolicy _overflowPolicy;
    private readonly CancellationTokenSource _shutdown = new();
    private readonly Task[] _workers;
    private bool _disposed;

    public TriggerDispatchService(
        int capacity,
        int maxConcurrency,
        QueueOverflowPolicy overflowPolicy,
        Func<SequenceRunRequest, CancellationToken, Task<SequenceRunResult>> handler)
    {
        if (capacity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(capacity));
        }

        if (maxConcurrency <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxConcurrency));
        }

        ArgumentNullException.ThrowIfNull(handler);
        _overflowPolicy = overflowPolicy;
        _channel = Channel.CreateBounded<DispatchWorkItem>(new BoundedChannelOptions(capacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = maxConcurrency == 1,
            SingleWriter = false,
            AllowSynchronousContinuations = false
        });
        _workers = Enumerable.Range(0, maxConcurrency)
            .Select(_ => Task.Run(() => ConsumeAsync(handler, _shutdown.Token)))
            .ToArray();
    }

    public async Task EnqueueAsync(DispatchWorkItem workItem, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(workItem);
        switch (_overflowPolicy)
        {
            case QueueOverflowPolicy.RejectNewest:
                if (!_channel.Writer.TryWrite(workItem))
                {
                    workItem.Reject(CreateQueueError("RUN_QUEUE_FULL", "运行队列已满，新 Trigger 被安全拒绝。"));
                }

                break;
            case QueueOverflowPolicy.DropOldest:
                if (!_channel.Writer.TryWrite(workItem))
                {
                    if (_channel.Reader.TryRead(out var dropped))
                    {
                        dropped.Reject(CreateQueueError("RUN_QUEUE_DROPPED", "运行队列已满，最旧的未执行 Trigger 被丢弃。"));
                    }

                    if (!_channel.Writer.TryWrite(workItem))
                    {
                        workItem.Reject(CreateQueueError("RUN_QUEUE_FULL", "运行队列仍不可写，新 Trigger 被安全拒绝。"));
                    }
                }

                break;
            case QueueOverflowPolicy.Wait:
                try
                {
                    await _channel.Writer.WriteAsync(workItem, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    workItem.Reject(new StructuredError
                    {
                        Category = ErrorCategory.Cancellation,
                        Code = "RUN_QUEUE_WAIT_CANCELLED",
                        Message = "等待运行队列容量时已取消。"
                    });
                }

                break;
            default:
                throw new ArgumentOutOfRangeException();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _channel.Writer.TryComplete();
        _shutdown.Cancel();
        try
        {
            await Task.WhenAll(_workers);
        }
        catch (OperationCanceledException)
        {
        }

        while (_channel.Reader.TryRead(out var pending))
        {
            pending.Reject(new StructuredError
            {
                Category = ErrorCategory.Cancellation,
                Code = "RUNNER_SHUTDOWN",
                Message = "Runner 已关闭，未执行 Trigger 被取消。"
            });
        }

        _shutdown.Dispose();
    }

    private async Task ConsumeAsync(
        Func<SequenceRunRequest, CancellationToken, Task<SequenceRunResult>> handler,
        CancellationToken cancellationToken)
    {
        await foreach (var workItem in _channel.Reader.ReadAllAsync(cancellationToken))
        {
            if (workItem.IsCompleted)
            {
                continue;
            }

            try
            {
                var result = await handler(workItem.Request, cancellationToken);
                workItem.Complete(result);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                workItem.Reject(new StructuredError
                {
                    Category = ErrorCategory.Cancellation,
                    Code = "RUNNER_SHUTDOWN",
                    Message = "Runner 关闭导致运行停止。"
                });
            }
            catch (Exception exception)
            {
                workItem.Reject(new StructuredError
                {
                    Category = ErrorCategory.Resource,
                    Code = "RUNNER_UNHANDLED",
                    Message = $"Runner 未处理异常：{exception.Message}"
                });
            }
        }
    }

    private static StructuredError CreateQueueError(string code, string message) => new()
    {
        Category = ErrorCategory.Resource,
        Code = code,
        Message = message,
        IsTransient = true
    };
}

public sealed class DispatchWorkItem
{
    private readonly TaskCompletionSource<SequenceRunResult> _completion =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    public DispatchWorkItem(SequenceRunRequest request)
    {
        Request = request ?? throw new ArgumentNullException(nameof(request));
    }

    public SequenceRunRequest Request { get; }
    public Task<SequenceRunResult> Completion => _completion.Task;
    public bool IsCompleted => _completion.Task.IsCompleted;

    public void Complete(SequenceRunResult result) => _completion.TrySetResult(result);

    public void Reject(StructuredError error) =>
        _completion.TrySetResult(SequenceRunResultFactory.CreateRejected(Request, error));
}

internal static class SequenceRunResultFactory
{
    public static SequenceRunResult CreateRejected(SequenceRunRequest request, StructuredError error)
    {
        var now = DateTimeOffset.UtcNow;
        return new SequenceRunResult
        {
            RunId = request.RunId,
            State = error.Category == ErrorCategory.Cancellation ? RunState.Stopped : RunState.Errored,
            Verdict = BusinessVerdict.NotEvaluated,
            StartedAtUtc = now,
            CompletedAtUtc = now,
            Invocations = [],
            Error = error,
            TestRecord = new TestRecord
            {
                RunId = request.RunId,
                EnvironmentMode = request.EnvironmentMode,
                Providers = [],
                SequenceId = request.SequenceId,
                SequenceVersion = request.SequenceVersion,
                TriggerSequenceNumber = request.Trigger.SequenceNumber,
                IdempotencyKey = request.Trigger.IdempotencyKey,
                StartedAtUtc = now,
                CompletedAtUtc = now
            }
        };
    }
}
