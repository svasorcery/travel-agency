using ErrorOr;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Travel.Modules.Flights.Api.Contracts;
using Travel.Modules.Flights.Application.Queries;
using Travel.Modules.Flights.Core.ValueObjects;
using Travel.Shared.Web;
using Wolverine;
using Wolverine.Http;

namespace Travel.Modules.Flights.Api.Endpoints;

public sealed class SearchEndpoint
{
    private static readonly HashSet<string> SupportedLocales = ["ru", "en"];

    [WolverinePost("/api/flights/search")]
    [AllowAnonymous]
    public static async Task<IResult> Post(
        SearchRequest req,
        IMessageBus bus,
        HttpRequest httpRequest,
        [FromQuery] string? currency,
        CancellationToken ct
    )
    {
        var currencyCode = CurrencyCode.Create(currency ?? "RUB");
        if (currencyCode.IsError)
            return Results.Problem(currencyCode.Errors.ToProblemDetails());

        var locale = ResolveLocale(httpRequest);

        var origin = IataCode.Create(req.Origin);
        if (origin.IsError)
            return Results.Problem(origin.Errors.ToProblemDetails());

        var dest = IataCode.Create(req.Destination);
        if (dest.IsError)
            return Results.Problem(dest.Errors.ToProblemDetails());

        var cabin = CabinClass.Parse(req.CabinClass);
        if (cabin.IsError)
            return Results.Problem(cabin.Errors.ToProblemDetails());

        var sc = SearchCriteria.Create(
            origin.Value,
            dest.Value,
            req.DepartureDate,
            req.ReturnDate,
            req.PassengerCount,
            cabin.Value,
            currencyCode.Value,
            locale
        );
        if (sc.IsError)
            return Results.Problem(sc.Errors.ToProblemDetails());

        var result = await bus.InvokeAsync<ErrorOr<SearchResult>>(
            new SearchFlightsQuery(sc.Value),
            ct
        );
        if (result.IsError)
            return Results.Problem(result.Errors.ToProblemDetails());

        return Results.Ok(
            new SearchResponse(
                result.Value.Offers.Select(OfferDto.From).ToArray(),
                result
                    .Value.PartialFailures.Select(f => new PartialFailureDto(
                        f.Provider,
                        f.ErrorCode,
                        f.ElapsedMs
                    ))
                    .ToArray()
            )
        );
    }

    /// <summary>
    /// Reads the best-match locale from the <c>Accept-Language</c> header.
    /// Supported: <c>ru</c>, <c>en</c>. Falls back to <c>ru</c>.
    /// </summary>
    internal static string ResolveLocale(HttpRequest request)
    {
        foreach (var value in request.Headers.AcceptLanguage.ToString().Split(','))
        {
            var tag = value.Trim().Split(';')[0].Trim().ToLowerInvariant();
            var primary = tag.Split('-')[0];
            if (SupportedLocales.Contains(primary))
                return primary;
        }

        return "ru";
    }
}
