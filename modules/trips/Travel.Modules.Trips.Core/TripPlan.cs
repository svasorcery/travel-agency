using ErrorOr;
using Travel.Modules.Trips.Core.DomainEvents;
using Travel.Shared.Abstractions;

namespace Travel.Modules.Trips.Core;

public sealed record TripBlockInput(
    Guid BlockId,
    string Title,
    string? Note = null,
    string? StartTime = null,
    string? EndTime = null,
    PlanningMoneyInput? Estimate = null
);

public sealed record TripDayInput(DateOnly Date, IReadOnlyList<TripBlockInput> Blocks);

public sealed record TripBlock
{
    public Guid BlockId { get; }
    public string Title { get; }
    public string? Note { get; }
    public string? StartTime { get; }
    public string? EndTime { get; }
    public PlanningMoney? Estimate { get; }

    private TripBlock(TripBlockInput input, PlanningMoney? estimate)
    {
        BlockId = input.BlockId;
        Title = input.Title;
        Note = input.Note;
        StartTime = input.StartTime;
        EndTime = input.EndTime;
        Estimate = estimate;
    }

    public static ErrorOr<TripBlock> Create(TripBlockInput? input, string currency)
    {
        if (
            input is null
            || TripBlockId.Create(input.BlockId).IsError
            || !TripText.IsValid(input.Title, 100)
            || (input.Note is not null && !TripText.IsValid(input.Note, 500, note: true))
            || (
                (input.StartTime is not null || input.EndTime is not null)
                && DayLocalTimeRange.Create(input.StartTime, input.EndTime).IsError
            )
        )
            return TripErrors.InvalidInput;
        PlanningMoney? estimate = null;
        if (input.Estimate is not null)
        {
            var parsed = PlanningMoney.Create(input.Estimate.Amount, input.Estimate.Currency);
            if (parsed.IsError || parsed.Value.Currency != currency)
                return TripErrors.InvalidInput;
            estimate = parsed.Value;
        }
        return new TripBlock(input, estimate);
    }

    public TripBlockInput ToInput() =>
        new(
            BlockId,
            Title,
            Note,
            StartTime,
            EndTime,
            Estimate is null ? null : new(Estimate.Amount, Estimate.Currency)
        );

    public override string ToString() => nameof(TripBlock);
}

public sealed record TripDay(DateOnly Date, IReadOnlyList<TripBlock> Blocks);

public sealed class TripPlan
{
    public Guid TripId { get; }
    public OwnerIdentity Owner { get; }
    public long Revision { get; }
    public string Title { get; }
    public TripBrief Brief { get; }
    public DestinationSelection Destination { get; }
    public IReadOnlyList<TripDay> Days { get; }
    public DateTimeOffset CreatedAt { get; }
    public DateTimeOffset UpdatedAt { get; }
    public string KnownUserEstimateSubtotal { get; }
    public int UnknownEstimateCount { get; }
    public IReadOnlyList<string> Warnings { get; }
    public IReadOnlyList<IDomainEvent> DomainEvents { get; }

    private TripPlan(
        Guid tripId,
        OwnerIdentity owner,
        long revision,
        string title,
        TripBrief brief,
        DestinationSelection destination,
        IReadOnlyList<TripDay> days,
        DateTimeOffset createdAt,
        DateTimeOffset updatedAt,
        IReadOnlyList<IDomainEvent> events
    )
    {
        TripId = tripId;
        Owner = owner;
        Revision = revision;
        Title = title;
        Brief = brief;
        Destination = destination;
        Days = days;
        CreatedAt = createdAt;
        UpdatedAt = updatedAt;
        DomainEvents = events;
        var subtotal = days.SelectMany(d => d.Blocks)
            .Aggregate(0L, (sum, block) => checked(sum + (block.Estimate?.MinorUnits ?? 0)));
        KnownUserEstimateSubtotal = PlanningMoney.FormatMinorUnits(subtotal);
        UnknownEstimateCount = days.SelectMany(d => d.Blocks).Count(b => b.Estimate is null);
        var warnings = new List<string>();
        if (days.Any(HasOverlappingTimes))
            warnings.Add("OverlappingTimes");
        if (brief.TotalBudget is { } budget && subtotal > budget.MinorUnits)
            warnings.Add("KnownEstimateExceedsBudget");
        Warnings = warnings.AsReadOnly();
    }

    public static ErrorOr<TripPlan> Create(
        Guid tripId,
        OwnerIdentity owner,
        string title,
        TripBriefInput brief,
        DateTimeOffset now
    )
    {
        var parsed = TripBrief.Create(brief);
        if (parsed.IsError)
            return parsed.Errors;
        var days = Enumerable
            .Range(0, parsed.Value.EndDate.DayNumber - parsed.Value.StartDate.DayNumber + 1)
            .Select(offset => new TripDayInput(
                parsed.Value.StartDate.AddDays(offset),
                Array.Empty<TripBlockInput>()
            ))
            .ToArray();
        var restored = Restore(
            tripId,
            owner,
            1,
            title,
            brief,
            CityCatalogue.Find(brief.DestinationId).Value,
            days,
            now,
            now
        );
        if (restored.IsError)
            return restored.Errors;
        var plan = restored.Value;
        return new TripPlan(
            tripId,
            owner,
            1,
            plan.Title,
            plan.Brief,
            plan.Destination,
            plan.Days,
            now,
            now,
            Array.AsReadOnly<IDomainEvent>([new TripPlanCreated(tripId, now)])
        );
    }

    public static ErrorOr<TripPlan> Restore(
        Guid tripId,
        OwnerIdentity owner,
        long revision,
        string title,
        TripBriefInput brief,
        DestinationSelection destination,
        IReadOnlyList<TripDayInput> days,
        DateTimeOffset createdAt,
        DateTimeOffset updatedAt
    )
    {
        var parsed = TripBrief.Create(brief);
        if (
            parsed.IsError
            || tripId == Guid.Empty
            || owner is null
            || revision <= 0
            || !TripText.IsValid(title, 100)
            || destination is null
            || destination.DestinationId != parsed.Value.DestinationId
            || !TripText.IsValid(destination.DisplayName, 100)
            || destination.TimeZone is not ("Europe/Moscow" or "Europe/Kaliningrad")
            || destination.CatalogueVersion != CityCatalogue.Version
            || createdAt.Offset != TimeSpan.Zero
            || updatedAt.Offset != TimeSpan.Zero
            || updatedAt < createdAt
            || days is null
            || days.Count != parsed.Value.EndDate.DayNumber - parsed.Value.StartDate.DayNumber + 1
        )
            return TripErrors.InvalidInput;
        var validDays = new List<TripDay>();
        var ids = new HashSet<Guid>();
        for (var index = 0; index < days.Count; index++)
        {
            var day = days[index];
            if (
                day is null
                || day.Date != parsed.Value.StartDate.AddDays(index)
                || day.Blocks is null
                || day.Blocks.Count > 8
            )
                return TripErrors.InvalidInput;
            var blocks = new List<TripBlock>();
            foreach (var input in day.Blocks)
            {
                var block = TripBlock.Create(input, parsed.Value.Currency);
                if (block.IsError || !ids.Add(block.Value.BlockId) || ids.Count > 112)
                    return TripErrors.InvalidInput;
                blocks.Add(block.Value);
            }
            validDays.Add(new(day.Date, blocks.AsReadOnly()));
        }
        return new TripPlan(
            tripId,
            owner,
            revision,
            title,
            parsed.Value,
            destination,
            validDays.AsReadOnly(),
            createdAt,
            updatedAt,
            Array.Empty<IDomainEvent>()
        );
    }

    public ErrorOr<TripPlan> Replace(
        string title,
        TripBriefInput brief,
        IReadOnlyList<TripDayInput> days,
        DateTimeOffset now
    )
    {
        var parsed = TripBrief.Create(brief);
        if (parsed.IsError)
            return parsed.Errors;
        if (
            Days.Any(day => day.Blocks.Count > 0)
            && (
                Brief.DestinationId != parsed.Value.DestinationId
                || Brief.Currency != parsed.Value.Currency
            )
        )
            return TripErrors.BriefChangeRequiresEmptyPlan;
        if (
            Days.Any(day =>
                day.Blocks.Count > 0
                && (day.Date < parsed.Value.StartDate || day.Date > parsed.Value.EndDate)
            )
        )
            return TripErrors.DateRangeWouldDropBlocks;
        var metadata =
            Brief.DestinationId == parsed.Value.DestinationId
                ? Destination
                : CityCatalogue.Find(parsed.Value.DestinationId).Value;
        var candidate = Restore(
            TripId,
            Owner,
            Revision,
            title,
            brief,
            metadata,
            days,
            CreatedAt,
            UpdatedAt
        );
        if (candidate.IsError)
            return candidate.Errors;
        if (
            candidate.Value.Days.Any(day =>
                day.Blocks.Count > 0 && (day.Date < Brief.StartDate || day.Date > Brief.EndDate)
            )
        )
            return TripErrors.InvalidInput;
        if (HasSameValues(candidate.Value))
            return this;
        if (Revision == long.MaxValue)
            return TripErrors.RevisionExhausted;
        if (now.Offset != TimeSpan.Zero || now < UpdatedAt)
            return TripErrors.InvalidInput;
        return new TripPlan(
            TripId,
            Owner,
            Revision + 1,
            title,
            candidate.Value.Brief,
            metadata,
            candidate.Value.Days,
            CreatedAt,
            now,
            Array.AsReadOnly<IDomainEvent>([new TripPlanRevised(TripId, Revision + 1, now)])
        );
    }

    public TripPlanDeleted Deleted(DateTimeOffset now) => new(TripId, Revision, now);

    private bool HasSameValues(TripPlan other) =>
        Title == other.Title
        && Brief.HasSameValues(other.Brief)
        && Days.Count == other.Days.Count
        && Days.Zip(other.Days)
            .All(pair =>
                pair.First.Date == pair.Second.Date
                && pair.First.Blocks.SequenceEqual(pair.Second.Blocks)
            );

    private static bool HasOverlappingTimes(TripDay day)
    {
        var timed = day
            .Blocks.Where(x => x.StartTime is not null)
            .OrderBy(x => x.StartTime, StringComparer.Ordinal)
            .ToArray();
        string? latestEnd = null;
        foreach (var block in timed)
        {
            if (latestEnd is not null && string.CompareOrdinal(block.StartTime, latestEnd) < 0)
                return true;
            if (latestEnd is null || string.CompareOrdinal(block.EndTime, latestEnd) > 0)
                latestEnd = block.EndTime;
        }
        return false;
    }

    public override string ToString() => nameof(TripPlan);
}
