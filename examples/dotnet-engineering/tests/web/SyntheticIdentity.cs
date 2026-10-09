using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.IdentityModel.Tokens;

namespace Example.WebTests;

internal sealed class SyntheticIdentity : IDisposable
{
    internal byte[] SigningKey { get; } = RandomNumberGenerator.GetBytes(32);

    internal string Token(bool canWrite, bool expired)
    {
        var claims = new List<Claim> { new("scope", "readings.read") };
        if (canWrite) claims.Add(new("scope", "readings.write"));
        var expires = DateTime.UtcNow.AddMinutes(expired ? -1 : 10);
        return new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken("synthetic-fixture", "reading-example", claims,
            expires.AddMinutes(-15), expires, new SigningCredentials(new SymmetricSecurityKey(SigningKey), SecurityAlgorithms.HmacSha256)));
    }

    public void Dispose() => CryptographicOperations.ZeroMemory(SigningKey);
}
