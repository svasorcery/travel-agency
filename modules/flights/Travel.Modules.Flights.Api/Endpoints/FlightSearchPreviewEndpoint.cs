using System.Globalization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Configuration;
using Travel.Modules.Flights.Application.NlSearch;
using Wolverine;
using Wolverine.Attributes;
using Wolverine.Http;

namespace Travel.Modules.Flights.Api.Endpoints;

public sealed record FlightSearchPreviewRequest(string? Query, string? ReferenceDate);

public sealed class FlightSearchPreviewEndpoint
{
    internal const string Path = "/api/flights/search/nl/preview";
    internal const string EnabledSetting = "Flights:NlSearchPreview:Enabled";

    [WolverinePost(Path)]
    [AllowAnonymous]
    [NonTransactional]
    [EnableRateLimiting("flights-nl-search")]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status413PayloadTooLarge)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status503ServiceUnavailable)]
    public static async Task<
        Results<Ok<FlightSearchPreviewDraft>, ProblemHttpResult, NotFound>
    > Post(
        FlightSearchPreviewRequest req,
        IConfiguration configuration,
        IMessageBus bus,
        CancellationToken ct
    )
    {
        if (!configuration.GetValue<bool>(EnabledSetting))
            return TypedResults.NotFound();

        if (
            string.IsNullOrWhiteSpace(req.Query)
            || req.Query.Length > 1000
            || !DateOnly.TryParseExact(
                req.ReferenceDate,
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var referenceDate
            )
        )
            return TypedResults.Problem(
                title: "Invalid flight description",
                detail: "Provide between 1 and 1000 characters and a reference date in yyyy-MM-dd.",
                statusCode: StatusCodes.Status400BadRequest
            );

        var draft = await FlightSearchPreviewParser.ParseAsync(req.Query, referenceDate, bus, ct);
        if (draft is null)
            return TypedResults.Problem(
                statusCode: StatusCodes.Status503ServiceUnavailable,
                title: "Flight description review is unavailable",
                type: "https://travel.local/errors/Flights.SearchPreviewUnavailable"
            );
        return TypedResults.Ok(draft);
    }
}
