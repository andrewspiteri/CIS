using System.Net;
using Example.Core;
using Example.Persistence;
using Npgsql;

namespace Example.Web;

/// <summary>Loopback-only synthetic host; tests supply a disposable database and signing key.</summary>
public static class ReadingWebHost
{
    public static WebApplication Create(string connectionString, byte[] signingKey)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { ApplicationName = typeof(ReadingWebHost).Assembly.FullName });
        builder.WebHost.ConfigureKestrel(server => server.Listen(IPAddress.Loopback, 0));
        builder.Services.AddSingleton(_ => NpgsqlDataSource.Create(connectionString));
        builder.Services.AddSingleton<PostgresReadingStore>();
        builder.Services.AddSingleton<IReadingStore>(services => services.GetRequiredService<PostgresReadingStore>());
        builder.Services.AddSingleton(_ => new ProcessingTelemetry("Example.Web"));
        builder.Services.AddSingleton<ReadingBatchService>();
        ReadingAuthentication.Configure(builder.Services, signingKey);
        builder.Services.AddOutputCache();
        builder.Services.AddOpenApi(ReadingOpenApi.Configure);
        var app = builder.Build();
        app.UseAuthentication();
        app.UseAuthorization();
        app.UseOutputCache();
        app.MapOpenApi().RequireAuthorization("read");
        ReadingEndpoints.Map(app);
        return app;
    }
}
