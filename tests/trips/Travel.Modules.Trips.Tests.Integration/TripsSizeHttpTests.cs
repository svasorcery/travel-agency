using System.Net;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Shouldly;
using Travel.Modules.Trips.Application;
using Travel.Modules.Trips.Core;
using Travel.Modules.Trips.Infrastructure.Serialization;
using Xunit;

namespace Travel.Modules.Trips.Tests.Integration;

public sealed class TripsSizeHttpTests
{
    [Fact]
    public async Task Near_limit_utf8_full_detail_is_readable_and_next_larger_candidate_never_commits()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var fixture = await TripsHttpFixture.Start();
        var brief = TripsCodecProtectionTests.Brief with { EndDate = new DateOnly(2026, 11, 27) };
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        };
        var title = new string('Ж', 100);
        var create = new CreateTripPlan(title, brief);
        using var created = await fixture.Client.PostAsync(
            "/api/trips",
            TripsHttpTests.Body(JsonSerializer.Serialize(create, options)),
            ct
        );
        created.StatusCode.ShouldBe(HttpStatusCode.Created);
        var location = created.Headers.Location!.ToString();
        var noteSizes = Enumerable.Repeat(300, 112).ToArray();
        var ids = Enumerable.Range(0, 112).Select(_ => Guid.NewGuid()).ToArray();
        var codec = new TripPlanJsonCodec();
        var now = new DateTimeOffset(2026, 10, 20, 9, 0, 0, TimeSpan.Zero);
        IReadOnlyList<TripDayInput> Days() =>
            Enumerable
                .Range(0, 14)
                .Select(day => new TripDayInput(
                    brief.StartDate.AddDays(day),
                    Enumerable
                        .Range(0, 8)
                        .Select(block =>
                        {
                            var index = day * 8 + block;
                            return new TripBlockInput(
                                ids[index],
                                new string('Ж', 100),
                                new string('Ж', noteSizes[index])
                            );
                        })
                        .ToArray()
                ))
                .ToArray();
        int Size() =>
            codec
                .MeasureDetailBytes(
                    TripPlan
                        .Restore(
                            Guid.NewGuid(),
                            TripsCodecProtectionTests.Owner,
                            2,
                            title,
                            brief,
                            CityCatalogue.Find(brief.DestinationId).Value,
                            Days(),
                            now,
                            now
                        )
                        .Value,
                    true
                )
                .Value;
        var low = 300;
        var high = 500;
        while (low < high)
        {
            var middle = (low + high + 1) / 2;
            Array.Fill(noteSizes, middle);
            if (Size() <= 128 * 1024)
                low = middle;
            else
                high = middle - 1;
        }
        Array.Fill(noteSizes, low);
        for (var index = 0; index < noteSizes.Length; index++)
        {
            noteSizes[index]++;
            if (Size() > 128 * 1024)
            {
                noteSizes[index]--;
                break;
            }
        }
        var acceptedSize = Size();
        acceptedSize.ShouldBeInRange(128 * 1024 - 2, 128 * 1024);
        var body = JsonSerializer.Serialize(new ReplaceTripPlan(title, brief, Days()), options);
        Encoding.UTF8.GetByteCount(body).ShouldBeLessThanOrEqualTo(128 * 1024);
        using var saved = await TripsHttpTests.Put(fixture.Client, location, body, "\"1\"");
        saved.StatusCode.ShouldBe(HttpStatusCode.OK, await saved.Content.ReadAsStringAsync(ct));
        var accepted = await saved.Content.ReadAsStringAsync(ct);
        Encoding.UTF8.GetByteCount(accepted).ShouldBe(acceptedSize - 18);
        using var reloaded = await fixture.Client.GetAsync(location, ct);
        reloaded.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await reloaded.Content.ReadAsStringAsync(ct)).ShouldBe(accepted);
        noteSizes[^1]++;
        var rejected = JsonSerializer.Serialize(new ReplaceTripPlan(title, brief, Days()), options);
        Encoding.UTF8.GetByteCount(rejected).ShouldBeLessThanOrEqualTo(128 * 1024);
        using var tooLarge = await TripsHttpTests.Put(fixture.Client, location, rejected, "\"2\"");
        tooLarge.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await tooLarge.Content.ReadAsStringAsync(ct)).ShouldContain("Trips.PlanTooLarge");
        using var unchanged = await fixture.Client.GetAsync(location, ct);
        (await unchanged.Content.ReadAsStringAsync(ct)).ShouldBe(accepted);
    }
}
