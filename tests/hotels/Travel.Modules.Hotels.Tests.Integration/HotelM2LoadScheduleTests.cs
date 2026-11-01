using System.Text.Json;
using Shouldly;

namespace Travel.Modules.Hotels.Tests.Integration;

public sealed class HotelM2LoadScheduleTests
{
    [Theory]
    [InlineData("S", 9, 8)]
    [InlineData("L", 8, 9)]
    public void Frozen_schedule_has_complete_denominator_source_cohorts_and_exact_loss_chain(
        string code,
        int hbx,
        int duffel
    )
    {
        var profile = new HotelM2LoadProfile(code, new(2026, 11, 6));
        var schedule = new HotelM2LoadSchedule(profile);
        var jobs = JsonSerializer.SerializeToElement(schedule.Jobs);
        jobs.GetArrayLength().ShouldBe(3152);
        var all = jobs.EnumerateArray().ToArray();
        foreach (
            var (kind, count) in new[]
            {
                ("search", 1255),
                ("rates", 22),
                ("prepare", 17),
                ("create", 17),
                ("status", 1800),
                ("callback", 41),
            }
        )
            all.Count(j => j.GetProperty("Kind").GetString() == kind).ShouldBe(count);
        all.Select(j => j.GetProperty("Id").GetInt32()).Distinct().Count().ShouldBe(3152);
        var creates = all.Where(j => j.GetProperty("Kind").GetString() == "create").ToArray();
        creates.Count(j => j.GetProperty("Provider").GetString() == "hotelbeds").ShouldBe(hbx);
        creates
            .Count(j => j.GetProperty("Provider").GetString() == "duffel-stays")
            .ShouldBe(duffel);
        var loss = creates.Single(j => j.GetProperty("Due").GetDouble() == 154.5);
        loss.GetProperty("Provider")
            .GetString()
            .ShouldBe(code == "S" ? "hotelbeds" : "duffel-stays");
        var prepare = all.Single(j =>
            j.GetProperty("Id").GetInt32() == loss.GetProperty("Prerequisite").GetInt32()
        );
        prepare.GetProperty("Due").GetDouble().ShouldBe(153);
        var rates = all.Single(j =>
            j.GetProperty("Id").GetInt32() == prepare.GetProperty("Prerequisite").GetInt32()
        );
        rates.GetProperty("Due").GetDouble().ShouldBe(151.2);
        var search = all.Single(j =>
            j.GetProperty("Id").GetInt32() == rates.GetProperty("Prerequisite").GetInt32()
        );
        search.GetProperty("Due").GetDouble().ShouldBe(150);
        var seeds = JsonSerializer.SerializeToElement(schedule.Seeds);
        seeds.GetArrayLength().ShouldBe(38);
        seeds
            .EnumerateArray()
            .Count(s => s.GetProperty("Role").GetString() == "draft")
            .ShouldBe(24);
        seeds
            .EnumerateArray()
            .Count(s => s.GetProperty("Role").GetString() == "confirmed")
            .ShouldBe(2);
        seeds
            .EnumerateArray()
            .Count(s => s.GetProperty("Role").GetString() == "observation")
            .ShouldBe(12);
    }
}
