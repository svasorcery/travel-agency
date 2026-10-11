using System.Reflection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Shouldly;
using Travel.Modules.Flights.Application.Refunds;
using Travel.Modules.Flights.Core.Providers;
using Travel.Modules.Flights.Core.Refunds;
using Travel.Modules.Flights.Infrastructure;
using Travel.Modules.Flights.Infrastructure.Payments;
using Travel.Shared.Abstractions;

namespace Travel.Modules.Flights.Tests.Unit.Refunds;

public sealed class CustomerRefundRegistrationTests
{
    [Fact]
    public async Task Startup_preset_uses_a_guard_visible_test_only_implementation()
    {
        var services = new ServiceCollection();
        services.AddSingleton<TimeProvider>(new CustomerRefundExecutionTests.Clock());
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?> { ["Flights:CustomerRefund:Preset"] = "NoEffect" }
            )
            .Build();
        Register(services, config, new EnvironmentStub());
        var descriptor = services.Single(s => s.ServiceType == typeof(ICustomerRefundGateway));
        descriptor.ImplementationType.ShouldBe(typeof(FictionalCustomerRefundGateway));
        descriptor.ImplementationType!.IsDefined(typeof(TestOnlyAttribute), false).ShouldBeTrue();
        await using var provider = services.BuildServiceProvider();
        var c = CustomerRefundDecisionTests.Admitted().CustomerRefund!;
        var result = await provider
            .GetRequiredService<ICustomerRefundGateway>()
            .ExecuteAsync(
                new(
                    c.Id,
                    c.Basis!,
                    c.Amount!,
                    CustomerRefundDestination.OriginalFictionalPaymentMethod
                ),
                CancellationToken.None
            );
        result.Value.State.ShouldBe(CustomerRefundState.ConfirmedNoEffect);
    }

    [Fact]
    public void Production_has_no_fake_adapter_and_assessment_disables_automatic_execution()
    {
        var services = new ServiceCollection();
        Register(
            services,
            new ConfigurationBuilder().Build(),
            new EnvironmentStub { EnvironmentName = Environments.Production }
        );
        services.ShouldNotContain(s => s.ServiceType == typeof(ICustomerRefundGateway));
        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<CustomerRefundAvailability>().CanExecute.ShouldBeFalse();
        var b = RefundTestBookings.Cancelled();
        CustomerRefundDecisionTests.Apply(
            b,
            b.DecideCustomerRefundAdmission(
                RefundTestBookings.Owner,
                RefundTestBookings.Cancellation,
                Guid.NewGuid(),
                RefundTestBookings.Now.AddSeconds(6),
                false
            )
        );
        b.CustomerRefund!.State.ShouldBe(CustomerRefundState.ManualReviewRequired);
        b.CustomerRefund.Reason.ShouldBe("GatewayUnavailable");
    }

    private static void Register(
        IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment
    ) =>
        typeof(FictionalCustomerRefundGateway)
            .Assembly.GetType(
                "Travel.Modules.Flights.Infrastructure.FlightsInfrastructureServiceCollectionExtensions"
            )!
            .GetMethod("AddCustomerRefundGateway", BindingFlags.Static | BindingFlags.NonPublic)!
            .Invoke(null, [services, configuration, environment]);

    private sealed class EnvironmentStub : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;
        public string ApplicationName { get; set; } = "FictionalRefundTests";
        public string ContentRootPath { get; set; } = Path.GetTempPath();
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
