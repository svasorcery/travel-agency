using System.Text.Json;
using ErrorOr;
using Travel.Modules.Hotels.Core;

namespace Travel.Modules.Hotels.Application;

public sealed class HotelReservationService(
    IHotelReservationStore store,
    IEnumerable<IHotelBookingProvider> providers,
    IHotelPrivateProtector protector,
    IHotelCapacity capacity,
    TimeProvider time,
    HotelPolicy? policy = null,
    HotelReviewedMap? reviewedMap = null
)
{
    private readonly HotelPolicy settings = policy ?? new();

    public async Task<ErrorOr<HotelReservationView>> QuoteAsync(
        Guid owner,
        Guid id,
        int? expectedRevision,
        SelectedStay selection,
        CancellationToken ct
    )
    {
        if (owner == Guid.Empty || id == Guid.Empty)
            return HotelErrors.Invalid;
        var loaded = await store.LoadAsync(id, null, ct);
        if (loaded.IsError)
            return loaded.Errors;
        var old = loaded.Value;
        if (old is not null && old.OwnerId != owner)
            return HotelErrors.Missing;
        if (old is not null && (old.Terms is null || old.PrivatePurgeAt <= time.GetUtcNow()))
            return HotelErrors.Expired;
        if (old is not null && (old.AttemptId is not null || old.QuoteRevision != expectedRevision))
            return HotelErrors.Conflict;
        if (old is null && expectedRevision is not null)
            return HotelErrors.Missing;
        var provider = providers.SingleOrDefault(p => p.Key == selection.Offer.Provider);
        if (selection.Offer.Source is not null)
        {
            var selected = selection.Offer.AdmissionBinding;
            if (
                provider is not IHotelSourceDescriptor current
                || selected is null
                || !current.CurrentSource.NewPurchaseAllowed
                || !current.CurrentSource.Matches(selected)
                || selected.Source != selection.Offer.Source
                || selected.ValidUntil <= time.GetUtcNow()
                || selected.MappingPair is not null
                    && reviewedMap?.IsCurrent(selected, time.GetUtcNow()) != true
            )
                return HotelErrors.Expired;
        }
        if (provider is null || !provider.GetSupport(selection).Supported)
            return HotelErrors.Unsupported;
        var prepared = await provider.PrepareAsync(selection, ct);
        if (prepared.Kind != PreparationKind.Ready || prepared.Stay is null)
            return prepared.Kind == PreparationKind.Failed
                ? HotelErrors.Unavailable
                : HotelErrors.Unsupported;
        var ready = prepared.Stay;
        if (
            ready.AdmissionBinding is { } sourceBinding
            && selection.Offer.AdmissionBinding is { MappingPair: not null } selectedBinding
        )
            ready = ready with
            {
                AdmissionBinding = sourceBinding with
                {
                    MappingPair = selectedBinding.MappingPair,
                    MappingRevision = selectedBinding.MappingRevision,
                    MappingEvidenceDigest = selectedBinding.MappingEvidenceDigest,
                    ValidUntil =
                        sourceBinding.ValidUntil < selectedBinding.ValidUntil
                            ? sourceBinding.ValidUntil
                            : selectedBinding.ValidUntil,
                },
            };
        var now = time.GetUtcNow();
        if (
            provider is IHotelSourceDescriptor descriptor
            && !(
                ready.AdmissionBinding is null
                && selection.Offer.Source is null
                && provider is IHotelLegacyContextConsumer legacy
                && legacy.AcceptsLegacyContext(ready.Context)
            )
            && (
                ready.AdmissionBinding is null
                || !descriptor.CurrentSource.NewPurchaseAllowed
                || !descriptor.CurrentSource.Matches(ready.AdmissionBinding)
            )
        )
            return HotelErrors.Expired;
        if (
            ready.AdmissionBinding is { MappingPair: not null } mapped
            && reviewedMap?.IsCurrent(mapped, now) != true
        )
            return HotelErrors.Expired;
        var until = new[]
        {
            now.AddSeconds(settings.QuoteSeconds),
            ready.FreshUntil,
            ready.SourceExpiresAt ?? DateTimeOffset.MaxValue,
            ready.AdmissionBinding?.ValidUntil ?? DateTimeOffset.MaxValue,
        }.Min();
        if (until <= now)
            return HotelErrors.Expired;
        if (!Purchasable(ready.Terms) || selection.Offer.Environment != "Test")
            return HotelErrors.Unsupported;
        var row =
            old
            ?? new HotelReservation
            {
                Id = id,
                OwnerId = owner,
                CreatedAt = now,
            };
        row.QuoteRevision++;
        row.Provider = provider.Key;
        row.Environment = selection.Offer.Environment;
        row.ProviderAccount = ready.AccountBinding;
        row.Terms = ready.Terms;
        row.ValidUntil = until;
        row.UpdatedAt = now;
        row.SourcePreparationReference = ReservationState.SafeReference(
            ready.SourcePreparationReference
        )
            ? ready.SourcePreparationReference
            : null;
        row.PrivatePurgeAt = now.AddHours(settings.DraftRetentionHours);
        row.State.ReadAllowance = settings.ReadAllowance;
        row.State.ReadWindowSeconds = settings.ReadWindowSeconds;
        row.State.SecondReadDelaySeconds = settings.SecondReadDelaySeconds;
        row.State.ThirdReadDelaySeconds = settings.ThirdReadDelaySeconds;
        row.State.FinishedRetentionDays = settings.FinishedRetentionDays;
        row.State.QuoteContractVersion = ready.AdmissionBinding is null ? 0 : 2;
        row.State.AdmissionBinding = ready.AdmissionBinding;
        row.State.AcceptedPrice = ready.AdmissionBinding is null ? null : ready.PriceEvidence;
        row.State.AcceptedConditions = ready.AdmissionBinding is null
            ? null
            : ready.ConditionEvidence;
        var encrypted = protector.Protect(
            JsonSerializer.Serialize(ready.Context),
            Binding(row, "context")
        );
        if (encrypted.IsError)
            return encrypted.Errors;
        row.ProtectedContext = encrypted.Value;
        var saved = await store.SaveAsync(row, old?.Version ?? 0, [], ct);
        return saved.IsError ? saved.Errors
            : !saved.Value ? HotelErrors.Conflict
            : HotelReservationView.From(row, now);
    }

    public async Task<ErrorOr<HotelReservationView>> PurchaseAsync(
        Guid owner,
        Guid id,
        HotelPurchase purchase,
        CancellationToken ct
    )
    {
        var loaded = await store.LoadAsync(id, owner, ct);
        if (loaded.IsError)
            return loaded.Errors;
        var row = loaded.Value;
        if (row is null)
            return HotelErrors.Missing;
        if (row.Terms is null)
            return HotelErrors.Conflict;
        if (
            purchase.ActionId == Guid.Empty
            || !purchase.Consent
            || purchase.Party is null
            || !purchase.Party.IsValid(row.Terms.Occupancy.Adults)
        )
            return HotelErrors.Invalid;
        if (row.AttemptId is not null)
            return Replay(row, purchase);
        var now = time.GetUtcNow();
        if (row.QuoteRevision != purchase.QuoteRevision)
            return HotelErrors.Conflict;
        if (row.ValidUntil <= now || row.PrivatePurgeAt <= now || row.ProtectedContext is null)
            return HotelErrors.Expired;
        if (!Purchasable(row.Terms))
            return HotelErrors.Unsupported;
        var provider = providers.SingleOrDefault(p => p.Key == row.Provider);
        if (provider is null)
            return HotelErrors.Unsupported;
        if (!CurrentAdmission(row, provider, now))
            return HotelErrors.Expired;
        if (
            provider is IHotelPartySupport partySupport
            && !partySupport.GetPartySupport(purchase.Party).Supported
        )
            return HotelErrors.Unsupported;
        using var permit = capacity.TryAcquire(row.Provider);
        if (permit is null)
            return HotelErrors.Capacity;
        var context = protector.Unprotect(row.ProtectedContext, Binding(row, "context"));
        var encryptedParty = protector.Protect(
            JsonSerializer.Serialize(purchase.Party),
            Binding(row, "party")
        );
        if (context.IsError || encryptedParty.IsError)
            return HotelErrors.Unavailable;
        ProviderContextHandle? handle;
        try
        {
            handle = JsonSerializer.Deserialize<ProviderContextHandle>(context.Value);
        }
        catch (JsonException)
        {
            return HotelErrors.Unavailable;
        }
        if (handle is null)
            return HotelErrors.Unavailable;
        if (
            row.State.QuoteContractVersion == 0
            && row.State.AdmissionBinding is null
            && (
                provider is not IHotelLegacyContextConsumer legacy
                || !legacy.AcceptsLegacyContext(handle)
            )
        )
            return HotelErrors.Expired;
        // Protection and capacity preparation can cross the quote deadline. Admission uses fresh time.
        now = time.GetUtcNow();
        if (row.ValidUntil <= now || row.PrivatePurgeAt <= now)
            return HotelErrors.Expired;
        if (!CurrentAdmission(row, provider, now))
            return HotelErrors.Expired;
        row.AttemptId = Guid.NewGuid();
        row.ActionId = purchase.ActionId;
        row.ProtectedParty = encryptedParty.Value;
        row.AdmittedAt = now;
        row.UpdatedAt = now;
        row.PrivatePurgeAt = null; // Unresolved purchase content has no automatic expiry.
        row.State.Outcome = ReservationOutcome.Submitted;
        row.State.EscalateAt = now.AddSeconds(settings.EscalationSeconds);
        var saved = await store.SaveAsync(
            row,
            row.Version,
            [new(id, true, row.State.EscalateAt.Value)],
            ct
        );
        if (saved.IsError)
            return saved.Errors;
        if (!saved.Value)
        {
            var winner = await store.LoadAsync(id, owner, ct);
            return winner.IsError ? winner.Errors
                : winner.Value is null ? HotelErrors.Missing
                : Replay(winner.Value, purchase);
        }
        // The committed claim is spent. Request disconnect does not cancel or repeat this sender.
        BookingObservation observation;
        using var sender = new CancellationTokenSource(TimeSpan.FromSeconds(145), time);
        try
        {
            observation = await provider.CreateAsync(
                new(id, row.AttemptId.Value, row.Terms, handle, purchase.Party),
                sender.Token
            );
        }
        catch (Exception ex)
            when (ex is OperationCanceledException or HttpRequestException or JsonException)
        {
            observation = new(ObservationKind.Unknown, Diagnostic: "TransportUnknown");
        }
        using var completion = new CancellationTokenSource(TimeSpan.FromSeconds(10), time);
        return await ApplyAsync(
            id,
            observation with
            {
                SenderFinished = true,
            },
            true,
            completion.Token
        );
    }

    private ErrorOr<HotelReservationView> Replay(HotelReservation row, HotelPurchase purchase)
    {
        if (row.ActionId != purchase.ActionId || row.QuoteRevision != purchase.QuoteRevision)
            return HotelErrors.Conflict;
        if (row.ProtectedParty is null || row.PrivatePurgeAt <= time.GetUtcNow())
            return HotelErrors.Conflict;
        var saved = protector.Unprotect(row.ProtectedParty, Binding(row, "party"));
        if (saved.IsError)
            return HotelErrors.Unavailable;
        return saved.Value == JsonSerializer.Serialize(purchase.Party)
            ? HotelReservationView.From(row, time.GetUtcNow())
            : HotelErrors.Conflict;
    }

    public async Task<ErrorOr<HotelReservationView>> DetailAsync(
        Guid owner,
        Guid id,
        CancellationToken ct
    )
    {
        var loaded = await store.LoadAsync(id, owner, ct);
        return loaded.IsError ? loaded.Errors
            : loaded.Value is null ? HotelErrors.Missing
            : HotelReservationView.From(loaded.Value, time.GetUtcNow());
    }

    public async Task<ErrorOr<HotelReservationView[]>> ListAsync(Guid owner, CancellationToken ct)
    {
        var loaded = await store.ListAsync(owner, ct);
        return loaded.IsError
            ? loaded.Errors
            : loaded.Value.Select(r => HotelReservationView.From(r, time.GetUtcNow())).ToArray();
    }

    public async Task<ErrorOr<HotelReservationView>> ApplyAsync(
        Guid id,
        BookingObservation observation,
        bool senderFinished,
        CancellationToken ct
    )
    {
        for (var retry = 0; retry < 4; retry++)
        {
            var loaded = await store.LoadAsync(id, null, ct);
            if (loaded.IsError)
                return loaded.Errors;
            var row = loaded.Value;
            if (row is null || row.AttemptId is null)
                return HotelErrors.Missing;
            var before = JsonSerializer.Serialize(row.State);
            var hadIdentity = row.State.BoundBookingId is not null;
            var wasFinished = row.SenderFinished;
            row.SenderFinished |= senderFinished;
            if (ReservationState.SafeReference(observation.RequestReference))
                row.ProviderRequestReference = observation.RequestReference;
            if (
                observation.Diagnostic
                is "TransportUnknown"
                    or "ReservedDetailsPending"
                    or "SourcePending"
                    or "SourceNotCreated"
                    or "BookingDisabled"
            )
                row.OutcomeDiagnostic = observation.Diagnostic;
            var retained =
                row.Terms is null && observation.Facts is not null
                    ? observation with
                    {
                        Facts = observation.Facts with { Actual = null, HotelReference = null },
                    }
                    : observation;
            row.State.Apply(
                retained with
                {
                    SenderFinished = row.SenderFinished,
                },
                row.Terms,
                time.GetUtcNow()
            );
            if (before == JsonSerializer.Serialize(row.State) && wasFinished == row.SenderFinished)
                return HotelReservationView.From(row, time.GetUtcNow());
            row.UpdatedAt = time.GetUtcNow();
            if (
                row.State.Outcome
                is ReservationOutcome.Confirmed
                    or ReservationOutcome.Cancelled
                    or ReservationOutcome.NotCreated
            )
            {
                var checkout = row.Terms is null
                    ? row.UpdatedAt
                    : new DateTimeOffset(
                        row.Terms.Dates.CheckOut.ToDateTime(TimeOnly.MinValue),
                        TimeSpan.Zero
                    );
                row.PrivatePurgeAt ??= (
                    checkout > row.UpdatedAt ? checkout : row.UpdatedAt
                ).AddDays(row.State.FinishedRetentionDays);
            }
            var work =
                !hadIdentity
                && row.State.BoundBookingId is not null
                && row.State.Outcome
                    is not (ReservationOutcome.Confirmed or ReservationOutcome.Cancelled)
                    ? new[] { new HotelWork(id, false, time.GetUtcNow()) }
                    : [];
            var saved = await store.SaveAsync(row, row.Version, work, ct);
            if (saved.IsError)
                return saved.Errors;
            if (saved.Value)
                return HotelReservationView.From(row, time.GetUtcNow());
        }
        return HotelErrors.Unavailable;
    }

    public static HotelPrivacyBinding Binding(HotelReservation row, string kind) =>
        new(row.OwnerId, row.Id, row.QuoteRevision, row.Terms!.Occupancy.Adults, kind);

    private bool CurrentAdmission(
        HotelReservation row,
        IHotelBookingProvider provider,
        DateTimeOffset now
    )
    {
        var binding = row.State.AdmissionBinding;
        if (row.State.QuoteContractVersion == 2 || binding is not null)
            return binding is { Version: 2 }
                && binding.ValidUntil > now
                && provider is IHotelSourceDescriptor source
                && source.CurrentSource.NewPurchaseAllowed
                && source.CurrentSource.Matches(binding)
                && binding.Source.Provider == row.Provider
                && (binding.TransportAccountBinding ?? binding.Source.Account)
                    == row.ProviderAccount
                && binding.Source.Environment == row.Environment
                && (binding.MappingPair is null || reviewedMap?.IsCurrent(binding, now) == true);
        // Existing M1 context consumers retain their original contract. A new source cannot use it.
        return row.State.QuoteContractVersion == 0 && provider is IHotelLegacyContextConsumer;
    }

    private static bool Purchasable(StayTerms terms) =>
        terms.Eligible
        && terms.PaymentTiming == "ImmediateTest"
        && terms.MandatoryLocalPayments is not null
        && terms.Cancellation.Kind != "Unknown";
}
