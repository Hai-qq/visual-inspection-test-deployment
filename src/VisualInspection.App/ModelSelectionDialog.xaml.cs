using System.Windows;
using System.Windows.Input;

namespace VisualInspection.App;

public partial class ModelSelectionDialog : Window
{
    public ModelSelectionDialog(
        IEnumerable<TestSequenceWizardV2Window.ModelPreview> models,
        TestSequenceWizardV2Window.ModelPreview? selectedModel)
    {
        InitializeComponent();
        ModelsList.ItemsSource = models.ToArray();
        ModelsList.SelectedItem = selectedModel;
        if (ModelsList.SelectedIndex < 0 && ModelsList.Items.Count > 0)
        {
            ModelsList.SelectedIndex = 0;
        }
    }

    public TestSequenceWizardV2Window.ModelPreview? SelectedModel =>
        ModelsList.SelectedItem as TestSequenceWizardV2Window.ModelPreview;

    private void Confirm_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedModel is null)
        {
            return;
        }

        DialogResult = true;
    }

    private void ModelsList_MouseDoubleClick(object sender, MouseButtonEventArgs e) => Confirm_Click(sender, e);
}
