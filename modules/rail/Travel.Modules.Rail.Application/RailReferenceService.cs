using System.Collections.Immutable;
using ErrorOr;
using Travel.Modules.Rail.Core;

namespace Travel.Modules.Rail.Application;

public sealed class RailReferenceService : IDisposable
{
    private readonly IRailRuntimeProfile profiles;
    private readonly IRailReferenceLoader loader;
    private readonly TimeProvider clock;
    private readonly ITimer expiryTimer;
    private CancellationTokenRegistration revokedBundle;

    public RailReferenceService(
        IRailRuntimeProfile profiles,
        IRailReferenceLoader loader,
        TimeProvider clock
    )
    {
        this.profiles = profiles;
        this.loader = loader;
        this.clock = clock;
        expiryTimer = clock.CreateTimer(
            _ =>
            {
                lock (gate)
                {
                    if (bundle?.ExpiresAt <= clock.GetUtcNow())
                        bundle = null;
                }
            },
            null,
            Timeout.InfiniteTimeSpan,
            Timeout.InfiniteTimeSpan
        );
    }

    private readonly object gate = new();
    private readonly SemaphoreSlim admission = new(2, 2);
    private ReferenceBundle? bundle;
    private Flight? flight;
    private DateTimeOffset cooldown;
    private bool disposed;

    private sealed class Flight(RailProfile profile, TimeProvider clock)
    {
        public RailProfile Profile { get; } = profile;
        public CancellationTokenSource Deadline { get; } = new(TimeSpan.FromSeconds(20), clock);
        public CancellationTokenSource? Linked;
        public Task<ErrorOr<ReferenceBundle>> Task = null!;
        public int Waiters;
        public bool Abandoned;
        public bool Settled;
    }

    public async Task<ErrorOr<StationSuggestions>> SuggestAsync(
        string query,
        CancellationToken cancellationToken
    )
    {
        if (string.IsNullOrWhiteSpace(query) || query.Length is < 3 or > 80)
            return Error.Validation("Rail.StationQuery.Invalid");
        if (!admission.Wait(0))
            return Error.Failure("Rail.Capacity");
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(22), clock);
        using var request = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            deadline.Token
        );
        Flight? joined = null;
        var profile = profiles.Current;
        using var profileWait = CancellationTokenSource.CreateLinkedTokenSource(
            request.Token,
            profile.Revoked
        );
        try
        {
            if (!profile.Enabled)
                return Unavailable(profile, "skipped", "source-disabled");
            ReferenceBundle? current;
            lock (gate)
            {
                if (disposed)
                    return Unavailable(profile, "failed", "source-disposed");
                current = ValidBundle(profile);
                if (current is null)
                {
                    if (flight is { Abandoned: true })
                        return Unavailable(profile, "failed", "loading-cancelled");
                    if (flight?.Linked?.IsCancellationRequested == true)
                        return Unavailable(
                            profile,
                            "failed",
                            flight.Deadline.IsCancellationRequested
                                ? "reference-timeout"
                                : "loading-cancelled"
                        );
                    if (clock.GetUtcNow() < cooldown)
                        return Unavailable(profile, "failed", "refresh-cooldown");
                    if (flight is null)
                    {
                        flight = new Flight(profile, clock);
                        flight.Linked = CancellationTokenSource.CreateLinkedTokenSource(
                            flight.Deadline.Token,
                            profile.Revoked
                        );
                        flight.Waiters = 1;
                        flight.Task = LoadAsync(flight);
                    }
                    else
                        flight.Waiters++;
                    joined = flight;
                }
            }
            if (current is null)
            {
                var loaded = await joined!.Task.WaitAsync(profileWait.Token);
                cancellationToken.ThrowIfCancellationRequested();
                if (
                    profiles.Current.Epoch != profile.Epoch
                    || profile.Revoked.IsCancellationRequested
                )
                    return Unavailable(profiles.Current, "skipped", "profile-changed");
                if (loaded.IsError)
                    return Unavailable(
                        profile,
                        loaded.FirstError.Code is "quota" or "source-disabled"
                            ? "skipped"
                            : "failed",
                        loaded.FirstError.Code
                    );
                current = loaded.Value;
            }
            lock (gate)
            {
                request.Token.ThrowIfCancellationRequested();
                if (ValidBundle(profile)?.Generation != current.Generation)
                    return Unavailable(profile, "failed", "reference-expired");
                var matches = current
                    .Catalogue.Stations.Where(x =>
                        x.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
                        || (
                            x.Settlement?.Contains(query, StringComparison.OrdinalIgnoreCase)
                            ?? false
                        )
                    )
                    .Take(21)
                    .ToArray();
                var more = matches.Length > 20;
                var limited = current.Catalogue.Limited || more;
                if (ValidBundle(profile)?.Generation != current.Generation)
                    return Unavailable(profiles.Current, "failed", "reference-expired");
                request.Token.ThrowIfCancellationRequested();
                return new StationSuggestions(
                    limited ? "limited" : "complete",
                    current.Catalogue.Limited ? ["catalogue-coverage-limited"]
                        : more ? ["suggestion-limit"]
                        : [],
                    matches.Take(20).ToImmutableArray(),
                    more,
                    current.Generation,
                    "yandex-rasp",
                    profile.Name,
                    profile.Epoch,
                    current.ReceivedAt,
                    current.ExpiresAt,
                    current.Attribution
                );
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            if (profiles.Current.Epoch != profile.Epoch || profile.Revoked.IsCancellationRequested)
                return Unavailable(profiles.Current, "skipped", "profile-changed");
            return Unavailable(profiles.Current, "failed", "reference-timeout");
        }
        finally
        {
            if (joined is not null)
                lock (gate)
                {
                    joined.Waiters--;
                    if (joined.Waiters == 0 && !joined.Settled)
                    {
                        joined.Abandoned = true;
                        _ = joined.Linked!.CancelAsync();
                    }
                }
            admission.Release();
        }
    }

    private async Task<ErrorOr<ReferenceBundle>> LoadAsync(Flight owned)
    {
        // Ensure ownership is assigned under the lock even for synchronous loaders.
        await Task.Yield();
        ErrorOr<ReferenceData> loaded;
        try
        {
            loaded = await loader.LoadAsync(owned.Profile, owned.Linked!.Token);
        }
        catch (OperationCanceledException)
        {
            loaded = Error.Failure("reference-timeout");
        }
        catch (Exception)
        {
            loaded = Error.Failure("reference-failed");
        }
        lock (gate)
        {
            try
            {
                if (disposed)
                    return Error.Failure("loading-cancelled");
                if (
                    profiles.Current.Epoch != owned.Profile.Epoch
                    || owned.Profile.Revoked.IsCancellationRequested
                )
                    return Error.Failure("profile-changed");
                if (owned.Deadline.IsCancellationRequested)
                {
                    cooldown = clock.GetUtcNow().AddSeconds(30);
                    return Error.Failure("reference-timeout");
                }
                if (owned.Abandoned || owned.Linked!.IsCancellationRequested)
                    return Error.Failure("loading-cancelled");
                if (loaded.IsError)
                {
                    if (loaded.FirstError.Code is not ("quota" or "source-disabled"))
                        cooldown = clock.GetUtcNow().AddSeconds(30);
                    return loaded.Errors;
                }
                var expires = loaded.Value.ReceivedAt.AddMinutes(30);
                if (expires <= clock.GetUtcNow())
                    return Error.Failure("reference-expired");
                bundle = new(
                    Guid.NewGuid().ToString("N"),
                    owned.Profile.Epoch,
                    owned.Profile.Name,
                    loaded.Value.Catalogue,
                    loaded.Value.Attribution,
                    loaded.Value.ReceivedAt,
                    expires
                );
                expiryTimer.Change(expires - clock.GetUtcNow(), Timeout.InfiniteTimeSpan);
                revokedBundle.Unregister();
                revokedBundle = owned.Profile.Revoked.Register(() =>
                {
                    lock (gate)
                    {
                        if (bundle?.ProfileEpoch == owned.Profile.Epoch)
                            bundle = null;
                    }
                });
                return bundle is null ? Error.Failure("source-disabled") : bundle;
            }
            finally
            {
                owned.Settled = true;
                if (ReferenceEquals(flight, owned))
                    flight = null;
                owned.Linked!.Dispose();
                owned.Deadline.Dispose();
            }
        }
    }

    private ReferenceBundle? ValidBundle(RailProfile profile)
    {
        if (
            bundle is not null
            && (
                bundle.ExpiresAt <= clock.GetUtcNow()
                || bundle.ProfileEpoch != profile.Epoch
                || profile.Revoked.IsCancellationRequested
                || !profile.Enabled
                || profiles.Current.Epoch != profile.Epoch
            )
        )
            bundle = null;
        return bundle;
    }

    public ErrorOr<RailReferenceSelection> Resolve(RailRequest request)
    {
        lock (gate)
        {
            var current = ValidBundle(profiles.Current);
            if (current is null || current.Generation != request.CatalogueGeneration)
                return Error.Validation("Rail.StationSelection.Expired");
            var from = current.Catalogue.Stations.FirstOrDefault(x => x.Reference == request.From);
            var to = current.Catalogue.Stations.FirstOrDefault(x => x.Reference == request.To);
            return from is null || to is null
                ? Error.Validation("Rail.StationSelection.Invalid")
                : new RailReferenceSelection(
                    current.Generation,
                    current.ProfileEpoch,
                    from,
                    to,
                    current.Attribution,
                    current.ExpiresAt
                );
        }
    }

    private static StationSuggestions Unavailable(
        RailProfile profile,
        string kind,
        string reason
    ) =>
        new(
            kind,
            [reason],
            [],
            false,
            null,
            "yandex-rasp",
            profile.Name,
            profile.Epoch,
            null,
            null,
            null
        );

    public void Dispose()
    {
        lock (gate)
        {
            if (disposed)
                return;
            disposed = true;
            bundle = null;
            if (flight is { Settled: false } owned)
            {
                owned.Abandoned = true;
                _ = owned.Linked!.CancelAsync();
            }
        }
        expiryTimer.Dispose();
        revokedBundle.Unregister();
    }
}
