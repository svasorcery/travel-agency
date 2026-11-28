using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Options;
using Shouldly;
using Travel.Modules.Trips.Application.SavedOptions;
using Travel.Modules.Trips.Core;
using Travel.Modules.Trips.Core.Options;
using Travel.Modules.Trips.Infrastructure.Privacy;
using Xunit;

namespace Travel.Modules.Trips.Tests.Integration;

public sealed class SavedOptionProtectionTests
{
    private static readonly OwnerIdentity Owner = OwnerIdentity
        .Create(TripsHttpFixture.Issuer, TripsHttpFixture.Owner)
        .Value;
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("A", 30, "RUB", null)]
    [InlineData("B", 30, "RUB", null)]
    [InlineData("B", 29, "RUB", null)]
    [InlineData("B", 30, "EUR", "1.00")]
    public async Task Issued_and_saved_bytes_round_trip_the_exact_option_across_key_ring_reopen(
        string catalogue,
        int lastDay,
        string currency,
        string? budget
    )
    {
        using var ring = new TripsTestRing();
        var crypto = new SavedOptionProtection(ring.Provider);
        var (brief, preview) = await Example(catalogue, lastDay, currency, budget);
        var now = DateTimeOffset.UtcNow;
        var ticket = crypto.Issue(Owner, brief, preview, now).Value;
        var other = crypto.Issue(Owner, brief, preview, now).Value;
        ticket.TripId.ShouldNotBe(other.TripId);
        using var reopened = new TripsProtectionProvider(
            Options.Create(ring.Settings),
            TimeProvider.System
        );
        var reader = new SavedOptionProtection(reopened);
        var selected = reader.OpenSelection(Owner, ticket.SelectionToken).Value;
        selected.TripId.ShouldBe(ticket.TripId);
        (selected.ExpiresAtUtc - selected.IssuedAtUtc).ShouldBe(TimeSpan.FromMinutes(15));
        selected.ExpiresAtUtc.ShouldBe(ticket.ExpiresAtUtc);
        selected.IssuedAtUtc.ShouldBeInRange(now.AddMilliseconds(-1), now);
        var history = new HistoricalTripOption(1, brief, preview, now);
        var envelope = crypto.ProtectHistory(Owner, ticket.TripId, history).Value;
        var loaded = reader.OpenHistory(Owner, ticket.TripId, envelope).Value;
        JsonSerializer.Serialize(loaded).ShouldBe(JsonSerializer.Serialize(history));
        JsonSerializer.Serialize(selected.Preview).ShouldBe(JsonSerializer.Serialize(preview));
        JsonSerializer.Serialize(selected.Brief).ShouldBe(JsonSerializer.Serialize(brief));
        foreach (
            var foreign in new[]
            {
                OwnerIdentity.Create(Owner.Issuer, Guid.NewGuid()).Value,
                OwnerIdentity.Create("https://other.test", Owner.Subject).Value,
            }
        )
        {
            reader.OpenSelection(foreign, ticket.SelectionToken).IsError.ShouldBeTrue();
            reader.OpenHistory(foreign, ticket.TripId, envelope).IsError.ShouldBeTrue();
        }
        reader.OpenHistory(Owner, other.TripId, envelope).IsError.ShouldBeTrue();
        reader.OpenHistory(Owner, ticket.TripId, ticket.SelectionToken).IsError.ShouldBeTrue();
        reader.OpenSelection(Owner, envelope).IsError.ShouldBeTrue();
        var bytes = Convert.FromBase64String(ticket.SelectionToken);
        bytes[^1] ^= 1;
        reader.OpenSelection(Owner, Convert.ToBase64String(bytes)).IsError.ShouldBeTrue();
    }

    [Fact]
    public async Task Strict_size_format_and_version_limits_apply_before_and_after_protection()
    {
        using var ring = new TripsTestRing();
        var crypto = new SavedOptionProtection(ring.Provider);
        var (brief, basePreview) = await Example("B", 30, "RUB", null);
        var now = DateTimeOffset.UtcNow;
        var id = Guid.NewGuid();
        var preview = SizedPreview(
            basePreview,
            SavedOptionProtection.MaximumPreviewBytes,
            p => JsonSerializer.SerializeToUtf8Bytes(p, JsonSerializerOptions.Web).Length
        );
        crypto.Issue(Owner, brief, preview, now).IsError.ShouldBeFalse();
        crypto
            .Issue(Owner, brief, preview with { Attribution = preview.Attribution + "x" }, now)
            .FirstError.ShouldBe(TripErrors.PlanTooLarge);
        var history = new HistoricalTripOption(1, brief, basePreview, now);
        var large = SizedPreview(
            basePreview,
            SavedOptionProtection.MaximumPlaintextBytes,
            p =>
                JsonSerializer
                    .SerializeToUtf8Bytes(history with { Preview = p }, JsonSerializerOptions.Web)
                    .Length
        );
        history = history with { Preview = large };
        var exact = crypto.ProtectHistory(Owner, id, history).Value;
        crypto.OpenHistory(Owner, id, exact).IsError.ShouldBeFalse();
        crypto
            .ProtectHistory(
                Owner,
                id,
                history with
                {
                    Preview = history.Preview with
                    {
                        Attribution = history.Preview.Attribution + "x",
                    },
                }
            )
            .FirstError.ShouldBe(TripErrors.PlanTooLarge);
        crypto.OpenSelection(Owner, new string('a', 65537)).IsError.ShouldBeTrue();
        var purposes = new[]
        {
            "trip-option-history",
            "1",
            Owner.Issuer,
            Owner.Subject.ToString("N"),
            id.ToString("N"),
        };
        foreach (
            var invalid in new[]
            {
                history with
                {
                    Version = 2,
                    Preview = basePreview,
                },
                history with
                {
                    Preview = basePreview with { Option = null },
                },
                history with
                {
                    Preview = basePreview with
                    {
                        Option = basePreview.Option! with
                        {
                            Price = basePreview.Option.Price with { KnownSubtotal = "0.00" },
                        },
                    },
                },
            }
        )
        {
            var forged = ring
                .Provider.Protect(
                    JsonSerializer.SerializeToUtf8Bytes(invalid, JsonSerializerOptions.Web),
                    purposes
                )
                .Value;
            crypto
                .OpenHistory(Owner, id, Convert.ToBase64String(forged))
                .FirstError.ShouldBe(TripErrors.Unavailable);
        }
    }

    // Exercise the serialized byte boundary using bounded, valid strings: each globe
    // is one Unicode scalar and occupies twelve escaped UTF-8 bytes in the wire JSON.
    private static TripOptionPreview SizedPreview(
        TripOptionPreview example,
        int size,
        Func<TripOptionPreview, int> measure
    )
    {
        var option = example.Option!;
        var days = option
            .Days.Select(
                (day, d) =>
                    day with
                    {
                        WindowStart = "10:00",
                        WindowEnd = "11:00",
                        EmptyReason = null,
                        ProgrammeMinutes = 40,
                        Stops = Enumerable
                            .Range(0, 4)
                            .Select(i => new OptionStop(
                                $"place-{d}-{i}",
                                "x",
                                $"10:{10 * i:D2}",
                                $"10:{10 * (i + 1):D2}",
                                10,
                                i == 0 ? null : $"edge-{d}-{i}",
                                0,
                                false
                            ))
                            .ToArray(),
                    }
            )
            .ToArray();
        var value = example with { Option = option with { Days = days }, Attribution = "x" };
        var remaining = size - measure(value);
        foreach (var (day, index) in days.Select((day, i) => (day, i)))
        {
            days[index] = day with
            {
                Stops = day
                    .Stops.Select(s =>
                    {
                        int count = Math.Min(199, remaining / 12);
                        remaining -= 12 * count;
                        return s with
                        {
                            Title = "x" + string.Concat(Enumerable.Repeat("🌍", count)),
                        };
                    })
                    .ToArray(),
            };
        }
        value = value with
        {
            Attribution =
                "x"
                + string.Concat(Enumerable.Repeat("🌍", remaining / 12))
                + new string('x', remaining % 12),
        };
        measure(value).ShouldBe(size);
        return value;
    }

    [Fact]
    public async Task Historical_v1_rejects_unknown_labels_invalid_nullable_money_and_out_of_range_values()
    {
        var (brief, preview) = await Example("B", 30, "RUB", null);
        using var ring = new TripsTestRing();
        var crypto = new SavedOptionProtection(ring.Provider);
        var id = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        var option = preview.Option!;
        var price = option.Price;
        var badOptions = new[]
        {
            option with
            {
                SelectionReason = "undocumented",
            },
            option with
            {
                ArrivalTransferMinutes = 1441,
            },
            option with
            {
                Hotel = option.Hotel with { Name = new string('x', 201) },
            },
            option with
            {
                Price = price with { UnpricedExpenses = ["other"] },
            },
            option with
            {
                Price = price with
                {
                    Components = [.. price.Components, new("other", "included", null, null)],
                },
            },
            option with
            {
                Price = price with
                {
                    FlightAndStayComplete = false,
                    Components =
                    [
                        .. price.Components,
                        new("localFee", "unknownMandatory", "10.00", "RUB"),
                    ],
                },
            },
        };
        foreach (
            var invalid in badOptions
                .Select(o => preview with { Option = o })
                .Concat([
                    preview with
                    {
                        CatalogueVersion = "C",
                    },
                    preview with
                    {
                        DatasetVersion = "next",
                        DataUrl =
                            "/trips-data/saint-petersburg-outdoor/next/saint-petersburg-vnext.manifest.json",
                    },
                    preview with
                    {
                        DatasetHash = new string('A', 64),
                    },
                    preview with
                    {
                        Attribution = new string('x', 1025),
                    },
                ])
        )
        {
            crypto.Issue(Owner, brief, invalid, now).IsError.ShouldBeTrue();
            var history = new HistoricalTripOption(1, brief, invalid, now);
            crypto.ProtectHistory(Owner, id, history).FirstError.ShouldBe(TripErrors.Unavailable);
            var encrypted = ring
                .Provider.Protect(
                    JsonSerializer.SerializeToUtf8Bytes(history, JsonSerializerOptions.Web),
                    "trip-option-history",
                    "1",
                    Owner.Issuer,
                    Owner.Subject.ToString("N"),
                    id.ToString("N")
                )
                .Value;
            crypto
                .OpenHistory(Owner, id, Convert.ToBase64String(encrypted))
                .FirstError.ShouldBe(TripErrors.Unavailable);
        }
    }

    [Fact]
    public async Task Historical_v1_reader_rejects_the_same_invalid_snapshots_as_the_browser()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "Travel.slnx")))
            root = root.Parent;
        var path = Path.Combine(root!.FullName, "tests/fixtures/trips-saved-option-v1.json");
        var fixture = JsonNode.Parse(await File.ReadAllTextAsync(path, Ct))!;
        var history = fixture["history"]!;
        using var ring = new TripsTestRing();
        var crypto = new SavedOptionProtection(ring.Provider);
        var id = Guid.NewGuid();
        string Envelope(JsonNode snapshot) =>
            Convert.ToBase64String(
                ring.Provider.Protect(
                    JsonSerializer.SerializeToUtf8Bytes(snapshot),
                    "trip-option-history",
                    "1",
                    Owner.Issuer,
                    Owner.Subject.ToString("N"),
                    id.ToString("N")
                ).Value
            );
        crypto.OpenHistory(Owner, id, Envelope(history)).IsError.ShouldBeFalse();
        foreach (var invalid in fixture["invalid"]!.AsArray())
        {
            var copy = history.DeepClone();
            var target = copy;
            foreach (var key in invalid!["path"]!.GetValue<string>().Split('.'))
            {
                target = target is JsonArray items
                    ? items[int.Parse(key, System.Globalization.CultureInfo.InvariantCulture)]!
                    : target[key]!;
            }
            target.ReplaceWith(invalid["value"]!.DeepClone());
            var opened = crypto.OpenHistory(Owner, id, Envelope(copy));
            opened.IsError.ShouldBeTrue(invalid["name"]!.GetValue<string>());
            opened.FirstError.ShouldBe(TripErrors.Unavailable);
        }
    }

    private static async Task<(TripBriefInput, TripOptionPreview)> Example(
        string version,
        int lastDay,
        string currency,
        string? budget
    )
    {
        using var data = new CuratedSaintPetersburgFactsTests.Package();
        var facts = (await data.Source().ReadAsync(Ct)).Value;
        var brief = TripBrief
            .Create(
                new(
                    "saint-petersburg",
                    "moscow",
                    new(2026, 10, 28),
                    new(2026, 10, lastDay),
                    2,
                    currency,
                    budget,
                    ["architecture"],
                    "relaxed"
                )
            )
            .Value;
        var preview = OptionPlanner
            .Preview(brief, facts, data.Clock.Now, FictionalOptionCatalogue.For(version, 2), Ct)
            .Value;
        return (brief.ToInput(), preview);
    }
}
