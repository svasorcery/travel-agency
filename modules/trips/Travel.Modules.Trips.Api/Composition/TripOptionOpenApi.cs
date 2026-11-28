using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;
using Travel.Modules.Trips.Api.Endpoints;
using Travel.Modules.Trips.Application.SavedOptions;
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
        path = path?.Replace("{tripId:guid}", "{tripId}", StringComparison.OrdinalIgnoreCase);
        var save = path == "api/trips/saved-options";
        var read = path == "api/trips/{tripId}/saved-option";
        if (
            !save
            && !read
            && path is not ("api/trips/option-preview" or "api/trips/option-preview/availability")
        )
            return;
        var preview = path == "api/trips/option-preview";
        operation.Responses ??= new OpenApiResponses();
        var output = await context.GetOrCreateSchemaAsync(
            save ? typeof(SavedTripLocation)
                : read ? typeof(SavedOptionDetail)
                : preview ? typeof(TripOptionPreview)
                : typeof(TripOptionAvailability),
            cancellationToken: ct
        );
        IOpenApiSchema successSchema = output;
        if (preview)
        {
            var negotiated = await context.GetOrCreateSchemaAsync(
                typeof(NegotiatedTripOption),
                cancellationToken: ct
            );
            successSchema = new OpenApiSchema { OneOf = [output, negotiated] };
            operation.Parameters ??= [];
            operation.Parameters.Add(
                new OpenApiParameter
                {
                    In = ParameterLocation.Header,
                    Name = "X-Trips-Option-Save",
                    Required = false,
                    Description =
                        "Request format 1 to receive the same preview and an optional owner-bound save selection.",
                    Schema = new OpenApiSchema { Type = JsonSchemaType.String, Pattern = "^1$" },
                }
            );
        }
        operation.Responses["200"] = new OpenApiResponse
        {
            Description =
                save ? "Existing private plan for the same owner and selection."
                : read ? "Historical saved option, or null for a manual plan."
                : preview ? "One fictional option, unsupported request, or no matching combination."
                : "Historical preview profile and example dates.",
            Content = new Dictionary<string, OpenApiMediaType>
            {
                ["application/json"] = new() { Schema = successSchema },
            },
        };
        if (save)
            operation.Responses["201"] = new OpenApiResponse
            {
                Description = "Created private plan at revision 1.",
                Headers = new Dictionary<string, IOpenApiHeader>
                {
                    ["Location"] = new OpenApiHeader
                    {
                        Required = true,
                        Schema = new OpenApiSchema { Type = JsonSchemaType.String },
                    },
                },
                Content = new Dictionary<string, OpenApiMediaType>
                {
                    ["application/json"] = new() { Schema = output },
                },
            };
        if (preview || save)
        {
            var body = await context.GetOrCreateSchemaAsync(
                save ? typeof(SaveSelectionRequest) : typeof(TripOptionRequest),
                cancellationToken: ct
            );
            body.AdditionalPropertiesAllowed = false;
            operation.RequestBody = new OpenApiRequestBody
            {
                Required = true,
                Description = save
                    ? "Owner-bound selection and optional title; maximum 128 KiB."
                    : "Current unsaved structured brief; maximum 4096 UTF-8 bytes.",
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
            var code in save ? new[] { "400", "401", "403", "404", "409", "413", "503" }
            : read ? new[] { "400", "401", "403", "404", "503" }
            : preview ? new[] { "400", "401", "403", "404", "413", "429", "503", "504" }
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
