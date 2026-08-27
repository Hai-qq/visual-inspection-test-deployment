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
            if (mainWindow.StartActionText.Text != "开始" ||
                mainWindow.StopActionText.Text != "停止" ||
                mainWindow.ResetActionText.Text != "复位" ||
                mainWindow.StartActionButton.FontSize < 14 ||
                mainWindow.StartActionButton.ActualHeight < 48 ||
                mainWindow.CurrentItemDetailsScrollViewer.ActualHeight < 130 ||
                mainWindow.SerialNumberInputTextBox.ActualHeight < 30 ||
                mainWindow.QualificationRateModeRadioButton.Content?.ToString() != "合格率" ||
                mainWindow.QualificationRateTitleText.Text != "合格率")
            {
                throw new InvalidOperationException("操作员按钮、详情滚动、序列号入口或合格率文案布局冒烟失败。");
            }

            var mainWindowViewModel = (MainWindowViewModel)mainWindow.DataContext;
            var adminHeaderButtons = mainWindow.HeaderAdminActionsPanel.Children
                .OfType<System.Windows.Controls.Button>()
                .ToArray();
            if (adminHeaderButtons.Length != 1 ||
                !ReferenceEquals(adminHeaderButtons[0], mainWindow.SequenceSettingsButton) ||
                mainWindow.SequenceSettingsButton.Content?.ToString() != "测试序列设置 V2")
            {
                throw new InvalidOperationException("操作员工作台应只保留测试序列设置入口，不应显示独立图源设置。");
            }

            if (mainWindowViewModel.StartCommand.CanExecute(null))
            {
                throw new InvalidOperationException("未录入序列号时，操作员不应能够开始检测。");
            }

            mainWindowViewModel.SerialNumberInput = "SN-SMOKE-001";
            mainWindow.Dispatcher.Invoke(() => { }, DispatcherPriority.DataBind);
            if (!mainWindowViewModel.SerialNumberStatusText.Contains("只检测一个产品 / 一张图片", StringComparison.Ordinal) ||
                !mainWindowViewModel.StartCommand.CanExecute(null))
            {
                throw new InvalidOperationException("操作员序列号门禁或单件检测提示未生效。");
            }

            if (Math.Abs(MainWindowViewModel.DetectionBorderThickness - 4) > 0.01 ||
                Math.Abs(MainWindowViewModel.GetOverlayScale(1920, 1080) - 3) > 0.01)
            {
                throw new InvalidOperationException("操作员检测框尺寸及分辨率自适应参数冒烟失败。");
            }

            var integratedWizard = new TestSequenceWizardV2Window(returnToOperatorOnCompletion: true);
            if (integratedWizard.ClosePreviewButton.Content?.ToString() != "返回操作台" ||
                integratedWizard.PreviewModeText.Text != "前端界面确认稿" ||
                integratedWizard.WizardNavigationPanel.Children.Count != 2)
            {
                throw new InvalidOperationException("V2 与操作员工作台的前端确认入口或精简导航冒烟失败。");
            }

            integratedWizard.Close();
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
            foreach (var model in wizardV2Window.Models)
            {
                model.Version = "1.0.0-smoke";
                model.Sha256 = new string('a', 64);
            }

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

            var originalFolderSourceAddress = wizardV2Window.FolderPathTextBox.Text;
            if (wizardV2Window.UsbCameraSourceRadioButton.IsEnabled ||
                wizardV2Window.IndustrialCameraSourceRadioButton.IsEnabled ||
                wizardV2Window.UsbCameraSourceRadioButton.IsChecked == true ||
                wizardV2Window.IndustrialCameraSourceRadioButton.IsChecked == true)
            {
                throw new InvalidOperationException("V2 USB / 工业相机图源卡没有保持待开发禁用状态。");
            }

            wizardV2Window.VideoFolderSourceRadioButton.IsChecked = true;
            wizardV2Window.UpdateLayout();
            wizardV2Window.Dispatcher.Invoke(() => { }, DispatcherPriority.DataBind);
            var selectedSourceBrush = wizardV2Window.VideoFolderSourceRadioButton.Background as System.Windows.Media.SolidColorBrush;
            var expectedSourceBrush = wizardV2Window.FindResource("GreenPaleBrush") as System.Windows.Media.SolidColorBrush;
            if (wizardV2Window.VideoFolderSourceRadioButton.IsChecked != true ||
                wizardV2Window.FolderSourceRadioButton.IsChecked == true ||
                selectedSourceBrush?.Color != expectedSourceBrush?.Color ||
                wizardV2Window.FolderSourceSettingsPanel.Visibility != Visibility.Visible ||
                !wizardV2Window.FolderSourceSettingsTitleText.Text.Contains("视频", StringComparison.Ordinal) ||
                wizardV2Window.Editor.SourceKindIndex != 3 ||
                wizardV2Window.FolderPathTextBox.Text.Length != 0)
            {
                throw new InvalidOperationException("V2 视频文件夹没有作为独立图源切换到对应路径面板。");
            }

            const string videoFolderSourceAddress = @"C:\检测视频\Fan";
            wizardV2Window.FolderPathTextBox.Text = videoFolderSourceAddress;
            wizardV2Window.FolderSourceRadioButton.IsChecked = true;
            wizardV2Window.Dispatcher.Invoke(() => { }, DispatcherPriority.DataBind);
            if (wizardV2Window.FolderSourceRadioButton.IsChecked != true ||
                wizardV2Window.VideoFolderSourceRadioButton.IsChecked == true ||
                !wizardV2Window.FolderSourceSettingsTitleText.Text.Contains("图片", StringComparison.Ordinal) ||
                wizardV2Window.FolderPathTextBox.Text != originalFolderSourceAddress)
            {
                throw new InvalidOperationException("V2 图片与视频图源没有保持互斥，或图片文件夹路径未恢复。");
            }

            wizardV2Window.VideoFolderSourceRadioButton.IsChecked = true;
            wizardV2Window.Dispatcher.Invoke(() => { }, DispatcherPriority.DataBind);
            if (wizardV2Window.FolderSourceRadioButton.IsChecked == true ||
                wizardV2Window.FolderPathTextBox.Text != videoFolderSourceAddress)
            {
                throw new InvalidOperationException("V2 视频文件夹路径在切换图源后没有独立保留。");
            }

            wizardV2Window.FolderSourceRadioButton.IsChecked = true;
            wizardV2Window.Dispatcher.Invoke(() => { }, DispatcherPriority.DataBind);
            if (wizardV2Window.FolderSourceSettingsPanel.Visibility != Visibility.Visible ||
                wizardV2Window.FolderPathTextBox.Text != originalFolderSourceAddress)
            {
                throw new InvalidOperationException("V2 返回图片文件夹后没有恢复路径配置面板。");
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

            if (System.Windows.Automation.AutomationProperties.GetName(wizardV2Window.RemoveSelectedModelButton) != "删除当前模型" ||
                wizardV2Window.RemoveSelectedModelButton.Visibility != Visibility.Visible)
            {
                throw new InvalidOperationException("V2 模型列表没有明确显示删除当前模型入口。");
            }

            wizardV2Window.NextButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            if (wizardV2Window.CurrentStepIndex != 2 || wizardV2Window.Steps[2].IsCompleted)
            {
                throw new InvalidOperationException("V2 未填写模型文件时没有阻止步骤完成。");
            }

            if (wizardV2Window.ModelTaskTypeComboBox.Items.Count != 3 ||
                wizardV2Window.ModelTaskTypeComboBox.Items.Cast<System.Windows.Controls.ComboBoxItem>()
                    .Any(item => item.Content?.ToString()?.Contains("分类", StringComparison.Ordinal) == true))
            {
                throw new InvalidOperationException("V2 模型任务类型仍包含已移除的图像分类。");
            }

            wizardV2Window.ModelTaskTypeComboBox.SelectedIndex = 2;
            wizardV2Window.Dispatcher.Invoke(() => { }, DispatcherPriority.DataBind);
            if (wizardV2Window.SelectedModel.TypeIndex != 3)
            {
                throw new InvalidOperationException("V2 当前模型类型切换冒烟失败。");
            }

            wizardV2Window.LabelSourceComboBox.SelectedIndex = 1;
            wizardV2Window.Dispatcher.Invoke(() => { }, DispatcherPriority.DataBind);
            if (wizardV2Window.ManualLabelEditorPanel.Visibility != Visibility.Visible)
            {
                throw new InvalidOperationException("V2 手动标签下拉选项没有展开精简编辑器。");
            }

            wizardV2Window.RemoveSelectedModelButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            if (wizardV2Window.ModelItemsList.Items.Count != originalModelCount)
            {
                throw new InvalidOperationException("V2 模型库删除当前模型交互冒烟失败。");
            }

            if (wizardV2Window.AddModelButton.ToolTip is null ||
                wizardV2Window.RemoveSelectedModelButton.ToolTip is null)
            {
                throw new InvalidOperationException("V2 模型库添加/删除操作 ToolTip 冒烟失败。");
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

            if (wizardV2Window.CurrentTestBlockStageIndex != 0 ||
                wizardV2Window.Step4Panel.Visibility != Visibility.Visible ||
                wizardV2Window.TestBlockModuleTabs.Children.Count != 2)
            {
                throw new InvalidOperationException("V2 普通检测仅保留基本信息与自定义函数页签冒烟失败。");
            }

            wizardV2Window.TestBlockBasicStageButton.RaiseEvent(
                new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));

            var expectedTargetModelCount = wizardV2Window.Models.Count(model => model.TypeIndex == 0);
            if (wizardV2Window.InspectionModelComboBox.Items.Count != expectedTargetModelCount ||
                wizardV2Window.InspectionModelComboBox.Items.Cast<TestSequenceWizardV2Window.ModelPreview>()
                    .Any(model => model.TypeIndex != 0))
            {
                throw new InvalidOperationException("V2 测试步模型下拉框没有按检测类型过滤项目模型库。");
            }

            var modelBeforeCanceledDropDown = wizardV2Window.SelectedInspectionItem?.Model;
            wizardV2Window.InspectionModelComboBox.IsDropDownOpen = true;
            wizardV2Window.InspectionModelComboBox.IsDropDownOpen = false;
            wizardV2Window.Dispatcher.Invoke(() => { }, DispatcherPriority.DataBind);
            if (wizardV2Window.DetectionEditorOverlay.Visibility != Visibility.Collapsed ||
                !ReferenceEquals(wizardV2Window.InspectionModelComboBox.SelectedItem, modelBeforeCanceledDropDown))
            {
                throw new InvalidOperationException("V2 未更改模型时关闭下拉框不应打开 Label 弹窗或清空当前显示。");
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

            var addedStep = wizardV2Window.SelectedInspectionItem
                ?? throw new InvalidOperationException("V2 新增测试步后选择状态丢失。");
            if (wizardV2Window.FindName("SequencePlanPanel") is not null ||
                !wizardV2Window.InspectionItemsSemanticsText.Text.Contains("从上到下", StringComparison.Ordinal) ||
                !wizardV2Window.Editor.InvocationOrderSummary.EndsWith(addedStep.FunctionCode, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("V2 已移除独立调用编排并改用测试步列表顺序的冒烟失败。");
            }

            var originalAddedStepLabel = addedStep.DetectionChildren.Single().Label;
            var reboundModel = wizardV2Window.Models[1];
            wizardV2Window.SelectInspectionModelForSmoke(reboundModel);
            wizardV2Window.Dispatcher.Invoke(() => { }, DispatcherPriority.DataBind);
            if (!ReferenceEquals(addedStep.Model, reboundModel) ||
                addedStep.DetectionChildren.Any(child => !reboundModel.Labels.Contains(child.Label)) ||
                addedStep.DetectionChildren.Any(child => string.Equals(child.Label, originalAddedStepLabel, StringComparison.Ordinal)) ||
                !wizardV2Window.DetectionEditor.LabelOptions.Select(option => option.Label)
                    .SequenceEqual(reboundModel.Labels) ||
                wizardV2Window.FindName("DetectionEditorModelComboBox") is not null)
            {
                throw new InvalidOperationException("V2 测试步改绑模型后没有清理旧检测子项并按新模型刷新 Label。");
            }

            if (wizardV2Window.InspectionTypeComboBox.Items.Count != 3 ||
                wizardV2Window.InspectionTypeComboBox.Items.Cast<System.Windows.Controls.ComboBoxItem>()
                    .All(item => item.Content?.ToString()?.Contains("图像分割", StringComparison.Ordinal) != true))
            {
                throw new InvalidOperationException("V2 测试步检测类型没有提供图像分割选项。");
            }

            reboundModel.UiTypeIndex = 2;
            wizardV2Window.Dispatcher.Invoke(() => { }, DispatcherPriority.DataBind);
            if (addedStep.TypeIndex != 2 || wizardV2Window.InspectionTypeComboBox.SelectedIndex != 2)
            {
                throw new InvalidOperationException("V2 已绑定模型切换为图像分割后测试步类型没有自动联动。");
            }

            wizardV2Window.SelectInspectionModelForSmoke(reboundModel);
            wizardV2Window.Dispatcher.Invoke(() => { }, DispatcherPriority.DataBind);
            if (addedStep.TypeIndex != 2 ||
                addedStep.TypeLabel != "图像分割" ||
                wizardV2Window.InspectionTypeComboBox.SelectedIndex != 2 ||
                wizardV2Window.NormalDetectionSetupPanel.Visibility != Visibility.Visible ||
                wizardV2Window.PoseBasicInfoHint.Visibility != Visibility.Collapsed ||
                wizardV2Window.DetectionEditorOverlay.Visibility != Visibility.Visible ||
                !wizardV2Window.DetectionEditor.LabelOptions.Select(option => option.Label)
                    .SequenceEqual(reboundModel.Labels))
            {
                throw new InvalidOperationException("V2 分割模型没有联动为图像分割测试步或复用逐 Label 配置界面。");
            }

            var anotherInspectionItem = wizardV2Window.InspectionItems.First(item => !ReferenceEquals(item, addedStep));
            wizardV2Window.InspectionItemsList.SelectedItem = anotherInspectionItem;
            wizardV2Window.InspectionItemsList.SelectedItem = addedStep;
            wizardV2Window.Dispatcher.Invoke(() => { }, DispatcherPriority.DataBind);
            if (addedStep.TypeIndex != 2 || wizardV2Window.InspectionTypeComboBox.SelectedIndex != 2)
            {
                throw new InvalidOperationException("V2 切换测试步后没有保留图像分割检测类型。");
            }

            wizardV2Window.OpenDetectionLabelEditorForSmoke(wizardV2Window.DetectionEditor.LabelOptions[0]);
            if (wizardV2Window.DetectionLabelEditorOverlay.Visibility != Visibility.Visible)
            {
                throw new InvalidOperationException("V2 图像分割测试步不能逐 Label 打开整图/ROI 与判定配置。");
            }

            wizardV2Window.CloseDetectionEditorForSmoke();
            reboundModel.UiTypeIndex = 0;
            wizardV2Window.Dispatcher.Invoke(() => { }, DispatcherPriority.DataBind);
            if (addedStep.TypeIndex != 0 || wizardV2Window.InspectionTypeComboBox.SelectedIndex != 0)
            {
                throw new InvalidOperationException("V2 模型从图像分割恢复为目标检测后测试步类型没有同步恢复。");
            }

            wizardV2Window.CloseDetectionEditorForSmoke();
            var detectionSteps = wizardV2Window.InspectionItems.Where(item => item.TypeIndex == 0).Take(2).ToArray();
            foreach (var detectionStep in detectionSteps)
            {
                wizardV2Window.InspectionItemsList.SelectedItem = detectionStep;
                wizardV2Window.Dispatcher.Invoke(() => { }, DispatcherPriority.DataBind);
                var displayedLabels = wizardV2Window.DetectionChildrenItemsControl.Items
                    .Cast<TestSequenceWizardV2Window.DetectionChildPreview>()
                    .Select(child => child.Label);
                if (!displayedLabels.SequenceEqual(detectionStep.DetectionChildren.Select(child => child.Label)) ||
                    detectionStep.DetectionChildren.Any(child => !detectionStep.Model.Labels.Contains(child.Label)))
                {
                    throw new InvalidOperationException("V2 切换测试步后检测子项没有跟随当前测试步及其绑定模型。");
                }
            }

            wizardV2Window.InspectionItemsList.SelectedItem = addedStep;
            wizardV2Window.Dispatcher.Invoke(() => { }, DispatcherPriority.DataBind);

            if (!wizardV2Window.MoveInspectionItem(addedStep, -1) ||
                wizardV2Window.InspectionItems[^2] != addedStep ||
                !wizardV2Window.Editor.InvocationOrderSummary.Contains(
                    $"{addedStep.FunctionCode} → {wizardV2Window.InspectionItems[^1].FunctionCode}",
                    StringComparison.Ordinal) ||
                !wizardV2Window.MoveInspectionItem(addedStep, 1) ||
                wizardV2Window.InspectionItems[^1] != addedStep)
            {
                throw new InvalidOperationException("V2 测试步上下移动与从上到下执行顺序同步冒烟失败。");
            }

            wizardV2Window.SelectInspectionTypeForSmoke(1);
            wizardV2Window.Dispatcher.Invoke(() => { }, DispatcherPriority.DataBind);
            if (wizardV2Window.PoseBasicInfoHint.Visibility != Visibility.Visible ||
                wizardV2Window.Step5Panel.Visibility != Visibility.Visible ||
                wizardV2Window.SelectedInspectionItem?.Model.TypeIndex != 1 ||
                wizardV2Window.InspectionModelComboBox.Items.Cast<TestSequenceWizardV2Window.ModelPreview>()
                    .Any(model => model.TypeIndex != 1) ||
                wizardV2Window.TestBlockModuleTabs.Children.Count != 2)
            {
                throw new InvalidOperationException("V2 姿态类型没有自动换绑姿态模型、过滤下拉框或弹出动作顺序。");
            }

            wizardV2Window.CancelPoseActionEditorButton.RaiseEvent(
                new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));

            wizardV2Window.RemoveSelectedInspectionItemButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            if (wizardV2Window.InspectionItemsList.Items.Count != originalInspectionItemCount ||
                !wizardV2Window.InspectionItems.Select(item => item.FunctionCode).SequenceEqual(originalFunctionCodes) ||
                !wizardV2Window.InspectionItemsSemanticsText.Text.Contains("从上到下", StringComparison.Ordinal))
            {
                throw new InvalidOperationException("V2 测试步顺序列表的加减交互冒烟失败。");
            }

            if (wizardV2Window.AddInspectionItemButton.ToolTip is null ||
                wizardV2Window.RemoveSelectedInspectionItemButton.ToolTip is null)
            {
                throw new InvalidOperationException("V2 加减操作 ToolTip 冒烟失败。");
            }

            var poseContentItem = wizardV2Window.InspectionItems.First(item => item.TypeIndex == 1);
            wizardV2Window.InspectionItemsList.SelectedItem = poseContentItem;
            wizardV2Window.SelectInspectionModelForSmoke(poseContentItem.Model);
            wizardV2Window.Dispatcher.Invoke(() => { }, DispatcherPriority.DataBind);
            var originalPoseStepNames = poseContentItem.PoseSteps.Select(step => step.Name).ToArray();
            var firstPoseStep = poseContentItem.PoseSteps[0];
            wizardV2Window.MovePoseStepForSmoke(firstPoseStep, 1);
            if (poseContentItem.PoseSteps[1] != firstPoseStep ||
                poseContentItem.PoseSteps.Select(step => step.Order).Where((order, index) => order != index + 1).Any() ||
                wizardV2Window.CurrentTestBlockStageIndex != 0 ||
                wizardV2Window.Step4Panel.Visibility != Visibility.Visible ||
                wizardV2Window.Step5Panel.Visibility != Visibility.Visible ||
                wizardV2Window.PoseContentPanel.Visibility != Visibility.Visible ||
                wizardV2Window.TestBlockModuleTabs.Children.Count != 2 ||
                !wizardV2Window.PoseOrderHeadingText.Text.Contains("执行顺序", StringComparison.Ordinal) ||
                wizardV2Window.AddPoseStepButton.ToolTip is null)
            {
                throw new InvalidOperationException("V2 姿态模型自动弹出动作顺序或连续编号冒烟失败。");
            }

            wizardV2Window.MovePoseStepForSmoke(firstPoseStep, -1);
            if (!poseContentItem.PoseSteps.Select(step => step.Name).SequenceEqual(originalPoseStepNames))
            {
                throw new InvalidOperationException("V2 姿态动作排序恢复冒烟失败。");
            }

            wizardV2Window.ApplyPoseActionEditorButton.RaiseEvent(
                new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            wizardV2Window.Dispatcher.Invoke(() => { }, DispatcherPriority.DataBind);
            if (wizardV2Window.Step5Panel.Visibility != Visibility.Collapsed ||
                wizardV2Window.CurrentTestBlockStageIndex != 0 ||
                wizardV2Window.Step4Panel.Visibility != Visibility.Visible ||
                wizardV2Window.PoseBasicInfoHint.Visibility != Visibility.Visible ||
                !wizardV2Window.EditPoseActionsButton.IsEnabled)
            {
                throw new InvalidOperationException("V2 姿态动作保存返回基本信息与重新编辑入口冒烟失败。");
            }

            var actionNameBeforeCancel = poseContentItem.PoseSteps[0].Name;
            wizardV2Window.EditPoseActionsButton.RaiseEvent(
                new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            poseContentItem.PoseSteps[0].Name = "取消本次修改";
            wizardV2Window.CancelPoseActionEditorButton.RaiseEvent(
                new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            wizardV2Window.Dispatcher.Invoke(() => { }, DispatcherPriority.DataBind);
            if (wizardV2Window.Step5Panel.Visibility != Visibility.Collapsed ||
                poseContentItem.PoseSteps[0].Name != actionNameBeforeCancel)
            {
                throw new InvalidOperationException("V2 姿态动作重新编辑与取消恢复冒烟失败。");
            }

            var targetDetectionItem = wizardV2Window.InspectionItems.First(item => item.TypeIndex == 0);
            wizardV2Window.InspectionItemsList.SelectedItem = targetDetectionItem;
            wizardV2Window.SelectInspectionModelForSmoke(targetDetectionItem.Model);
            wizardV2Window.UpdateLayout();
            wizardV2Window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
            if (wizardV2Window.CurrentStepIndex != 3 ||
                wizardV2Window.CurrentTestBlockStageIndex != 0 ||
                wizardV2Window.Steps.Count != 5 ||
                wizardV2Window.DetectionEditorOverlay.Visibility != Visibility.Visible ||
                wizardV2Window.DetectionLabelEditorOverlay.Visibility != Visibility.Collapsed ||
                wizardV2Window.DetectionEditor.LabelOptions.Count < 2)
            {
                throw new InvalidOperationException("V2 选择普通检测模型后未自动打开逐 Label 选择弹窗。");
            }

            var configuredDetectionItem = wizardV2Window.SelectedInspectionItem
                ?? throw new InvalidOperationException("V2 检测 Label 选择后测试步选择丢失。");
            var originalChildCount = configuredDetectionItem.DetectionChildren.Count;
            var originalLabel = configuredDetectionItem.DetectionChildren[0].Label;
            var additionalLabel = wizardV2Window.DetectionEditor.LabelOptions.First(option => !option.IsConfigured);
            wizardV2Window.OpenDetectionLabelEditorForSmoke(additionalLabel);
            additionalLabel.UseRoi = true;
            foreach (var roi in additionalLabel.RoiOptions)
            {
                roi.IsSelected = true;
            }

            wizardV2Window.RefreshDetectionLabelScopeForSmoke();
            wizardV2Window.UpdateLayout();
            wizardV2Window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
            if (wizardV2Window.DetectionLabelEditorOverlay.Visibility != Visibility.Visible ||
                wizardV2Window.DetectionEditor.ActiveLabel != additionalLabel ||
                !wizardV2Window.DetectionLabelScopeSummaryText.Text.Contains("ROI-01", StringComparison.Ordinal) ||
                !wizardV2Window.DetectionLabelScopeSummaryText.Text.Contains("ROI-02", StringComparison.Ordinal) ||
                wizardV2Window.RoiPreviewSurface.ActualWidth <= 0 ||
                wizardV2Window.RoiPreviewSurface.ActualHeight <= 0)
            {
                throw new InvalidOperationException("V2 单 Label 弹窗或多 ROI 实时区域摘要冒烟失败。");
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

            wizardV2Window.ApplyDetectionLabelEditorButton.RaiseEvent(
                new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            var additionalChild = configuredDetectionItem.DetectionChildren.FirstOrDefault(child =>
                string.Equals(child.Label, additionalLabel.Label, StringComparison.Ordinal));
            if (configuredDetectionItem.DetectionChildren.Count != originalChildCount + 1 ||
                configuredDetectionItem.DetectionChildren.All(child =>
                    !string.Equals(child.Label, originalLabel, StringComparison.Ordinal)) ||
                additionalChild is null ||
                !additionalChild.ScopeSummary.Contains("ROI-01", StringComparison.Ordinal) ||
                !additionalChild.ScopeSummary.Contains("ROI-02", StringComparison.Ordinal) ||
                wizardV2Window.DetectionEditorOverlay.Visibility != Visibility.Visible ||
                wizardV2Window.DetectionLabelEditorOverlay.Visibility != Visibility.Collapsed)
            {
                throw new InvalidOperationException("V2 第二个 Label 追加保存或第一个 Label 保留冒烟失败。");
            }

            var additionalScopeBeforeReedit = additionalChild.ScopeSummary;
            var originalOption = wizardV2Window.DetectionEditor.LabelOptions.First(option =>
                string.Equals(option.Label, originalLabel, StringComparison.Ordinal));
            wizardV2Window.OpenDetectionLabelEditorForSmoke(originalOption);
            wizardV2Window.DetectionEditor.ThresholdText = "2";
            wizardV2Window.ApplyDetectionLabelEditorButton.RaiseEvent(
                new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            var reconfiguredOriginal = configuredDetectionItem.DetectionChildren.FirstOrDefault(child =>
                string.Equals(child.Label, originalLabel, StringComparison.Ordinal));
            var retainedAdditional = configuredDetectionItem.DetectionChildren.FirstOrDefault(child =>
                string.Equals(child.Label, additionalLabel.Label, StringComparison.Ordinal));
            if (configuredDetectionItem.DetectionChildren.Count != originalChildCount + 1 ||
                reconfiguredOriginal is null ||
                !reconfiguredOriginal.RuleSummary.Contains("= 2", StringComparison.Ordinal) ||
                retainedAdditional is null ||
                retainedAdditional.ScopeSummary != additionalScopeBeforeReedit)
            {
                throw new InvalidOperationException("V2 重新编辑当前 Label 时覆盖了其他已保存 Label。");
            }

            wizardV2Window.OpenDetectionLabelEditorForSmoke(additionalLabel);
            if (!additionalLabel.UseRoi || additionalLabel.RoiOptions.Count(roi => roi.IsSelected) != 2)
            {
                throw new InvalidOperationException("V2 单 Label 的多 ROI 配置在重新进入时未保留。");
            }

            wizardV2Window.CloseDetectionEditorForSmoke();

            wizardV2Window.ShowTargetRuleStepForPreview();
            wizardV2Window.UpdateLayout();
            wizardV2Window.Dispatcher.Invoke(() => { }, DispatcherPriority.DataBind);
            if (wizardV2Window.CurrentStepIndex != 3 ||
                wizardV2Window.CurrentTestBlockStageIndex != 0 ||
                wizardV2Window.DetectionEditorOverlay.Visibility != Visibility.Visible ||
                wizardV2Window.DetectionLabelEditorOverlay.Visibility != Visibility.Visible ||
                wizardV2Window.DetectionEditor.ActiveLabel is null ||
                !wizardV2Window.DetectionEditor.RuleSummary.Contains("Pass", StringComparison.Ordinal))
            {
                throw new InvalidOperationException("V2 单 Label 判定条件弹窗未并入基本信息冒烟失败。");
            }

            wizardV2Window.CloseDetectionEditorForSmoke();

            var targetRuleItem = wizardV2Window.InspectionItems.First(item => item.TypeIndex == 0);
            var originalAdditionalRuleCount = targetRuleItem.AdditionalRules.Count;
            wizardV2Window.AddAdditionalRuleButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            if (targetRuleItem.AdditionalRules.Count != originalAdditionalRuleCount + 1 ||
                wizardV2Window.AdditionalRulesItemsControl.Items.Count != originalAdditionalRuleCount + 1)
            {
                throw new InvalidOperationException("V2 AND/OR 多规则编辑器新增冒烟失败。");
            }

            targetRuleItem.AdditionalRules.RemoveAt(targetRuleItem.AdditionalRules.Count - 1);

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
                wizardV2Window.CurrentTestBlockStageIndex != 0 ||
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

            targetRuleItem = wizardV2Window.InspectionItems.First(item => item.TypeIndex == 0);
            wizardV2Window.InspectionItemsList.SelectedItem = targetRuleItem;
            wizardV2Window.Dispatcher.Invoke(() => { }, DispatcherPriority.DataBind);
            if (!wizardV2Window.TargetRuleSummaryText.Text.Contains("1 到 4", StringComparison.Ordinal))
            {
                throw new InvalidOperationException("V2 数量范围判定摘要实时联动冒烟失败。");
            }

            wizardV2Window.DetectionEditorOverlay.Visibility = Visibility.Collapsed;
            wizardV2Window.ShowTriggerStepForPreview();
            wizardV2Window.Dispatcher.Invoke(() => { }, DispatcherPriority.DataBind);
            if (wizardV2Window.CurrentStepIndex != 3 ||
                wizardV2Window.CurrentTestBlockStageIndex != 3 ||
                wizardV2Window.Step8Panel.Visibility != Visibility.Visible ||
                !wizardV2Window.CustomFunctionSummaryText.Text.Contains("Python 文件", StringComparison.Ordinal))
            {
                throw new InvalidOperationException("V2 自定义函数页签与摘要冒烟失败。");
            }

            var customFunctionItem = wizardV2Window.SelectedInspectionItem
                ?? throw new InvalidOperationException("V2 自定义函数测试步选择丢失。");
            customFunctionItem.CustomFunctionName = string.Empty;
            wizardV2Window.NextButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            if (wizardV2Window.CurrentStepIndex != 3 ||
                wizardV2Window.CurrentTestBlockStageIndex != 3 ||
                wizardV2Window.Steps[3].IsCompleted)
            {
                throw new InvalidOperationException("V2 自定义函数名称为空时没有阻止测试步整体完成。");
            }

            customFunctionItem.CustomFunctionName = "fan_custom_check";
            customFunctionItem.CustomFunctionFilePath = "functions/fan_check.py";
            customFunctionItem.CustomFunctionDelayMsText = "250";
            wizardV2Window.Dispatcher.Invoke(() => { }, DispatcherPriority.DataBind);
            if (!wizardV2Window.CustomFunctionSummaryText.Text.Contains("fan_custom_check", StringComparison.Ordinal) ||
                !wizardV2Window.CustomFunctionSummaryText.Text.Contains("延时 250 ms", StringComparison.Ordinal))
            {
                throw new InvalidOperationException("V2 自定义函数文件、名称与延时摘要没有实时更新。");
            }

            var anotherFunctionItem = wizardV2Window.InspectionItems.First(item => !ReferenceEquals(item, customFunctionItem));
            wizardV2Window.CustomFunctionStepComboBox.SelectedItem = anotherFunctionItem;
            wizardV2Window.Dispatcher.Invoke(() => { }, DispatcherPriority.DataBind);
            if (!ReferenceEquals(wizardV2Window.SelectedInspectionItem, anotherFunctionItem) ||
                wizardV2Window.CustomFunctionDelayTextBox.Text != "200")
            {
                throw new InvalidOperationException("V2 自定义函数页不能直接切换测试步或恢复独立延时。");
            }

            wizardV2Window.CustomFunctionStepComboBox.SelectedItem = customFunctionItem;
            wizardV2Window.Dispatcher.Invoke(() => { }, DispatcherPriority.DataBind);
            if (!ReferenceEquals(wizardV2Window.SelectedInspectionItem, customFunctionItem) ||
                wizardV2Window.CustomFunctionDelayTextBox.Text != "250")
            {
                throw new InvalidOperationException("V2 返回测试步后自定义函数配置未保留。");
            }

            wizardV2Window.NextButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            if (wizardV2Window.CurrentStepIndex != 4 ||
                !wizardV2Window.Steps.Take(4).All(step => step.IsCompleted) ||
                wizardV2Window.Steps[4].IsCompleted ||
                !wizardV2Window.ReviewTriggerSummaryText.Text.Contains("自定义函数", StringComparison.Ordinal) ||
                !wizardV2Window.ReviewCustomFunctionDetailsText.Text.Contains("fan_custom_check", StringComparison.Ordinal) ||
                !wizardV2Window.ReviewModelsSummaryText.Text.Contains("Fan 主体检测", StringComparison.Ordinal) ||
                !wizardV2Window.ReviewInspectionOrderText.Text.Contains("风扇到位", StringComparison.Ordinal))
            {
                throw new InvalidOperationException("V2 五步向导最终页及自定义函数汇总冒烟失败。");
            }

            wizardV2Window.NextButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            if (wizardV2Window.NextButton.IsEnabled ||
                wizardV2Window.CompletionStatusBorder.Visibility != Visibility.Visible ||
                wizardV2Window.Steps.Any(step => !step.IsCompleted))
            {
                throw new InvalidOperationException("V2 向导最终确认状态冒烟失败。");
            }

            const string persistedProjectName = "V2 UI Smoke Draft";
            wizardV2Window.Editor.ProjectName = persistedProjectName;
            await wizardV2Window.Editor.SaveDraftAsync();
            var reopenedWizard = new TestSequenceWizardV2Window();
            await reopenedWizard.Editor.InitializeAsync();
            if (reopenedWizard.Editor.ProjectName != persistedProjectName ||
                reopenedWizard.InspectionItems.Count != originalInspectionItemCount ||
                !reopenedWizard.InspectionItems.Select(item => item.FunctionCode).SequenceEqual(originalFunctionCodes) ||
                !reopenedWizard.Editor.StatusMessage.Contains("已加载 Draft", StringComparison.Ordinal))
            {
                throw new InvalidOperationException("V2 Draft 保存与重新打开恢复冒烟失败。");
            }

            reopenedWizard.Close();

            wizardV2Window.Close();
            mainWindow.Close();

            Directory.CreateDirectory(Path.GetDirectoryName(ReceiptPath)!);
            await File.WriteAllTextAsync(
                ReceiptPath,
                $"通过{Environment.NewLine}{DateTimeOffset.UtcNow:O}{Environment.NewLine}登录窗口 + 操作员主窗口按钮/详情滚动/非空序列号门禁/单序列号单件单图/操作台仅保留测试序列设置/合格率命名/检测框保留且隐藏模型标签与置信度文字 + V2 返回操作台前端确认入口 + 独立兼容图像源设置窗口 + 经典测试序列设置窗口 + 普通检测项新增/删除 + V2 五步导航整体居中/跳步不补绿/必填失效退绿/未完成拦截/前后导航且底栏仅保留上一步下一步/图片与视频文件夹互斥选择及独立路径保持/USB 与工业相机待开发禁用状态 + 多模型库加减/图像分类移除/模型架构字段从当前前端隐藏/检测姿态分割类型映射/标签来源下拉与手动编辑 + 测试步动态模型绑定与顺序加减/内部函数标识不在前端显示/列表从上到下执行及上下移动/独立 Sequence Plan 前端移除/普通与姿态均仅基本信息和自定义函数页签/选择姿态模型自动弹出动作顺序/保存返回基本信息摘要与重新编辑入口/Model 后 Label 选择层/单 Label 独立区域与判定弹窗/第二 Label 追加保存/第一 Label 定向重编辑保留其他项/每标签整图或多命名 ROI/固定相机提示/数量范围校验/自定义函数名称文件延时与逐测试步独立配置/最终页仅汇总前端内容且无生命周期状态/既有草稿重载回归/ToolTip/最终确认",
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
