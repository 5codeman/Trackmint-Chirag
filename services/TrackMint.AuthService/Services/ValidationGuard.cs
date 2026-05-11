using System.Text.RegularExpressions;
using TrackMint.AuthService.Exceptions;

namespace TrackMint.AuthService.Services;

internal static partial class ValidationGuard
{
    public static void AgainstBlank(string? value, string fieldName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ValidationException($"{fieldName} is required.");
        }
    }

    public static void AgainstInvalidPassword(string password)
    {
        if (string.IsNullOrWhiteSpace(password) || password.Length < 8)
        {
            throw new ValidationException("Password must be at least 8 characters.");
        }

        if (!PasswordRegex().IsMatch(password))
        {
            throw new ValidationException("Password must include uppercase, lowercase, and a number.");
        }
    }

    [GeneratedRegex(@"^(?=.*[a-z])(?=.*[A-Z])(?=.*\d).+$", RegexOptions.Compiled)]
    private static partial Regex PasswordRegex();
}
