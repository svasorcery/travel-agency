using System.Text;
using Shouldly;
using Travel.Modules.Rail.Core;
using Travel.Modules.Rail.Infrastructure;
using Xunit;

namespace Travel.Modules.Rail.Tests.Integration;

public sealed class ScheduleParserTests
{
    [Fact]
    public void Missing_search_echo_is_query_mismatch_even_for_empty_schedule()
    {
        var body = System.Text.Json.Nodes.JsonNode.Parse(Body())!.AsObject();
        body.Remove("search");
        var result = Parse(body.ToJsonString());
        result.Kind.ShouldBe("failed");
        result.Reasons.ShouldContain("query-mismatch");
    }

    [Fact]
    public void Thirty_three_ticket_types_limit_only_optional_ticket_component()
    {
        var places = string.Join(
            ',',
            Enumerable.Repeat(
                "{\"name\":null,\"currency\":\"RUB\",\"price\":{\"whole\":0,\"cents\":0}}",
                33
            )
        );
        var row =
            Row[..^1] + ",\"tickets_info\":{\"et_marker\":false,\"places\":[" + places + "]}}";
        var result = Parse(Body(row, 1));
        result.Kind.ShouldBe("complete");
        result.Items[0].Tickets.Fares.Length.ShouldBe(32);
        result.Items[0].Tickets.FaresState.ShouldBe("limited");
        result.Items[0].Tickets.Reasons.ShouldContain("output-limit");
        result.Items[0].Tickets.ElectronicTicket.ShouldBe("false");
        result.Items[0].Tickets.Fares[0].Price.Amount.ShouldBe("0.00");
    }

    [Theory]
    [InlineData("fiction\n1", "failed")]
    [InlineData("fiction\u007f1", "failed")]
    [InlineData("поезд:opaque|variant", "complete")]
    public void Opaque_UID_preserves_punctuation_but_rejects_controls(
        string uid,
        string expected
    ) =>
        Parse(
            Body(
                Row.Replace(
                    "\"fiction-1\"",
                    System.Text.Json.JsonSerializer.Serialize(uid),
                    StringComparison.Ordinal
                ),
                1
            )
        )
            .Kind.ShouldBe(expected);

    [Theory]
    [InlineData("7200.0", "complete")]
    [InlineData("7.2e3", "complete")]
    [InlineData("7200.0000000000000000000000000000000001", "failed")]
    public void Critical_duration_is_exact_before_decimal_conversion(
        string duration,
        string expected
    ) =>
        Parse(
            Body(
                Row.Replace(
                    "\"duration\":7200",
                    "\"duration\":" + duration,
                    StringComparison.Ordinal
                ),
                1
            )
        )
            .Kind.ShouldBe(expected);

    [Fact]
    public void Interval_total_does_not_invent_a_next_page_reason()
    {
        var interval =
            """{"from":{"code":"s1","type":"station"},"to":{"code":"s2","type":"station"},"thread":{"transport_type":"train"},"has_transfers":false}""";
        var result = Parse(Body("", 1, interval));
        result.Kind.ShouldBe("limited");
        result.Reasons.ShouldContain("interval-service-unsupported");
        result.Reasons.ShouldNotContain("page-limit");
    }

    [Fact]
    public void Invalid_optional_carrier_preserves_schedule_with_reason()
    {
        var row = Row.Replace(
            "\"uid\":\"fiction-1\"",
            "\"carrier\":123,\"uid\":\"fiction-1\"",
            StringComparison.Ordinal
        );
        var result = Parse(Body(row, 1));
        result.Kind.ShouldBe("complete");
        result.Items[0].Reasons.ShouldContain("optional-label-unavailable");
    }

    public static RailRequest Request =>
        new(
            StationRef.Create("yandex-rasp", "s1").Value,
            StationRef.Create("yandex-rasp", "s2").Value,
            new string('a', 32),
            "2026-10-20",
            "train"
        );
    public const string Row =
        """{"from":{"code":"s1","type":"station"},"to":{"code":"s2","type":"station"},"thread":{"uid":"fiction-1","transport_type":"train","number":"001","title":"Вымышленный поезд"},"has_transfers":false,"departure":"2026-10-20T23:30:00+03:00","arrival":"2026-10-21T03:30:00+05:00","duration":7200}""";

    public static string Body(string rows = "", int count = 0, string? interval = null) =>
        """{"search":{"from":{"code":"s1","type":"station"},"to":{"code":"s2","type":"station"},"date":"2026-10-20"},"pagination":{"total":TOTAL,"offset":0,"limit":50},"segments":[ROWS]INTERVAL}"""
            .Replace(
                "TOTAL",
                count.ToString(System.Globalization.CultureInfo.InvariantCulture),
                StringComparison.Ordinal
            )
            .Replace("ROWS", rows, StringComparison.Ordinal)
            .Replace(
                "INTERVAL",
                interval is null ? "" : ",\"interval_segments\":[" + interval + "]",
                StringComparison.Ordinal
            );

    public static Travel.Modules.Rail.Application.ModeResult Parse(string body) =>
        YandexRailScheduleParser.Parse(
            Encoding.UTF8.GetBytes(body),
            Request,
            "train",
            new DateTimeOffset(2026, 10, 20, 12, 0, 0, TimeSpan.Zero)
        );

    [Fact]
    public void Positive_preserves_offsets_and_optional_source_date_is_not_required()
    {
        var result = Parse(Body(Row, 1));
        result.Kind.ShouldBe("complete");
        result.Items[0].Departure.Offset.ShouldBe(TimeSpan.FromHours(3));
        result.Items[0].Arrival.Offset.ShouldBe(TimeSpan.FromHours(5));
        result.Items[0].SourceReportedDate.ShouldBeNull();
    }

    [Fact]
    public void Empty_still_requires_query_echo()
    {
        Parse(Body()).Kind.ShouldBe("complete");
        Parse(Body().Replace("s1", "s9", StringComparison.Ordinal))
            .Reasons.ShouldContain("query-mismatch");
    }

    [Theory]
    [InlineData("\"limit\":50", "\"limit\":49")]
    [InlineData("\"duration\":7200", "\"duration\":7201")]
    [InlineData("+03:00\"", "\"")]
    [InlineData("\"has_transfers\":false", "\"has_transfers\":true")]
    [InlineData("\"uid\":\"fiction-1\"", "\"uid\":\"fiction-1\",\"uid\":\"other\"")]
    public void Critical_invalid_record_rejects_mode(string before, string after) =>
        Parse(Body(Row, 1).Replace(before, after, StringComparison.Ordinal))
            .Kind.ShouldBe("failed");

    [Fact]
    public void Duplicate_folding_uses_original_count()
    {
        var result = Parse(Body(Row + "," + Row, 2));
        result.Kind.ShouldBe("complete");
        result.OriginalCount.ShouldBe(2);
        result.Items.Length.ShouldBe(1);
        Parse(Body(Row + "," + Row.Replace("001", "002", StringComparison.Ordinal), 2))
            .Kind.ShouldBe("failed");
    }

    [Fact]
    public void Interval_count_does_not_invent_total_arithmetic()
    {
        var interval =
            """{"from":{"code":"s1","type":"station"},"to":{"code":"s2","type":"station"},"thread":{"transport_type":"train"},"has_transfers":false}""";
        var result = Parse(Body("", 0, interval));
        result.Kind.ShouldBe("limited");
        result.Items.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("true", "{\"whole\":1234.0,\"cents\":5.0}", "RUB", "true", "reported", "1234.05")]
    [InlineData("false", "null", "RUB", "false", "not-provided", null)]
    [InlineData("\"bad\"", "{\"whole\":1234,\"cents\":5}", "RUB", "unknown", "reported", "1234.05")]
    [InlineData("false", "{\"whole\":1234,\"cents\":101}", "RUB", "false", "unavailable", null)]
    [InlineData("true", "{\"whole\":1234}", "RUB", "true", "not-provided", null)]
    [InlineData("true", "{\"whole\":1,\"cents\":0}", "JPY", "true", "unavailable", null)]
    public void Ticket_components_are_independent(
        string marker,
        string price,
        string currency,
        string expectedMarker,
        string expectedPrice,
        string? amount
    )
    {
        var ticket =
            ",\"tickets_info\":{\"et_marker\":"
            + marker
            + ",\"places\":[{\"name\":null,\"currency\":\""
            + currency
            + "\",\"price\":"
            + price
            + "}]}";
        var row = Row[..^1] + ticket + "}";
        var result = Parse(Body(row, 1));
        result.Kind.ShouldBe("complete");
        var data = result.Items[0].Tickets;
        data.ElectronicTicket.ShouldBe(expectedMarker);
        data.Fares[0].Price.State.ShouldBe(expectedPrice);
        data.Fares[0].Price.Amount.ShouldBe(amount);
        data.SeatAvailability.ShouldBe("unknown");
    }

    [Theory]
    [InlineData("1e-100")]
    [InlineData("-1e-100")]
    [InlineData("1.000000000000000000000000000000000000001")]
    public void Raw_fraction_never_rounds_to_free_or_integral_price(string amount)
    {
        var row =
            Row[..^1]
            + ",\"tickets_info\":{\"et_marker\":false,\"places\":[{\"currency\":\"RUB\",\"price\":{\"whole\":"
            + amount
            + ",\"cents\":0}}]}}";
        var result = Parse(Body(row, 1));
        result.Kind.ShouldBe("complete");
        result.Items[0].Tickets.Fares[0].Price.State.ShouldBe("unavailable");
        result.Items[0].Tickets.ElectronicTicket.ShouldBe("false");
    }

    [Fact]
    public void Duplicate_optional_ticket_block_does_not_destroy_schedule()
    {
        var row = Row[..^1] + ",\"tickets_info\":null,\"tickets_info\":{}}";
        var result = Parse(Body(row, 1));
        result.Kind.ShouldBe("complete");
        result.Items[0].Tickets.FaresState.ShouldBe("unavailable");
    }
}
