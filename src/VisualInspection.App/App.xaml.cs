using System.IO;
using System.Windows;
using VisualInspection.App.Demo;
using VisualInspection.App.Services;
using VisualInspection.App.ViewModels;
using VisualInspection.Core.Security;
using VisualInspection.Core.V2.Configuration;
using VisualInspection.Infrastructure.Persistence;
using VisualInspection.Infrastructure.V2.Persistence;

namespace VisualInspection.App;

public partial class App : Application
{
    protected override async void OnStartup(StartupEventArgs e)
    {
        ShutdownMode = System.Windows.ShutdownMode.OnExplicitShutdown;
        base.OnStartup(e);
        var sampleExportArgumentIndex = Array.FindIndex(
            e.Args,
            argument => argument.Equals("--export-sample-sequence", StringComparison.OrdinalIgnoreCase));
        var sequenceVerifyArgumentIndex = Array.FindIndex(
            e.Args,
            argument => argument.Equals("--verify-portable-sequence", StringComparison.OrdinalIgnoreCase));

        try
        {
            if (sampleExportArgumentIndex >= 0)
            {
                if (sampleExportArgumentIndex + 1 >= e.Args.Length)
                {
                    throw new ArgumentException("--export-sample-sequence 后必须提供 .sequence.json 输出路径。");
                }

                string modelPath;
                string imageDirectory;
                if (sampleExportArgumentIndex + 3 < e.Args.Length)
                {
                    modelPath = Path.GetFullPath(e.Args[sampleExportArgumentIndex + 2]);
                    var imagePath = Path.GetFullPath(e.Args[sampleExportArgumentIndex + 3]);
                    if (!File.Exists(modelPath) || !File.Exists(imagePath))
                    {
                        throw new FileNotFoundException("代表性 Fan 模型或图片不存在。");
                    }

                    imageDirectory = Path.GetDirectoryName(imagePath)!;
                }
                else
                {
                    var assets = await FrontendDemoAssetSeeder.EnsureAsync()
                        ?? throw new InvalidOperationException(
                            "当前构建未包含代表性 Fan 资源；请同时提供模型与图片路径。");
                    modelPath = assets.ModelPath;
                    imageDirectory = assets.ImageDirectory;
                }

                var project = SampleProjectFactory.Create(imageDirectory, modelPath);
                var portable = ProjectConfigurationV1Migrator.Migrate(project);
                await PortableSequenceFile.ExportAsync(portable, e.Args[sampleExportArgumentIndex + 1], AppContext.BaseDirectory);
                Shutdown(0);
                return;
            }

            if (sequenceVerifyArgumentIndex >= 0)
            {
                if (sequenceVerifyArgumentIndex + 1 >= e.Args.Length)
                {
                    throw new ArgumentException("--verify-portable-sequence 后必须提供 .sequence.json 路径。");
                }

                var verified = await ApplicationBootstrapper.LoadPortableSequenceAsync(
                    e.Args[sequenceVerifyArgumentIndex + 1]);
                if (verified.Project.TestSequences.Count != 1 || verified.Project.Models.Count == 0)
                {
                    throw new InvalidDataException("sequence 未形成一个可执行型号及对应模型。");
                }

                Shutdown(0);
                return;
            }

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
            var captureTrigger = e.Args.Contains(
                "--v2-wizard-trigger-snapshot",
                StringComparer.OrdinalIgnoreCase);
            if (captureInspectionItems || capturePoseContent || captureInputSource || captureModels || captureRoi || captureRule || captureTrigger ||
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
                    snapshot.ShowSourceStepForPreview();
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
                else if (captureTrigger)
                {
                    snapshot.ShowTriggerStepForPreview();
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
                                        : captureTrigger
                                            ? snapshot.SaveTriggerSnapshot
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
            var previewTrigger = e.Args.Contains(
                "--v2-wizard-trigger-preview",
                StringComparer.OrdinalIgnoreCase);
            if (previewInputSource || previewModels || previewRoi || previewRule || previewTrigger ||
                e.Args.Contains("--v2-wizard-preview", StringComparer.OrdinalIgnoreCase) ||
                e.Args.Contains("--frontend-demo", StringComparer.OrdinalIgnoreCase))
            {
                var preview = new TestSequenceWizardV2Window();
                MainWindow = preview;
                preview.Show();
                if (previewInputSource)
                {
                    preview.ShowSourceStepForPreview();
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
                else if (previewTrigger)
                {
                    preview.ShowTriggerStepForPreview();
                }

                ShutdownMode = System.Windows.ShutdownMode.OnLastWindowClose;
                return;
            }

            if (e.Args.Contains("--operator-preview", StringComparer.OrdinalIgnoreCase))
            {
                var previewBootstrap = await ApplicationBootstrapper.LoadOrCreateProjectAsync();
                var previewSession = new UserSession(
                    Guid.Empty,
                    "operator-preview",
                    "界面预览",
                    UserRole.Admin);
                var operatorPreview = new MainWindow(
                    new MainWindowViewModel(previewBootstrap, previewSession),
                    previewBootstrap,
                    previewSession)
                {
                    Title = "管理员 · 操作员工作台（界面预览）"
                };
                MainWindow = operatorPreview;
                operatorPreview.Show();
                ShutdownMode = System.Windows.ShutdownMode.OnLastWindowClose;
                return;
            }

            if (e.Args.Contains("--ui-construction-smoke", StringComparer.OrdinalIgnoreCase) ||
                e.Args.Contains("--ui-layout-review", StringComparer.OrdinalIgnoreCase))
            {
                Shutdown(await UiConstructionSmokeRunner.RunAsync(captureLayoutReview:
                    e.Args.Contains("--ui-layout-review", StringComparer.OrdinalIgnoreCase)));
                return;
            }

            if (e.Args.Contains("--acceptance-smoke", StringComparer.OrdinalIgnoreCase))
            {
                Shutdown(await AcceptanceSmokeRunner.RunAsync());
                return;
            }

            IUserAccountStore userStore = new JsonUserAccountStore(DemoUserSeeder.UserAccountFilePath);
            await DemoUserSeeder.EnsureAsync(userStore);
            var login = new LoginWindow(new AuthenticationService(userStore));
            if (login.ShowDialog() != true || login.Session is null)
            {
                Shutdown(0);
                return;
            }

            var result = await ApplicationBootstrapper.LoadOrCreateProjectAsync();
            var window = new MainWindow(new MainWindowViewModel(result, login.Session), result, login.Session);
            MainWindow = window;
            window.Show();
            ShutdownMode = System.Windows.ShutdownMode.OnLastWindowClose;
        }
        catch (Exception exception)
        {
            if (sampleExportArgumentIndex >= 0 || sequenceVerifyArgumentIndex >= 0)
            {
                Console.Error.WriteLine(exception);
                Shutdown(1);
                return;
            }

            MessageBox.Show(
                $"应用程序无法加载项目配置。{Environment.NewLine}{Environment.NewLine}{exception.Message}",
                "视觉检测测试部署系统",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(1);
        }
    }
}
