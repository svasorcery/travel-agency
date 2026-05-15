using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Anthropic;
using Anthropic.Core;
using Microsoft.Extensions.AI;
using Travel.AI.NlSearch;
using Xunit;

namespace Travel.Tests.AiEvals.Flights;

/// <summary>
/// Parameterised AI-eval suite for the NL-search extraction path.
/// All cases are skipped when <c>ANTHROPIC_API_KEY</c> is not set — the suite
/// is intentionally green-by-skip on dev machines and branch CI builds.
/// It only runs (and incurs API cost) on the master CI job that sets the secret.
///
/// Assertion strategy (spec §17):
/// - <c>Clear</c>: exact IATA match for origin/destination + departure date within ±1 day.
/// - <c>DateInference</c>: IATA match + departure date within ±1 day of expected (clamped
///   from the case's tolerance_days) + plausibility check anchored to <see cref="ReferenceDate"/>.
/// - <c>Ambiguous</c>: origin still resolves when given; result is a well-formed DTO
///   with non-empty cabin class and positive passenger count.
/// - Aggregate: pass-rate across all cases ≥ 0.9 (one regression fails the suite).
/// </summary>
[Trait("Category", "AiEval")]
public sealed class NlSearchEvalRunner
{
    // Fixed reference date for relative-date cases ("next Friday", "this weekend").
    // 2026-06-01 is a Monday → "next Friday" resolves to 2026-06-05.
    private static readonly DateOnly ReferenceDate = new(2026, 6, 1);

    // Pass-rate gate: at least 90% of cases must pass.
    private const double PassRateThreshold = 0.9;

    // ── data loading ─────────────────────────────────────────────────────────

    private static readonly Lazy<IReadOnlyList<NlSearchEvalCase>> _cases = new(LoadCases);

    private static IReadOnlyList<NlSearchEvalCase> LoadCases()
    {
        // The JSON is copied to the output directory by the csproj ItemGroup.
        var path = Path.Combine(AppContext.BaseDirectory, "Flights", "nl-search-cases.json");

        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<List<NlSearchEvalCase>>(
            json,
            new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                Converters = { new EvalKindConverter() },
            }
        )!;
    }

    public static IEnumerable<object[]> Cases => _cases.Value.Select(c => new object[] { c });

    // ── per-case theory ──────────────────────────────────────────────────────

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task NlSearch_ExtractsCorrectly(NlSearchEvalCase c)
    {
        var apiKey = Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY");
        if (string.IsNullOrWhiteSpace(apiKey))
            Assert.Skip("ANTHROPIC_API_KEY not set — AI-eval suite skipped.");

        var chat = new AnthropicClient(new ClientOptions { ApiKey = apiKey }).AsIChatClient(
            "claude-opus-4-7"
        );

        var extraction = await NlSearchExtractor.ExtractAsync(
            chat,
            c.Query,
            today: ReferenceDate,
            ct: CancellationToken.None
        );
        var result = extraction.Result;

        var summary = new StringBuilder();
        summary.Append($"[{c.Id}] query=\"{c.Query}\" → ");
        summary.Append(
            $"origin={result.Origin} dest={result.Destination} dep={result.DepartureDate} ret={result.ReturnDate}"
        );

        switch (c.Kind)
        {
            case EvalKind.Clear:
                // Exact IATA match (alternatives pipe-delimited) + date within ±1 day.
                AssertIataMatch(result.Origin, c.Expected.Origin, $"[{c.Id}] origin");
                AssertIataMatch(
                    result.Destination,
                    c.Expected.Destination,
                    $"[{c.Id}] destination"
                );
                if (c.Expected.DepartureDate is not null)
                    AssertDateWithinDays(
                        result.DepartureDate,
                        DateOnly.Parse(c.Expected.DepartureDate),
                        toleranceDays: 1,
                        $"[{c.Id}] departure date"
                    );
                if (c.Expected.ReturnDate is not null)
                    AssertDateWithinDays(
                        result.ReturnDate,
                        DateOnly.Parse(c.Expected.ReturnDate),
                        toleranceDays: 1,
                        $"[{c.Id}] return date"
                    );
                summary.Append(" PASS");
                break;

            case EvalKind.DateInference:
                // IATA match + departure within ±1 day of expected (if given), anchored to ReferenceDate.
                AssertIataMatch(result.Origin, c.Expected.Origin, $"[{c.Id}] origin");
                AssertIataMatch(
                    result.Destination,
                    c.Expected.Destination,
                    $"[{c.Id}] destination"
                );

                if (c.Expected.DepartureDate is not null)
                {
                    AssertDateWithinDays(
                        result.DepartureDate,
                        DateOnly.Parse(c.Expected.DepartureDate),
                        toleranceDays: 1,
                        $"[{c.Id}] departure date"
                    );
                }
                else
                {
                    // No expected date — verify it is plausibly "near" (within 30 days from reference).
                    Assert.True(
                        result.DepartureDate >= ReferenceDate,
                        $"[{c.Id}] inferred departure date {result.DepartureDate} is before reference date {ReferenceDate}"
                    );
                    Assert.True(
                        result.DepartureDate <= ReferenceDate.AddDays(30),
                        $"[{c.Id}] inferred departure date {result.DepartureDate} is more than 30 days after reference date {ReferenceDate} (suspicious for 'next Friday/weekend')"
                    );
                }

                summary.Append(" PASS");
                break;

            case EvalKind.Ambiguous:
                // When origin is given it must still resolve to a valid IATA code.
                if (!string.IsNullOrWhiteSpace(c.Expected.Origin))
                    AssertIataMatch(result.Origin, c.Expected.Origin, $"[{c.Id}] origin");

                // Result must be a well-formed SearchCriteriaDto — passenger count ≥ 1,
                // cabin class non-empty, and any returned IATA codes are 3 letters.
                Assert.True(
                    result.PassengerCount >= 1,
                    $"[{c.Id}] PassengerCount must be ≥ 1, got {result.PassengerCount}"
                );
                Assert.False(
                    string.IsNullOrWhiteSpace(result.CabinClass),
                    $"[{c.Id}] CabinClass must not be empty for ambiguous query"
                );
                if (!string.IsNullOrWhiteSpace(result.Origin))
                    Assert.True(
                        result.Origin.Length == 3,
                        $"[{c.Id}] Origin '{result.Origin}' is not a 3-letter IATA code"
                    );
                if (!string.IsNullOrWhiteSpace(result.Destination))
                    Assert.True(
                        result.Destination.Length == 3,
                        $"[{c.Id}] Destination '{result.Destination}' is not a 3-letter IATA code"
                    );

                summary.Append(" PASS (ambiguous — well-formedness asserted)");
                break;
        }

        // Emit a per-case line visible in test output.
        Console.WriteLine(summary);
    }

    // ── aggregate pass-rate gate ─────────────────────────────────────────────

    /// <summary>
    /// Runs all eval cases and asserts that at least <see cref="PassRateThreshold"/> × 100%
    /// pass. A single regression fails the suite without having to examine individual cases.
    /// </summary>
    [Fact]
    public async Task NlSearch_PassRate_MeetsThreshold()
    {
        var apiKey = Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY");
        if (string.IsNullOrWhiteSpace(apiKey))
            Assert.Skip("ANTHROPIC_API_KEY not set — AI-eval suite skipped.");

        var chat = new AnthropicClient(new ClientOptions { ApiKey = apiKey }).AsIChatClient(
            "claude-opus-4-7"
        );

        var cases = _cases.Value;
        var passed = 0;
        var failed = new List<string>();

        foreach (var c in cases)
        {
            try
            {
                var extraction = await NlSearchExtractor.ExtractAsync(
                    chat,
                    c.Query,
                    today: ReferenceDate,
                    ct: CancellationToken.None
                );
                var result = extraction.Result;

                switch (c.Kind)
                {
                    case EvalKind.Clear:
                        if (
                            !IataMatches(result.Origin, c.Expected.Origin)
                            || !IataMatches(result.Destination, c.Expected.Destination)
                        )
                            throw new Exception($"[{c.Id}] IATA mismatch");
                        if (c.Expected.DepartureDate is not null)
                        {
                            var diff = Math.Abs(
                                result.DepartureDate.DayNumber
                                    - DateOnly.Parse(c.Expected.DepartureDate).DayNumber
                            );
                            if (diff > 1)
                                throw new Exception(
                                    $"[{c.Id}] departure date {result.DepartureDate} off by {diff} days"
                                );
                        }

                        break;

                    case EvalKind.DateInference:
                        if (
                            !IataMatches(result.Origin, c.Expected.Origin)
                            || !IataMatches(result.Destination, c.Expected.Destination)
                        )
                            throw new Exception($"[{c.Id}] IATA mismatch");
                        if (
                            result.DepartureDate < ReferenceDate
                            || result.DepartureDate > ReferenceDate.AddDays(30)
                        )
                            throw new Exception(
                                $"[{c.Id}] inferred date {result.DepartureDate} out of plausible range"
                            );
                        break;

                    case EvalKind.Ambiguous:
                        if (
                            result.PassengerCount < 1
                            || string.IsNullOrWhiteSpace(result.CabinClass)
                        )
                            throw new Exception($"[{c.Id}] malformed ambiguous result");
                        break;
                }

                passed++;
                Console.WriteLine($"[pass] {c.Id}");
            }
            catch (Exception ex)
            {
                failed.Add($"{c.Id}: {ex.Message}");
                Console.WriteLine($"[fail] {c.Id}: {ex.Message}");
            }
        }

        var passRate = (double)passed / cases.Count;
        Console.WriteLine(
            $"Pass rate: {passed}/{cases.Count} = {passRate:P0} (threshold: {PassRateThreshold:P0})"
        );

        Assert.True(
            passRate >= PassRateThreshold,
            $"NL-search pass rate {passRate:P0} is below threshold {PassRateThreshold:P0}. "
                + $"Failed cases: {string.Join(", ", failed)}"
        );
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Asserts that <paramref name="actual" /> is one of the pipe-delimited alternatives
    /// in <paramref name="expectedAlternatives" />.
    /// If <paramref name="expectedAlternatives" /> is null or empty the assertion is skipped.
    /// </summary>
    private static void AssertIataMatch(
        string actual,
        string? expectedAlternatives,
        string fieldName
    )
    {
        if (string.IsNullOrWhiteSpace(expectedAlternatives))
            return;

        var alts = expectedAlternatives.Split(
            '|',
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries
        );

        Assert.Contains(actual, alts, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Asserts that <paramref name="actual"/> is within <paramref name="toleranceDays"/> of <paramref name="expected"/>.
    /// Handles null <paramref name="actual"/> gracefully (fails with a clear message).
    /// </summary>
    private static void AssertDateWithinDays(
        DateOnly? actual,
        DateOnly expected,
        int toleranceDays,
        string fieldName
    )
    {
        Assert.NotNull(actual);
        var diff = Math.Abs(actual.Value.DayNumber - expected.DayNumber);
        Assert.True(
            diff <= toleranceDays,
            $"{fieldName}: got {actual} but expected {expected} (tolerance ±{toleranceDays} day(s), diff={diff})"
        );
    }

    /// <summary>
    /// Returns true if <paramref name="actual"/> matches any pipe-delimited alternative in <paramref name="expectedAlternatives"/>,
    /// or if <paramref name="expectedAlternatives"/> is null/empty (skip).
    /// </summary>
    private static bool IataMatches(string actual, string? expectedAlternatives)
    {
        if (string.IsNullOrWhiteSpace(expectedAlternatives))
            return true;

        var alts = expectedAlternatives.Split(
            '|',
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries
        );
        return alts.Contains(actual, StringComparer.OrdinalIgnoreCase);
    }
}

// ── model types ──────────────────────────────────────────────────────────────

public sealed record NlSearchEvalCase(
    string Id,
    string Query,
    EvalExpected Expected,
    [property: JsonPropertyName("tolerance_days")] int ToleranceDays,
    EvalKind Kind
);

public sealed record EvalExpected(
    string? Origin,
    string? Destination,
    [property: JsonPropertyName("departure_date")] string? DepartureDate,
    [property: JsonPropertyName("return_date")] string? ReturnDate
);

[JsonConverter(typeof(EvalKindConverter))]
public enum EvalKind
{
    Clear,
    Ambiguous,
    DateInference,
}

/// <summary>
/// Custom converter that maps "date-inference" JSON string → <see cref="EvalKind.DateInference" />.
/// <see cref="JsonStringEnumConverter" /> cannot handle hyphenated names via [JsonPropertyName] on members.
/// </summary>
internal sealed class EvalKindConverter : JsonConverter<EvalKind>
{
    public override EvalKind Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options
    )
    {
        var value = reader.GetString();
        return value switch
        {
            "clear" => EvalKind.Clear,
            "ambiguous" => EvalKind.Ambiguous,
            "date-inference" => EvalKind.DateInference,
            _ => throw new JsonException($"Unknown EvalKind value: '{value}'"),
        };
    }

    public override void Write(Utf8JsonWriter writer, EvalKind value, JsonSerializerOptions options)
    {
        var str = value switch
        {
            EvalKind.Clear => "clear",
            EvalKind.Ambiguous => "ambiguous",
            EvalKind.DateInference => "date-inference",
            _ => throw new JsonException($"Unknown EvalKind: {value}"),
        };
        writer.WriteStringValue(str);
    }
}
