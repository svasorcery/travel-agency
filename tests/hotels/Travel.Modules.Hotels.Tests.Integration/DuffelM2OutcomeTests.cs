using System.Net;
using Microsoft.Extensions.Options;
using Shouldly;
using Travel.Modules.Hotels.Core;
using Travel.Modules.Hotels.Infrastructure.Providers.Duffel;

namespace Travel.Modules.Hotels.Tests.Integration;

public sealed class DuffelM2OutcomeTests
{
    [Theory]
    [InlineData(429, "SupplierRateLimit")]
    [InlineData(401, "SupplierAuthentication")]
    [InlineData(200, "Decode")]
    public async Task M2_source_keeps_attempted_supplier_failure_distinct(int status, string reason)
    {
        var options = Settings();
        var handler = new DuffelStaysProviderTests.Handler("{}", (HttpStatusCode)status);
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
        result.Kind.ShouldBe(ExecutionKind.Failed);
        result.Reason.ShouldBe(reason);
        result.Attempted.ShouldBeTrue();
        handler.Calls.ShouldBe(1);
    }

    [Fact]
    public async Task M2_local_capacity_is_unattempted_and_makes_no_HTTP_call()
    {
        var options = Settings();
        var capacity = new HotelCapacity(options, TimeProvider.System);
        using var held = capacity.TryAcquire("duffel-stays");
        var handler = new DuffelStaysProviderTests.Handler("{}");
        var provider = new DuffelStaysProvider(
            new HttpClient(handler),
            options,
            capacity,
            TimeProvider.System
        );
        var result = await provider.SearchAsync(
            DuffelStaysProviderTests.Request(),
            TestContext.Current.CancellationToken
        );
        result.Reason.ShouldBe("LocalCapacity");
        result.Attempted.ShouldBeFalse();
        handler.Calls.ShouldBe(0);
    }

    private static IOptions<HotelsOptions> Settings() =>
        Options.Create(
            new HotelsOptions
            {
                Enabled = true,
                ApiKey = "duffel_test_owned_only",
                Aggregation = new()
                {
                    Enabled = true,
                    ExternalUseApproved = true,
                    ApprovedUseScope = "DuffelHotelbedsSearchComparisonBooking",
                    ConfigurationRevision = "owned-1",
                    ApprovalRevision = "owned-1",
                },
            }
        );
}
