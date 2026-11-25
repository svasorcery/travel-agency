using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;
using Travel.Modules.Trips.Api.Contracts;
using Travel.Modules.Trips.Core;

namespace Travel.Modules.Trips.Api.Composition;

internal sealed class TripWorkspaceOpenApi : IOpenApiOperationTransformer
{
    private sealed record DetailFields(
        Guid TripId,
        string Revision,
        string Title,
        TripBriefInput Brief,
        DestinationSelection Destination,
        DateTimeOffset CreatedAt,
        DateTimeOffset UpdatedAt,
        IReadOnlyList<TripDayInput> Days,
        string KnownUserEstimateSubtotal,
        int UnknownEstimateCount,
        IReadOnlyList<string> Warnings
    );

    public async Task TransformAsync(
        OpenApiOperation operation,
        OpenApiOperationTransformerContext context,
        CancellationToken ct
    )
    {
        var path = context
            .Description.RelativePath?.Trim('/')
            .Replace("{tripId:guid}", "{tripId}", StringComparison.OrdinalIgnoreCase);
        var method = context.Description.HttpMethod;
        if (
            path is not ("api/trips/{tripId}" or "api/trips/{tripId}/workspace-v2")
            || method is not ("GET" or "PUT")
        )
            return;

        var versioned = path.EndsWith("/workspace-v2", StringComparison.Ordinal);
        var fields = await context.GetOrCreateSchemaAsync(
            typeof(DetailFields),
            cancellationToken: ct
        );
        var stay = await context.GetOrCreateSchemaAsync(
            typeof(TripStayInput),
            cancellationToken: ct
        );
        var legacyDetail = Detail(fields, "manualStay", stay, required: false);
        var detail = versioned
            ? new OpenApiSchema
            {
                OneOf =
                [
                    legacyDetail,
                    Detail(fields, "manualStays", Stays(stay, 2), required: true),
                ],
            }
            : legacyDetail;

        var responses = operation.Responses ??= new OpenApiResponses();
        responses["200"] = new OpenApiResponse
        {
            Description = "Private saved plan and exact revision.",
            Content = JsonContent(detail),
            Headers = new Dictionary<string, IOpenApiHeader>
            {
                ["ETag"] = new OpenApiHeader { Required = true, Schema = RevisionTag() },
            },
        };

        if (method == "PUT")
        {
            operation.Parameters ??= [];
            operation.Parameters.Add(
                new OpenApiParameter
                {
                    Name = "If-Match",
                    In = ParameterLocation.Header,
                    Required = true,
                    Schema = RevisionTag(),
                }
            );
            var body = await context.GetOrCreateSchemaAsync(
                versioned ? typeof(ReplaceTripPlanV2Dto) : typeof(ReplaceTripPlanDto),
                cancellationToken: ct
            );
            body.AdditionalPropertiesAllowed = false;
            body.Required = new HashSet<string> { "title", "brief", "days" };
            if (versioned)
            {
                body.Required.Add("manualStays");
                body.Properties!["manualStays"] = Stays(stay, 0);
            }
            operation.RequestBody = new OpenApiRequestBody
            {
                Required = true,
                Content = JsonContent(body),
            };
        }

        var problem = await context.GetOrCreateSchemaAsync(
            typeof(ProblemDetails),
            cancellationToken: ct
        );
        foreach (
            var status in method == "PUT"
                ? new[] { "400", "401", "403", "404", "409", "412", "428", "503" }
                : new[] { "400", "401", "403", "404", "409", "503" }
        )
        {
            responses[status] = new OpenApiResponse
            {
                Description =
                    status == "409" && !versioned
                        ? "Plan conflict. Plans with multiple manual stays require workspace-v2."
                        : "Trips problem.",
                Content = new Dictionary<string, OpenApiMediaType>
                {
                    ["application/problem+json"] = new() { Schema = problem },
                },
            };
        }
    }

    private static OpenApiSchema Detail(
        OpenApiSchema fields,
        string staysField,
        IOpenApiSchema stays,
        bool required
    )
    {
        var properties = new Dictionary<string, IOpenApiSchema>(fields.Properties!);
        var requiredFields = new HashSet<string>(properties.Keys);
        properties.Add(staysField, stays);
        if (required)
            requiredFields.Add(staysField);
        return new OpenApiSchema
        {
            Type = JsonSchemaType.Object,
            Properties = properties,
            Required = requiredFields,
            AdditionalPropertiesAllowed = false,
        };
    }

    private static OpenApiSchema Stays(IOpenApiSchema stay, int minimum) =>
        new()
        {
            Type = JsonSchemaType.Array,
            Items = stay,
            MinItems = minimum,
            MaxItems = TripPlan.MaximumManualStays,
        };

    private static OpenApiSchema RevisionTag() =>
        new() { Type = JsonSchemaType.String, Pattern = "^\"[1-9][0-9]{0,18}\"$" };

    private static Dictionary<string, OpenApiMediaType> JsonContent(IOpenApiSchema schema) =>
        new() { ["application/json"] = new() { Schema = schema } };
}
