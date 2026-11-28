using System.Globalization;
using ErrorOr;
using Travel.Modules.Trips.Core;
using Travel.Modules.Trips.Core.Options;

namespace Travel.Modules.Trips.Application.SavedOptions;

public sealed class SavedOptionService(
    ISavedOptionStore store,
    ISavedOptionProtection protection,
    TripPlanService plans
)
{
    public async Task<ErrorOr<SavedOptionWrite>> SaveAsync(
        OwnerIdentity owner,
        string token,
        string? title,
        Func<TripOptionPreview, bool> admissible,
        CancellationToken ct
    )
    {
        ct.ThrowIfCancellationRequested();
        var opened = protection.OpenSelection(owner, token);
        if (opened.IsError)
            return opened.Errors;
        var selected = opened.Value;
        return await store.TrySaveAsync(owner, selected.TripId, selected.ExpiresAtUtc, Prepare, ct);

        async Task<ErrorOr<NewSavedOption>> Prepare(CancellationToken cancellation)
        {
            if (!admissible(selected.Preview))
                return TripErrors.SelectionUnavailable;
            var name = string.IsNullOrWhiteSpace(title) ? DefaultTitle(selected.Brief) : title;
            var row = await plans.PrepareNewAsync(
                selected.TripId,
                owner,
                name,
                selected.Brief,
                cancellation
            );
            if (row.IsError)
                return row.Errors;
            var history = protection.ProtectHistory(
                owner,
                selected.TripId,
                new(1, selected.Brief, selected.Preview, row.Value.CreatedAt)
            );
            return history.IsError ? history.Errors : new NewSavedOption(row.Value, history.Value);
        }
    }

    public async Task<ErrorOr<SavedOptionDetail>> ReadAsync(
        OwnerIdentity owner,
        Guid tripId,
        CancellationToken ct
    )
    {
        var loaded = await store.LoadAsync(owner, tripId, ct);
        if (loaded.IsError)
            return loaded.Errors;
        if (loaded.Value.ProtectedOption is not { } envelope)
            return new SavedOptionDetail(null);
        var snapshot = protection.OpenHistory(owner, tripId, envelope);
        return snapshot.IsError ? snapshot.Errors : new SavedOptionDetail(snapshot.Value);
    }

    private static string DefaultTitle(TripBriefInput brief)
    {
        var culture = CultureInfo.GetCultureInfo("ru-RU");
        var startFormat =
            brief.StartDate.Month == brief.EndDate.Month
            && brief.StartDate.Year == brief.EndDate.Year
                ? "%d"
            : brief.StartDate.Year == brief.EndDate.Year ? "d MMMM"
            : "d MMMM yyyy";
        return $"Санкт-Петербург · {brief.StartDate.ToString(startFormat, culture)}–{brief.EndDate.ToString("d MMMM yyyy", culture)}";
    }
}
