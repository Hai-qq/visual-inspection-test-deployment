using System.Windows;
using VisualInspection.App.Services;
using VisualInspection.App.ViewModels;
using VisualInspection.Core.Security;
using VisualInspection.Infrastructure.Persistence;

namespace VisualInspection.App;

public partial class App : Application
{
    protected override async void OnStartup(StartupEventArgs e)
    {
        ShutdownMode = System.Windows.ShutdownMode.OnExplicitShutdown;
        base.OnStartup(e);

        try
        {
            if (e.Args.Contains("--ui-construction-smoke", StringComparer.OrdinalIgnoreCase))
            {
                Shutdown(await UiConstructionSmokeRunner.RunAsync());
                return;
            }

            if (e.Args.Contains("--acceptance-smoke", StringComparer.OrdinalIgnoreCase))
            {
                Shutdown(await AcceptanceSmokeRunner.RunAsync());
                return;
            }

            var result = await ApplicationBootstrapper.LoadOrCreateProjectAsync();
            IUserAccountStore userStore = new JsonUserAccountStore(DemoUserSeeder.UserAccountFilePath);
            await DemoUserSeeder.EnsureAsync(userStore);
            var login = new LoginWindow(new AuthenticationService(userStore));
            if (login.ShowDialog() != true || login.Session is null)
            {
                Shutdown(0);
                return;
            }

            var window = new MainWindow(new MainWindowViewModel(result, login.Session), result, login.Session);
            MainWindow = window;
            window.Show();
            ShutdownMode = System.Windows.ShutdownMode.OnLastWindowClose;
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                $"应用程序无法加载项目配置。{Environment.NewLine}{Environment.NewLine}{exception.Message}",
                "视觉检测测试部署系统",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(1);
        }
    }
}
