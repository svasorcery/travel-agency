using System.Collections.Immutable;
using System.Text;
using System.Text.Json;
using ErrorOr;
using Travel.Modules.Rail.Core;

namespace Travel.Modules.Rail.Application;

public sealed class RailJourneyDetailsService(
    IRailRuntimeProfile profiles,
    RailReferenceService references,
    IRailJourneyVerificationProvider verification,
    IRailJourneyRouteProvider routes,
    IRailSearchCache cache,
    TimeProvider clock,
    RailReadAdmission admission
)
{
    public async Task<ErrorOr<RailJourneyDetailsResult>> GetAsync(
        RailJourneyDetailsRequest request,
        CancellationToken cancellationToken
    )
    {
        var search = request.Search;
        var selection = request.Selection;
        if (
            RailRules.Validate(search, clock.GetUtcNow()).IsError
            || !Guid.TryParseExact(request.ExpectedProfileEpoch, "N", out _)
            || string.IsNullOrWhiteSpace(selection.Key)
            || Encoding.UTF8.GetByteCount(selection.Key) > 1024
            || selection.Key.Any(char.IsControl)
            || selection.Transport is not ("train" or "suburban")
            || search.Transport != "all" && search.Transport != selection.Transport
            || selection.Arrival <= selection.Departure
            || selection.Departure.ToString(
                "yyyy-MM-dd",
                System.Globalization.CultureInfo.InvariantCulture
            ) != search.DepartureDate
            || selection.Departure.Ticks % TimeSpan.TicksPerSecond != 0
            || selection.Arrival.Ticks % TimeSpan.TicksPerSecond != 0
        )
            return Error.Validation("Rail.Selection.Invalid");
        var profile = profiles.Current;
        RailVerificationEvidence? evidence = null;
        DateTimeOffset? received = null;
        var stage = "admission";
        RailJourneyDetailsResult Result(string kind, string reason, string? atStage = null) =>
            new(
                kind,
                [reason],
                atStage ?? stage,
                request,
                "yandex-rasp",
                profile.Name,
                profile.Epoch,
                evidence,
                received,
                null,
                clock.GetUtcNow(),
                [],
                null,
                null,
                null
            );
        RailJourneyDetailsResult Revoked()
        {
            profile = profiles.Current;
            evidence = null;
            received = null;
            return Result("skipped", "profile-changed", "admission");
        }
        bool Changed() =>
            profiles.Current.Epoch != profile.Epoch || profile.Revoked.IsCancellationRequested;
        if (
            profile.Epoch != request.ExpectedProfileEpoch
            || profile.Revoked.IsCancellationRequested
        )
            return Revoked();
        if (!profile.Enabled)
            return Result("skipped", "source-disabled");
        if (!profile.JourneyDetailsEnabled)
            return Result("skipped", "details-disabled");
        var reference = references.Resolve(search);
        if (reference.IsError)
            return reference.Errors;
        if (!admission.TryEnter())
            return Error.Failure("Rail.Capacity");
        var children = new List<Task>(2);
        var sources = new List<CancellationTokenSource>(4);
        var lastChildToken = CancellationToken.None;
        var providerFault = false;
        var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(8), clock);
        var linked = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            profile.Revoked,
            deadline.Token
        );
        async Task<T> Child<T>(Func<CancellationToken, Task<T>> run)
        {
            linked.Token.ThrowIfCancellationRequested();
            var childDeadline = new CancellationTokenSource(TimeSpan.FromSeconds(5), clock);
            var childCancellation = CancellationTokenSource.CreateLinkedTokenSource(
                linked.Token,
                childDeadline.Token
            );
            lastChildToken = childCancellation.Token;
            sources.Add(childDeadline);
            sources.Add(childCancellation);
            Task<T> child;
            try
            {
                child = run(childCancellation.Token);
            }
            catch (Exception error) when (error is not OperationCanceledException)
            {
                providerFault = true;
                throw;
            }
            children.Add(child);
            T value;
            try
            {
                value = await child.WaitAsync(childCancellation.Token);
            }
            catch (Exception error) when (error is not OperationCanceledException)
            {
                providerFault = true;
                throw;
            }
            childCancellation.Token.ThrowIfCancellationRequested();
            return value;
        }
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (Changed())
                return Revoked();
            var key = RailSearchCacheKey.Create(search, profile, reference.Value.Generation);
            var captured = cache.Get(key, clock.GetUtcNow());
            if (captured is not null && !captured.Items.Any(x => Matches(x, selection)))
                captured = null;
            stage = "verification";
            var page = await Child(ct =>
                verification.VerifyAsync(search, selection.Transport, profile, ct)
            );
            evidence = new(
                selection.Transport,
                page.Page.Kind,
                page.Page.Reasons,
                page.Page.ReceivedAt
            );
            cancellationToken.ThrowIfCancellationRequested();
            if (Changed())
                return Revoked();
            if (references.Resolve(search).IsError)
                return Error.Validation("Rail.StationSelection.Expired");
            linked.Token.ThrowIfCancellationRequested();
            if (page.Page.Kind is not ("complete" or "limited"))
                return Result(
                    page.Page.Kind,
                    page.Page.Reasons.FirstOrDefault() ?? "provider-failed"
                );
            if (
                page.Page.ReceivedAt is null
                || page.Page.ReceivedAt > clock.GetUtcNow()
                || page.Page.Transport != selection.Transport
            )
                return Result("failed", "mapping");
            var matches = page.Page.Items.Where(x => Matches(x, selection)).ToArray();
            if (matches.Length != 1)
            {
                if (captured is not null && matches.Length == 0)
                    cache.RemoveIfSame(key, captured);
                cancellationToken.ThrowIfCancellationRequested();
                if (Changed())
                    return Revoked();
                if (references.Resolve(search).IsError)
                    return Error.Validation("Rail.StationSelection.Expired");
                linked.Token.ThrowIfCancellationRequested();
                return Result("skipped", "selection-not-confirmed");
            }
            var selected = matches[0];
            if (RailRules.Journey(selected, search).IsError)
                return Result("failed", "mapping");
            var facts = page.Locators.Where(x => x.Key == selection.Key).ToArray();
            if (facts.Length == 0 || facts.All(x => x.State == "missing"))
                return Result("skipped", "route-locator-unavailable");
            var fact = facts[0];
            if (
                facts.Any(x => x.State != "reported" || x.Locator != fact.Locator)
                || fact.Locator is null
                || fact.Locator.Source != "yandex-rasp"
                || fact.Locator.Uid != selected.ThreadReference
            )
                return Result("failed", "invalid-route-locator");
            cancellationToken.ThrowIfCancellationRequested();
            if (Changed())
                return Revoked();
            if (references.Resolve(search).IsError)
                return Error.Validation("Rail.StationSelection.Expired");
            linked.Token.ThrowIfCancellationRequested();
            stage = "route";
            var route = await Child(ct => routes.RouteAsync(selected, fact.Locator, profile, ct));
            received = route.ReceivedAt;
            cancellationToken.ThrowIfCancellationRequested();
            if (Changed())
                return Revoked();
            if (references.Resolve(search).IsError)
                return Error.Validation("Rail.StationSelection.Expired");
            linked.Token.ThrowIfCancellationRequested();
            if (route.Kind is not ("complete" or "limited"))
                return Result(route.Kind, route.Reasons.FirstOrDefault() ?? "provider-failed");
            var validated = RailRouteRules.Validate(
                route.Stops,
                search.From,
                search.To,
                selection.Departure,
                selection.Arrival
            );
            var completed = clock.GetUtcNow();
            if (
                validated.IsError
                || route.BoardingIndex != validated.Value.BoardingIndex
                || route.AlightingIndex != validated.Value.AlightingIndex
                || !received.HasValue
                || received > completed
                || evidence.ReceivedAt > received
            )
                return Result("failed", "route-mismatch");
            var expires = new[] { received.Value.AddSeconds(60), reference.Value.ExpiresAt }.Min();
            if (expires <= completed)
                return Result("failed", "expired");
            var reply = new RailJourneyDetailsResult(
                route.Kind,
                route.Reasons,
                stage,
                request,
                "yandex-rasp",
                profile.Name,
                profile.Epoch,
                evidence,
                received,
                expires,
                completed,
                validated.Value.Stops,
                validated.Value.BoardingIndex,
                validated.Value.AlightingIndex,
                reference.Value.Attribution
            );
            if (JsonSerializer.SerializeToUtf8Bytes(reply).Length > 512 * 1024)
                return Result("failed", "output-limit");
            cancellationToken.ThrowIfCancellationRequested();
            if (Changed())
                return Revoked();
            if (references.Resolve(search).IsError)
                return Error.Validation("Rail.StationSelection.Expired");
            linked.Token.ThrowIfCancellationRequested();
            return reply;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            if (Changed())
                return Revoked();
            if (references.Resolve(search).IsError)
                return Error.Validation("Rail.StationSelection.Expired");
            evidence ??=
                stage == "verification"
                    ? new(selection.Transport, "failed", ["timeout"], null)
                    : null;
            return Result("failed", "timeout");
        }
        catch (Exception error) when (providerFault && error is not OperationCanceledException)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (Changed())
                return Revoked();
            if (references.Resolve(search).IsError)
                return Error.Validation("Rail.StationSelection.Expired");
            if (linked.IsCancellationRequested || lastChildToken.IsCancellationRequested)
            {
                evidence ??=
                    stage == "verification"
                        ? new(selection.Transport, "failed", ["timeout"], null)
                        : null;
                return Result("failed", "timeout");
            }
            evidence ??=
                stage == "verification"
                    ? new(selection.Transport, "failed", ["provider-failed"], null)
                    : null;
            return Result("failed", "provider-failed");
        }
        finally
        {
            linked.Cancel();
            _ = ReleaseWhenSettled(children, sources, linked, deadline);
        }
    }

    private static bool Matches(ScheduledJourney journey, RailJourneySelection selection) =>
        journey.Key == selection.Key
        && journey.Transport == selection.Transport
        && RailRouteRules.SameTime(journey.Departure, selection.Departure)
        && RailRouteRules.SameTime(journey.Arrival, selection.Arrival);

    private async Task ReleaseWhenSettled(
        List<Task> children,
        List<CancellationTokenSource> sources,
        CancellationTokenSource linked,
        CancellationTokenSource deadline
    )
    {
        try
        {
            await Task.WhenAll(children);
        }
        catch (Exception)
        {
            return; /* Observe late faults without retaining/logging supplier text. */
        }
        finally
        {
            foreach (var source in sources)
                source.Dispose();
            linked.Dispose();
            deadline.Dispose();
            admission.Release();
        }
    }
}
