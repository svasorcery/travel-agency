using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;
using Travel.Modules.Trips.Api.Contracts;
using Travel.Modules.Trips.Application.FlightLinks;

namespace Travel.Modules.Trips.Api.Composition;

internal sealed class FlightLinkOpenApi : IOpenApiOperationTransformer
{
    public async Task TransformAsync(
        OpenApiOperation operation,
        OpenApiOperationTransformerContext context,
        CancellationToken ct
    )
    {
        var path = context
            .Description.RelativePath?.Trim('/')
            .Replace("{tripId:guid}", "{tripId}", StringComparison.OrdinalIgnoreCase);
        var type = path switch
        {
            "api/trips/flight-links/availability" => typeof(LinkAvailabilityResponse),
            "api/trips/{tripId}/flight-links" => typeof(LinkMetadataResponse),
            "api/trips/{tripId}/flight-links/status" => typeof(LinkStatusResponse),
            "api/trips/{tripId}/flight-order-candidates" => typeof(TripFlightCandidatePage),
            _ => null,
        };
        if (type == null)
            return;
        var success = new OpenApiResponse
        {
            Description = "Private trip flight links.",
            Content = new Dictionary<string, OpenApiMediaType>
            {
                ["application/json"] = new()
                {
                    Schema = await context.GetOrCreateSchemaAsync(type, cancellationToken: ct),
                },
            },
        };
        var patch = string.Equals(
            context.Description.HttpMethod,
            "PATCH",
            StringComparison.OrdinalIgnoreCase
        );
        if (type == typeof(LinkMetadataResponse))
            success.Headers = new Dictionary<string, IOpenApiHeader>
            {
                ["ETag"] = new OpenApiHeader { Required = true, Schema = Tag() },
            };
        operation.Responses ??= new OpenApiResponses();
        operation.Responses["200"] = success;
        operation.Parameters ??= [];
        if (patch)
        {
            operation.Parameters.Add(
                new OpenApiParameter
                {
                    Name = "If-Match",
                    In = ParameterLocation.Header,
                    Required = true,
                    Schema = Tag(),
                }
            );
            var body = await context.GetOrCreateSchemaAsync(
                typeof(FlightLinkPatchRequest),
                cancellationToken: ct
            );
            body.AdditionalPropertiesAllowed = false;
            body.Required = new HashSet<string> { "op", "orderId" };
            body.Properties!["op"] = new OpenApiSchema
            {
                Type = JsonSchemaType.String,
                Enum = [JsonValue.Create("add"), JsonValue.Create("remove")],
            };
            operation.RequestBody = new OpenApiRequestBody
            {
                Required = true,
                Description = "One operation, maximum 1024 UTF-8 bytes.",
                Content = new Dictionary<string, OpenApiMediaType>
                {
                    {
                        "application/json",
                        new() { Schema = body }
                    },
                },
            };
        }
        if (type == typeof(TripFlightCandidatePage))
            operation.Parameters.Add(
                new OpenApiParameter
                {
                    Name = "offset",
                    In = ParameterLocation.Query,
                    Schema = new OpenApiSchema
                    {
                        Type = JsonSchemaType.Integer,
                        Minimum = "0",
                        Maximum = "10000",
                    },
                }
            );
        var error = await context.GetOrCreateSchemaAsync(
            typeof(ProblemDetails),
            cancellationToken: ct
        );
        foreach (
            var code in patch
                ? new[] { "400", "401", "403", "404", "409", "412", "413", "428", "503", "504" }
                : new[] { "400", "401", "403", "404", "503", "504" }
        )
            operation.Responses[code] = new OpenApiResponse
            {
                Description = "Trips problem.",
                Content = new Dictionary<string, OpenApiMediaType>
                {
                    {
                        "application/problem+json",
                        new() { Schema = error }
                    },
                },
            };
    }

    private static OpenApiSchema Tag() =>
        new() { Type = JsonSchemaType.String, Pattern = "^\"fl-(0|[1-9][0-9]{0,18})\"$" };
}
