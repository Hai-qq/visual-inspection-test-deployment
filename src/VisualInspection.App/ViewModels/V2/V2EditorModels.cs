using VisualInspection.App.ViewModels;

namespace VisualInspection.App.ViewModels.V2;

public sealed class RulePreviewViewModel : ObservableObject
{
    private TestSequenceWizardV2Window.ModelPreview _model;
    private string _targetLabel;
    private int _ruleMethodIndex;
    private int _metricIndex;
    private string _thresholdText = "1";
    private string _upperThresholdText = "2";
    private string _confidenceText = "0.50";
    private int _outcomeIndex;

    public RulePreviewViewModel(
        string targetLabel,
        TestSequenceWizardV2Window.ModelPreview model,
        Guid? ruleId = null,
        Guid? modelBindingId = null)
    {
        RuleId = ruleId ?? Guid.NewGuid();
        ModelBindingId = modelBindingId ?? Guid.NewGuid();
        _model = model ?? throw new ArgumentNullException(nameof(model));
        _targetLabel = targetLabel;
    }

    public Guid RuleId { get; }
    public Guid ModelBindingId { get; }

    public TestSequenceWizardV2Window.ModelPreview Model
    {
        get => _model;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            if (SetProperty(ref _model, value) && !value.Labels.Contains(TargetLabel))
            {
                TargetLabel = value.Labels.FirstOrDefault() ?? string.Empty;
            }
        }
    }

    public string TargetLabel
    {
        get => _targetLabel;
        set => SetProperty(ref _targetLabel, value ?? string.Empty);
    }

    public int RuleMethodIndex
    {
        get => _ruleMethodIndex;
        set => SetProperty(ref _ruleMethodIndex, value);
    }

    public int MetricIndex
    {
        get => _metricIndex;
        set => SetProperty(ref _metricIndex, value);
    }

    public string ThresholdText
    {
        get => _thresholdText;
        set => SetProperty(ref _thresholdText, value ?? string.Empty);
    }

    public string UpperThresholdText
    {
        get => _upperThresholdText;
        set => SetProperty(ref _upperThresholdText, value ?? string.Empty);
    }

    public string ConfidenceText
    {
        get => _confidenceText;
        set => SetProperty(ref _confidenceText, value ?? string.Empty);
    }

    public int OutcomeIndex
    {
        get => _outcomeIndex;
        set => SetProperty(ref _outcomeIndex, value);
    }
}
