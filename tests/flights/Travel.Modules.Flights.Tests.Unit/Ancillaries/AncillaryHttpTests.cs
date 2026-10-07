using System.Security.Claims;
using ErrorOr;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using Travel.Modules.Flights.Api.Contracts;
using Travel.Modules.Flights.Api.Endpoints;
using Travel.Modules.Flights.Application.Commands;
using Travel.Modules.Flights.Application.Handlers.Booking;
using Travel.Modules.Flights.Application.Observability;
using Travel.Modules.Flights.Core.Providers;
using Travel.Modules.Flights.Core.ValueObjects.Identifiers;
using Travel.Modules.Flights.Tests.Unit.Booking;
using Wolverine;

namespace Travel.Modules.Flights.Tests.Unit.Ancillaries;

public sealed class AncillaryHttpTests
{
    [Theory]
    [InlineData(179, "InProgress")]
    [InlineData(180, "ManualReviewRequired")]
    [InlineData(300, "ManualReviewRequired")]
    public void Missed_creation_worker_deadline_is_visible_without_claiming_no_effects(
        int seconds,
        string state
    )
    {
        var (booking, _, now) = AncillaryConfirmationTests.Held(false);
        var response = BookingCreationResponse.From(new(booking, now.AddSeconds(seconds)));
        response.State.ShouldBe(state);
        response.CanConfirm.ShouldBeFalse();
        response.CanCancel.ShouldBeFalse();
        response.CanRefresh.ShouldBeFalse();
        booking.CurrentCreation!.Outcome.ShouldBe(
            Travel.Modules.Flights.Core.Booking.BookingCreationOutcome.InProgress
        );
        booking.HasUnresolvedCreation.ShouldBeTrue();
    }

    [Fact]
    public async Task Omitted_selected_refresh_without_book_authority_stops_before_provider_access()
    {
        var (booking, _, _) = AncillaryConfirmationTests.Held(false);
        var reads = 0;
        var provider = (IFlightBookingProvider)
            BookingCreationCommitTests.Proxy.Make(
                typeof(IFlightBookingProvider),
                (method, _) =>
                {
                    if (method.Name == "get_Id")
                        return ProviderId.Duffel;
                    reads++;
                    throw new InvalidOperationException("Unauthorized supplier access.");
                }
            );
        var metrics = (IFlightsMetrics)
            BookingCreationCommitTests.Proxy.Make(typeof(IFlightsMetrics), (_, _) => null);
        var result = await QuoteOfferHandler.Handle(
            new(
                booking.ProviderOfferRef!,
                ProviderId.Duffel,
                booking.Id,
                booking.PassengerCount,
                booking.OwnerUserId,
                Selections: null,
                HasBookingAuthority: false
            ),
            [provider],
            BookingCreationCommitTests.Session(
                booking,
                [],
                () => throw new InvalidOperationException("Unauthorized commit.")
            ),
            BookingCreationCommitTests.Outbox([]),
            metrics,
            AncillaryCatalogTests.Clock,
            NullLogger<QuoteOfferCommand>.Instance,
            TestContext.Current.CancellationToken
        );
        result.IsError.ShouldBeTrue();
        result.FirstError.Code.ShouldBe("Flights.ServicePermissionRequired");
        reads.ShouldBe(0);
    }

    [Fact]
    public async Task Signed_in_subject_without_book_authority_cannot_claim_selected_quote_or_dispatch_bus()
    {
        var calls = 0;
        var bus = (IMessageBus)
            BookingCreationCommitTests.Proxy.Make(
                typeof(IMessageBus),
                (method, _) =>
                {
                    if (method.Name != "InvokeAsync")
                        throw new InvalidOperationException(method.Name);
                    calls++;
                    return Task.FromResult<ErrorOr<QuotedOfferResult>>(
                        Error.NotFound("Flights.OfferNotFound", "Unavailable.")
                    );
                }
            );
        var http = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(
                new ClaimsIdentity(
                    [new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString())],
                    "fictional"
                )
            ),
        };
        var result = await QuoteOfferEndpoint.Post(
            new("off_fictional", "duffel", Selections: [new("ase_bag", 1)]),
            bus,
            TestContext.Current.CancellationToken,
            http
        );
        result.ShouldBeOfType<ForbidHttpResult>();
        calls.ShouldBe(0);
    }
}
