using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace VisualInspection.App.Services;

/// <summary>Optional local WPF render evidence for visual review; never applies or exports a project.</summary>
internal sealed class UiLayoutReviewCapture
{
    internal string DirectoryPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "VisualInspectionTestDeployment", "ui-layout-review", DateTime.UtcNow.ToString("yyyyMMdd-HHmmss"));

    internal void Save(Window window, string name)
    {
        window.UpdateLayout();
        window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
        var surface = (FrameworkElement)VisualTreeHelper.GetChild(window, 0);
        var dpi = VisualTreeHelper.GetDpi(surface);
        var bitmap = new RenderTargetBitmap(
            Math.Max(1, (int)Math.Ceiling(surface.ActualWidth * dpi.DpiScaleX)),
            Math.Max(1, (int)Math.Ceiling(surface.ActualHeight * dpi.DpiScaleY)),
            dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
        var background = new DrawingVisual();
        using (var drawing = background.RenderOpen())
            drawing.DrawRectangle(window.Background, null, new Rect(surface.RenderSize));
        bitmap.Render(background);
        bitmap.Render(surface);
        Directory.CreateDirectory(DirectoryPath);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(Path.Combine(DirectoryPath, $"{name}.png"));
        encoder.Save(stream);
    }

    internal void SaveWizardStates()
    {
        foreach (var size in new[] { (1380d, 860d), (1120d, 720d) })
        {
            var window = new TestSequenceWizardV2Window
            {
                Width = size.Item1, Height = size.Item2,
                SuppressModelSelectionDialogForSmoke = true,
                SuppressSequenceExportForSmoke = true,
                SuppressApplyToOperatorForSmoke = true
            };
            try
            {
                window.Show();
                window.UpdateLayout();
                var suffix = $"{size.Item1}x{size.Item2}";
                Save(window, $"10-project-{suffix}");
                window.ShowSourceStepForPreview();
                Save(window, $"11-source-{suffix}");
                window.ShowModelsStepForPreview();
                Save(window, $"12-models-{suffix}");
                window.ShowInspectionItemsStepForPreview();
                Save(window, $"13-test-step-{suffix}");
                window.DetectionChildrenScrollViewer.ScrollToEnd();
                Save(window, $"14-last-rule-{suffix}");
                window.TestBlockTriggerStageButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Save(window, $"15-function-{suffix}");
                ClickStep(window, 4);
                Save(window, $"16-export-{suffix}");
                window.ReviewDetailsExpander.IsExpanded = true;
                window.Step9Panel.ScrollToEnd();
                Save(window, $"16b-export-details-{suffix}");
                window.ReviewDetailsExpander.IsExpanded = false;
                window.Step9Panel.ScrollToHome();
                ClickStep(window, 0);
                for (var step = 0; step < 4; step++)
                    window.NextButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Save(window, $"16c-export-confirmed-{suffix}");
                window.ShowTargetContentStepForPreview();
                Save(window, $"17-label-list-{suffix}");
                window.ShowTargetRuleStepForPreview();
                Save(window, $"18-label-rule-{suffix}");
                window.RoiRegionRadioButton.IsChecked = true;
                Save(window, $"19-roi-{suffix}");
                window.DetectionEditor.MetricIndex = 1;
                window.DetectionEditor.RuleMethodIndex = 1;
                Save(window, $"20-missing-range-{suffix}");
                window.DetectionLabelRuleScrollViewer.ScrollToEnd();
                Save(window, $"21-missing-range-end-{suffix}");
                window.CloseDetectionEditorForSmoke();
                window.ShowPoseContentStepForPreview();
                Save(window, $"22-pose-{suffix}");
                window.PoseActionEditorScrollViewer.ScrollToEnd();
                Save(window, $"23-pose-parameters-{suffix}");
            }
            finally
            {
                window.Close();
            }
        }
    }

    private static void ClickStep(TestSequenceWizardV2Window window, int index)
    {
        window.UpdateLayout();
        var container = (FrameworkElement)window.WizardStepsItemsControl.ItemContainerGenerator.ContainerFromIndex(index);
        var button = (Button)VisualTreeHelper.GetChild(container, 0);
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    }
}
