using System.Windows;
using System.Windows.Input;
using VisualInspection.App.Services;
using VisualInspection.Core.Security;

namespace VisualInspection.App;

public partial class LoginWindow : Window
{
    private readonly AuthenticationService _authenticationService;

    public LoginWindow(AuthenticationService authenticationService)
    {
        InitializeComponent();
        _authenticationService = authenticationService;
        Loaded += (_, _) => PasswordInput.Focus();
    }

    public UserSession? Session { get; private set; }

    private async void Login_Click(object sender, RoutedEventArgs e)
    {
        await AuthenticateAsync();
    }

    private async void PasswordInput_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            await AuthenticateAsync();
        }
    }

    private async Task AuthenticateAsync()
    {
        LoginStatusText.Text = "正在验证账户...";
        LoginStatusText.Foreground = (System.Windows.Media.Brush)FindResource("MutedTextBrush");
        var session = await _authenticationService.AuthenticateAsync(UsernameBox.Text, PasswordInput.Password);
        if (session is null)
        {
            LoginStatusText.Text = "用户名或密码不正确，或者账户已被禁用。";
            LoginStatusText.Foreground = (System.Windows.Media.Brush)FindResource("FailBrush");
            PasswordInput.SelectAll();
            PasswordInput.Focus();
            return;
        }

        Session = session;
        DialogResult = true;
    }

    private void FillAdmin_Click(object sender, RoutedEventArgs e)
    {
        UsernameBox.Text = DemoUserSeeder.AdminUsername;
        PasswordInput.Password = DemoUserSeeder.AdminPassword;
        LoginStatusText.Text = "已填入本地管理员验收账户。";
        LoginStatusText.Foreground = (System.Windows.Media.Brush)FindResource("GreenDarkBrush");
    }

    private void FillOperator_Click(object sender, RoutedEventArgs e)
    {
        UsernameBox.Text = DemoUserSeeder.OperatorUsername;
        PasswordInput.Password = DemoUserSeeder.OperatorPassword;
        LoginStatusText.Text = "已填入本地操作员验收账户。";
        LoginStatusText.Foreground = (System.Windows.Media.Brush)FindResource("GreenDarkBrush");
    }
}
