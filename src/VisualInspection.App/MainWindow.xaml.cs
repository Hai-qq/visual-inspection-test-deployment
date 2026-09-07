using System.Windows;
using Microsoft.Win32;
using VisualInspection.App.Services;
using VisualInspection.App.ViewModels;
using VisualInspection.Core.Security;

namespace VisualInspection.App;

public partial class MainWindow : Window
{
    private readonly UserSession _session;
    private readonly ApplicationBootstrapResult _bootstrap;

    public MainWindow(MainWindowViewModel viewModel, ApplicationBootstrapResult bootstrap, UserSession? session = null)
    {
        InitializeComponent();
        _session = session ?? new UserSession(Guid.Empty, "admin", "演示管理员", UserRole.Admin);
        _bootstrap = bootstrap;
        ConfigureViewModel(viewModel);
    }

    private void ConfigureViewModel(MainWindowViewModel viewModel)
    {
        viewModel.RequestSerialNumber = () =>
        {
            var dialog = new SerialNumberDialog { Owner = this };
            return dialog.ShowDialog() == true ? dialog.SerialNumber : null;
        };
        DataContext = viewModel;
    }

    private async void ImportSequence_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "导入测试序列",
            Filter = "Visual Inspection sequence (*.sequence.json)|*.sequence.json|JSON 文件 (*.json)|*.json",
            CheckFileExists = true,
            Multiselect = false
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            var imported = await ApplicationBootstrapper.LoadPortableSequenceAsync(dialog.FileName);
            var replacement = new MainWindow(
                new MainWindowViewModel(imported, _session),
                imported,
                _session);
            Application.Current.MainWindow = replacement;
            replacement.Show();
            Close();
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                this,
                $"测试序列导入失败。{Environment.NewLine}{Environment.NewLine}{exception.Message}",
                "导入测试序列",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private async void SequenceSettings_Click(object sender, RoutedEventArgs e)
    {
        if (!_session.IsAdmin)
        {
            MessageBox.Show(this, "只有管理员可以打开测试序列设置。", "权限不足", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var settings = new TestSequenceWizardV2Window(returnToOperatorOnCompletion: true, _bootstrap.Project)
        {
            Owner = this
        };
        settings.ShowDialog();
        if (settings.AppliedProject is null)
        {
            return;
        }

        try
        {
            var applied = await ApplicationBootstrapper.LoadConfiguredSequenceAsync(settings.AppliedProject);
            var replacement = new MainWindow(
                new MainWindowViewModel(applied, _session),
                applied,
                _session);
            Application.Current.MainWindow = replacement;
            replacement.Show();
            Close();
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                this,
                $"当前配置加载失败，操作台仍保留原配置。{Environment.NewLine}{Environment.NewLine}{exception.Message}",
                "应用测试序列",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

}
