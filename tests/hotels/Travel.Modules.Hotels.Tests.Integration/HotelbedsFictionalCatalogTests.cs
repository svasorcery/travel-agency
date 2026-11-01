using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using Travel.Modules.Hotels.Core;
using Travel.Modules.Hotels.Infrastructure.Providers.Duffel;
using Travel.Modules.Hotels.Infrastructure.Providers.Hotelbeds;

namespace Travel.Modules.Hotels.Tests.Integration;

public sealed class HotelbedsFictionalCatalogTests
{
    [Theory]
    [InlineData(9)]
    [InlineData(100)]
    public async Task Availability_requests_exact_sorted_configured_property_scope(int count)
    {
        var ids = Enumerable.Range(900001, count).Reverse().ToArray();
        var options = Settings();
        SetIds(options, ids);
        var wire = new Capture();
        var provider = Provider(options, wire);
        await provider.SearchAsync(Request(), TestContext.Current.CancellationToken);
        wire.Ids.Single().ShouldBe(ids.Order().ToArray());
    }

    public static TheoryData<int[]?> InvalidCatalogs =>
        new()
        {
            (int[]?)null,
            Array.Empty<int>(),
            new[] { 0 },
            new[] { -1 },
            new[] { 900001, 900001 },
            Enumerable.Range(900001, 101).ToArray(),
        };

    [Theory]
    [MemberData(nameof(InvalidCatalogs))]
    public async Task Invalid_scope_refuses_before_any_transport(int[]? ids)
    {
        var options = Settings();
        SetIds(options, ids);
        var wire = new Capture();
        var result = await Provider(options, wire)
            .SearchAsync(Request(), TestContext.Current.CancellationToken);
        result.Kind.ShouldBe(ExecutionKind.Unsupported);
        result.Reason.ShouldBe("NotConfigured");
        wire.Ids.ShouldBeEmpty();
    }

    [Fact]
    public async Task Default_and_destination_guards_remain_single_property_and_zero_external_dispatch()
    {
        var wire = new Capture();
        var provider = Provider(Settings(), wire);
        await provider.SearchAsync(Request(), TestContext.Current.CancellationToken);
        wire.Ids.Single().ShouldBe([900001]);
        var result = await provider.SearchAsync(
            Request() with
            {
                Area = new("london", 51.5074m, -0.1278m, 5),
            },
            TestContext.Current.CancellationToken
        );
        result.Kind.ShouldBe(ExecutionKind.Unsupported);
        result.Reason.ShouldBe("DestinationUnsupported");
        wire.Ids.Count.ShouldBe(1);
    }

    [Fact]
    public async Task Mutated_catalog_requires_a_new_composition_before_dispatch()
    {
        var ids = new[] { 900001, 900002 };
        var options = Settings();
        SetIds(options, ids);
        var wire = new Capture();
        var provider = Provider(options, wire);
        ids[1] = 900003;
        var result = await provider.SearchAsync(Request(), TestContext.Current.CancellationToken);
        result.Kind.ShouldBe(ExecutionKind.Unsupported);
        wire.Ids.ShouldBeEmpty();
    }

    private static void SetIds(HotelbedsOptions options, int[]? ids) =>
        options.FictionalHotelIds = ids;

    private static HotelbedsOptions Settings() =>
        new()
        {
            Enabled = true,
            BookingEnabled = true,
            Fictional = true,
            AccountAlias = "owned-catalog",
            ConfigurationRevision = "catalog-v1",
            ApprovalRevision = "catalog-v1",
            ApiKey = "owned-fictional-key",
            Secret = "owned-fictional-secret",
        };

    private static StaySearch Request() =>
        new(
            new("fictional", 51.5m, -0.1m, 5, true),
            new(new(2026, 11, 6), new(2026, 11, 8)),
            new(2)
        );

    private static HotelbedsStayProvider Provider(HotelbedsOptions options, Capture wire)
    {
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 10, 30, 0, 0, 0, TimeSpan.Zero));
        var module = Options.Create(new HotelsOptions());
        return new(
            new HotelbedsFictionalHttpClient(wire),
            Options.Create(options),
            new(module, time),
            time
        );
    }

    private sealed class Capture : HttpMessageHandler, IHotelbedsFictionalTransport
    {
        public List<int[]> Ids { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken ct
        )
        {
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
            Ids.Add(
                body.RootElement.GetProperty("hotels")
                    .GetProperty("hotel")
                    .EnumerateArray()
                    .Select(x => x.GetInt32())
                    .ToArray()
            );
            return new(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    "{\"hotels\":{\"checkIn\":\"2026-11-06\",\"checkOut\":\"2026-11-08\",\"hotels\":[]}}",
                    Encoding.UTF8,
                    "application/json"
                ),
            };
        }
    }
}
