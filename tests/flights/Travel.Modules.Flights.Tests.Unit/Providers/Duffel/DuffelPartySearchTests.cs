using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using Travel.Modules.Flights.Core.ValueObjects;
using Travel.Modules.Flights.Infrastructure.Providers.Duffel;
using Travel.Modules.Flights.Infrastructure.Providers.Travelpayouts;
using Xunit;

namespace Travel.Modules.Flights.Tests.Unit.Providers.Duffel;

public sealed class DuffelPartySearchTests
{
    private static SearchCriteria Criteria(int count) =>
        SearchCriteria
            .Create(
                IataCode.Create("LED").Value,
                IataCode.Create("DME").Value,
                new DateOnly(2027, 1, 1),
                null,
                count,
                CabinClass.Economy,
                CurrencyCode.Create("USD").Value
            )
            .Value;

    private sealed class Transport : HttpMessageHandler
    {
        public int Calls;
        public string? Body;

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage req,
            CancellationToken ct
        )
        {
            Calls++;
            Body = req.Content is null ? null : await req.Content.ReadAsStringAsync(ct);
            return new(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    "{\"data\":{\"offers\":[]}}",
                    Encoding.UTF8,
                    "application/json"
                ),
            };
        }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(9)]
    public async Task Request_prices_exact_number_of_adults(int count)
    {
        var transport = new Transport();
        using var http = new HttpClient(transport)
        {
            BaseAddress = new Uri("https://example.invalid"),
        };
        var options = Options.Create(new DuffelOptions { ApiKey = "fictional" });
        var provider = new DuffelFlightSearchProvider(
            new(http, options),
            options,
            new FakeTimeProvider(),
            NullLogger<DuffelFlightSearchProvider>.Instance
        );
        var result = await provider.SearchAsync(
            Criteria(count),
            TestContext.Current.CancellationToken
        );
        result.IsError.ShouldBeFalse();
        using var json = JsonDocument.Parse(transport.Body!);
        var passengers = json.RootElement.GetProperty("data").GetProperty("passengers");
        passengers.GetArrayLength().ShouldBe(count);
        foreach (var passenger in passengers.EnumerateArray())
            passenger.GetProperty("type").GetString().ShouldBe("adult");
    }

    [Theory]
    [InlineData(2)]
    [InlineData(9)]
    public async Task Travelpayouts_direct_call_defends_group_with_zero_http(int count)
    {
        var transport = new Transport();
        using var http = new HttpClient(transport)
        {
            BaseAddress = new Uri("https://example.invalid"),
        };
        var opts = Options.Create(new TravelpayoutsOptions());
        var provider = new TravelpayoutsSearchProvider(
            new(http, opts),
            opts,
            new(opts),
            null!,
            new FakeTimeProvider(),
            null!,
            NullLogger<TravelpayoutsSearchProvider>.Instance
        );
        provider.GetSupport(Criteria(count)).ReasonCode.ShouldBe("passenger-count-unsupported");
        var result = await provider.SearchAsync(
            Criteria(count),
            TestContext.Current.CancellationToken
        );
        result.IsError.ShouldBeFalse();
        result.Value.ShouldBeEmpty();
        transport.Calls.ShouldBe(0);
    }
}
