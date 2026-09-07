using System.Windows;
using System.Windows.Input;

namespace VisualInspection.App;

public partial class SerialNumberDialog : Window
{
    public SerialNumberDialog()
    {
        InitializeComponent();
        Loaded += (_, _) => SerialNumberTextBox.Focus();
    }

    public string SerialNumber => SerialNumberTextBox.Text.Trim();

    private void Confirm_Click(object sender, RoutedEventArgs e) => Confirm();

    private void SerialNumberTextBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            Confirm();
        }
    }

    private void Confirm()
    {
        if (SerialNumber.Length == 0)
        {
            ValidationText.Text = "请输入或扫描产品序列号。";
            SerialNumberTextBox.Focus();
            return;
        }

        DialogResult = true;
    }
}
