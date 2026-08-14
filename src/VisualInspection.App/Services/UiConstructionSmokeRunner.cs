using System.IO;
using System.Windows;
using System.Windows.Threading;
using VisualInspection.App.Demo;
using VisualInspection.App.ViewModels;
using VisualInspection.Core.Security;
using VisualInspection.Infrastructure.Persistence;

namespace VisualInspection.App.Services;

public static class UiConstructionSmokeRunner
{
    public static string ReceiptPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "VisualInspectionTestDeployment",
        "ui-construction-smoke.txt");

    public static async Task<int> RunAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var bootstrap = await ApplicationBootstrapper.LoadOrCreateProjectAsync(cancellationToken);
            IUserAccountStore userStore = new JsonUserAccountStore(DemoUserSeeder.UserAccountFilePath);
            await DemoUserSeeder.EnsureAsync(userStore, cancellationToken);
            var authenticationService = new AuthenticationService(userStore);
            var loginWindow = new LoginWindow(authenticationService);
            var adminSession = await authenticationService.AuthenticateAsync(
                DemoUserSeeder.AdminUsername,
                DemoUserSeeder.AdminPassword,
                cancellationToken) ?? throw new InvalidOperationException("无法创建管理员渲染会话。");
            var mainWindow = new MainWindow(new MainWindowViewModel(bootstrap, adminSession), bootstrap, adminSession);
            var settingsWindow = new InputSourceSettingsWindow(
                bootstrap.Project,
                bootstrap.Store,
                bootstrap.DemoDataDirectory);
            var sequenceSettingsWindow = new TestSequenceSettingsWindow(
                SampleProjectFactory.Create(bootstrap.DemoDataDirectory),
                bootstrap.Store,
                bootstrap.PreviewFrame);
            var wizardV2Window = new TestSequenceWizardV2Window();
            loginWindow.Show();
            loginWindow.UpdateLayout();
            loginWindow.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
            loginWindow.Close();
            mainWindow.Show();
            mainWindow.UpdateLayout();
            mainWindow.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
            settingsWindow.Show();
            settingsWindow.UpdateLayout();
            settingsWindow.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
            settingsWindow.Close();
            sequenceSettingsWindow.Show();
            sequenceSettingsWindow.UpdateLayout();
            sequenceSettingsWindow.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
            var originalNormalItemCount = sequenceSettingsWindow.NormalItemsList.Items.Count;
            sequenceSettingsWindow.AddNormalItemButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            if (sequenceSettingsWindow.NormalItemsList.Items.Count != originalNormalItemCount + 1)
            {
                throw new InvalidOperationException("普通检测项新增交互冒烟失败。");
            }

            sequenceSettingsWindow.DeleteNormalItemButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            if (sequenceSettingsWindow.NormalItemsList.Items.Count != originalNormalItemCount)
            {
                throw new InvalidOperationException("普通检测项删除交互冒烟失败。");
            }

            sequenceSettingsWindow.Close();
            wizardV2Window.Show();
            wizardV2Window.UpdateLayout();
            wizardV2Window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
            wizardV2Window.NextButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            if (wizardV2Window.CurrentStepIndex != 1)
            {
                throw new InvalidOperationException("V2 向导下一步交互冒烟失败。");
            }

            wizardV2Window.PreviousButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            if (wizardV2Window.CurrentStepIndex != 0)
            {
                throw new InvalidOperationException("V2 向导上一步交互冒烟失败。");
            }

            for (var index = 0; index < 6; index++)
            {
                wizardV2Window.NextButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            }

            if (wizardV2Window.CurrentStepIndex != 6)
            {
                throw new InvalidOperationException("V2 向导顺序导航冒烟失败。");
            }

            wizardV2Window.SkipPoseButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            if (wizardV2Window.CurrentStepIndex != 7)
            {
                throw new InvalidOperationException("V2 向导姿态跳过交互冒烟失败。");
            }

            wizardV2Window.NextButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            wizardV2Window.NextButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            if (wizardV2Window.NextButton.IsEnabled || wizardV2Window.PrototypeStatusBorder.Visibility != Visibility.Visible)
            {
                throw new InvalidOperationException("V2 向导最终确认状态冒烟失败。");
            }

            wizardV2Window.Close();
            mainWindow.Close();

            Directory.CreateDirectory(Path.GetDirectoryName(ReceiptPath)!);
            await File.WriteAllTextAsync(
                ReceiptPath,
                $"通过{Environment.NewLine}{DateTimeOffset.UtcNow:O}{Environment.NewLine}登录窗口 + 主窗口 + 图像源设置窗口 + 经典测试序列设置窗口 + 普通检测项新增/删除 + V2 向导前后导航/姿态跳过/最终确认",
                cancellationToken);
            return 0;
        }
        catch (Exception exception)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ReceiptPath)!);
            await File.WriteAllTextAsync(
                ReceiptPath,
                $"失败{Environment.NewLine}{DateTimeOffset.UtcNow:O}{Environment.NewLine}{exception}",
                CancellationToken.None);
            return 4;
        }
    }
}
