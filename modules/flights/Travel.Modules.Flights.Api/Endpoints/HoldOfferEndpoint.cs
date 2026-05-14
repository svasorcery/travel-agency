using ErrorOr;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Travel.Modules.Flights.Api.Contracts;
using Travel.Modules.Flights.Application.Commands;
using Travel.Modules.Flights.Core.ValueObjects;
using Travel.Shared.Web;
using Wolverine;
using Wolverine.Http;

namespace Travel.Modules.Flights.Api.Endpoints;

public sealed class HoldOfferEndpoint
{
    [WolverinePost("/api/flights/orders/hold")]
    [Authorize]
    public static async Task<IResult> Post(
        HoldOfferRequest req,
        IMessageBus bus,
        TimeProvider timeProvider,
        CancellationToken ct
    )
    {
        if (req.Passengers.Length != 1)
            return Results.Problem(
                new List<Error>
                {
                    Error.Validation(
                        "Flights.SinglePassengerRequired",
                        "M1 supports single-passenger booking only."
                    ),
                }.ToProblemDetails()
            );

        var dto = req.Passengers[0];

        var gender = Gender.Parse(dto.Gender);
        if (gender.IsError)
            return Results.Problem(gender.Errors.ToProblemDetails());

        var phone = PhoneNumber.Create(dto.Phone);
        if (phone.IsError)
            return Results.Problem(phone.Errors.ToProblemDetails());

        var today = DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);
        var passenger = PassengerInfo.Create(
            dto.GivenName,
            dto.FamilyName,
            dto.DateOfBirth,
            gender.Value,
            dto.Email,
            phone.Value,
            today
        );
        if (passenger.IsError)
            return Results.Problem(passenger.Errors.ToProblemDetails());

        var result = await bus.InvokeAsync<ErrorOr<HeldOrderResult>>(
            new HoldOfferCommand(req.AggregateId, passenger.Value),
            ct
        );
        if (result.IsError)
            return Results.Problem(result.Errors.ToProblemDetails());

        return Results.Ok(
            new HeldOrderResponse(
                result.Value.AggregateId,
                result.Value.ProviderOrderId,
                result.Value.HeldUntil
            )
        );
    }
}
