using System.Text.Json;
using Travel.Modules.Hotels.Tests.Integration;

namespace Travel.Hotels.Fixture;

internal sealed record HotelM2SourceSample(
    double At,
    HotelWireSnapshot[] Sources,
    HotelObservedPurchase[] Purchases
);

internal sealed record HotelM2PurchaseFact(
    int JobId,
    double Due,
    Guid? ResourceId,
    string? ExpectedProvider,
    string? ResponseProvider,
    double? HttpCompleted,
    int? Status,
    bool ResponseAccepted,
    double? AuthoritativeObservedAt,
    bool SourceMatched,
    int? Effects
);

// Parent-side evidence for the two owned Hotels wires. The actual runner feeds this
// consumer; it owns the full history, while child observations remain bounded.
internal sealed class HotelsM2Evidence(IEnumerable<HotelLoadJob> plan)
{
    private readonly object gate = new();
    private readonly Dictionary<int, HotelM2PurchaseFact> purchases = plan.Where(j =>
            j.Kind == "create"
        )
        .ToDictionary(
            j => j.Id,
            j => new HotelM2PurchaseFact(
                j.Id,
                j.Due,
                null,
                j.Provider,
                null,
                null,
                null,
                false,
                null,
                false,
                null
            )
        );
    private readonly List<HotelM2SourceSample> samples = [];
    private readonly Dictionary<string, HotelWireSnapshot[]> boundaries = new(
        StringComparer.Ordinal
    );
    private bool sealedEvidence;

    public bool Active
    {
        get
        {
            lock (gate)
                return samples.Count > 0 || boundaries.Count > 0;
        }
    }

    public void RecordHttp(
        int jobId,
        Guid? resource,
        string? responseProvider,
        double at,
        int? status,
        bool accepted
    )
    {
        lock (gate)
        {
            if (sealedEvidence)
                return;
            var fact = purchases[jobId];
            if (fact.HttpCompleted is not null || !double.IsFinite(at) || at < fact.Due)
                throw new InvalidOperationException("Invalid or repeated purchase response.");
            purchases[jobId] = fact with
            {
                ResourceId = resource,
                ResponseProvider = responseProvider,
                HttpCompleted = at,
                Status = status,
                ResponseAccepted = accepted,
            };
        }
    }

    public void Boundary(string stage, HotelWireSnapshot[] sources)
    {
        Validate(sources);
        if (
            stage is not ("beforeTimed" or "afterTimed")
            || sources.Any(s =>
                s.Active != 0 || s.Retained.ValueBytes is null || s.EffectBytes is null
            )
        )
            throw new InvalidOperationException(
                "Detailed source boundary requires quiescent byte accounting."
            );
        lock (gate)
        {
            if (sealedEvidence || boundaries.ContainsKey(stage))
                throw new InvalidOperationException("Source boundary cannot be overwritten.");
            boundaries.Add(stage, sources);
        }
    }

    public void Sample(
        double parentObservedAt,
        HotelWireSnapshot[] sources,
        HotelObservedPurchase[] rows
    )
    {
        Validate(sources);
        if (
            rows.Length > 38
            || rows.Select(r => r.Id).Distinct().Count() != rows.Length
            || sources.Any(s => !s.Recording)
        )
            throw new InvalidOperationException("Invalid bounded source observation.");
        lock (gate)
        {
            if (
                sealedEvidence
                || samples.Count == 361
                || !double.IsFinite(parentObservedAt)
                || parentObservedAt < 0
                || samples.Count > 0 && parentObservedAt < samples[^1].At
            )
                throw new InvalidOperationException(
                    "Source sample is late, repeated or exceeds the plan."
                );
            samples.Add(new(parentObservedAt, sources, rows));
        }
    }

    public bool PhasePurchaseSuccess(int jobId, double start, double end)
    {
        lock (gate)
        {
            var p = Evaluate(purchases[jobId]);
            return HotelsLoadLedger.PhasePurchaseSuccess(
                p.Due,
                p.HttpCompleted ?? double.NaN,
                p.AuthoritativeObservedAt,
                p.Status,
                p.ResponseAccepted && p.SourceMatched,
                p.Effects,
                start,
                end
            );
        }
    }

    public bool WindowPurchaseSuccess(int jobId, double end)
    {
        lock (gate)
        {
            var p = Evaluate(purchases[jobId]);
            return HotelsLoadLedger.WindowPurchaseSuccess(
                p.Due,
                p.HttpCompleted ?? double.NaN,
                p.AuthoritativeObservedAt,
                p.Status,
                p.ResponseAccepted && p.SourceMatched,
                p.Effects,
                end
            );
        }
    }

    public object Report()
    {
        lock (gate)
        {
            var facts = purchases.Values.OrderBy(p => p.JobId).Select(Evaluate).ToArray();
            return new
            {
                expectedSamples = 361,
                completeEvidence = samples.Count == 361 && boundaries.Count == 2,
                boundaries = boundaries.ToDictionary(p => p.Key, p => p.Value),
                samples = samples.ToArray(),
                purchases = facts,
                phases = new[]
                {
                    ("Normal", 30d, 150d),
                    ("RecoveryAdmission", 240d, 300d),
                    ("RecoverySteady", 300d, 360d),
                }
                    .Select(phase => new
                    {
                        phase = phase.Item1,
                        offered = facts.Count(p => p.Due >= phase.Item2 && p.Due < phase.Item3),
                        useful = facts.Count(p =>
                            PhasePurchaseSuccess(p.JobId, phase.Item2, phase.Item3)
                        ),
                    })
                    .ToArray(),
                windows = Enumerable
                    .Range(300, 16)
                    .Select(end => new
                    {
                        end,
                        offered = facts.Count(p => p.Due >= end - 60 && p.Due < end),
                        useful = facts.Count(p => WindowPurchaseSuccess(p.JobId, end)),
                    })
                    .ToArray(),
            };
        }
    }

    public async Task SaveAsync(string path, CancellationToken ct)
    {
        object report;
        lock (gate)
        {
            sealedEvidence = true;
            report = Report();
        }
        await using var output = new FileStream(
            path,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None
        );
        await JsonSerializer.SerializeAsync(
            output,
            report,
            new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true },
            ct
        );
    }

    private HotelM2PurchaseFact Evaluate(HotelM2PurchaseFact fact)
    {
        if (fact.ResourceId is null || fact.ExpectedProvider is not ("hotelbeds" or "duffel-stays"))
            return fact;
        double? firstConfirmed = null;
        Guid? attempt = null;
        var matched = fact.ResponseProvider == fact.ExpectedProvider;
        int? effects = null;
        var baselineUnknown =
            boundaries
                .GetValueOrDefault("beforeTimed")
                ?.Single(s => s.Provider == fact.ExpectedProvider)
                .UnknownEffects
            ?? 0;
        foreach (var sample in samples)
        {
            var row = sample.Purchases.SingleOrDefault(r => r.Id == fact.ResourceId);
            if (row?.AttemptId is not { } id || id == Guid.Empty)
                continue;
            attempt ??= id;
            var source = sample.Sources.Single(s => s.Provider == fact.ExpectedProvider);
            var key = HotelWireMeasurements.Correlation(fact.ExpectedProvider, id);
            var ambiguous =
                sample.Purchases.Count(r =>
                    r.Provider == fact.ExpectedProvider
                    && r.AttemptId is { } other
                    && HotelWireMeasurements.Correlation(r.Provider, other) == key
                ) != 1;
            matched &=
                row.Provider == fact.ExpectedProvider
                && row.SourceMatched
                && id == attempt
                && !ambiguous
                && source.UnknownEffects == baselineUnknown;
            effects = Math.Max(effects ?? 0, source.Effects.GetValueOrDefault(key));
            if (row.Confirmed && firstConfirmed is null)
                firstConfirmed = sample.At; // Parent arrival time, never a retrospective DB timestamp.
        }
        if (
            attempt is { } finalAttempt
            && boundaries.TryGetValue("afterTimed", out var finalSources)
        )
        {
            var source = finalSources.Single(s => s.Provider == fact.ExpectedProvider);
            matched &= source.UnknownEffects == baselineUnknown;
            effects = Math.Max(
                effects ?? 0,
                source.Effects.GetValueOrDefault(
                    HotelWireMeasurements.Correlation(fact.ExpectedProvider, finalAttempt)
                )
            );
        }
        return fact with
        {
            AuthoritativeObservedAt = firstConfirmed,
            SourceMatched = matched && attempt is not null,
            Effects = effects,
        };
    }

    private static void Validate(HotelWireSnapshot[] sources)
    {
        if (
            sources.Length != 2
            || sources.Select(s => s.Provider).Distinct().Count() != 2
            || sources.Any(s =>
                s.Provider is not ("hotelbeds" or "duffel-stays")
                || s.Overflow
                || s.Events is null
                || s.Effects is null
                || s.Events.Length > 64
                || s.Effects.Count > 64
            )
        )
            throw new InvalidOperationException(
                "Source evidence is missing, mismatched or overflowed."
            );
    }
}
