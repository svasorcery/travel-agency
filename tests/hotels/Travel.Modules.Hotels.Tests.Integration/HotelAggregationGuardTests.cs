using Microsoft.Extensions.Options;
using Shouldly;
using Travel.Modules.Hotels.Core;
using Travel.Modules.Hotels.Infrastructure.Providers.Duffel;
using Travel.Modules.Hotels.Infrastructure.Providers.Hotelbeds;

namespace Travel.Modules.Hotels.Tests.Integration;

public sealed class HotelAggregationGuardTests
{
    [Fact]
    public async Task M2_off_fictional_Duffel_cannot_create_or_read_through_ordinary_transport()
    {
        var settings = Options.Create(
            new HotelsOptions
            {
                Enabled = true,
                BookingEnabled = true,
                ReceiverEnabled = true,
                ApiKey = "duffel_test_owned_only",
                WebhookSecret = "owned-fictional-secret",
                Aggregation = new() { Enabled = false, Fictional = true },
            }
        );
        var handler = new DuffelStaysProviderTests.Handler("{}");
        var provider = new DuffelStaysProvider(
            new HttpClient(handler),
            settings,
            new(settings, TimeProvider.System),
            TimeProvider.System
        );
        var party = new HotelParty(
            [new("Fictional", "Traveller")],
            "fictional@example.invalid",
            "+44000000000",
            true,
            true
        );
        var create = await provider.CreateAsync(
            new(
                Guid.NewGuid(),
                Guid.NewGuid(),
                DuffelStaysProviderTests.Terms(),
                new(1, "{\"QuoteId\":\"quo_owned\",\"RateId\":\"rat_owned\"}"),
                party
            ),
            TestContext.Current.CancellationToken
        );
        create.Kind.ShouldBe(ObservationKind.Unknown);
        var read = await provider.ReadAsync("bok_owned", TestContext.Current.CancellationToken);
        read.Kind.ShouldBe(ObservationKind.Unknown);
        provider.CurrentSource.NewPurchaseAllowed.ShouldBeFalse();
        handler.Calls.ShouldBe(0);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Fictional_Duffel_flag_never_opens_an_ordinary_HTTP_client(
        bool aggregationEnabled
    )
    {
        var settings = Options.Create(
            new HotelsOptions
            {
                Enabled = true,
                ApiKey = "duffel_test_owned_fictional",
                Aggregation = new()
                {
                    Enabled = aggregationEnabled,
                    Fictional = true,
                    ConfigurationRevision = "owned",
                    ApprovalRevision = "owned",
                },
            }
        );
        var handler = new DuffelStaysProviderTests.Handler("{}");
        var provider = new DuffelStaysProvider(
            new HttpClient(handler),
            settings,
            new(settings, TimeProvider.System),
            TimeProvider.System
        );
        var result = await provider.SearchAsync(
            DuffelStaysProviderTests.Request(),
            TestContext.Current.CancellationToken
        );
        result.Reason.ShouldBe("NotConfigured");
        result.Attempted.ShouldBeFalse();
        handler.Calls.ShouldBe(0);
    }

    [Fact]
    public async Task Own_Hotelbeds_approval_does_not_override_missing_pair_approval()
    {
        var handler = new DuffelStaysProviderTests.Handler("{}");
        var options = Options.Create(
            new HotelbedsOptions
            {
                Enabled = true,
                ExternalUseApproved = true,
                AccountAlias = "owned",
                ConfigurationRevision = "r1",
                ApprovalRevision = "r1",
                ApiKey = "owned-fictional-key",
                Secret = "owned-fictional-secret",
                ClientCertificateThumbprint = new string('A', 40),
            }
        );
        var global = Options.Create(
            new HotelsOptions
            {
                Aggregation = new() { Enabled = true, ExternalUseApproved = false },
            }
        );
        var provider = new HotelbedsStayProvider(
            new HttpClient(handler),
            options,
            new(global, TimeProvider.System),
            TimeProvider.System,
            global
        );
        var result = await provider.SearchAsync(
            DuffelStaysProviderTests.Request(),
            TestContext.Current.CancellationToken
        );
        result.Reason.ShouldBe("RightsNotCleared");
        result.Attempted.ShouldBeFalse();
        handler.Calls.ShouldBe(0);
    }

    [Fact]
    public async Task Enabling_actual_aggregation_without_pair_rights_stops_Duffel_before_dispatch()
    {
        var settings = new HotelsOptions
        {
            Enabled = true,
            ApiKey = "duffel_test_owned_fictional",
            Aggregation = new() { Enabled = true },
        };
        var handler = new DuffelStaysProviderTests.Handler("{}");
        var options = Options.Create(settings);
        var provider = new DuffelStaysProvider(
            new HttpClient(handler),
            options,
            new(options, TimeProvider.System),
            TimeProvider.System
        );
        var result = await provider.SearchAsync(
            DuffelStaysProviderTests.Request(),
            TestContext.Current.CancellationToken
        );
        result.Kind.ShouldBe(ExecutionKind.Unsupported);
        result.Reason.ShouldBe("RightsNotCleared");
        result.Attempted.ShouldBeFalse();
        handler.Calls.ShouldBe(0);
    }

    [Fact]
    public void Disabled_Hotelbeds_needs_no_secrets_and_Production_cannot_enable_it()
    {
        new HotelbedsOptions().IsValid(true).ShouldBeTrue();
        new HotelbedsOptions
        {
            Enabled = true,
            Fictional = true,
            AccountAlias = "owned",
            ConfigurationRevision = "r1",
            ApprovalRevision = "owned",
            ApiKey = "fictional-key",
            Secret = "fictional-secret",
        }
            .IsValid(true)
            .ShouldBeFalse();
    }
}
