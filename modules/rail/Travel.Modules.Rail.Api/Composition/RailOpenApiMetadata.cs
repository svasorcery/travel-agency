using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;
using Travel.Modules.Rail.Api.Contracts;

namespace Travel.Modules.Rail.Api.Composition;

// These endpoints deliberately read bounded, strict JSON/query data themselves.
// Describe that same public contract without replacing the runtime guards with
// automatic body binding (which would run before the module's 8KiB/duplicate checks).
public sealed class RailOpenApiMetadata : IOpenApiOperationTransformer
{
    public async Task TransformAsync(
        OpenApiOperation operation,
        OpenApiOperationTransformerContext context,
        CancellationToken cancellationToken
    )
    {
        var path = context.Description.RelativePath?.TrimEnd('/');
        if (path is not ("api/rail/stations" or "api/rail/search" or "api/rail/journey-details"))
            return;
        var details = path == "api/rail/journey-details";
        var search = path != "api/rail/stations";
        if (search)
        {
            var schema = await context.GetOrCreateSchemaAsync(
                typeof(RailSearchRequest),
                cancellationToken: cancellationToken
            );
            schema.Properties!["transport"] = new OpenApiSchema
            {
                Type = JsonSchemaType.String,
                Enum =
                [
                    JsonValue.Create("train"),
                    JsonValue.Create("suburban"),
                    JsonValue.Create("all"),
                ],
            };
            schema.Properties["catalogueGeneration"] = new OpenApiSchema
            {
                Type = JsonSchemaType.String,
                Pattern = "^[0-9a-fA-F]{32}$",
            };
            schema.Properties["departureDate"] = new OpenApiSchema
            {
                Type = JsonSchemaType.String,
                Format = "date",
                Description =
                    "Explicit departure date, UTC today minus one through today plus thirty days.",
            };
            foreach (var name in new[] { "from", "to" })
                if (schema.Properties[name] is OpenApiSchema reference)
                {
                    reference.Properties!["source"] = new OpenApiSchema
                    {
                        Type = JsonSchemaType.String,
                        Const = "yandex-rasp",
                    };
                    reference.Properties["code"] = new OpenApiSchema
                    {
                        Type = JsonSchemaType.String,
                        Pattern = "^s[0-9]+$",
                        MaxLength = 128,
                    };
                }
            var input = schema;
            if (details)
            {
                input = await context.GetOrCreateSchemaAsync(
                    typeof(RailJourneyDetailsRequestMetadata),
                    cancellationToken: cancellationToken
                );
                input.Properties!["search"] = schema;
                input.Properties["expectedProfileEpoch"] = new OpenApiSchema
                {
                    Type = JsonSchemaType.String,
                    Pattern = "^[0-9a-fA-F]{32}$",
                };
                if (input.Properties["selection"] is OpenApiSchema selection)
                {
                    selection.Properties!["transport"] = new OpenApiSchema
                    {
                        Type = JsonSchemaType.String,
                        Enum = [JsonValue.Create("train"), JsonValue.Create("suburban")],
                    };
                    selection.Properties["key"] = new OpenApiSchema
                    {
                        Type = JsonSchemaType.String,
                        MinLength = 1,
                        MaxLength = 1024,
                        Description =
                            "Opaque current-search key, at most 1024 UTF-8 bytes, without control characters.",
                    };
                    foreach (var field in new[] { "departure", "arrival" })
                        selection.Properties[field] = new OpenApiSchema
                        {
                            Type = JsonSchemaType.String,
                            Format = "date-time",
                            Pattern = @"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(?:Z|[+-]\d{2}:\d{2})$",
                        };
                }
            }
            operation.RequestBody = new OpenApiRequestBody
            {
                Required = true,
                Description =
                    "Strict JSON, at most 8192 bytes; unknown and duplicate fields are rejected. Select both stations from the current catalogue generation.",
                Content = new Dictionary<string, OpenApiMediaType>
                {
                    ["application/json"] = new() { Schema = input },
                },
            };
        }
        else
        {
            operation.Parameters =
            [
                new OpenApiParameter
                {
                    Name = "q",
                    In = ParameterLocation.Query,
                    Required = true,
                    Description =
                        "Station or settlement name, 3–80 characters. No other or repeated query parameters are accepted.",
                    Schema = new OpenApiSchema
                    {
                        Type = JsonSchemaType.String,
                        MinLength = 3,
                        MaxLength = 80,
                    },
                },
            ];
        }
        operation.Responses ??= new OpenApiResponses();
        var problem = await context.GetOrCreateSchemaAsync(
            typeof(ProblemDetails),
            cancellationToken: cancellationToken
        );
        foreach (
            var status in search
                ? new[] { "400", "413", "429", "500" }
                : new[] { "400", "429", "500" }
        )
            operation.Responses[status] = new OpenApiResponse
            {
                Description = status switch
                {
                    "400" =>
                        "Invalid request or expired station selection; no supplier search is dispatched.",
                    "413" => "Search body exceeds 8192 bytes.",
                    "429" => "Process capacity is full; no queue is created.",
                    _ => "Unexpected platform failure, with a safe problem body.",
                },
                Content = new Dictionary<string, OpenApiMediaType>
                {
                    ["application/problem+json"] = new() { Schema = problem },
                },
            };
        operation.Responses["200"].Description =
            "Execution envelope: complete, limited, skipped or failed. Source failures are typed outcomes; ticket facts are independent of schedule completeness.";
        foreach (var response in operation.Responses.Values.OfType<OpenApiResponse>())
        {
            response.Headers ??= new Dictionary<string, IOpenApiHeader>();
            response.Headers["Cache-Control"] = new OpenApiHeader
            {
                Required = true,
                Description = "Source data and errors must not be retained by HTTP caches.",
                Schema = new OpenApiSchema { Type = JsonSchemaType.String, Const = "no-store" },
            };
        }
    }
}
