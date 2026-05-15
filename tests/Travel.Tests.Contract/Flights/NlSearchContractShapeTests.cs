using System.Reflection;
using System.Text.Json;
using Shouldly;
using Travel.AI.NlSearch.Contracts;
using Xunit;
using HostContracts = Travel.Modules.Flights.Application.Contracts;

namespace Travel.Tests.Contract.Flights;

/// <summary>
/// Contract-shape tests for the NL-search Wolverine message pair:
/// <see cref="NlSearchRequested" /> (Travel.Host → Travel.AI) and
/// <see cref="NlSearchParsed" /> (Travel.AI → Travel.Host).
///
/// Strategy: serialise representative instances to JSON and assert against a
/// committed constant.  Any field rename, type change, or serialisation attribute
/// removal that would silently break the cross-service wire contract causes an
/// immediate assertion failure here.
///
/// NOTE: Full bidirectional Pact message verification (PactNet 5.x MessagePact) is
/// deferred to M2 — see ADR 0020.  The snapshot approach is sufficient for M1 because
/// both sides of the contract live in the same repo and drift is caught at build time.
/// </summary>
[Trait("Category", "Contract")]
public sealed class NlSearchContractShapeTests
{
    private static readonly JsonSerializerOptions PrettyOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        // Emit Cyrillic (and other non-ASCII) characters as-is rather than \uXXXX escapes,
        // so the pinned expected strings are human-readable.
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    // ── NlSearchRequested ────────────────────────────────────────────────────

    private const string ExpectedNlSearchRequestedJson = """
        {
          "query": "из Москвы в Санкт-Петербург 25 июня 2026",
          "correlationId": "a1b2c3d4-e5f6-7890-abcd-ef1234567890",
          "locale": "ru"
        }
        """;

    [Fact]
    public void NlSearchRequested_WireShape_IsStable()
    {
        var msg = new NlSearchRequested(
            Query: "из Москвы в Санкт-Петербург 25 июня 2026",
            CorrelationId: new Guid("a1b2c3d4-e5f6-7890-abcd-ef1234567890"),
            Locale: "ru"
        );

        var json = Normalise(JsonSerializer.Serialize(msg, PrettyOptions));
        json.ShouldBe(Normalise(ExpectedNlSearchRequestedJson.Trim()));
    }

    // ── NlSearchParsed (one-way) ─────────────────────────────────────────────

    private const string ExpectedNlSearchParsedOneWayJson = """
        {
          "correlationId": "a1b2c3d4-e5f6-7890-abcd-ef1234567890",
          "origin": "DME",
          "destination": "LED",
          "departureDate": "2026-06-25",
          "returnDate": null,
          "passengerCount": 1,
          "cabinClass": "economy",
          "currency": "RUB",
          "inputTokens": 0,
          "outputTokens": 0,
          "costUsd": 0,
          "modelId": ""
        }
        """;

    [Fact]
    public void NlSearchParsed_WireShape_IsStable()
    {
        // Model-usage fields default to 0/"" — they are populated by Travel.AI but the
        // wire shape must still include them so the cross-service contract is explicit.
        var msg = new NlSearchParsed(
            CorrelationId: new Guid("a1b2c3d4-e5f6-7890-abcd-ef1234567890"),
            Origin: "DME",
            Destination: "LED",
            DepartureDate: new DateOnly(2026, 6, 25),
            ReturnDate: null,
            PassengerCount: 1,
            CabinClass: "economy",
            Currency: "RUB"
        );

        var json = Normalise(JsonSerializer.Serialize(msg, PrettyOptions));
        json.ShouldBe(Normalise(ExpectedNlSearchParsedOneWayJson.Trim()));
    }

    // ── NlSearchParsed (round-trip) ──────────────────────────────────────────

    private const string ExpectedNlSearchParsedRoundTripJson = """
        {
          "correlationId": "b2c3d4e5-f6a7-8901-bcde-f12345678901",
          "origin": "LED",
          "destination": "DME",
          "departureDate": "2026-08-15",
          "returnDate": "2026-08-22",
          "passengerCount": 2,
          "cabinClass": "business",
          "currency": "RUB",
          "inputTokens": 1200,
          "outputTokens": 340,
          "costUsd": 0.0435,
          "modelId": "claude-opus-4-7"
        }
        """;

    [Fact]
    public void NlSearchParsed_RoundTrip_WireShape_IsStable()
    {
        var msg = new NlSearchParsed(
            CorrelationId: new Guid("b2c3d4e5-f6a7-8901-bcde-f12345678901"),
            Origin: "LED",
            Destination: "DME",
            DepartureDate: new DateOnly(2026, 8, 15),
            ReturnDate: new DateOnly(2026, 8, 22),
            PassengerCount: 2,
            CabinClass: "business",
            Currency: "RUB",
            InputTokens: 1200,
            OutputTokens: 340,
            CostUsd: 0.0435m,
            ModelId: "claude-opus-4-7"
        );

        var json = Normalise(JsonSerializer.Serialize(msg, PrettyOptions));
        json.ShouldBe(Normalise(ExpectedNlSearchParsedRoundTripJson.Trim()));
    }

    // ── Two-sided structural equality ────────────────────────────────────────

    /// <summary>
    /// Asserts that the host-side (<c>Travel.Modules.Flights.Application.Contracts</c>) and
    /// AI-side (<c>Travel.AI.NlSearch.Contracts</c>) records for <c>NlSearchRequested</c>
    /// and <c>NlSearchParsed</c> have identical constructor-parameter shapes:
    /// same names, same types (in declaration order).
    ///
    /// If the two sides drift the Wolverine message bus would silently discard fields, so
    /// this test is a hard regression guard.  No live API call required.
    /// </summary>
    [Fact]
    public void Host_and_AI_nl_search_contracts_have_identical_shape()
    {
        // ── NlSearchRequested ────────────────────────────────────────────────
        var aiRequested = typeof(NlSearchRequested);
        var hostRequested = typeof(HostContracts.NlSearchRequested);

        AssertRecordShapeEquals(aiRequested, hostRequested, "NlSearchRequested");

        // ── NlSearchParsed ───────────────────────────────────────────────────
        var aiParsed = typeof(NlSearchParsed);
        var hostParsed = typeof(HostContracts.NlSearchParsed);

        AssertRecordShapeEquals(aiParsed, hostParsed, "NlSearchParsed");
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    /// <summary>Normalise line endings to LF for cross-platform string comparison.</summary>
    private static string Normalise(string s) => s.Replace("\r\n", "\n");

    /// <summary>
    /// Asserts that two types have the same primary constructor parameters
    /// (same names, same types, same order).  Uses the primary constructor
    /// (the one that has the most parameters, which for C# records is always
    /// the positional constructor generated from the record declaration).
    /// </summary>
    private static void AssertRecordShapeEquals(Type aiType, Type hostType, string label)
    {
        static (string Name, Type ParamType)[] GetParams(Type t)
        {
            // Pick the primary constructor — the one with the most parameters.
            var ctor = t.GetConstructors(BindingFlags.Public | BindingFlags.Instance)
                .OrderByDescending(c => c.GetParameters().Length)
                .First();
            return ctor.GetParameters()
                .Select(p => (p.Name ?? string.Empty, p.ParameterType))
                .ToArray();
        }

        var aiParams = GetParams(aiType);
        var hostParams = GetParams(hostType);

        aiParams.Length.ShouldBe(
            hostParams.Length,
            $"{label}: parameter count mismatch — AI={aiParams.Length}, Host={hostParams.Length}"
        );

        for (var i = 0; i < aiParams.Length; i++)
        {
            aiParams[i]
                .Name.ShouldBe(
                    hostParams[i].Name,
                    $"{label}[{i}]: parameter name mismatch — AI='{aiParams[i].Name}', Host='{hostParams[i].Name}'"
                );
            aiParams[i]
                .ParamType.ShouldBe(
                    hostParams[i].ParamType,
                    $"{label}[{i}] '{aiParams[i].Name}': parameter type mismatch — AI={aiParams[i].ParamType.Name}, Host={hostParams[i].ParamType.Name}"
                );
        }
    }
}
