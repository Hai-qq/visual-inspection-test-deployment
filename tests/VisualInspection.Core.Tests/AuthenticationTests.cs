using VisualInspection.Core.Security;
using VisualInspection.Infrastructure.Persistence;

namespace VisualInspection.Core.Tests;

public sealed class AuthenticationTests
{
    [Fact]
    public void PasswordHasher_RoundTripsWithoutStoringPlaintext()
    {
        const string password = "Strong-Test-Password-123";

        var encoded = PasswordHasher.Hash(password);

        Assert.DoesNotContain(password, encoded, StringComparison.Ordinal);
        Assert.True(PasswordHasher.Verify(password, encoded));
        Assert.False(PasswordHasher.Verify("wrong-password", encoded));
        Assert.False(PasswordHasher.Verify(password, "invalid"));
    }

    [Fact]
    public async Task AuthenticationService_EnforcesCredentialsEnabledStateAndRole()
    {
        var admin = Account("admin", "管理员", UserRole.Admin, "Admin@123", enabled: true);
        var disabled = Account("disabled", "禁用账户", UserRole.Operator, "Operator@123", enabled: false);
        var service = new AuthenticationService(new MemoryUserStore([admin, disabled]));

        var session = await service.AuthenticateAsync("ADMIN", "Admin@123");

        Assert.NotNull(session);
        Assert.True(session.IsAdmin);
        Assert.Equal("管理员", session.DisplayName);
        Assert.Null(await service.AuthenticateAsync("admin", "bad"));
        Assert.Null(await service.AuthenticateAsync("disabled", "Operator@123"));
    }

    [Fact]
    public async Task JsonUserAccountStore_RoundTripsHashedAccounts()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"visual-inspection-users-{Guid.NewGuid():N}");
        var path = Path.Combine(directory, "users.json");
        try
        {
            var store = new JsonUserAccountStore(path);
            var accounts = new[] { Account("operator", "操作员", UserRole.Operator, "Operator@123", enabled: true) };

            await store.SaveAsync(accounts);
            var loaded = await store.ListAsync();

            Assert.Single(loaded);
            Assert.Equal(UserRole.Operator, loaded[0].Role);
            Assert.True(PasswordHasher.Verify("Operator@123", loaded[0].PasswordHash));
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    private static UserAccount Account(
        string username,
        string displayName,
        UserRole role,
        string password,
        bool enabled) =>
        new()
        {
            Username = username,
            DisplayName = displayName,
            Role = role,
            PasswordHash = PasswordHasher.Hash(password),
            Enabled = enabled
        };

    private sealed class MemoryUserStore(IReadOnlyList<UserAccount> accounts) : IUserAccountStore
    {
        public Task<IReadOnlyList<UserAccount>> ListAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(accounts);

        public Task SaveAsync(IReadOnlyCollection<UserAccount> accounts, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }
}
