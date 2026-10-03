using System.Text.Json;
using System.Text.Json.Nodes;
using ErrorOr;
using Shouldly;
using Travel.Modules.Flights.Core.ValueObjects;
using Travel.Modules.Flights.Core.ValueObjects.Identifiers;
using Travel.Modules.Flights.Core.ValueObjects.Offer;
using Travel.Shared.Abstractions;

namespace Travel.Modules.Flights.Tests.Unit.ValueObjects;

public sealed class PassengerPartyTests
{
    private static readonly DateOnly Today = new(2026, 10, 3);

    [Theory]
    [InlineData("mr")]
    [InlineData("ms")]
    [InlineData("mrs")]
    [InlineData("miss")]
    [InlineData("dr")]
    public void Titles_are_explicit_supported_codes(string code) =>
        PassengerTitle.Create(code).IsError.ShouldBeFalse();

    [Theory]
    [InlineData("MR")]
    [InlineData("")]
    [InlineData("sir")]
    [InlineData("raw-pii@example.test")]
    public void Unsupported_titles_are_rejected_without_echo(string code)
    {
        var result = PassengerTitle.Create(code);
        result.IsError.ShouldBeTrue();
        result.FirstError.Description.ShouldNotContain(code == "" ? "sentinel" : code);
    }

    [Theory]
    [InlineData("Ænne")]
    [InlineData("Иван")]
    [InlineData("Name2")]
    [InlineData("ABCDEFGHIJKLMNOPQRSTU")]
    public void Unsupported_names_are_rejected_by_new_details_only(string name) =>
        BookingPassengerDetails
            .Create(Info(name), PassengerTitle.Create("mr").Value)
            .IsError.ShouldBeTrue();

    [Theory]
    [InlineData("Émile")]
    [InlineData("Łukasz")]
    [InlineData("O'Neil")]
    [InlineData("Anne-Marie")]
    [InlineData("Jean Luc")]
    public void Restricted_latin_names_are_supported(string name) =>
        BookingPassengerDetails
            .Create(Info(name), PassengerTitle.Create("dr").Value)
            .IsError.ShouldBeFalse();

    [Theory]
    [InlineData("unspecified")]
    [InlineData("sentinel@example.test")]
    public void Json_bypassed_gender_is_revalidated_and_never_echoed(string gender)
    {
        var info = JsonSerializer.Deserialize<PassengerInfo>(
            JsonSerializer.Serialize(Info()).Replace("male", gender)
        )!;
        var result = BookingPassengerDetails.Create(info, PassengerTitle.Create("mr").Value);
        result.IsError.ShouldBeTrue();
        result.FirstError.Description.ShouldNotContain(gender);
    }

    [Fact]
    public void New_details_revalidate_json_phone_and_email_bounds()
    {
        foreach (var phone in new[] { "+1٢025550123", "+12025550123456789", "bad" })
        {
            var json = JsonNode.Parse(JsonSerializer.Serialize(Info()))!;
            json["Phone"]!["Value"] = phone;
            var restored = JsonSerializer.Deserialize<PassengerInfo>(json.ToJsonString())!;
            restored.Phone.Value.ShouldBe(phone);
            BookingPassengerDetails
                .Create(restored, PassengerTitle.Create("mr").Value)
                .IsError.ShouldBeTrue();
        }
        BookingPassengerDetails
            .Create(
                Info(email: new string('a', 250) + "@x.test"),
                PassengerTitle.Create("mr").Value
            )
            .IsError.ShouldBeTrue();
    }

    [Fact]
    public void Adult_cutoff_uses_first_origin_calendar_date_and_demo_leap_convention()
    {
        var details = BookingPassengerDetails
            .Create(Info(dob: new(2008, 2, 29)), PassengerTitle.Create("mr").Value)
            .Value;
        details.ValidateForTravel(new(2026, 2, 27), Today).IsError.ShouldBeTrue();
        details.ValidateForTravel(new(2026, 2, 28), Today).IsError.ShouldBeFalse();
        details.ValidateForTravel(new(2026, 2, 28), new(2008, 2, 28)).IsError.ShouldBeTrue();
    }

    [Fact]
    public void New_details_and_passenger_string_surfaces_are_redacted()
    {
        var details = BookingPassengerDetails
            .Create(Info(), PassengerTitle.Create("mr").Value)
            .Value;
        var passenger = BookingPassenger
            .Create(BookingPassengerId.Create(Guid.NewGuid()).Value, details)
            .Value;
        foreach (var surface in new[] { details.ToString(), passenger.ToString() })
        foreach (
            var sentinel in new[] { "FictionalGiven", "Family", "example.test", "1980", "+1202" }
        )
            surface.ShouldNotContain(sentinel);
    }

    [Fact]
    public void References_are_opaque_ordinal_and_bounded()
    {
        foreach (var bad in new[] { "", " ref", "ref ", "ref\n", new string('r', 257) })
            SupplierPassengerReference.Create(bad).IsError.ShouldBeTrue();
        SupplierPassengerReference.Create(new string('r', 256)).IsError.ShouldBeFalse();
        SupplierPassengerReference
            .Create("Ref")
            .Value.ShouldNotBe(SupplierPassengerReference.Create("ref").Value);
        BookingPassengerId.Create(Guid.Empty).IsError.ShouldBeTrue();
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(9)]
    public void Binding_validates_exact_count_and_defensively_copies_slots(int count)
    {
        var supplier = Enumerable
            .Range(1, count)
            .Select(i => new SupplierPassengerSlot(
                SupplierPassengerReference.Create($"ref{i}").Value,
                BookingPassengerKind.Adult
            ))
            .ToArray();
        var party = BookableOfferParty.Create(supplier, new(2030, 1, 1), true, false).Value;
        var slots = supplier
            .Select(s => new QuotePassengerSlot(
                BookingPassengerId.Create(Guid.NewGuid()).Value,
                s.Reference,
                s.Kind
            ))
            .ToArray();
        var binding = QuoteBinding.Create(Guid.NewGuid(), party, slots).Value;
        var original = binding.Slots[0];
        slots[0] = slots[0] with { Id = BookingPassengerId.Create(Guid.NewGuid()).Value };
        supplier[0] = supplier[0] with
        {
            Reference = SupplierPassengerReference.Create("foreign").Value,
        };
        binding.Slots[0].ShouldBe(original);
        binding.Party.Passengers[0].Reference.ShouldBe(original.SupplierReference);
        binding.Validate().IsError.ShouldBeFalse();
        var passengers = binding
            .Slots.Select(s =>
                BookingPassenger
                    .Create(
                        s.Id,
                        BookingPassengerDetails
                            .Create(Info(), PassengerTitle.Create("mr").Value)
                            .Value
                    )
                    .Value
            )
            .ToArray();
        binding.ValidatePassengers(new(passengers), Today).IsError.ShouldBeFalse();
        passengers[0] = BookingPassenger
            .Create(BookingPassengerId.Create(Guid.NewGuid()).Value, passengers[0].Details)
            .Value;
        binding
            .ValidatePassengers(new(passengers), Today)
            .FirstError.Type.ShouldBe(ErrorType.Conflict);
        binding.ValidatePassengers(new([]), Today).IsError.ShouldBeTrue();
    }

    [Fact]
    public void Binding_rejects_empty_revision_duplicate_ids_refs_and_wrong_membership()
    {
        var reference = SupplierPassengerReference.Create("ref1").Value;
        var party = BookableOfferParty
            .Create([new(reference, BookingPassengerKind.Adult)], new(2030, 1, 1), true, false)
            .Value;
        var slot = new QuotePassengerSlot(
            BookingPassengerId.Create(Guid.NewGuid()).Value,
            reference,
            BookingPassengerKind.Adult
        );
        QuoteBinding.Create(Guid.Empty, party, [slot]).IsError.ShouldBeTrue();
        QuoteBinding.Create(Guid.NewGuid(), party, [slot, slot]).IsError.ShouldBeTrue();
        QuoteBinding
            .Create(
                Guid.NewGuid(),
                party,
                [
                    slot with
                    {
                        SupplierReference = SupplierPassengerReference.Create("foreign").Value,
                    },
                ]
            )
            .IsError.ShouldBeTrue();
        BookableOfferParty.Create([], new(2030, 1, 1), true, false).IsError.ShouldBeTrue();
        BookableOfferParty
            .Create(
                Enumerable.Repeat(
                    new SupplierPassengerSlot(reference, BookingPassengerKind.Adult),
                    10
                ),
                new(2030, 1, 1),
                true,
                false
            )
            .IsError.ShouldBeTrue();
    }

    [Fact]
    public void Revalidated_binding_rejects_json_default_id_null_details_and_duplicate_ids()
    {
        var refs = new[]
        {
            SupplierPassengerReference.Create("one").Value,
            SupplierPassengerReference.Create("two").Value,
        };
        var party = BookableOfferParty
            .Create(
                refs.Select(r => new SupplierPassengerSlot(r, BookingPassengerKind.Adult)),
                new(2030, 1, 1),
                true,
                false
            )
            .Value;
        var id = BookingPassengerId.Create(Guid.NewGuid()).Value;
        QuoteBinding
            .Create(
                Guid.NewGuid(),
                party,
                [
                    new(id, refs[0], BookingPassengerKind.Adult),
                    new(id, refs[1], BookingPassengerKind.Adult),
                ]
            )
            .IsError.ShouldBeTrue();
        var binding = QuoteBinding
            .Create(
                Guid.NewGuid(),
                party,
                [
                    new(id, refs[0], BookingPassengerKind.Adult),
                    new(
                        BookingPassengerId.Create(Guid.NewGuid()).Value,
                        refs[1],
                        BookingPassengerKind.Adult
                    ),
                ]
            )
            .Value;
        var json = JsonNode.Parse(JsonSerializer.Serialize(binding))!;
        json["Slots"]![0]!["Id"]!["Value"] = Guid.Empty;
        JsonSerializer
            .Deserialize<QuoteBinding>(json.ToJsonString())!
            .Validate()
            .IsError.ShouldBeTrue();
        json = JsonNode.Parse(JsonSerializer.Serialize(binding))!;
        json["Slots"]![0]!["SupplierReference"]!["Value"] = " raw-pii ";
        var result = JsonSerializer.Deserialize<QuoteBinding>(json.ToJsonString())!.Validate();
        result.IsError.ShouldBeTrue();
        result.FirstError.Description.ShouldNotContain("raw-pii");
        json = JsonNode.Parse(JsonSerializer.Serialize(binding))!;
        json["Slots"]![0]!["Kind"] = 0;
        JsonSerializer
            .Deserialize<QuoteBinding>(json.ToJsonString())!
            .Validate()
            .IsError.ShouldBeTrue();
    }

    [Fact]
    public void Slot_errors_expose_only_valid_local_id_and_fixed_field_metadata()
    {
        var binding = Aggregates.PassengerPartyReplayTests.Binding();
        var underage = BookingPassenger
            .Create(
                binding.Slots[0].Id,
                BookingPassengerDetails
                    .Create(Info(dob: new(2020, 1, 1)), PassengerTitle.Create("mr").Value)
                    .Value
            )
            .Value;
        var result = binding.ValidatePassengers(new([underage]), Today);
        result.IsError.ShouldBeTrue();
        result.FirstError.Metadata!["bookingPassengerId"].ShouldBe(binding.Slots[0].Id.Value);
        result.FirstError.Metadata["field"].ShouldBe("dateOfBirth");
        result.FirstError.Metadata.Count.ShouldBe(2);
    }

    [Fact]
    public void Amount_normalization_and_json_preserve_party_capabilities_and_calendar_date()
    {
        var binding = Aggregates.PassengerPartyReplayTests.Binding();
        var aggregate = Aggregates.PassengerPartyReplayTests.Quoted(binding);
        var offer = new BookableOffer(
            aggregate.OfferId!.Value,
            aggregate.Itinerary!,
            aggregate.TotalAmount!,
            new ProviderId("duffel"),
            new DateTimeOffset(2026, 10, 3, 12, 0, 0, TimeSpan.Zero),
            aggregate.ExpiresAt!.Value,
            new FareConditions(false, false, null, null),
            "off_test",
            binding.Party
        );
        var changed = (BookableOffer)
            offer.WithAmount(Money.Create(123, CurrencyCode.Create("USD").Value).Value);
        changed.Party.ShouldBe(binding.Party);
        var restored = JsonSerializer.Deserialize<BookableOffer>(
            JsonSerializer.Serialize(changed)
        )!;
        restored.Party!.FirstDepartureLocalDate.ShouldBe(new DateOnly(2030, 1, 1));
        restored.Party.Passengers[0].Reference.Value.ShouldBe("ref_test");
        restored.Party.SupportsHold.ShouldBe(true);
        restored.Party.RequiresIdentityDocuments.ShouldBe(false);
        var historical = JsonNode.Parse(JsonSerializer.Serialize(offer))!.AsObject();
        historical.Remove("Party");
        JsonSerializer.Deserialize<BookableOffer>(historical.ToJsonString())!.Party.ShouldBeNull();
    }

    internal static PassengerInfo Info(
        string name = "FictionalGiven",
        DateOnly? dob = null,
        string email = "fictional@example.test"
    ) =>
        PassengerInfo
            .Create(
                name,
                "Family",
                dob ?? new(1980, 1, 1),
                Gender.Male,
                email,
                PhoneNumber.Create("+12025550123").Value,
                DateOnly.MaxValue
            )
            .Value;
}
