using System.IO;
using VisualInspection.Core.Security;

namespace VisualInspection.App.Services;

public static class DemoUserSeeder
{
    public const string AdminUsername = "admin";
    public const string AdminPassword = "Admin@123";
    public const string OperatorUsername = "operator";
    public const string OperatorPassword = "Operator@123";

    public static string UserAccountFilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "VisualInspectionTestDeployment",
        "users",
        "users.json");

    public static async Task EnsureAsync(IUserAccountStore store, CancellationToken cancellationToken = default)
    {
        if ((await store.ListAsync(cancellationToken)).Count > 0)
        {
            return;
        }

        await store.SaveAsync(
        [
            new UserAccount
            {
                Username = AdminUsername,
                DisplayName = "演示管理员",
                Role = UserRole.Admin,
                PasswordHash = PasswordHasher.Hash(AdminPassword)
            },
            new UserAccount
            {
                Username = OperatorUsername,
                DisplayName = "演示操作员",
                Role = UserRole.Operator,
                PasswordHash = PasswordHasher.Hash(OperatorPassword)
            }
        ], cancellationToken);
    }
}
