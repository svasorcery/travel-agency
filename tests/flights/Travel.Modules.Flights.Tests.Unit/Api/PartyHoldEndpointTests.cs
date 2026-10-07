using System.Security.Claims;
using System.Text.Json;
using ErrorOr;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using Travel.Modules.Flights.Api.Contracts;
using Travel.Modules.Flights.Api.Endpoints;
using Travel.Modules.Flights.Application.Commands;
using Travel.Modules.Flights.Application.Privacy;
using Travel.Modules.Flights.Core.ValueObjects;
using Travel.Shared.Abstractions;
using Travel.Tests.Fixtures;
using Wolverine;
using Xunit;

namespace Travel.Modules.Flights.Tests.Unit.Api;

public sealed class PartyHoldEndpointTests
{
    private static readonly Guid Owner = Guid.NewGuid();

    private static HttpContext Context()
    {
        var context = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(
                new ClaimsIdentity(
                    [new Claim(ClaimTypes.NameIdentifier, Owner.ToString())],
                    "Bearer"
                )
            ),
        };
        // The direct endpoint fixture supplies the same server-only metadata as middleware.
        // The signed HTTP suite exercises its real derivation from bounded original bytes.
        var metadata =
            typeof(Travel.Modules.Flights.Api.Middleware.IdempotencyKeyMiddleware).GetNestedType(
                "CreationMetadata",
                System.Reflection.BindingFlags.NonPublic
            )!;
        context.Items["Flights.Creation.RequestMetadata"] = Activator.CreateInstance(
            metadata,
            Guid.NewGuid(),
            new string('a', 64)
        );
        return context;
    }

    private static HoldOfferRequest Request()
    {
        var binding = TestPii.Binding(2);
        return new(
            Guid.NewGuid(),
            binding
                .Slots.Select(s => new PassengerInfoDto(
                    "Fictional",
                    "Person",
                    new DateOnly(1990, 1, 1),
                    "female",
                    "private-person@example.test",
                    "+79161234567",
                    s.Id.Value,
                    "ms"
                ))
                .ToArray(),
            binding.Revision
        );
    }

    private sealed class Protector : IBookingPassengerPartyProtector
    {
        public int Calls;

        public ErrorOr<ProtectedPassengerPartySnapshot> Protect(
            BookingPassengerPartyProtectionContext c,
            EquatableArray<BookingPassenger> p
        )
        {
            Calls++;
            return TestPii.PartyProtector.Protect(c, p);
        }

        public ErrorOr<EquatableArray<BookingPassenger>> Unprotect(
            BookingPassengerPartyProtectionContext c,
            ProtectedPassengerPartySnapshot s
        ) => TestPii.PartyProtector.Unprotect(c, s);
    }

    [Fact]
    public async Task Whole_party_is_encrypted_once_before_bus_with_no_plaintext_command()
    {
        var req = Request();
        HoldOfferCommand? captured = null;
        var protector = new Protector();
        var bus = new FakeBus(m =>
        {
            captured = (HoldOfferCommand)m;
            return Task.FromResult<object?>(
                (ErrorOr<HeldOrderResult>)
                    new HeldOrderResult(
                        req.AggregateId,
                        "ord_group",
                        DateTimeOffset.UtcNow.AddHours(1)
                    )
            );
        });
        var result = await HoldOfferEndpoint.Post(
            req,
            Context(),
            bus,
            new FakeTimeProvider(new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero)),
            protector,
            TestContext.Current.CancellationToken
        );
        result.ShouldBeOfType<Ok<HeldOrderResponse>>();
        protector.Calls.ShouldBe(1);
        captured.ShouldNotBeNull();
        captured.PassengerCount.ShouldBe(2);
        captured.QuoteRevision.ShouldBe(req.QuoteRevision);
        var serialized = JsonSerializer.Serialize(captured);
        serialized.ShouldNotContain("Fictional");
        serialized.ShouldNotContain("private-person");
        serialized.ShouldNotContain("+7916");
        serialized.ShouldNotContain("1990-01-01");
        var unprotected = protector.Unprotect(
            new(req.AggregateId, Owner, req.QuoteRevision, 2),
            captured.ProtectedPassengerParty
        );
        unprotected.IsError.ShouldBeFalse();
        unprotected
            .Value.Select(p => p.Id.Value)
            .ToArray()
            .ShouldBe(req.Passengers.Select(p => p.BookingPassengerId).ToArray());
    }

    [Theory]
    [InlineData("revision")]
    [InlineData("slot")]
    [InlineData("title")]
    public async Task Missing_binding_stops_before_protection_or_bus(string missing)
    {
        var req = Request();
        if (missing == "revision")
            req = req with { QuoteRevision = Guid.Empty };
        if (missing == "slot")
            req.Passengers[0] = req.Passengers[0] with { BookingPassengerId = Guid.Empty };
        if (missing == "title")
            req.Passengers[0] = req.Passengers[0] with { Title = "" };
        var protector = new Protector();
        var calls = 0;
        var bus = new FakeBus(_ =>
        {
            calls++;
            throw new InvalidOperationException();
        });
        var result = await HoldOfferEndpoint.Post(
            req,
            Context(),
            bus,
            TimeProvider.System,
            protector,
            TestContext.Current.CancellationToken
        );
        result.ShouldBeOfType<ProblemHttpResult>().ProblemDetails.Status.ShouldBe(400);
        protector.Calls.ShouldBe(0);
        calls.ShouldBe(0);
    }

    [Fact]
    public async Task Raw_invalid_details_never_encrypt_or_dispatch_and_have_safe_slot_error()
    {
        var req = Request();
        req.Passengers[1] = req.Passengers[1] with { GivenName = "secret123" };
        var protector = new Protector();
        var calls = 0;
        var bus = new FakeBus(_ =>
        {
            calls++;
            throw new InvalidOperationException();
        });
        var result = await HoldOfferEndpoint.Post(
            req,
            Context(),
            bus,
            TimeProvider.System,
            protector,
            TestContext.Current.CancellationToken
        );
        var problem = result.ShouldBeOfType<ProblemHttpResult>().ProblemDetails;
        problem.Status.ShouldBe(400);
        protector.Calls.ShouldBe(0);
        calls.ShouldBe(0);
        var json = JsonSerializer.Serialize(problem);
        json.ShouldContain("Flights.PassengerInvalid");
        json.ShouldContain(req.Passengers[1].BookingPassengerId.ToString());
        json.ShouldNotContain("secret123");
        json.ShouldNotContain("private-person");
    }

    [Fact]
    public void Passenger_extensions_drop_foreign_metadata_and_limit_only_fixed_fields()
    {
        var id = Guid.NewGuid();
        var errors = Enumerable
            .Range(0, 80)
            .Select(_ =>
                Error.Validation(
                    "Flights.PassengerGivenNameInvalid",
                    "private-value",
                    new()
                    {
                        ["bookingPassengerId"] = id,
                        ["field"] = "givenName",
                        ["raw"] = "private-value",
                    }
                )
            )
            .ToList();
        errors.Add(
            Error.Validation(
                "Flights.PassengerPhoneInvalid",
                "private-value",
                new() { ["bookingPassengerId"] = Guid.Empty, ["field"] = "phone" }
            )
        );
        errors.Add(
            Error.Validation(
                "Flights.PassengerEmailInvalid",
                "private-value",
                new() { ["bookingPassengerId"] = Guid.NewGuid(), ["field"] = "evil-field" }
            )
        );
        var problem = FlightsPassengerProblemDetails.From(errors);
        var json = JsonSerializer.Serialize(problem);
        json.ShouldNotContain("private-value");
        json.ShouldNotContain("evil-field");
        using var parsed = JsonDocument.Parse(
            JsonSerializer.Serialize(problem.Extensions["passengerErrors"])
        );
        parsed.RootElement.GetArrayLength().ShouldBe(63);
    }

    private sealed class FakeBus(Func<object, Task<object?>> invokeFunc) : IMessageBus
    {
        public string? TenantId { get; set; }

        public IAsyncEnumerable<TResponse> StreamAsync<TResponse>(
            object message,
            CancellationToken cancellation = default
        ) => throw new NotSupportedException("Streaming is not used by this test.");

        public IAsyncEnumerable<TResponse> StreamAsync<TResponse>(
            object message,
            DeliveryOptions options,
            CancellationToken cancellation = default
        ) => throw new NotSupportedException("Streaming is not used by this test.");

        public Task<T> InvokeAsync<T>(
            object message,
            CancellationToken cancellation = default,
            TimeSpan? timeout = null
        )
        {
            var result = invokeFunc(message).GetAwaiter().GetResult();
            return Task.FromResult((T)result!);
        }

        public Task InvokeAsync(
            object message,
            CancellationToken cancellation = default,
            TimeSpan? timeout = null
        ) => invokeFunc(message);

        public Task InvokeAsync(
            object message,
            DeliveryOptions options,
            CancellationToken cancellation = default,
            TimeSpan? timeout = null
        ) => invokeFunc(message);

        public Task<T> InvokeAsync<T>(
            object message,
            DeliveryOptions options,
            CancellationToken cancellation = default,
            TimeSpan? timeout = null
        )
        {
            var result = invokeFunc(message).GetAwaiter().GetResult();
            return Task.FromResult((T)result!);
        }

        public Task InvokeForTenantAsync(
            string tenantId,
            object message,
            CancellationToken cancellation = default,
            TimeSpan? timeout = null
        ) => throw new NotSupportedException();

        public Task<T> InvokeForTenantAsync<T>(
            string tenantId,
            object message,
            CancellationToken cancellation = default,
            TimeSpan? timeout = null
        ) => throw new NotSupportedException();

        public ValueTask SendAsync<T>(T message, DeliveryOptions? options = null) =>
            ValueTask.CompletedTask;

        public ValueTask PublishAsync<T>(T message, DeliveryOptions? options = null) =>
            ValueTask.CompletedTask;

        public ValueTask BroadcastToTopicAsync(
            string topicName,
            object message,
            DeliveryOptions? options = null
        ) => ValueTask.CompletedTask;

        public IDestinationEndpoint EndpointFor(Uri uri) => throw new NotSupportedException();

        public IDestinationEndpoint EndpointFor(string endpointName) =>
            throw new NotSupportedException();

        public IReadOnlyList<Envelope> PreviewSubscriptions(object message) =>
            throw new NotSupportedException();

        public IReadOnlyList<Envelope> PreviewSubscriptions(
            object message,
            DeliveryOptions options
        ) => throw new NotSupportedException();
    }
}
