using VisualInspection.Core.V2.Configuration;

namespace VisualInspection.Core.V2.Execution;

public sealed record TriggerSignalEvaluation
{
    public required bool IsTriggered { get; init; }
    public required bool StableLevel { get; init; }
    public required bool IsDebouncing { get; init; }
}

public sealed class TriggerSignalProcessor
{
    private readonly Dictionary<Guid, SignalState> _states = [];

    public TriggerSignalEvaluation Process(
        ExternalTriggerBinding binding,
        bool rawLevel,
        DateTimeOffset sampledAtUtc)
    {
        ArgumentNullException.ThrowIfNull(binding);
        if (!_states.TryGetValue(binding.BindingId, out var state))
        {
            state = new SignalState
            {
                RawLevel = rawLevel,
                CandidateSinceUtc = sampledAtUtc
            };
            _states[binding.BindingId] = state;
            if (binding.DebounceMs == 0)
            {
                state.IsInitialized = true;
                state.StableLevel = rawLevel;
                return Result(IsLevelCondition(binding.TriggerCondition, rawLevel), state, false);
            }

            return Result(false, state, true);
        }

        if (state.RawLevel != rawLevel)
        {
            state.RawLevel = rawLevel;
            state.CandidateSinceUtc = sampledAtUtc;
            if (binding.DebounceMs > 0)
            {
                return Result(false, state, true);
            }
        }

        if (state.IsInitialized && state.StableLevel == rawLevel)
        {
            return Result(false, state, false);
        }

        var stableFor = sampledAtUtc - state.CandidateSinceUtc;
        if (stableFor < TimeSpan.FromMilliseconds(binding.DebounceMs))
        {
            return Result(false, state, true);
        }

        var previous = state.StableLevel;
        var wasInitialized = state.IsInitialized;
        state.IsInitialized = true;
        state.StableLevel = rawLevel;
        var triggered = binding.TriggerCondition switch
        {
            TriggerCondition.RisingEdge => wasInitialized && !previous && rawLevel,
            TriggerCondition.FallingEdge => wasInitialized && previous && !rawLevel,
            TriggerCondition.HighLevel => rawLevel,
            TriggerCondition.LowLevel => !rawLevel,
            _ => false
        };
        return Result(triggered, state, false);
    }

    public void Reset(Guid bindingId) => _states.Remove(bindingId);

    private static bool IsLevelCondition(TriggerCondition condition, bool level) =>
        condition == TriggerCondition.HighLevel && level || condition == TriggerCondition.LowLevel && !level;

    private static TriggerSignalEvaluation Result(bool triggered, SignalState state, bool debouncing) => new()
    {
        IsTriggered = triggered,
        StableLevel = state.StableLevel,
        IsDebouncing = debouncing
    };

    private sealed class SignalState
    {
        public bool IsInitialized { get; set; }
        public bool RawLevel { get; set; }
        public bool StableLevel { get; set; }
        public DateTimeOffset CandidateSinceUtc { get; set; }
    }
}
