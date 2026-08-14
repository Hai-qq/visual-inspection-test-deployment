using System.Windows.Media;
using VisualInspection.Core.Domain;

namespace VisualInspection.App.ViewModels;

public sealed class TestSequenceItemViewModel(
    int number,
    string name,
    string standard,
    ExecutionState state = ExecutionState.Pending)
    : ObservableObject
{
    private ExecutionState _state = state;

    public int Number { get; } = number;
    public string Name { get; } = name;
    public string Standard { get; } = standard;

    public ExecutionState State
    {
        get => _state;
        set
        {
            if (SetProperty(ref _state, value))
            {
                OnPropertyChanged(nameof(ResultText));
                OnPropertyChanged(nameof(ResultBrush));
                OnPropertyChanged(nameof(IsCurrent));
            }
        }
    }

    public string ResultText => State switch
    {
        ExecutionState.Pass => "通过",
        ExecutionState.Fail => "不通过",
        ExecutionState.Error => "错误",
        ExecutionState.Running => "运行中",
        ExecutionState.Stopped => "已停止",
        _ => "待执行"
    };

    public Brush ResultBrush => State switch
    {
        ExecutionState.Pass => Brushes.SeaGreen,
        ExecutionState.Fail => Brushes.IndianRed,
        ExecutionState.Error => Brushes.DarkOrange,
        ExecutionState.Running => new SolidColorBrush(Color.FromRgb(61, 205, 88)),
        _ => Brushes.SlateGray
    };

    public bool IsCurrent => State == ExecutionState.Running;
}
