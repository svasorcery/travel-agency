using Microsoft.Extensions.AI;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using Travel.AI.TripsBriefExtraction;
using Travel.IntegrationContracts.AI.TripsBriefExtraction;
using Xunit;
using static Travel.AI.Tests.TripsBriefExtraction.TripsBriefExtractorTests;

namespace Travel.AI.Tests.TripsBriefExtraction;

public sealed class TripsBriefStrictOutputTests
{
    public static IEnumerable<object[]> Malformed()
    {
        yield return [ValidJson.Replace("\"value\":\"moscow\"", "\"value\":\"paris\"")];
        yield return [ValidJson.Replace("\"state\":\"extracted\"", "\"state\":\"guess\"")];
        yield return [ValidJson.Replace("\"state\":\"extracted\"", "\"state\":\"missing\"")];
        yield return [ValidJson.Replace("\"value\":\"moscow\"", "\"value\":null")];
        yield return [ValidJson.Replace("\"end\":6", "\"end\":7")];
        yield return [ValidJson.Replace("\"start\":0", "\"start\":0.5")];
        yield return [ValidJson.Replace("\"start\":0", "\"start\":0,\"start\":0")];
        yield return [ValidJson.Replace("\"start\":0", "\"start\":0,\"quote\":\"private\"")];
        yield return [ValidJson.Replace("\"issues\":[]", "\"issues\":[],\"ownerId\":\"foreign\"")];
        yield return
        [
            ValidJson.Replace(
                "\"issues\":[]",
                "\"issues\":[{\"code\":\"sendMoney\",\"slots\":[],\"evidence\":[{\"start\":0,\"end\":6}]}]"
            ),
        ];
        yield return
        [
            ValidJson.Replace(
                "\"issues\":[]",
                "\"issues\":[{\"code\":\"unsupportedAction\",\"slots\":[\"money\"],\"evidence\":[{\"start\":0,\"end\":6}]}]"
            ),
        ];
        yield return [ValidJson.Replace("\"issues\":[]", "\"issues\":[[[[[[[[[[]]]]]]]]]]")];
        yield return [ValidJson.Replace("\"value\":\"moscow\"", "\"value\":\"\\ud800\"")];
        yield return ["" + new string('x', 16 * 1024 + 1)];
        yield return [ValidJson + " prose"];
        foreach (var slot in new[] { "adults", "durationDays" })
        foreach (var value in new[] { "\"2\"", "2.5", "0", "99", "null", "{}" })
            yield return
            [
                SetField(
                    ValidJson,
                    slot,
                    $"{{\"state\":\"extracted\",\"value\":{value},\"evidence\":[{{\"start\":0,\"end\":6}}],\"alternatives\":[]}}"
                ),
            ];
        yield return
        [
            SetField(
                ValidJson,
                "interests",
                "{\"state\":\"extracted\",\"value\":[\"history\",\"architecture\"],\"evidence\":[{\"start\":0,\"end\":6}],\"alternatives\":[]}"
            ),
        ];
        yield return
        [
            SetField(
                ValidJson,
                "interests",
                "{\"state\":\"extracted\",\"value\":[\"history\",\"history\"],\"evidence\":[{\"start\":0,\"end\":6}],\"alternatives\":[]}"
            ),
        ];
        yield return
        [
            SetField(
                ValidJson,
                "startDate",
                "{\"state\":\"extracted\",\"value\":\"2026-02-30\",\"evidence\":[{\"start\":0,\"end\":6}],\"alternatives\":[]}"
            ),
        ];
    }

    [Theory]
    [InlineData(0xd800)]
    [InlineData(0xdc00)]
    public async Task Invalid_utf16_is_rejected_before_xunit_argument_serialization_can_replace_it(
        int codepoint
    )
    {
        var time = new FakeTimeProvider();
        var options = Enabled();
        var chat = new FakeChat();
        var ledger = new FakeLedger();
        var reply = await new TripsBriefExtractor(
            chat,
            options,
            new(options, time),
            time
        ).ExtractAsync(
            Request(time, options) with
            {
                Text = "bad" + (char)codepoint,
            },
            ledger,
            TestContext.Current.CancellationToken
        );
        reply.Status.ShouldBe("InputUnavailable");
        chat.Calls.ShouldBe(0);
        ledger.Admitted.ShouldBeFalse();
    }

    [Fact]
    public void Published_fictional_corpus_fits_complete_input_envelope_without_a_model_call()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (
            root is not null
            && !File.Exists(
                Path.Combine(
                    root.FullName,
                    "openspec",
                    "changes",
                    "trips-m3-brief-confirmation",
                    "evaluation-plan.md"
                )
            )
        )
            root = root.Parent;
        root.ShouldNotBeNull("test artifacts must preserve current-worktree source provenance");
        var rows = File.ReadAllLines(
                Path.Combine(
                    root.FullName,
                    "openspec",
                    "changes",
                    "trips-m3-brief-confirmation",
                    "evaluation-plan.md"
                )
            )
            .Where(line =>
                System.Text.RegularExpressions.Regex.IsMatch(line, "^\\| [DH][0-9]{2} \\|")
            )
            .ToArray();
        rows.Length.ShouldBe(48);
        var time = new FakeTimeProvider();
        var options = Enabled();
        foreach (var row in rows)
        {
            var columns = row.Split('|');
            var request = Request(time, options) with { Text = columns[2].Trim() };
            var estimate = TripsBriefExtractor.EstimateInput(request, options.Model);
            estimate.WithinBounds.ShouldBeTrue(
                $"{columns[1].Trim()} complete input estimate {estimate.EstimatedInputTokens} exceeds {TripsBriefExtractor.MaxInputTokens}; revise source/corpus before any model execution"
            );
        }
    }

    private static string SetField(string json, string slot, string field) =>
        json.Replace(
            $"\"{slot}\":{{\"state\":\"missing\",\"value\":null,\"evidence\":[],\"alternatives\":[]}}",
            $"\"{slot}\":{field}"
        );

    [Theory]
    [MemberData(nameof(Malformed))]
    public async Task Rejects_exact_schema_type_depth_unicode_and_value_violations(string json)
    {
        var time = new FakeTimeProvider();
        var options = Enabled();
        var chat = new FakeChat { Json = json };
        var ledger = new FakeLedger();
        var reply = await new TripsBriefExtractor(
            chat,
            options,
            new(options, time),
            time
        ).ExtractAsync(Request(time, options), ledger, TestContext.Current.CancellationToken);
        reply.Status.ShouldBe("InvalidOutput");
        reply.Fields.ShouldBeNull();
        reply.CostUsd.ShouldBe(.000015m);
        chat.Calls.ShouldBe(1);
        ledger.Completed.ShouldBe(1);
    }

    [Theory]
    [InlineData("bad\ttext")]
    [InlineData("bad\rtext")]
    [InlineData("bad\0text")]
    public async Task Rejects_invalid_input_unicode_and_controls_without_ledger_or_provider(
        string text
    )
    {
        var time = new FakeTimeProvider();
        var options = Enabled();
        var chat = new FakeChat();
        var ledger = new FakeLedger();
        var reply = await new TripsBriefExtractor(
            chat,
            options,
            new(options, time),
            time
        ).ExtractAsync(
            Request(time, options) with
            {
                Text = text,
            },
            ledger,
            TestContext.Current.CancellationToken
        );
        reply.Status.ShouldBe("InputUnavailable");
        chat.Calls.ShouldBe(0);
        ledger.Admitted.ShouldBeFalse();
    }

    [Fact]
    public async Task Surrogate_pair_split_in_evidence_is_rejected()
    {
        var time = new FakeTimeProvider();
        var options = Enabled();
        var chat = new FakeChat { Json = ValidJson.Replace("\"start\":0", "\"start\":1") };
        var result = await new TripsBriefExtractor(
            chat,
            options,
            new(options, time),
            time
        ).ExtractAsync(
            Request(time, options) with
            {
                Text = "😀Москва",
            },
            new FakeLedger(),
            TestContext.Current.CancellationToken
        );
        result.Status.ShouldBe("InvalidOutput");
    }

    [Fact]
    public async Task Complete_envelope_budget_rejects_valid_text_that_does_not_fit_without_trimming()
    {
        var time = new FakeTimeProvider();
        var options = Enabled();
        var request = Request(time, options) with { Text = new string('я', 1000) };
        TripsBriefExtractor.EstimateInput(request, options.Model).WithinBounds.ShouldBeFalse();
        var chat = new FakeChat();
        var ledger = new FakeLedger();
        (
            await new TripsBriefExtractor(chat, options, new(options, time), time).ExtractAsync(
                request,
                ledger,
                TestContext.Current.CancellationToken
            )
        ).Status.ShouldBe("InputUnavailable");
        chat.Calls.ShouldBe(0);
        ledger.Admitted.ShouldBeFalse();
    }
}
