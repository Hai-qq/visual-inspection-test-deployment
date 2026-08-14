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
            wizardV2Window.ShowModelsStepForPreview();
            if (wizardV2Window.CurrentStepIndex != 2 || wizardV2Window.Steps.Take(2).Any(step => step.IsCompleted))
            {
                throw new InvalidOperationException("V2 直接跳到后续步骤时错误补绿了中间步骤。");
            }

            wizardV2Window.PreviousButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            wizardV2Window.PreviousButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            wizardV2Window.NextButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            if (wizardV2Window.CurrentStepIndex != 1 ||
                !wizardV2Window.Steps[0].IsCompleted ||
                wizardV2Window.Steps[1].IsCompleted)
            {
                throw new InvalidOperationException("V2 向导下一步交互冒烟失败。");
            }

            var originalProjectName = wizardV2Window.ProjectNameTextBox.Text;
            wizardV2Window.ProjectNameTextBox.Text = string.Empty;
            if (wizardV2Window.Steps[0].IsCompleted)
            {
                throw new InvalidOperationException("V2 已完成步骤的必填项清空后没有取消绿色完成状态。");
            }

            wizardV2Window.ProjectNameTextBox.Text = originalProjectName;
            if (!wizardV2Window.Steps[0].IsCompleted)
            {
                throw new InvalidOperationException("V2 已确认步骤恢复有效内容后没有恢复完成状态。");
            }

            wizardV2Window.UsbCameraSourceRadioButton.IsChecked = true;
            wizardV2Window.UpdateLayout();
            wizardV2Window.Dispatcher.Invoke(() => { }, DispatcherPriority.DataBind);
            var selectedSourceBrush = wizardV2Window.UsbCameraSourceRadioButton.Background as System.Windows.Media.SolidColorBrush;
            var expectedSourceBrush = wizardV2Window.FindResource("GreenPaleBrush") as System.Windows.Media.SolidColorBrush;
            if (wizardV2Window.UsbCameraSourceRadioButton.IsChecked != true ||
                wizardV2Window.FolderSourceRadioButton.IsChecked == true ||
                selectedSourceBrush?.Color != expectedSourceBrush?.Color)
            {
                throw new InvalidOperationException("V2 USB 图源卡选中及绿色状态冒烟失败。");
            }

            wizardV2Window.PreviousButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            if (wizardV2Window.CurrentStepIndex != 0)
            {
                throw new InvalidOperationException("V2 向导上一步交互冒烟失败。");
            }

            for (var index = 0; index < 2; index++)
            {
                wizardV2Window.NextButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            }

            if (wizardV2Window.CurrentStepIndex != 2)
            {
                throw new InvalidOperationException("V2 多模型步骤导航冒烟失败。");
            }

            var originalModelCount = wizardV2Window.ModelItemsList.Items.Count;
            if (originalModelCount < 3)
            {
                throw new InvalidOperationException("V2 项目模型库没有展示多个模型。");
            }

            wizardV2Window.AddModelButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            if (wizardV2Window.ModelItemsList.Items.Count != originalModelCount + 1 ||
                wizardV2Window.SelectedModel is null)
            {
                throw new InvalidOperationException("V2 模型库加号交互冒烟失败。");
            }

            wizardV2Window.NextButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            if (wizardV2Window.CurrentStepIndex != 2 || wizardV2Window.Steps[2].IsCompleted)
            {
                throw new InvalidOperationException("V2 未填写模型文件时没有阻止步骤完成。");
            }

            wizardV2Window.ModelTaskTypeComboBox.SelectedIndex = 3;
            wizardV2Window.Dispatcher.Invoke(() => { }, DispatcherPriority.DataBind);
            if (wizardV2Window.SelectedModel.TypeIndex != 3)
            {
                throw new InvalidOperationException("V2 当前模型类型切换冒烟失败。");
            }

            wizardV2Window.RemoveSelectedModelButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            if (wizardV2Window.ModelItemsList.Items.Count != originalModelCount)
            {
                throw new InvalidOperationException("V2 模型库减号交互冒烟失败。");
            }

            if (wizardV2Window.AddModelButton.ToolTip is null ||
                wizardV2Window.RemoveSelectedModelButton.ToolTip is null)
            {
                throw new InvalidOperationException("V2 模型库加减操作 ToolTip 冒烟失败。");
            }

            wizardV2Window.NextButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            wizardV2Window.UpdateLayout();
            wizardV2Window.Dispatcher.Invoke(() => { }, DispatcherPriority.DataBind);
            if (wizardV2Window.CurrentStepIndex != 3)
            {
                throw new InvalidOperationException("V2 向导顺序导航冒烟失败。");
            }

            if (wizardV2Window.InspectionModelComboBox.Items.Count != originalModelCount)
            {
                throw new InvalidOperationException("V2 检测项动态绑定项目模型库冒烟失败。");
            }

            var originalInspectionItemCount = wizardV2Window.InspectionItemsList.Items.Count;
            wizardV2Window.AddInspectionItemButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            if (wizardV2Window.InspectionItemsList.Items.Count != originalInspectionItemCount + 1)
            {
                throw new InvalidOperationException("V2 检测项加号交互冒烟失败。");
            }

            wizardV2Window.InspectionTypeComboBox.SelectedIndex = 1;
            wizardV2Window.Dispatcher.Invoke(() => { }, DispatcherPriority.DataBind);
            if (wizardV2Window.PoseContentPanel.Visibility != Visibility.Visible ||
                wizardV2Window.TargetContentPanel.Visibility != Visibility.Collapsed)
            {
                throw new InvalidOperationException("V2 姿态类型联动冒烟失败。");
            }

            wizardV2Window.RemoveSelectedInspectionItemButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            if (wizardV2Window.InspectionItemsList.Items.Count != originalInspectionItemCount)
            {
                throw new InvalidOperationException("V2 检测项减号交互冒烟失败。");
            }

            if (wizardV2Window.AddInspectionItemButton.ToolTip is null ||
                wizardV2Window.RemoveSelectedInspectionItemButton.ToolTip is null)
            {
                throw new InvalidOperationException("V2 加减操作 ToolTip 冒烟失败。");
            }

            wizardV2Window.NextButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            wizardV2Window.ShowTargetContentStepForPreview();
            wizardV2Window.UpdateLayout();
            wizardV2Window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
            if (wizardV2Window.RoiPreviewSurface.ActualWidth <= 0 ||
                wizardV2Window.RoiPreviewSurface.ActualHeight <= 0)
            {
                throw new InvalidOperationException("V2 ROI 预览区域未完成布局。");
            }

            var originalRoi = wizardV2Window.RoiLogicalRect;
            wizardV2Window.ApplyRoiSelectionForSmoke(
                new System.Windows.Point(
                    wizardV2Window.RoiPreviewSurface.ActualWidth * 0.15,
                    wizardV2Window.RoiPreviewSurface.ActualHeight * 0.20),
                new System.Windows.Point(
                    wizardV2Window.RoiPreviewSurface.ActualWidth * 0.75,
                    wizardV2Window.RoiPreviewSurface.ActualHeight * 0.80));
            if (wizardV2Window.RoiLogicalRect == originalRoi ||
                wizardV2Window.RoiLogicalRect.Width <= 0 ||
                wizardV2Window.RoiLogicalRect.Height <= 0 ||
                wizardV2Window.RoiSelectionRectangle.Visibility != Visibility.Visible ||
                !wizardV2Window.RoiCoordinatesText.Text.Contains("X1 96", StringComparison.Ordinal))
            {
                throw new InvalidOperationException("V2 ROI 拖拽框选与坐标回填冒烟失败。");
            }

            wizardV2Window.RedrawRoiButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            if (wizardV2Window.RedrawRoiButton.ToolTip is null ||
                !wizardV2Window.FooterHintText.Text.Contains("按住鼠标左键拖动", StringComparison.Ordinal))
            {
                throw new InvalidOperationException("V2 ROI 重新框选提示冒烟失败。");
            }

            wizardV2Window.NextButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            wizardV2Window.UpdateLayout();
            wizardV2Window.Dispatcher.Invoke(() => { }, DispatcherPriority.DataBind);
            if (wizardV2Window.CurrentStepIndex != 5 ||
                !wizardV2Window.TargetRuleSummaryText.Text.Contains("X1 96", StringComparison.Ordinal))
            {
                throw new InvalidOperationException("V2 判定条件没有继承当前 ROI 冒烟失败。");
            }

            wizardV2Window.TargetRuleMethodComboBox.SelectedIndex = 2;
            wizardV2Window.ExpectedCountTextBox.Text = "3";
            wizardV2Window.Dispatcher.Invoke(() => { }, DispatcherPriority.DataBind);
            if (!wizardV2Window.TargetRuleSummaryText.Text.Contains("数量大于 3", StringComparison.Ordinal) ||
                wizardV2Window.RangeMaximumPanel.Visibility != Visibility.Collapsed)
            {
                throw new InvalidOperationException("V2 数量大于判定摘要实时联动冒烟失败。");
            }

            wizardV2Window.TargetRuleMethodComboBox.SelectedIndex = 1;
            wizardV2Window.ExpectedCountTextBox.Text = "5";
            wizardV2Window.RangeMaximumCountTextBox.Text = "2";
            wizardV2Window.Dispatcher.Invoke(() => { }, DispatcherPriority.DataBind);
            if (wizardV2Window.RangeMaximumPanel.Visibility != Visibility.Visible ||
                !wizardV2Window.TargetRuleSummaryText.Text.Contains("最小数量不能大于最大数量", StringComparison.Ordinal))
            {
                throw new InvalidOperationException("V2 数量范围输入与错误摘要联动冒烟失败。");
            }

            wizardV2Window.NextButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            if (wizardV2Window.CurrentStepIndex != 5 || wizardV2Window.Steps[5].IsCompleted)
            {
                throw new InvalidOperationException("V2 无效数量范围未阻止下一步。");
            }

            wizardV2Window.ExpectedCountTextBox.Text = "1";
            wizardV2Window.RangeMaximumCountTextBox.Text = "4";
            var poseRuleItem = wizardV2Window.InspectionItems.First(item => item.TypeIndex == 1);
            wizardV2Window.InspectionItemsList.SelectedItem = poseRuleItem;
            wizardV2Window.PoseActionComboBox.SelectedIndex = 1;
            wizardV2Window.PoseHoldTimeTextBox.Text = "450";
            wizardV2Window.PoseMaxWaitTextBox.Text = "6000";
            wizardV2Window.Dispatcher.Invoke(() => { }, DispatcherPriority.DataBind);
            if (wizardV2Window.PoseRulePanel.Visibility != Visibility.Visible ||
                !wizardV2Window.PoseRuleSummaryText.Text.Contains("放置", StringComparison.Ordinal) ||
                !wizardV2Window.PoseRuleSummaryText.Text.Contains("450 ms", StringComparison.Ordinal) ||
                !wizardV2Window.PoseRuleSummaryText.Text.Contains("6000 ms", StringComparison.Ordinal))
            {
                throw new InvalidOperationException("V2 姿态判定摘要实时联动冒烟失败。");
            }

            var targetRuleItem = wizardV2Window.InspectionItems.First(item => item.TypeIndex == 0);
            wizardV2Window.InspectionItemsList.SelectedItem = targetRuleItem;
            wizardV2Window.Dispatcher.Invoke(() => { }, DispatcherPriority.DataBind);
            if (!wizardV2Window.TargetRuleSummaryText.Text.Contains("1 到 4", StringComparison.Ordinal))
            {
                throw new InvalidOperationException("V2 数量范围判定摘要实时联动冒烟失败。");
            }

            wizardV2Window.NextButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            wizardV2Window.NextButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            if (wizardV2Window.CurrentStepIndex != 7)
            {
                throw new InvalidOperationException("V2 八步向导最终页导航冒烟失败。");
            }

            if (!wizardV2Window.Steps.Take(7).All(step => step.IsCompleted) ||
                wizardV2Window.Steps[7].IsCompleted)
            {
                throw new InvalidOperationException("V2 最终确认前的步骤完成状态冒烟失败。");
            }

            wizardV2Window.NextButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            if (wizardV2Window.NextButton.IsEnabled ||
                wizardV2Window.PrototypeStatusBorder.Visibility != Visibility.Visible ||
                wizardV2Window.Steps.Any(step => !step.IsCompleted))
            {
                throw new InvalidOperationException("V2 向导最终确认状态冒烟失败。");
            }

            wizardV2Window.Close();
            mainWindow.Close();

            Directory.CreateDirectory(Path.GetDirectoryName(ReceiptPath)!);
            await File.WriteAllTextAsync(
                ReceiptPath,
                $"通过{Environment.NewLine}{DateTimeOffset.UtcNow:O}{Environment.NewLine}登录窗口 + 主窗口 + 图像源设置窗口 + 经典测试序列设置窗口 + 普通检测项新增/删除 + V2 八步向导跳步不补绿/必填失效退绿/未完成拦截/前后导航/USB 图源卡绿色选中/多模型库加减与类型切换/检测项动态模型绑定/检测项加减/姿态类型联动/ROI 拖拽框选与坐标回填/目标与姿态判定摘要实时联动/数量范围校验/ToolTip/最终确认",
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
