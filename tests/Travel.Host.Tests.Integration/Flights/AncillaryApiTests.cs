using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using ErrorOr;
using Shouldly;
using Travel.Modules.Flights.Application.Queries;
using Xunit;

namespace Travel.Host.Tests.Integration.Flights;

// Existing-CI runtime only: its HTTP fixture initializes signing/PII test keys.
[Collection(HostIntegrationCollection.Name)]
public sealed class AncillaryApiTests(FlightsApiFixture fixture) : IClassFixture<FlightsApiFixture>
{
    private static readonly Guid Owner = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid Booking = Guid.Parse("11111111-1111-1111-1111-111111111111");

    [Fact]
    public async Task Selected_quote_requires_book_scope_before_bus_or_claim()
    {
        fixture.Bus.Reset();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/flights/orders/quote");
        request.Headers.Authorization = new(
            "Bearer",
            ProfileJwtTestTokens.Create(Owner, scope: "")
        );
        request.Content = JsonContent.Create(
            new
            {
                ProviderOfferRef = "off_fictional",
                Provider = "duffel",
                Selections = new[] { new { SelectionKey = "ase_bag", Quantity = 1 } },
            }
        );
        using var response = await fixture.Client.SendAsync(
            request,
            TestContext.Current.CancellationToken
        );
        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        fixture.Bus.InvocationCount.ShouldBe(0);
    }

    [Fact]
    public async Task Signed_subject_controls_catalog_query_and_forged_demo_headers_do_not()
    {
        fixture.Bus.Reset();
        fixture.Bus.OnCapture<GetAncillariesQuery>(query =>
        {
            query.UserId.ShouldBe(Owner);
            query.AggregateId.ShouldBe(Booking);
            return (ErrorOr<AncillaryCatalogResult>)
                Error.NotFound("Flights.OfferNotFound", "Unavailable.");
        });
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            "/api/flights/orders/ancillaries"
        );
        request.Headers.Authorization = new("Bearer", ProfileJwtTestTokens.Create(Owner));
        request.Headers.Add(TestAuthHandler.UserIdHeader, Guid.NewGuid().ToString());
        request.Content = JsonContent.Create(
            new
            {
                AggregateId = Booking,
                QuoteRevision = Guid.NewGuid(),
                IncludeSeats = true,
            }
        );
        using var response = await fixture.Client.SendAsync(
            request,
            TestContext.Current.CancellationToken
        );
        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        response.Headers.CacheControl!.NoStore.ShouldBeTrue();
        fixture.Bus.InvocationCount.ShouldBe(1);
    }

    [Theory]
    [InlineData(false, 401)]
    [InlineData(true, 403)]
    public async Task Catalog_without_book_authority_never_dispatches(bool signed, int status)
    {
        fixture.Bus.Reset();
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            "/api/flights/orders/ancillaries"
        );
        if (signed)
            request.Headers.Authorization = new(
                "Bearer",
                ProfileJwtTestTokens.Create(Owner, scope: "")
            );
        request.Content = JsonContent.Create(
            new
            {
                AggregateId = Booking,
                QuoteRevision = Guid.NewGuid(),
                IncludeSeats = false,
            }
        );
        using var response = await fixture.Client.SendAsync(
            request,
            TestContext.Current.CancellationToken
        );
        ((int)response.StatusCode).ShouldBe(status);
        fixture.Bus.InvocationCount.ShouldBe(0);
    }

    [Fact]
    public async Task Creation_read_is_owner_bound_no_store_and_does_not_trigger_supplier_work()
    {
        fixture.Bus.Reset();
        fixture.Bus.OnCapture<GetBookingCreationQuery>(query =>
        {
            query.UserId.ShouldBe(Owner);
            query.AggregateId.ShouldBe(Booking);
            return (ErrorOr<BookingCreationStatusResult>)
                Error.NotFound("Flights.OfferNotFound", "Unavailable.");
        });
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"/api/flights/orders/{Booking}/creation"
        );
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            ProfileJwtTestTokens.Create(Owner)
        );
        using var response = await fixture.Client.SendAsync(
            request,
            TestContext.Current.CancellationToken
        );
        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        response.Headers.CacheControl!.NoStore.ShouldBeTrue();
        fixture.Bus.InvocationCount.ShouldBe(1);
    }
}
