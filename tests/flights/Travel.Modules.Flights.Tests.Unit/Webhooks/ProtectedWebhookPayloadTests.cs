using System.Text;
using System.Text.Json;
using Shouldly;
using Travel.Modules.Flights.Application.Webhooks;
using Travel.Modules.Flights.Infrastructure.Webhooks;
using Travel.Tests.Fixtures;

namespace Travel.Modules.Flights.Tests.Unit.Webhooks;

public sealed class ProtectedWebhookPayloadTests
{
    [Fact]
    public void Protected_body_preserves_signed_bytes_and_binds_routing_metadata()
    {
        var id = Guid.NewGuid();
        var bytes = Encoding.UTF8.GetBytes(
            "{ \"id\": \"evt_fictional\", \"type\": \"order.created\", \"object\": {\"id\":\"ord_fictional\",\"passengers\":[{\"given_name\":\"PrivateFictionalName\"}]}}".Replace(
                "\\\"",
                "\""
            )
        );
        var codec = new ProtectedWebhookPayloadCodec(TestPii.Provider);
        var result = codec.Protect(id, "duffel", "evt_fictional", "order.created", bytes);
        result.IsError.ShouldBeFalse();
        result.Value.ShouldNotContain("PrivateFictionalName");
        var entry = new WebhookInboxEntry(
            id,
            "order.created",
            result.Value,
            DateTimeOffset.UtcNow,
            null,
            "duffel",
            "evt_fictional"
        );
        codec.Decode(entry).Value.Bytes.ShouldBe(bytes);
        codec.Decode(entry with { Id = Guid.NewGuid() }).IsError.ShouldBeTrue();
        codec.Decode(entry with { EventId = "evt_other" }).IsError.ShouldBeTrue();
        var json = System.Text.Json.Nodes.JsonNode.Parse(result.Value)!;
        json["providerOrderId"] = "ord_other";
        codec.Decode(entry with { StoredPayload = json.ToJsonString() }).IsError.ShouldBeTrue();
    }

    [Fact]
    public void Protected_envelope_without_discriminator_is_not_downgraded_to_legacy_noop()
    {
        var id = Guid.NewGuid();
        var bytes = Encoding.UTF8.GetBytes(
            "{\"id\":\"evt_fixture\",\"type\":\"order.created\",\"object\":{\"id\":\"ord_fixture\",\"documents\":[{\"type\":\"ticket\",\"unique_identifier\":\"TKT-FIXTURE\"}]}}"
        );
        var json = System
            .Text.Json.Nodes.JsonNode.Parse(
                TestPii.Webhooks.Protect(id, "duffel", "evt_fixture", "order.created", bytes).Value
            )!
            .AsObject();
        json.Remove("format");
        var entry = new WebhookInboxEntry(
            id,
            "order.created",
            json.ToJsonString(),
            DateTimeOffset.UtcNow,
            null,
            "duffel",
            "evt_fixture"
        );
        TestPii.Webhooks.Decode(entry).IsError.ShouldBeTrue();
        var result = TestPii.WebhookReader.Read(entry);
        result.IsError.ShouldBeTrue();
        result.FirstError.Code.ShouldBe("Flights.PiiEnvelopeInvalid");
    }

    [Fact]
    public void Invalid_legacy_field_types_are_not_silently_acknowledgeable()
    {
        var reader = TestPii.WebhookReader;
        foreach (
            var body in new[]
            {
                "[]",
                "{\"object\":{\"id\":123}}",
                "{\"object\":{\"id\":\"ord_fixture\",\"documents\":[{\"type\":\"ticket\",\"unique_identifier\":123}]}}",
            }
        )
        {
            var entry = new WebhookInboxEntry(
                Guid.NewGuid(),
                "order.created",
                body,
                DateTimeOffset.UtcNow,
                null
            );
            reader.Read(entry).IsError.ShouldBeTrue();
        }
    }

    [Fact]
    public void Processed_payload_needs_no_key_and_legacy_bad_json_retains_noop_semantics()
    {
        using var ring = new FlightPiiTestRing(false);
        var reader = new DuffelWebhookPayloadReader(new ProtectedWebhookPayloadCodec(ring.Crypto));
        var processed = new WebhookInboxEntry(
            Guid.NewGuid(),
            "order.created",
            "bad-json",
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow
        );
        reader.Read(processed).Value.Kind.ShouldBe(BookingWebhookKind.Ignored);
        reader
            .Read(processed with { ProcessedAt = null })
            .Value.Kind.ShouldBe(BookingWebhookKind.Ignored);
    }
}
