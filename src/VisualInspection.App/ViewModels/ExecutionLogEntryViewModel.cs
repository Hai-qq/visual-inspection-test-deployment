namespace VisualInspection.App.ViewModels;

public sealed record ExecutionLogEntryViewModel(
    DateTimeOffset Timestamp,
    string Level,
    string Message)
{
    public string TimeText => Timestamp.ToLocalTime().ToString("HH:mm:ss.fff");
    public string LevelText => Level switch
    {
        "INFO" => "信息",
        "WARN" => "警告",
        "ERROR" => "错误",
        _ => Level
    };
}
