using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Travel.Modules.Rail.Application;
using Travel.Modules.Rail.Core;
using Wolverine.Http;

namespace Travel.Modules.Rail.Api.Endpoints;

public static class RailEndpoints
{
    [
        WolverineGet("/api/rail/stations"),
        AllowAnonymous,
        ProducesResponseType(typeof(StationSuggestions), 200)
    ]
    public static async Task<IResult> Stations(
        HttpContext context,
        [FromServices] RailReferenceService service,
        CancellationToken cancellationToken
    )
    {
        context.Response.Headers.CacheControl = "no-store";
        var query = context.Request.Query;
        if (query.Count != 1 || !query.TryGetValue("q", out var text) || text.Count != 1)
            return Problem("Rail.StationQuery.Invalid", 400);
        var result = await service.SuggestAsync(text[0]!, cancellationToken);
        return result.IsError
            ? Problem(result.FirstError.Code, result.FirstError.Code == "Rail.Capacity" ? 429 : 400)
            : Results.Ok(result.Value);
    }

    [
        WolverinePost("/api/rail/search"),
        AllowAnonymous,
        ProducesResponseType(typeof(RailSearchResult), 200),
        Consumes("application/json")
    ]
    public static async Task<IResult> Search(
        HttpContext context,
        [FromServices] RailSearchService service,
        CancellationToken cancellationToken
    )
    {
        context.Response.Headers.CacheControl = "no-store";
        if (context.Request.ContentLength > 8192)
            return Problem("Rail.Request.TooLarge", 413);
        try
        {
            using var buffer = new MemoryStream();
            var chunk = new byte[1024];
            while (true)
            {
                var read = await context.Request.Body.ReadAsync(chunk, cancellationToken);
                if (read == 0)
                    break;
                if (buffer.Length + read > 8192)
                    return Problem("Rail.Request.TooLarge", 413);
                buffer.Write(chunk, 0, read);
            }
            using var document = JsonDocument.Parse(
                buffer.ToArray(),
                new JsonDocumentOptions { MaxDepth = 8 }
            );
            var body = document.RootElement;
            Strict(body, ["from", "to", "catalogueGeneration", "departureDate", "transport"]);
            var from = Reference(body.GetProperty("from"));
            var to = Reference(body.GetProperty("to"));
            if (from is null || to is null)
                return Problem("Rail.Request.Invalid", 400);
            var request = new RailRequest(
                from,
                to,
                body.GetProperty("catalogueGeneration").GetString()!,
                body.GetProperty("departureDate").GetString()!,
                body.GetProperty("transport").GetString()!
            );
            if (
                request.CatalogueGeneration is null
                || request.DepartureDate is null
                || request.Transport is null
            )
                return Problem("Rail.Request.Invalid", 400);
            var result = await service.SearchAsync(request, cancellationToken);
            return result.IsError
                ? Problem(
                    result.FirstError.Code,
                    result.FirstError.Code == "Rail.Capacity" ? 429 : 400
                )
                : Results.Ok(result.Value);
        }
        catch (Exception error)
            when (error is JsonException or InvalidOperationException or KeyNotFoundException)
        {
            return Problem("Rail.Request.Invalid", 400);
        }
    }

    [
        WolverinePost("/api/rail/journey-details"),
        AllowAnonymous,
        ProducesResponseType(typeof(RailJourneyDetailsResult), 200),
        Consumes("application/json")
    ]
    public static async Task<IResult> JourneyDetails(
        HttpContext context,
        [FromServices] RailJourneyDetailsService service,
        CancellationToken cancellationToken
    )
    {
        context.Response.Headers.CacheControl = "no-store";
        if (context.Request.ContentLength > 8192)
            return Problem("Rail.Request.TooLarge", 413);
        RailJourneyDetailsRequest request;
        try
        {
            using var buffer = new MemoryStream();
            var chunk = new byte[1024];
            while (true)
            {
                var read = await context.Request.Body.ReadAsync(chunk, cancellationToken);
                if (read == 0)
                    break;
                if (buffer.Length + read > 8192)
                    return Problem("Rail.Request.TooLarge", 413);
                buffer.Write(chunk, 0, read);
            }
            using var document = JsonDocument.Parse(
                buffer.ToArray(),
                new JsonDocumentOptions { MaxDepth = 8 }
            );
            var body = document.RootElement;
            Strict(body, ["search", "expectedProfileEpoch", "selection"]);
            var search = body.GetProperty("search");
            Strict(search, ["from", "to", "catalogueGeneration", "departureDate", "transport"]);
            var from = Reference(search.GetProperty("from"));
            var to = Reference(search.GetProperty("to"));
            if (from is null || to is null)
                return Problem("Rail.Request.Invalid", 400);
            var selection = body.GetProperty("selection");
            Strict(selection, ["key", "transport", "departure", "arrival"]);
            var departure = RailRouteRules.Time(selection.GetProperty("departure").GetString());
            var arrival = RailRouteRules.Time(selection.GetProperty("arrival").GetString());
            if (departure.IsError || arrival.IsError)
                return Problem("Rail.Selection.Invalid", 400);
            string Required(JsonElement value, string name) =>
                value.GetProperty(name).GetString() ?? throw new JsonException();
            request = new RailJourneyDetailsRequest(
                new(
                    from,
                    to,
                    Required(search, "catalogueGeneration"),
                    Required(search, "departureDate"),
                    Required(search, "transport")
                ),
                Required(body, "expectedProfileEpoch"),
                new(
                    Required(selection, "key"),
                    Required(selection, "transport"),
                    departure.Value,
                    arrival.Value
                )
            );
        }
        catch (Exception error)
            when (error is JsonException or InvalidOperationException or KeyNotFoundException)
        {
            return Problem("Rail.Request.Invalid", 400);
        }
        var result = await service.GetAsync(request, cancellationToken);
        return result.IsError
            ? Problem(result.FirstError.Code, result.FirstError.Code == "Rail.Capacity" ? 429 : 400)
            : Results.Ok(result.Value);
    }

    private static void Strict(JsonElement value, string[] fields)
    {
        if (value.ValueKind != JsonValueKind.Object)
            throw new JsonException();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in value.EnumerateObject())
            if (!fields.Contains(property.Name, StringComparer.Ordinal) || !seen.Add(property.Name))
                throw new JsonException();
        if (seen.Count != fields.Length)
            throw new JsonException();
    }

    private static StationRef? Reference(JsonElement value)
    {
        Strict(value, ["source", "code"]);
        var source = value.GetProperty("source").GetString();
        var code = value.GetProperty("code").GetString();
        if (
            source != "yandex-rasp"
            || string.IsNullOrEmpty(code)
            || code[0] != 's'
            || code.Length < 2
            || code.AsSpan(1).IndexOfAnyExceptInRange('0', '9') >= 0
        )
            return null;
        var reference = StationRef.Create(source, code);
        return reference.IsError ? null : reference.Value;
    }

    private static IResult Problem(string code, int status) =>
        Results.Problem(
            statusCode: status,
            title: "Rail request cannot be completed.",
            extensions: new Dictionary<string, object?> { { "code", code } }
        );
}
