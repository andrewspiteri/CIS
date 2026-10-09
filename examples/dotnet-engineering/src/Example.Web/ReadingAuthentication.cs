using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;

namespace Example.Web;

/// <summary>Authentication and scope policies for disposable synthetic identities.</summary>
internal static class ReadingAuthentication
{
    internal static void Configure(IServiceCollection services, byte[] signingKey)
    {
        ArgumentNullException.ThrowIfNull(signingKey);
        if (signingKey.Length < 32) throw new ArgumentException("Use at least 256 bits for the synthetic signing key.", nameof(signingKey));
        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(options =>
        {
            options.IncludeErrorDetails = false;
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidIssuer = "synthetic-fixture",
                ValidateAudience = true,
                ValidAudience = "reading-example",
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = new SymmetricSecurityKey(signingKey),
                ValidateLifetime = true,
                ClockSkew = TimeSpan.Zero,
                ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
            };
        });
        services.AddAuthorizationBuilder()
            .AddPolicy("read", policy => policy.RequireAuthenticatedUser().RequireClaim("scope", "readings.read"))
            .AddPolicy("write", policy => policy.RequireAuthenticatedUser().RequireClaim("scope", "readings.write"));
    }
}
