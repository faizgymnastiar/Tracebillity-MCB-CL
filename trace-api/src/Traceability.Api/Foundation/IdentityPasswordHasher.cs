using Microsoft.AspNetCore.Identity;
using Traceability.Application;

namespace Traceability.Api.Foundation;

/// <summary>
/// Hash password memakai PasswordHasher bawaan framework (ASP.NET Core Identity,
/// PBKDF2) — tanpa menyimpan password bawaan apa pun (Fase 1 langkah 3).
/// </summary>
public sealed class IdentityPasswordHasher : IPasswordHasher
{
    private static readonly PasswordHasher<object> Hasher = new();

    public string Hash(string password) => Hasher.HashPassword(new object(), password);

    public bool Verify(string hash, string password)
    {
        if (string.IsNullOrEmpty(hash) || string.IsNullOrEmpty(password)) return false;
        return Hasher.VerifyHashedPassword(new object(), hash, password) != PasswordVerificationResult.Failed;
    }
}
