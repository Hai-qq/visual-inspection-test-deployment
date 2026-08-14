using System.Security.Cryptography;

namespace VisualInspection.Core.Security;

public enum UserRole
{
    Admin,
    Operator
}

public sealed record UserAccount
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Username { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public UserRole Role { get; init; }
    public string PasswordHash { get; init; } = string.Empty;
    public bool Enabled { get; init; } = true;
}

public sealed record UserSession(Guid UserId, string Username, string DisplayName, UserRole Role)
{
    public bool IsAdmin => Role == UserRole.Admin;
}

public interface IUserAccountStore
{
    Task<IReadOnlyList<UserAccount>> ListAsync(CancellationToken cancellationToken = default);
    Task SaveAsync(IReadOnlyCollection<UserAccount> accounts, CancellationToken cancellationToken = default);
}

public static class PasswordHasher
{
    private const int Iterations = 100_000;
    private const int SaltSize = 16;
    private const int HashSize = 32;
    private const string Scheme = "PBKDF2-SHA256";

    public static string Hash(string password)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(password);
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, HashSize);
        return $"{Scheme}${Iterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
    }

    public static bool Verify(string password, string encodedHash)
    {
        if (string.IsNullOrEmpty(password) || string.IsNullOrWhiteSpace(encodedHash))
        {
            return false;
        }

        try
        {
            var parts = encodedHash.Split('$');
            if (parts.Length != 4 || parts[0] != Scheme || !int.TryParse(parts[1], out var iterations) || iterations < 10_000)
            {
                return false;
            }

            var salt = Convert.FromBase64String(parts[2]);
            var expected = Convert.FromBase64String(parts[3]);
            var actual = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, expected.Length);
            return CryptographicOperations.FixedTimeEquals(actual, expected);
        }
        catch (FormatException)
        {
            return false;
        }
    }
}

public sealed class AuthenticationService(IUserAccountStore store)
{
    public async Task<UserSession?> AuthenticateAsync(
        string username,
        string password,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrEmpty(password))
        {
            return null;
        }

        var account = (await store.ListAsync(cancellationToken)).FirstOrDefault(candidate =>
            candidate.Enabled && string.Equals(candidate.Username, username.Trim(), StringComparison.OrdinalIgnoreCase));
        if (account is null || !PasswordHasher.Verify(password, account.PasswordHash))
        {
            return null;
        }

        return new UserSession(account.Id, account.Username, account.DisplayName, account.Role);
    }
}
