using System.Windows;
using VisualInspection.App.Services;
using VisualInspection.App.ViewModels;
using VisualInspection.Core.Security;

namespace VisualInspection.App;

public partial class MainWindow : Window
{
    private ApplicationBootstrapResult _bootstrap;
    private readonly UserSession _session;

    public MainWindow(MainWindowViewModel viewModel, ApplicationBootstrapResult bootstrap, UserSession? session = null)
    {
        InitializeComponent();
        _bootstrap = bootstrap;
        _session = session ?? new UserSession(Guid.Empty, "admin", "演示管理员", UserRole.Admin);
        ConfigureViewModel(viewModel);
    }

    private void ConfigureViewModel(MainWindowViewModel viewModel)
    {
        DataContext = viewModel;
    }

    private async void SequenceSettings_Click(object sender, RoutedEventArgs e)
    {
        if (!_session.IsAdmin)
        {
            MessageBox.Show(this, "只有管理员可以打开测试序列设置。", "权限不足", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var settings = new TestSequenceSettingsWindow(
            _bootstrap.Project,
            _bootstrap.Store,
            _bootstrap.PreviewFrame)
        {
            Owner = this
        };
        if (settings.ShowDialog() != true)
        {
            return;
        }

        await ReloadProjectAsync();
    }

    private async void InputSourceSettings_Click(object sender, RoutedEventArgs e)
    {
        if (!_session.IsAdmin)
        {
            MessageBox.Show(this, "只有管理员可以打开图源设置。", "权限不足", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var settings = new InputSourceSettingsWindow(
            _bootstrap.Project,
            _bootstrap.Store,
            _bootstrap.DemoDataDirectory)
        {
            Owner = this
        };
        if (settings.ShowDialog() != true)
        {
            return;
        }

        await ReloadProjectAsync();
    }

    private async Task ReloadProjectAsync()
    {
        _bootstrap = await ApplicationBootstrapper.LoadOrCreateProjectAsync();
        ConfigureViewModel(new MainWindowViewModel(_bootstrap, _session));
    }
}
