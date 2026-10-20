using System.Collections.Immutable;
using System.Text.Json;
using ErrorOr;
using Travel.Modules.Rail.Core;

namespace Travel.Modules.Rail.Application;

public sealed class RailSearchService(
    IRailRuntimeProfile profiles,
    RailReferenceService references,
    IRailScheduleProvider provider,
    IRailSearchCache cache,
    TimeProvider clock
)
{
    private readonly SemaphoreSlim admission = new(2, 2);

    public async Task<ErrorOr<RailSearchResult>> SearchAsync(
        RailRequest request,
        CancellationToken cancellationToken
    )
    {
        if (RailRules.Validate(request, clock.GetUtcNow()).IsError)
            return Error.Validation("Rail.Request.Invalid");
        var profile = profiles.Current;
        var modes =
            request.Transport == "all" ? new[] { "train", "suburban" } : [request.Transport];
        if (!profile.Enabled)
            return Assemble(
                request,
                profile,
                modes
                    .Select(m => ModeResult.Unavailable(m, "skipped", "source-disabled"))
                    .ToArray(),
                null
            );
        var reference = references.Resolve(request);
        if (reference.IsError)
            return reference.Errors;
        if (!admission.Wait(0))
            return Error.Failure("Rail.Capacity");
        var actual = new List<Task<ModeResult>>(2);
        var deadlines = new List<CancellationTokenSource>(2);
        var searchDeadline = new CancellationTokenSource(TimeSpan.FromSeconds(8), clock);
        var linked = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            profile.Revoked,
            searchDeadline.Token
        );
        try
        {
            var key = string.Join(
                "|",
                "rail-m1-v1",
                profile.Epoch,
                reference.Value.Generation,
                request.From.Source,
                request.From.Code,
                request.To.Code,
                request.DepartureDate,
                request.Transport
            );
            var cached = cache.Get(key, clock.GetUtcNow());
            if (cached is not null)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (
                    profiles.Current.Epoch != profile.Epoch
                    || profile.Revoked.IsCancellationRequested
                )
                    return Assemble(
                        request,
                        profiles.Current,
                        modes
                            .Select(m => ModeResult.Unavailable(m, "skipped", "profile-changed"))
                            .ToArray(),
                        null
                    );
                if (references.Resolve(request).IsError || cached.ExpiresAt <= clock.GetUtcNow())
                    return Error.Validation("Rail.StationSelection.Expired");
                cancellationToken.ThrowIfCancellationRequested();
                if (
                    profiles.Current.Epoch != profile.Epoch
                    || profile.Revoked.IsCancellationRequested
                )
                    return Assemble(
                        request,
                        profiles.Current,
                        modes
                            .Select(m => ModeResult.Unavailable(m, "skipped", "profile-changed"))
                            .ToArray(),
                        null
                    );
                linked.Token.ThrowIfCancellationRequested();
                return cached;
            }
            var reads = new List<Task<ModeResult>>(2);
            foreach (var mode in modes)
            {
                if (profiles.Current.Epoch != profile.Epoch || linked.IsCancellationRequested)
                    break;
                var childDeadline = new CancellationTokenSource(TimeSpan.FromSeconds(5), clock);
                var childCancellation = CancellationTokenSource.CreateLinkedTokenSource(
                    linked.Token,
                    childDeadline.Token
                );
                deadlines.Add(childDeadline);
                deadlines.Add(childCancellation);
                Task<ModeResult> child;
                try
                {
                    child = provider.SearchAsync(request, mode, profile, childCancellation.Token);
                }
                catch (Exception)
                {
                    child = Task.FromResult(
                        ModeResult.Unavailable(mode, "failed", "provider-failed")
                    );
                }
                actual.Add(child);
                reads.Add(ReadChild(child, mode, childCancellation.Token));
            }
            var outcomes = await Task.WhenAll(reads).WaitAsync(linked.Token);
            cancellationToken.ThrowIfCancellationRequested();
            if (profiles.Current.Epoch != profile.Epoch || profile.Revoked.IsCancellationRequested)
                return Assemble(
                    request,
                    profiles.Current,
                    modes
                        .Select(m => ModeResult.Unavailable(m, "skipped", "profile-changed"))
                        .ToArray(),
                    null
                );
            if (references.Resolve(request).IsError)
                return Error.Validation("Rail.StationSelection.Expired");
            var result = Assemble(request, profile, outcomes, reference.Value);
            if (
                result.Items.Length > 100
                || JsonSerializer.SerializeToUtf8Bytes(result).Length > 4 * 1024 * 1024
            )
                result = Assemble(
                    request,
                    profile,
                    modes
                        .Select(m => ModeResult.Unavailable(m, "failed", "normalization-limit"))
                        .ToArray(),
                    null
                );
            cancellationToken.ThrowIfCancellationRequested();
            if (profiles.Current.Epoch != profile.Epoch || profile.Revoked.IsCancellationRequested)
                return Assemble(
                    request,
                    profiles.Current,
                    modes
                        .Select(m => ModeResult.Unavailable(m, "skipped", "profile-changed"))
                        .ToArray(),
                    null
                );
            if (references.Resolve(request).IsError)
                return Error.Validation("Rail.StationSelection.Expired");
            cancellationToken.ThrowIfCancellationRequested();
            linked.Token.ThrowIfCancellationRequested();
            if (profiles.Current.Epoch != profile.Epoch || profile.Revoked.IsCancellationRequested)
                return Assemble(
                    request,
                    profiles.Current,
                    modes
                        .Select(m => ModeResult.Unavailable(m, "skipped", "profile-changed"))
                        .ToArray(),
                    null
                );
            if (
                result.Kind == "complete"
                && !linked.IsCancellationRequested
                && profiles.Current.Epoch == profile.Epoch
                && result.ExpiresAt > clock.GetUtcNow()
            )
                cache.Set(key, result);
            cancellationToken.ThrowIfCancellationRequested();
            if (profiles.Current.Epoch != profile.Epoch || profile.Revoked.IsCancellationRequested)
                return Assemble(
                    request,
                    profiles.Current,
                    modes
                        .Select(m => ModeResult.Unavailable(m, "skipped", "profile-changed"))
                        .ToArray(),
                    null
                );
            if (references.Resolve(request).IsError)
                return Error.Validation("Rail.StationSelection.Expired");
            cancellationToken.ThrowIfCancellationRequested();
            linked.Token.ThrowIfCancellationRequested();
            return result;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return Assemble(
                request,
                profiles.Current,
                modes
                    .Select(m =>
                        ModeResult.Unavailable(
                            m,
                            profile.Revoked.IsCancellationRequested ? "skipped" : "failed",
                            profile.Revoked.IsCancellationRequested ? "profile-changed" : "timeout"
                        )
                    )
                    .ToArray(),
                null
            );
        }
        finally
        {
            linked.Cancel();
            _ = ReleaseWhenSettled(actual, deadlines, linked, searchDeadline);
        }
    }

    private static async Task<ModeResult> ReadChild(
        Task<ModeResult> child,
        string mode,
        CancellationToken ct
    )
    {
        try
        {
            var result = await child.WaitAsync(ct);
            ct.ThrowIfCancellationRequested();
            return result;
        }
        catch (OperationCanceledException)
        {
            return ModeResult.Unavailable(mode, "failed", "timeout");
        }
        catch (Exception)
        {
            return ModeResult.Unavailable(mode, "failed", "provider-failed");
        }
    }

    private async Task ReleaseWhenSettled(
        List<Task<ModeResult>> children,
        List<CancellationTokenSource> deadlines,
        CancellationTokenSource linked,
        CancellationTokenSource searchDeadline
    )
    {
        try
        {
            await Task.WhenAll(children);
        }
        catch (Exception)
        {
            return; /* Safe normalized outcome already returned; do not log supplier exception text. */
        }
        finally
        {
            foreach (var cts in deadlines)
                cts.Dispose();
            linked.Dispose();
            searchDeadline.Dispose();
            admission.Release();
        }
    }

    private RailSearchResult Assemble(
        RailRequest request,
        RailProfile profile,
        ModeResult[] results,
        RailReferenceSelection? reference
    )
    {
        var usable = results.Where(x => x.Kind is "complete" or "limited").ToArray();
        var kind =
            usable.Length > 0 ? (results.All(x => x.Kind == "complete") ? "complete" : "limited")
            : results.Any(x => x.Kind == "failed") ? "failed"
            : "skipped";
        var items = usable
            .SelectMany(x => x.Items)
            .OrderBy(x => x.Departure)
            .ThenBy(x => x.Arrival)
            .ThenBy(x => x.Key, StringComparer.Ordinal)
            .ToImmutableArray();
        var times = usable
            .Where(x => x.ReceivedAt.HasValue)
            .Select(x => x.ReceivedAt!.Value)
            .ToArray();
        DateTimeOffset? received = times.Length > 0 ? times.Min() : null;
        var shortLife = items.Length == 0 || items.Any(x => x.Tickets.Damaged);
        DateTimeOffset? expiry =
            received is null || reference is null
                ? null
                : new[]
                {
                    received.Value.AddSeconds(shortLife ? 30 : 300),
                    reference.ExpiresAt,
                }.Min();
        return new(
            kind,
            results.SelectMany(x => x.Reasons).Distinct().ToImmutableArray(),
            request,
            "yandex-rasp",
            profile.Name,
            profile.Epoch,
            results
                .Select(x => new ModeEvidence(
                    x.Transport,
                    x.Kind,
                    x.Reasons,
                    x.OriginalCount,
                    x.NormalizedCount,
                    x.ReceivedAt
                ))
                .ToImmutableArray(),
            items,
            received,
            expiry,
            clock.GetUtcNow(),
            usable.Length > 0 ? reference?.Attribution : null
        );
    }
}
