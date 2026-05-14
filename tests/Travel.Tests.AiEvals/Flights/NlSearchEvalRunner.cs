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
/// All 15 cases are skipped when <c>ANTHROPIC_API_KEY</c> is not set — the suite
/// is intentionally green-by-skip on dev machines and branch CI builds.
/// It only runs (and incurs API cost) on the master CI job that sets the secret.
/// </summary>
[Trait("Category", "AiEval")]
public sealed class NlSearchEvalRunner
{
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

    // ── theory ───────────────────────────────────────────────────────────────

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

        var result = await NlSearchExtractor.ExtractAsync(chat, c.Query, CancellationToken.None);

        var summary = new StringBuilder();
        summary.Append($"[{c.Id}] query=\"{c.Query}\" → ");
        summary.Append(
            $"origin={result.Origin} dest={result.Destination} dep={result.DepartureDate} ret={result.ReturnDate}"
        );

        switch (c.Kind)
        {
            case EvalKind.Clear:
                AssertIataMatch(result.Origin, c.Expected.Origin, $"[{c.Id}] origin");
                AssertIataMatch(
                    result.Destination,
                    c.Expected.Destination,
                    $"[{c.Id}] destination"
                );
                if (c.Expected.DepartureDate is not null)
                {
                    Assert.Equal(DateOnly.Parse(c.Expected.DepartureDate), result.DepartureDate);
                }

                if (c.Expected.ReturnDate is not null)
                {
                    Assert.Equal(DateOnly.Parse(c.Expected.ReturnDate), result.ReturnDate);
                }

                summary.Append(" PASS");
                break;

            case EvalKind.DateInference:
                // Origin/destination must still match; departure date is within tolerance.
                AssertIataMatch(result.Origin, c.Expected.Origin, $"[{c.Id}] origin");
                AssertIataMatch(
                    result.Destination,
                    c.Expected.Destination,
                    $"[{c.Id}] destination"
                );
                if (c.Expected.DepartureDate is not null)
                {
                    var expected = DateOnly.Parse(c.Expected.DepartureDate);
                    var diff = Math.Abs(result.DepartureDate.DayNumber - expected.DayNumber);
                    Assert.True(
                        diff <= c.ToleranceDays,
                        $"[{c.Id}] departure date {result.DepartureDate} is {diff} days from expected {expected}, tolerance={c.ToleranceDays}"
                    );
                }

                // For date-inference the LLM picks a concrete date; we just verify it's in
                // the plausible future (within 30 days of today) if no hard expected date given.
                var today = DateOnly.FromDateTime(DateTime.UtcNow);
                Assert.True(
                    result.DepartureDate >= today,
                    $"[{c.Id}] inferred departure date {result.DepartureDate} is in the past"
                );
                Assert.True(
                    result.DepartureDate <= today.AddDays(30),
                    $"[{c.Id}] inferred departure date {result.DepartureDate} is more than 30 days away (suspicious for 'next Friday/weekend')"
                );
                summary.Append(" PASS");
                break;

            case EvalKind.Ambiguous:
                // Just assert the handler returned something non-null without throwing.
                Assert.NotNull(result);
                summary.Append(" PASS (ambiguous — non-null result accepted)");
                break;
        }

        // Emit a per-case line visible in test output.
        Console.WriteLine(summary);
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
