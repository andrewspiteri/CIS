using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace Example.Web;

internal static class ReadingOpenApi
{
    internal static void Configure(OpenApiOptions options)
    {
        options.AddDocumentTransformer((document, _, _) =>
        {
            // A disposable loopback port is deployment context, not a supported API server contract.
            document.Servers?.Clear();
            document.Components ??= new OpenApiComponents();
            document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
            document.Components.SecuritySchemes["Bearer"] = new OpenApiSecurityScheme { Type = SecuritySchemeType.Http, Scheme = "bearer", BearerFormat = "JWT" };
            return Task.CompletedTask;
        });
        options.AddOperationTransformer((operation, context, _) =>
        {
            if (!context.Description.ActionDescriptor.EndpointMetadata.OfType<IAuthorizeData>().Any()) return Task.CompletedTask;
            operation.Security = [new OpenApiSecurityRequirement { [new OpenApiSecuritySchemeReference("Bearer", context.Document)] = [] }];
            operation.Responses ??= new OpenApiResponses();
            operation.Responses["401"] = new OpenApiResponse { Description = "Authentication required" };
            operation.Responses["403"] = new OpenApiResponse { Description = "Insufficient permission" };
            return Task.CompletedTask;
        });
    }
}
