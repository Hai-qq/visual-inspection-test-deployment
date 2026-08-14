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
            var captureInspectionItems = e.Args.Contains(
                "--v2-wizard-items-snapshot",
                StringComparer.OrdinalIgnoreCase);
            var capturePoseContent = e.Args.Contains(
                "--v2-wizard-pose-snapshot",
                StringComparer.OrdinalIgnoreCase);
            var captureInputSource = e.Args.Contains(
                "--v2-wizard-source-snapshot",
                StringComparer.OrdinalIgnoreCase);
            var captureModels = e.Args.Contains(
                "--v2-wizard-models-snapshot",
                StringComparer.OrdinalIgnoreCase);
            var captureRoi = e.Args.Contains(
                "--v2-wizard-roi-snapshot",
                StringComparer.OrdinalIgnoreCase);
            var captureRule = e.Args.Contains(
                "--v2-wizard-rule-snapshot",
                StringComparer.OrdinalIgnoreCase);
            if (captureInspectionItems || capturePoseContent || captureInputSource || captureModels || captureRoi || captureRule ||
                e.Args.Contains("--v2-wizard-snapshot", StringComparer.OrdinalIgnoreCase))
            {
                var snapshot = new TestSequenceWizardV2Window();
                snapshot.Show();
                if (captureInspectionItems)
                {
                    snapshot.ShowInspectionItemsStepForPreview();
                }
                else if (capturePoseContent)
                {
                    snapshot.ShowPoseContentStepForPreview();
                }
                else if (captureInputSource)
                {
                    snapshot.ShowUsbSourceStepForPreview();
                }
                else if (captureModels)
                {
                    snapshot.ShowModelsStepForPreview();
                }
                else if (captureRoi)
                {
                    snapshot.ShowTargetContentStepForPreview();
                }
                else if (captureRule)
                {
                    snapshot.ShowTargetRuleStepForPreview();
                }

                await snapshot.Dispatcher.InvokeAsync(
                    capturePoseContent
                        ? snapshot.SavePoseSnapshot
                        : captureInputSource
                            ? snapshot.SaveSourceSnapshot
                            : captureModels
                                ? snapshot.SaveModelsSnapshot
                                : captureRoi
                                    ? snapshot.SaveRoiSnapshot
                                    : captureRule
                                        ? snapshot.SaveRuleSnapshot
                                    : snapshot.SaveSnapshot,
                    System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                snapshot.Close();
                Shutdown(0);
                return;
            }

            var previewInputSource = e.Args.Contains(
                "--v2-wizard-source-preview",
                StringComparer.OrdinalIgnoreCase);
            var previewModels = e.Args.Contains(
                "--v2-wizard-models-preview",
                StringComparer.OrdinalIgnoreCase);
            var previewRoi = e.Args.Contains(
                "--v2-wizard-roi-preview",
                StringComparer.OrdinalIgnoreCase);
            var previewRule = e.Args.Contains(
                "--v2-wizard-rule-preview",
                StringComparer.OrdinalIgnoreCase);
            if (previewInputSource || previewModels || previewRoi || previewRule ||
                e.Args.Contains("--v2-wizard-preview", StringComparer.OrdinalIgnoreCase))
            {
                var preview = new TestSequenceWizardV2Window();
                MainWindow = preview;
                preview.Show();
                if (previewInputSource)
                {
                    preview.ShowUsbSourceStepForPreview();
                }
                else if (previewModels)
                {
                    preview.ShowModelsStepForPreview();
                }
                else if (previewRoi)
                {
                    preview.ShowTargetContentStepForPreview();
                }
                else if (previewRule)
                {
                    preview.ShowTargetRuleStepForPreview();
                }

                ShutdownMode = System.Windows.ShutdownMode.OnLastWindowClose;
                return;
            }

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
