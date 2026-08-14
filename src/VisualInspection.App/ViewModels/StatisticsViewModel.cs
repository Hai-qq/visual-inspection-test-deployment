namespace VisualInspection.App.ViewModels;

public sealed class StatisticsViewModel : ObservableObject
{
    private int _passCount;
    private int _failCount;
    private int _errorCount;
    private bool _isCountMode = true;

    public int PassCount
    {
        get => _passCount;
        set
        {
            if (SetProperty(ref _passCount, value)) NotifyCalculatedValues();
        }
    }

    public int FailCount
    {
        get => _failCount;
        set
        {
            if (SetProperty(ref _failCount, value)) NotifyCalculatedValues();
        }
    }

    public int ErrorCount
    {
        get => _errorCount;
        set
        {
            if (SetProperty(ref _errorCount, value)) NotifyCalculatedValues();
        }
    }

    public int ValidCount => PassCount + FailCount;
    public int TotalCount => ValidCount;
    public bool HasValidResults => ValidCount > 0;
    public double PassRate => ValidCount == 0 ? 0 : (double)PassCount / ValidCount;
    public double FailRate => ValidCount == 0 ? 0 : (double)FailCount / ValidCount;
    public string PassRateText => HasValidResults ? $"{PassRate:P1}" : "--";
    public double PassBarWidth => ValidCount == 0 ? 0 : 256d * PassCount / ValidCount;
    public double FailBarWidth => ValidCount == 0 ? 0 : 256d * FailCount / ValidCount;

    public bool IsCountMode
    {
        get => _isCountMode;
        set
        {
            if (SetProperty(ref _isCountMode, value)) OnPropertyChanged(nameof(IsRateMode));
        }
    }

    public bool IsRateMode
    {
        get => !IsCountMode;
        set
        {
            if (value) IsCountMode = false;
        }
    }

    private void NotifyCalculatedValues()
    {
        OnPropertyChanged(nameof(ValidCount));
        OnPropertyChanged(nameof(TotalCount));
        OnPropertyChanged(nameof(HasValidResults));
        OnPropertyChanged(nameof(PassRate));
        OnPropertyChanged(nameof(FailRate));
        OnPropertyChanged(nameof(PassRateText));
        OnPropertyChanged(nameof(PassBarWidth));
        OnPropertyChanged(nameof(FailBarWidth));
    }
}
