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
            var defaultWizardWidth = wizardV2Window.Width;
            wizardV2Window.Width = wizardV2Window.MinWidth;
            wizardV2Window.UpdateLayout();
            var stepsOrigin = wizardV2Window.WizardStepsItemsControl.TranslatePoint(
                new Point(0, 0),
                wizardV2Window.WizardStepsScrollViewer);
            var stepsLeftGap = stepsOrigin.X;
            var stepsRightGap = wizardV2Window.WizardStepsScrollViewer.ViewportWidth -
                stepsOrigin.X - wizardV2Window.WizardStepsItemsControl.ActualWidth;
            if (Math.Abs(stepsLeftGap - stepsRightGap) > 1.5)
            {
                throw new InvalidOperationException("V2 顶部五步导航没有作为一个整体居中。");
            }

            wizardV2Window.Width = defaultWizardWidth;
            wizardV2Window.UpdateLayout();

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

            var testBlockModuleButtons = new[]
            {
                wizardV2Window.TestBlockBasicStageButton,
                wizardV2Window.TestBlockContentStageButton,
                wizardV2Window.TestBlockRuleStageButton,
                wizardV2Window.TestBlockTriggerStageButton
            };
            if (testBlockModuleButtons.Any(button =>
                    button.Content is not string label ||
                    string.IsNullOrWhiteSpace(label) ||
                    char.IsDigit(label[0])))
            {
                throw new InvalidOperationException("V2 测试步功能页签不应显示二级步骤编号。");
            }

            wizardV2Window.Width = wizardV2Window.MinWidth;
            wizardV2Window.UpdateLayout();
            foreach (var button in testBlockModuleButtons)
            {
                var buttonOrigin = button.TranslatePoint(new Point(0, 0), wizardV2Window.TestBlockStageBar);
                if (buttonOrigin.X < 0 ||
                    buttonOrigin.X + button.ActualWidth > wizardV2Window.TestBlockStageBar.ActualWidth + 0.5)
                {
                    throw new InvalidOperationException("V2 测试步功能页签超出编辑器边框。");
                }
            }

            wizardV2Window.Width = defaultWizardWidth;
            wizardV2Window.UpdateLayout();

            wizardV2Window.TestBlockContentStageButton.RaiseEvent(
                new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            if (wizardV2Window.CurrentTestBlockStageIndex != 1 ||
                wizardV2Window.Step5Panel.Visibility != Visibility.Visible)
            {
                throw new InvalidOperationException("V2 测试步功能页签导航冒烟失败。");
            }

            wizardV2Window.TestBlockBasicStageButton.RaiseEvent(
                new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));

            if (wizardV2Window.InspectionModelComboBox.Items.Count != originalModelCount)
            {
                throw new InvalidOperationException("V2 测试步动态绑定项目模型库冒烟失败。");
            }

            var originalInspectionItemCount = wizardV2Window.InspectionItemsList.Items.Count;
            var originalFunctionCodes = wizardV2Window.InspectionItems
                .Select(item => item.FunctionCode)
                .ToArray();
            wizardV2Window.AddInspectionItemButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            if (wizardV2Window.InspectionItemsList.Items.Count != originalInspectionItemCount + 1 ||
                wizardV2Window.SelectedInspectionItem is not { FunctionCode: var addedFunctionCode } ||
                originalFunctionCodes.Contains(addedFunctionCode, StringComparer.Ordinal))
            {
                throw new InvalidOperationException("V2 测试步加号与稳定函数标识交互冒烟失败。");
            }

            wizardV2Window.InspectionTypeComboBox.SelectedIndex = 1;
            wizardV2Window.Dispatcher.Invoke(() => { }, DispatcherPriority.DataBind);
            if (wizardV2Window.PoseContentPanel.Visibility != Visibility.Visible ||
                wizardV2Window.TargetContentPanel.Visibility != Visibility.Collapsed)
            {
                throw new InvalidOperationException("V2 姿态类型联动冒烟失败。");
            }

            wizardV2Window.RemoveSelectedInspectionItemButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            if (wizardV2Window.InspectionItemsList.Items.Count != originalInspectionItemCount ||
                !wizardV2Window.InspectionItems.Select(item => item.FunctionCode).SequenceEqual(originalFunctionCodes) ||
                !wizardV2Window.InspectionItemsSemanticsText.Text.Contains("不设执行顺序", StringComparison.Ordinal))
            {
                throw new InvalidOperationException("V2 测试步无序集合的加减交互冒烟失败。");
            }

            if (wizardV2Window.AddInspectionItemButton.ToolTip is null ||
                wizardV2Window.RemoveSelectedInspectionItemButton.ToolTip is null)
            {
                throw new InvalidOperationException("V2 加减操作 ToolTip 冒烟失败。");
            }

            var poseContentItem = wizardV2Window.InspectionItems.First(item => item.TypeIndex == 1);
            wizardV2Window.InspectionItemsList.SelectedItem = poseContentItem;
            wizardV2Window.TestBlockContentStageButton.RaiseEvent(
                new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            wizardV2Window.Dispatcher.Invoke(() => { }, DispatcherPriority.DataBind);
            var originalPoseStepNames = poseContentItem.PoseSteps.Select(step => step.Name).ToArray();
            var firstPoseStep = poseContentItem.PoseSteps[0];
            wizardV2Window.MovePoseStepForSmoke(firstPoseStep, 1);
            if (poseContentItem.PoseSteps[1] != firstPoseStep ||
                poseContentItem.PoseSteps.Select(step => step.Order).Where((order, index) => order != index + 1).Any() ||
                wizardV2Window.PoseContentPanel.Visibility != Visibility.Visible ||
                !wizardV2Window.PoseOrderHeadingText.Text.Contains("执行顺序", StringComparison.Ordinal) ||
                wizardV2Window.AddPoseStepButton.ToolTip is null)
            {
                throw new InvalidOperationException("V2 姿态动作排序与连续编号冒烟失败。");
            }

            wizardV2Window.MovePoseStepForSmoke(firstPoseStep, -1);
            if (!poseContentItem.PoseSteps.Select(step => step.Name).SequenceEqual(originalPoseStepNames))
            {
                throw new InvalidOperationException("V2 姿态动作排序恢复冒烟失败。");
            }

            wizardV2Window.ShowTargetContentStepForPreview();
            wizardV2Window.UpdateLayout();
            wizardV2Window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
            if (wizardV2Window.CurrentStepIndex != 3 ||
                wizardV2Window.CurrentTestBlockStageIndex != 1 ||
                wizardV2Window.Steps.Count != 5 ||
                wizardV2Window.RoiPreviewSurface.ActualWidth <= 0 ||
                wizardV2Window.RoiPreviewSurface.ActualHeight <= 0)
            {
                throw new InvalidOperationException("V2 五步向导的测试步检测内容模块未完成布局。");
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

            wizardV2Window.ShowTargetRuleStepForPreview();
            wizardV2Window.UpdateLayout();
            wizardV2Window.Dispatcher.Invoke(() => { }, DispatcherPriority.DataBind);
            if (wizardV2Window.CurrentStepIndex != 3 ||
                wizardV2Window.CurrentTestBlockStageIndex != 2 ||
                !wizardV2Window.TargetRuleSummaryText.Text.Contains("X1 96", StringComparison.Ordinal))
            {
                throw new InvalidOperationException("V2 测试步判定条件模块没有继承当前 ROI 冒烟失败。");
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
            if (wizardV2Window.CurrentStepIndex != 3 ||
                wizardV2Window.CurrentTestBlockStageIndex != 2 ||
                wizardV2Window.Steps[3].IsCompleted)
            {
                throw new InvalidOperationException("V2 测试步内的无效数量范围未阻止整体完成。");
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

            wizardV2Window.ShowTriggerStepForPreview();
            wizardV2Window.Dispatcher.Invoke(() => { }, DispatcherPriority.DataBind);
            if (wizardV2Window.CurrentStepIndex != 3 ||
                wizardV2Window.CurrentTestBlockStageIndex != 3 ||
                wizardV2Window.ExternalTriggerFieldsPanel.Visibility != Visibility.Visible ||
                !wizardV2Window.FunctionContractSummaryText.Text.Contains("PLC.Line1.FanPresent", StringComparison.Ordinal) ||
                !wizardV2Window.FunctionContractSummaryText.Text.Contains("上升沿", StringComparison.Ordinal))
            {
                throw new InvalidOperationException("V2 测试步外部触发接口与函数摘要冒烟失败。");
            }

            wizardV2Window.TriggerSignalTextBox.Text = string.Empty;
            wizardV2Window.NextButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            if (wizardV2Window.CurrentStepIndex != 3 ||
                wizardV2Window.CurrentTestBlockStageIndex != 3 ||
                wizardV2Window.Steps[3].IsCompleted)
            {
                throw new InvalidOperationException("V2 外部触发点位为空时没有阻止测试步整体完成。");
            }

            wizardV2Window.TriggerSignalTextBox.Text = "PLC.Line1.FanPresent";
            wizardV2Window.TriggerConditionComboBox.SelectedIndex = 1;
            wizardV2Window.TriggerDebounceTextBox.Text = "80";
            wizardV2Window.DefaultDelayTextBox.Text = "250";
            wizardV2Window.FunctionTimeoutTextBox.Text = "7000";
            wizardV2Window.Dispatcher.Invoke(() => { }, DispatcherPriority.DataBind);
            if (!wizardV2Window.FunctionContractSummaryText.Text.Contains("下降沿", StringComparison.Ordinal) ||
                !wizardV2Window.FunctionContractSummaryText.Text.Contains("去抖 80 ms", StringComparison.Ordinal) ||
                !wizardV2Window.FunctionContractSummaryText.Text.Contains("延时 250 ms", StringComparison.Ordinal) ||
                !wizardV2Window.FunctionContractSummaryText.Text.Contains("超时 7000 ms", StringComparison.Ordinal))
            {
                throw new InvalidOperationException("V2 测试步触发运行参数没有实时更新函数摘要。");
            }

            var sequentialItem = wizardV2Window.InspectionItems.First(item => item.TriggerModeIndex == 0);
            wizardV2Window.InspectionItemsList.SelectedItem = sequentialItem;
            wizardV2Window.Dispatcher.Invoke(() => { }, DispatcherPriority.DataBind);
            if (wizardV2Window.ExternalTriggerFieldsPanel.Visibility != Visibility.Collapsed ||
                wizardV2Window.DefaultDelayTextBox.Text != "200")
            {
                throw new InvalidOperationException("V2 切换测试步后没有恢复该功能独立的触发运行参数。");
            }

            var externalItem = wizardV2Window.InspectionItems.First(item => item.TriggerModeIndex == 1);
            wizardV2Window.InspectionItemsList.SelectedItem = externalItem;
            wizardV2Window.Dispatcher.Invoke(() => { }, DispatcherPriority.DataBind);
            if (wizardV2Window.ExternalTriggerFieldsPanel.Visibility != Visibility.Visible ||
                wizardV2Window.DefaultDelayTextBox.Text != "250")
            {
                throw new InvalidOperationException("V2 返回外部触发测试步后独立运行参数未保留。");
            }

            wizardV2Window.NextButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            if (wizardV2Window.CurrentStepIndex != 4 ||
                !wizardV2Window.Steps.Take(4).All(step => step.IsCompleted) ||
                wizardV2Window.Steps[4].IsCompleted ||
                !wizardV2Window.ReviewTriggerSummaryText.Text.Contains("外部 1", StringComparison.Ordinal))
            {
                throw new InvalidOperationException("V2 五步向导最终页及测试步触发汇总冒烟失败。");
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
                $"通过{Environment.NewLine}{DateTimeOffset.UtcNow:O}{Environment.NewLine}登录窗口 + 主窗口 + 图像源设置窗口 + 经典测试序列设置窗口 + 普通检测项新增/删除 + V2 五步导航整体居中/跳步不补绿/必填失效退绿/未完成拦截/前后导航/USB 图源卡绿色选中/多模型库加减与类型切换/测试步动态模型绑定与无序加减/稳定函数标识/无编号功能页签整合且不越框/姿态动作显式排序与连续编号/ROI 拖拽框选与坐标回填/目标与姿态判定摘要实时联动/数量范围校验/PLC-IO-传感器外部触发契约/去抖-延时-超时校验/ToolTip/最终确认",
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
