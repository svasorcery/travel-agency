using System.Security.Claims;
using ErrorOr;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Travel.Modules.Hotels.Application;
using Travel.Modules.Hotels.Core;
using Travel.Shared.Web;
using Wolverine.Http;

namespace Travel.Modules.Hotels.Api.Endpoints;

public sealed record HotelSearchDto(
    string AreaKey,
    DateOnly CheckIn,
    DateOnly CheckOut,
    int Adults,
    int Rooms = 1,
    int Children = 0
);

public sealed record HotelRatesDto(Guid SearchId, string SelectionKey);

public sealed record HotelQuoteDto(Guid RatesId, string SelectionKey, int? ExpectedRevision);

public static class HotelEndpoints
{
    [WolverinePost("/api/hotels/search")]
    [AllowAnonymous]
    public static async Task<IResult> Search(
        HotelSearchDto body,
        [FromServices] HotelSearchService service,
        CancellationToken ct
    )
    {
        var dates = StayDates.Create(body.CheckIn, body.CheckOut);
        var occupancy = RoomOccupancy.Create(body.Adults, body.Rooms, body.Children);
        var area = HotelSearchService.Areas.SingleOrDefault(a => a.Key == body.AreaKey);
        return dates.IsError || occupancy.IsError || area is null
            ? Problem(HotelErrors.Invalid)
            : Reply(await service.SearchAsync(new(area, dates.Value, occupancy.Value), ct));
    }

    [WolverinePost("/api/hotels/rates")]
    [AllowAnonymous]
    public static async Task<IResult> Rates(
        HotelRatesDto body,
        [FromServices] HotelSearchService service,
        CancellationToken ct
    ) => Reply(await service.RatesAsync(body.SearchId, body.SelectionKey, ct));

    [WolverinePut("/api/hotels/reservations/{id:guid}/quote")]
    [Authorize(Policy = "hotels:book")]
    public static async Task<IResult> Quote(
        Guid id,
        HotelQuoteDto body,
        HttpContext context,
        [FromServices] HotelSearchService search,
        [FromServices] HotelReservationService service,
        CancellationToken ct
    )
    {
        if (!context.User.TryGetUserId(out var owner))
            return Results.Forbid();
        var selection = search.Select(body.RatesId, body.SelectionKey);
        return selection.IsError
            ? Results.Problem(selection.Errors.ToProblemDetails())
            : Reply(
                await service.QuoteAsync(owner, id, body.ExpectedRevision, selection.Value, ct)
            );
    }

    [WolverinePost("/api/hotels/reservations/{id:guid}/purchase")]
    [Authorize(Policy = "hotels:book")]
    public static async Task<IResult> Purchase(
        Guid id,
        HotelPurchase body,
        HttpContext context,
        [FromServices] HotelReservationService service,
        CancellationToken ct
    ) =>
        context.User.TryGetUserId(out var owner)
            ? Reply(await service.PurchaseAsync(owner, id, body, ct))
            : Results.Forbid();

    [WolverineGet("/api/hotels/reservations/{id:guid}")]
    [Authorize(Policy = "hotels:book")]
    public static async Task<IResult> Detail(
        Guid id,
        HttpContext context,
        [FromServices] HotelReservationService service,
        CancellationToken ct
    ) =>
        context.User.TryGetUserId(out var owner)
            ? Reply(await service.DetailAsync(owner, id, ct))
            : Results.Forbid();

    [WolverineGet("/api/hotels/reservations")]
    [Authorize(Policy = "hotels:book")]
    public static async Task<IResult> List(
        HttpContext context,
        [FromServices] HotelReservationService service,
        CancellationToken ct
    ) =>
        context.User.TryGetUserId(out var owner)
            ? Reply(await service.ListAsync(owner, ct))
            : Results.Forbid();

    private static IResult Reply<T>(ErrorOr<T> result) =>
        result.IsError
            ? result.FirstError.Code == HotelErrors.Capacity.Code
                ? Results.Problem(statusCode: 429, title: "Please try again later.")
                : Results.Problem(result.Errors.ToProblemDetails())
            : Results.Ok(result.Value);

    private static IResult Problem(Error error) =>
        Results.Problem(new List<Error> { error }.ToProblemDetails());
}
