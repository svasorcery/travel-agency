using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;
using Travel.Modules.Trips.Api.Endpoints;
using Travel.Modules.Trips.Core.Options;

namespace Travel.Modules.Trips.Api.Composition;

internal sealed class TripOptionOpenApi : IOpenApiOperationTransformer
{
    public async Task TransformAsync(
        OpenApiOperation operation,
        OpenApiOperationTransformerContext context,
        CancellationToken ct
    )
    {
        var path = context.Description.RelativePath?.Trim('/');
        if (path is not ("api/trips/option-preview" or "api/trips/option-preview/availability"))
            return;
        var preview = context.Description.HttpMethod == "POST";
        operation.Responses ??= new OpenApiResponses();
        var output = await context.GetOrCreateSchemaAsync(
            preview ? typeof(TripOptionPreview) : typeof(TripOptionAvailability),
            cancellationToken: ct
        );
        operation.Responses["200"] = new OpenApiResponse
        {
            Description = preview
                ? "One fictional option, unsupported request, or no matching combination."
                : "Historical preview profile and example dates.",
            Content = new Dictionary<string, OpenApiMediaType>
            {
                ["application/json"] = new() { Schema = output },
            },
        };
        if (preview)
        {
            var body = await context.GetOrCreateSchemaAsync(
                typeof(TripOptionRequest),
                cancellationToken: ct
            );
            body.AdditionalPropertiesAllowed = false;
            operation.RequestBody = new OpenApiRequestBody
            {
                Required = true,
                Description = "Current unsaved structured brief; maximum 4096 UTF-8 bytes.",
                Content = new Dictionary<string, OpenApiMediaType>
                {
                    ["application/json"] = new() { Schema = body },
                },
            };
        }
        var problem = await context.GetOrCreateSchemaAsync(
            typeof(ProblemDetails),
            cancellationToken: ct
        );
        foreach (
            var code in preview
                ? new[] { "400", "401", "403", "404", "413", "429", "503", "504" }
                : ["400", "401", "403", "404"]
        )
            operation.Responses[code] = new OpenApiResponse
            {
                Description = "Trips problem.",
                Content = new Dictionary<string, OpenApiMediaType>
                {
                    ["application/problem+json"] = new() { Schema = problem },
                },
            };
    }
}
