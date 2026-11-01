using Microsoft.Extensions.DependencyInjection;
using Travel.Modules.Hotels.Application;
using Travel.Modules.Hotels.Core;
using Travel.Modules.Hotels.Infrastructure.Providers.Duffel;
using Travel.Modules.Hotels.Tests.Integration;

namespace Travel.Hotels.Fixture;

internal sealed record HotelM2FinalRow(
    Guid Id,
    string Provider,
    Guid? AttemptId,
    bool SenderFinished,
    ReservationOutcome Outcome,
    bool HasBookingId,
    int ReadsClaimed,
    int ReadAllowance,
    DateTimeOffset? FirstKnownAt,
    DateTimeOffset? ReadUntil,
    DateTimeOffset? AdmittedAt,
    DateTimeOffset UpdatedAt
);

internal sealed partial class HotelsL1ChildState
{
    private async Task<object> PrepareM2Async(CancellationToken ct)
    {
        var profile = fixture.LoadProfile!;
        var stages = new List<L1StageMemory> { Stage("beforeSeed") };
        var seed = await HotelM2LoadSeed.PrepareAsync(
            profile,
            fixture.Services,
            fixture.DuffelWire,
            fixture.HotelbedsWire,
            fixture.LoadControl!,
            ct
        );
        stages.Add(Stage("afterSeed"));
        var cache = fixture.Services.GetRequiredService<HotelSnapshotCache>();
        var proof = HotelM2LoadCacheProof.Run(profile, cache, seed, fixture.Clock);
        stages.Add(Stage("afterCacheProof"));
        drafts.AddRange(seed.Rows.Where(r => r.Role == "draft").Select(r => r.Id));
        statusStock.AddRange(
            seed.StatusStock.Select(s => new L1Stock(s.Id, s.QuoteId ?? "", s.BookingId))
        );
        observationStock.AddRange(
            seed.ObservationStock.Select(s => new L1Stock(s.Id, s.QuoteId!, s.BookingId))
        );
        var sources = CaptureSources(true);
        var capacity = fixture.Services.GetRequiredService<HotelCapacity>().GetSourceSnapshots();
        if (capacity.Any(s => s.Capacity.TotalStarts != 0 || s.Capacity.WindowUntil != default))
            throw new InvalidOperationException(
                "Setup cannot reset or pre-spend runtime admission."
            );
        var retained = await RetainedCountAsync(ct);
        if (retained != 38)
            throw new InvalidOperationException("M2 seed cardinality mismatch.");
        stages.Add(Stage("beforeTimed"));
        return new
        {
            profile = profile.Code,
            drafts,
            observationStock,
            statusStock,
            m2Stock = seed.StatusStock.Concat(seed.ObservationStock).ToArray(),
            seedReceipt = new
            {
                drafts = 24,
                confirmed = 2,
                spentWithoutKnownIdentity = 12,
                sourceStarts = sources.ToDictionary(s => s.Provider, s => s.Starts),
                sourceCreates = new
                {
                    duffel = fixture.DuffelWire.Creates,
                    hotelbeds = fixture.HotelbedsWire.Creates,
                },
                applicationAdmissionUnspent = true,
            },
            cacheProof = proof,
            retainedBefore = retained,
            inboxBefore = await InboxFootprintAsync(ct),
            initialSources = sources,
            initialFake = (object?)null,
            memoryStages = stages,
            supplierStarts = sources.Sum(s => s.Starts),
            supplierCreates = fixture.DuffelWire.Creates + fixture.HotelbedsWire.Creates,
            settings = new
            {
                foregroundStarts = 24,
                recoveryStarts = 6,
                foregroundConcurrency = 1,
                recoveryConcurrency = 1,
                windowSeconds = 60,
                readTimeoutSeconds = 15,
                createTimeoutSeconds = 140,
                cacheEntries = 128,
                cacheBytes = 16777216,
                responseBytes = 2097152,
                properties = 100,
                rates = 200,
            },
        };
    }
}
