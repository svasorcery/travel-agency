using ErrorOr;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Travel.Modules.Flights.Api.Contracts;
using Travel.Modules.Flights.Application.Commands;
using Travel.Modules.Flights.Application.Privacy;
using Travel.Modules.Flights.Core.ValueObjects;
using Travel.Modules.Flights.Core.ValueObjects.Identifiers;
using Travel.Shared.Abstractions;
using Travel.Shared.Web;
using Wolverine;
using Wolverine.Http;

namespace Travel.Modules.Flights.Api.Endpoints;

public sealed class HoldOfferEndpoint
{
    [WolverinePost("/api/flights/orders/hold")]
    [Authorize("flights:book")]
    public static async Task<IResult> Post(
        HoldOfferRequest req,
        HttpContext httpContext,
        IMessageBus bus,
        TimeProvider timeProvider,
        [FromServices] IBookingPassengerPartyProtector protector,
        CancellationToken ct
    )
    {
        if (!httpContext.User.TryGetUserId(out var userId))
            return Results.Problem(IdentityProblemDetails.InvalidUserIdentity());

        if (
            req.QuoteRevision == Guid.Empty
            || req.Passengers is null
            || req.Passengers.Any(p =>
                p is null || p.BookingPassengerId == Guid.Empty || string.IsNullOrEmpty(p.Title)
            )
        )
            return Results.Problem(
                new List<Error>
                {
                    Error.Validation(
                        "Flights.QuoteBindingRequired",
                        "Quote revision and passenger bindings are required."
                    ),
                }.ToProblemDetails()
            );
        if (req.AggregateId == Guid.Empty || req.Passengers.Length is < 1 or > 9)
            return Results.Problem(
                new List<Error>
                {
                    Error.Validation("Flights.CommandInvalid", "Booking party is invalid."),
                }.ToProblemDetails()
            );
        if (
            req.Passengers.Select(p => p.BookingPassengerId).Distinct().Count()
            != req.Passengers.Length
        )
            return Results.Problem(
                new List<Error>
                {
                    Error.Conflict(
                        "Flights.PassengerSlotsMismatch",
                        "Passenger slots must be unique."
                    ),
                }.ToProblemDetails()
            );
        var today = DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);
        var passengers = new List<BookingPassenger>();
        var errors = new List<Error>();
        foreach (var dto in req.Passengers)
        {
            var id = BookingPassengerId.Create(dto.BookingPassengerId).Value;
            var details = BookingPassengerDetails.CreateRaw(
                id,
                dto.Title,
                dto.GivenName,
                dto.FamilyName,
                dto.DateOfBirth,
                dto.Gender,
                dto.Email,
                dto.Phone,
                today
            );
            if (details.IsError)
            {
                errors.AddRange(details.Errors);
                continue;
            }
            passengers.Add(BookingPassenger.Create(id, details.Value).Value);
        }
        if (errors.Count > 0)
            return Results.Problem(FlightsPassengerProblemDetails.From(errors));
        var context = new BookingPassengerPartyProtectionContext(
            req.AggregateId,
            userId,
            req.QuoteRevision,
            passengers.Count
        );
        var protectedParty = protector.Protect(
            context,
            new EquatableArray<BookingPassenger>(passengers.ToArray())
        );
        if (protectedParty.IsError)
            return Results.Problem(protectedParty.Errors.ToProblemDetails());
        var result = await bus.InvokeAsync<ErrorOr<HeldOrderResult>>(
            new HoldOfferCommand(
                req.AggregateId,
                userId,
                req.QuoteRevision,
                passengers.Count,
                protectedParty.Value
            ),
            ct
        );
        if (result.IsError)
            return Results.Problem(FlightsPassengerProblemDetails.From(result.Errors));

        return Results.Ok(
            new HeldOrderResponse(
                result.Value.AggregateId,
                result.Value.ProviderOrderId,
                result.Value.HeldUntil
            )
        );
    }
}
