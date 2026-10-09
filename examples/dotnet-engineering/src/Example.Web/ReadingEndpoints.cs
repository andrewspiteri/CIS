using Example.Core;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace Example.Web;

internal static class ReadingEndpoints
{
    internal static void Map(WebApplication app)
    {
        using var stream = typeof(ReadingEndpoints).Assembly.GetManifestResourceStream("Example.Web.ReadingPage.html")
            ?? throw new InvalidOperationException("The embedded reading page is missing.");
        using var reader = new StreamReader(stream);
        var page = reader.ReadToEnd();
        // Public shell serves static content only. Protected endpoints own all data access.
        app.MapGet("/", () => Results.Content(page, "text/html")).CacheOutput();
        app.MapGet("/api/readings", async (IReadingStore store, CancellationToken cancellation) =>
            TypedResults.Ok(await store.ReadAllAsync(cancellation))).RequireAuthorization("read");
        app.MapPost("/api/readings", ApplyAsync).RequireAuthorization("write");
    }

    private static async Task<Results<Ok<ReadingAccepted>, BadRequest<ProblemDetails>, Conflict<ProblemDetails>>> ApplyAsync(
        Reading reading, ReadingBatchService service, CancellationToken cancellation)
    {
        try
        {
            var accepted = await service.ApplyAsync([reading], reading.Id.ToString(), cancellation);
            return TypedResults.Ok(new ReadingAccepted(1, accepted));
        }
        catch (ArgumentException)
        {
            return TypedResults.BadRequest(new ProblemDetails { Status = 400, Title = "The reading is invalid." });
        }
        catch (InvalidOperationException)
        {
            return TypedResults.Conflict(new ProblemDetails { Status = 409, Title = "The reading identity has conflicting content." });
        }
    }
}
