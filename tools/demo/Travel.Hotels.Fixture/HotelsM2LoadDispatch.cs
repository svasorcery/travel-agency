using System.Collections.Concurrent;
using System.Net.Http.Json;
using System.Text.Json;
using Travel.Modules.Hotels.Application;
using Travel.Modules.Hotels.Core;
using Travel.Modules.Hotels.Tests.Integration;

namespace Travel.Hotels.Fixture;

internal sealed record HotelM2DispatchResult(
    int JobId,
    string Terminal,
    double Started,
    double Completed,
    int? Status,
    string? Outcome,
    bool Useful,
    string? Refusal,
    Guid? ResourceId,
    string? ResponseProvider,
    bool PurchaseAccepted
)
{
    public ProviderStatus[] SourceStatuses { get; init; } = [];
    public HotelM2ExecutionFailure? DriverFailure { get; init; }
}

internal sealed class HotelsM2LoadDispatch(
    HttpClient client,
    HotelM2LoadSchedule schedule,
    Func<double> elapsed,
    Func<HotelM2PlannedJob, CancellationToken, Task<HttpResponseMessage>> callback
)
{
    private sealed record Link(
        Guid Id,
        string? Group,
        IReadOnlyDictionary<string, string>? Selections,
        int Revision = 0,
        string? Provider = null
    );

    private readonly ConcurrentDictionary<int, Link> links = new();
    private readonly HashSet<int> retained = schedule
        .Jobs.Where(j => j.Prerequisite is not null)
        .Select(j => j.Prerequisite!.Value)
        .ToHashSet();
    private readonly SemaphoreSlim active = new(64, 64);
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        MaxDepth = 32,
    };

    public async Task<HotelM2DispatchResult> ExecuteAsync(
        HotelM2PlannedJob job,
        CancellationToken ct
    )
    {
        var started = elapsed();
        int? status = null;
        string? outcome = null,
            refusal = null,
            provider = null;
        var terminal = "FailedBeforeResponse";
        ProviderStatus[] sourceStatuses = [];
        HotelM2ExecutionFailure? driverFailure = null;
        CancellationTokenSource? deadline = null;
        var useful = false;
        var accepted = false;
        Guid? resource = job.ReservationId;
        if (!active.Wait(0))
            return new(
                job.Id,
                "NotDispatched",
                started,
                elapsed(),
                null,
                null,
                false,
                "DriverCapacity",
                resource,
                null,
                false
            );
        try
        {
            deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
            deadline.CancelAfter(TimeSpan.FromSeconds(job.Kind == "create" ? 150 : 20));
            if (job.Kind == "search" && job.Due == 162)
                deadline.CancelAfter(TimeSpan.FromMilliseconds(50));
            using var request = new HttpRequestMessage();
            request.Headers.Add(
                HotelM2LoadControl.Header,
                job.Id.ToString(System.Globalization.CultureInfo.InvariantCulture)
            );
            request.Headers.Authorization = new("Bearer", "fictional-hotels-owner");
            object? body = null;
            Link? prerequisite = null;
            if (job.Prerequisite is { } prior && !links.TryGetValue(prior, out prerequisite))
                return new(
                    job.Id,
                    "NotDispatched",
                    started,
                    elapsed(),
                    null,
                    null,
                    false,
                    "PrerequisiteUnavailable",
                    resource,
                    null,
                    false
                );
            if (job.Kind == "search")
            {
                request.Method = HttpMethod.Post;
                request.RequestUri = new("/api/hotels/search", UriKind.Relative);
                body = new
                {
                    areaKey = "fictional",
                    checkIn = schedule.Profile.CheckIn,
                    checkOut = schedule.Profile.CheckOut,
                    adults = schedule.Profile.Adults,
                    rooms = 1,
                    children = 0,
                };
            }
            else if (job.Kind == "rates")
            {
                request.Method = HttpMethod.Post;
                request.RequestUri = new("/api/hotels/rates", UriKind.Relative);
                body = new { searchId = prerequisite!.Id, propertyGroupKey = prerequisite.Group };
            }
            else if (job.Kind == "prepare")
            {
                if (
                    prerequisite!.Selections is null
                    || !prerequisite.Selections.TryGetValue(job.Provider!, out var selection)
                )
                    return new(
                        job.Id,
                        "NotDispatched",
                        started,
                        elapsed(),
                        null,
                        null,
                        false,
                        "SourcePrerequisiteUnavailable",
                        resource,
                        null,
                        false
                    );
                request.Method = HttpMethod.Put;
                request.RequestUri = new(
                    $"/api/hotels/reservations/{resource}/quote",
                    UriKind.Relative
                );
                body = new
                {
                    ratesId = prerequisite.Id,
                    selectionKey = selection,
                    expectedRevision = 1,
                };
            }
            else if (job.Kind == "create")
            {
                if (
                    prerequisite!.Id != resource
                    || prerequisite.Provider != job.Provider
                    || prerequisite.Revision != 2
                )
                    return new(
                        job.Id,
                        "NotDispatched",
                        started,
                        elapsed(),
                        null,
                        null,
                        false,
                        "SourceQuoteMismatch",
                        resource,
                        null,
                        false
                    );
                request.Method = HttpMethod.Post;
                request.RequestUri = new(
                    $"/api/hotels/reservations/{resource}/purchase",
                    UriKind.Relative
                );
                body = new HotelPurchase(
                    schedule.Profile.Id("timed-action", job.Id),
                    prerequisite.Revision,
                    true,
                    HotelM2LoadSeed.Party(schedule.Profile.Adults)
                );
            }
            else if (job.Kind == "status")
            {
                request.Method = HttpMethod.Get;
                request.RequestUri = new($"/api/hotels/reservations/{resource}", UriKind.Relative);
            }
            if (body is not null)
                request.Content = JsonContent.Create(body);
            using var response =
                job.Kind == "callback"
                    ? await callback(job, deadline.Token)
                    : await client.SendAsync(
                        request,
                        HttpCompletionOption.ResponseHeadersRead,
                        deadline.Token
                    );
            status = (int)response.StatusCode;
            terminal = "Response";
            if (!response.IsSuccessStatusCode)
                refusal = "HttpRefusal";
            else if (job.Kind == "callback")
                useful = true;
            else
            {
                var bytes = await ReadBounded(response, deadline.Token);
                if (job.Kind == "search")
                {
                    var value = JsonSerializer.Deserialize<HotelSearchSnapshot>(bytes, Json)!;
                    sourceStatuses = value.Statuses;
                    outcome = value.Result.Kind.ToString();
                    useful = HealthySearch(value, schedule.Profile);
                    if (useful && retained.Contains(job.Id))
                    {
                        var group = value.Groups.Single(g =>
                            g.Offers.Any(o =>
                                o.Provider == "hotelbeds" && o.Terms.Property.Id == "900001"
                            )
                        );
                        links[job.Id] = new(value.Id, group.Key, null);
                    }
                }
                else if (job.Kind == "rates")
                {
                    var value = JsonSerializer.Deserialize<HotelRatesSnapshot>(bytes, Json)!;
                    outcome = value.Result.Kind.ToString();
                    useful =
                        value.SearchId == prerequisite!.Id
                        && value.Result.Kind == ExecutionKind.Complete
                        && value.Statuses.Length == 2
                        && value.Statuses.All(s => s.Kind == ExecutionKind.Complete && s.Attempted)
                        && value.Result.Items.Length == 2
                        && value
                            .Result.Items.Select(o => o.Provider)
                            .Order()
                            .SequenceEqual(new[] { "duffel-stays", "hotelbeds" })
                        && value.Result.Items.All(o => o.Purchasable && o.SelectionKey.Length > 0);
                    if (useful && retained.Contains(job.Id))
                        links[job.Id] = new(
                            value.Id,
                            null,
                            value.Result.Items.ToDictionary(o => o.Provider, o => o.SelectionKey)
                        );
                }
                else
                {
                    var value = JsonSerializer.Deserialize<HotelReservationView>(bytes, Json)!;
                    outcome = value.Outcome.ToString();
                    provider = value.Provider;
                    var identity = value.Id == resource && value.Provider == job.Provider;
                    if (job.Kind == "prepare")
                    {
                        useful =
                            identity
                            && value.Outcome == ReservationOutcome.Quoted
                            && value.QuoteRevision == 2;
                        if (useful)
                            links[job.Id] = new(
                                value.Id,
                                null,
                                null,
                                value.QuoteRevision,
                                value.Provider
                            );
                    }
                    else if (job.Kind == "create")
                        accepted =
                            identity
                            && value.AttemptId is not null
                            && value.Outcome != ReservationOutcome.Quoted;
                    else
                        useful =
                            identity
                            && value.Outcome == ReservationOutcome.Confirmed
                            && response.Headers.CacheControl?.NoStore == true;
                }
                if (!useful && job.Kind != "create")
                    refusal = "SemanticOutcome";
            }
        }
        catch (OperationCanceledException)
            when (ct.IsCancellationRequested || deadline?.IsCancellationRequested == true)
        {
            terminal = "Cancelled";
            refusal = "CancelledOrDeadline";
        }
        catch (Exception ex)
        {
            refusal = "RequestOrDecodeFailure";
            driverFailure = HotelsM2Execution.Describe(ex, "dispatch", elapsed(), job, started) with
            {
                ObservedHttpStatus = status,
            };
        }
        finally
        {
            deadline?.Dispose();
            active.Release();
        }
        return new(
            job.Id,
            terminal,
            started,
            elapsed(),
            status,
            outcome,
            useful,
            refusal,
            resource,
            provider,
            accepted
        )
        {
            SourceStatuses = sourceStatuses,
            DriverFailure = driverFailure,
        };
    }

    internal static bool HealthySearch(HotelSearchSnapshot value, HotelM2LoadProfile p)
    {
        if (
            value.Result.Kind != ExecutionKind.Complete
            || value.Statuses.Length != 2
            || value.Statuses.Any(s => s.Kind != ExecutionKind.Complete || !s.Attempted)
            || !value
                .Statuses.Select(s => s.Provider)
                .Order()
                .SequenceEqual(new[] { "duffel-stays", "hotelbeds" })
            || value.Groups.Length != p.Groups
            || value.Groups.Sum(g => g.Offers.Length) != p.Leaves
            || value.Groups.Count(g => g.MatchReason == "Reviewed") != p.MatchedPairs
            || value.Request.Area.Key != "fictional"
            || value.Request.Occupancy.Adults != p.Adults
            || value.Request.Dates != new StayDates(p.CheckIn, p.CheckOut)
        )
            return false;
        if (value.RankingPolicyVersion != HotelCostRanking.Version || !RankingIntact(value.Groups))
            return false;
        var all = value.Groups.SelectMany(g => g.Offers).ToArray();
        foreach (var provider in new[] { "duffel-stays", "hotelbeds" })
        {
            var expected = Enumerable
                .Range(1, p.PropertiesPerSource)
                .Select(i => p.Property(provider, i).Id)
                .ToHashSet(StringComparer.Ordinal);
            var actual = all.Where(o => o.Provider == provider).ToArray();
            if (
                actual.Length != expected.Count
                || !expected.SetEquals(actual.Select(o => o.Terms.Property.Id))
            )
                return false;
            foreach (var offer in actual)
            {
                var index =
                    provider == "hotelbeds"
                        ? int.Parse(
                            offer.Terms.Property.Id,
                            System.Globalization.CultureInfo.InvariantCulture
                        ) - 900000
                        : int.Parse(
                            offer.Terms.Property.Id["acc_load_".Length..],
                            System.Globalization.CultureInfo.InvariantCulture
                        );
                if (
                    offer.Terms.Property != p.Property(provider, index)
                    || offer.Terms.Dates != new StayDates(p.CheckIn, p.CheckOut)
                    || offer.Terms.Occupancy.Adults != p.Adults
                    || offer.Terms.Total.Amount
                        != decimal.Parse(
                            p.Amount(provider),
                            System.Globalization.CultureInfo.InvariantCulture
                        )
                    || offer.Terms.Total.Currency != p.Currency(provider, index)
                    || offer.Source
                        != new HotelSourceIdentity(
                            provider,
                            provider == "hotelbeds" ? "hotelbeds-owned" : "duffel-owned",
                            "Test",
                            "Fictional"
                        )
                )
                    return false;
                if (
                    provider == "hotelbeds"
                    && (offer.Price?.LocalObligations is null) != p.UnknownLocal(provider, index)
                )
                    return false;
            }
        }
        var hbx = all.Where(o => o.Provider == "hotelbeds").ToArray();
        return hbx.Length == p.PropertiesPerSource
            && hbx.Count(o => o.Price?.SupplierPayable.Currency == "EUR") == p.MatchedPairs / 5
            && hbx.Count(o => o.Price?.LocalObligations is null) == p.MatchedPairs / 7;
    }

    internal static bool ExpectedTimeout(string? outcome, ProviderStatus[] sources) =>
        outcome == "Limited"
        && sources.Length == 2
        && sources.Count(s =>
            s.Provider == "duffel-stays"
            && s.Kind == ExecutionKind.Complete
            && s.Attempted
            && s.Reason is null
        ) == 1
        && sources.Count(s =>
            s.Provider == "hotelbeds"
            && s.Kind == ExecutionKind.Failed
            && s.Attempted
            && s.Reason == "Timeout"
        ) == 1;

    private static bool RankingIntact(HotelPropertyGroup[] groups)
    {
        var expected = HotelCostRanking.Rank(groups);
        if (!groups.Select(g => g.Key).SequenceEqual(expected.Select(g => g.Key)))
            return false;
        return groups
            .Zip(expected)
            .All(pair =>
                pair.First.RankingPolicyVersion == HotelCostRanking.Version
                && pair.First.PrimaryBucket == pair.Second.PrimaryBucket
                && pair.First.Minima.SequenceEqual(pair.Second.Minima)
                && pair.First.Offers.Select(o => o.SelectionKey)
                    .SequenceEqual(pair.Second.Offers.Select(o => o.SelectionKey))
                && pair.First.Offers.Zip(pair.Second.Offers)
                    .All(o => o.First.Ranking == o.Second.Ranking)
            );
    }

    private static async Task<byte[]> ReadBounded(
        HttpResponseMessage response,
        CancellationToken ct
    )
    {
        if (response.Content.Headers.ContentLength > 2 * 1024 * 1024)
            throw new InvalidDataException("Public response bound exceeded.");
        await using var input = await response.Content.ReadAsStreamAsync(ct);
        using var output = new MemoryStream();
        var buffer = new byte[8192];
        int read;
        while ((read = await input.ReadAsync(buffer, ct)) != 0)
        {
            if (output.Length + read > 2 * 1024 * 1024)
                throw new InvalidDataException("Public response bound exceeded.");
            output.Write(buffer, 0, read);
        }
        return output.ToArray();
    }
}
