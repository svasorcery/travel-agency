using System.Globalization;
using System.Text.Json;
using Travel.IntegrationContracts.AI.FareSummary;
using Travel.Modules.Flights.Core.ValueObjects;

namespace Travel.Modules.Flights.Application.FareRules;

/// <summary>All text is controlled here; the model may only organize known fact IDs.</summary>
public static class FareSummaryFormatter
{
    public static FareSummaryFact[] BuildFacts(QuotedFareSnapshot snapshot)
    {
        if (!Usable(snapshot))
            return [];
        var evidence = snapshot.Evidence;
        var facts = new List<FareSummaryFact>
        {
            Fact("f0", "change", evidence.ChangeBeforeDeparture, new("offer")),
            Fact("f1", "refund", evidence.RefundBeforeDeparture, new("offer")),
        };
        facts.AddRange(
            evidence
                .Legs.OrderBy(x => x.Leg)
                .Select(x =>
                    Fact($"f{x.Leg + 2}", "change", x.ChangeBeforeDeparture, new("leg", x.Leg))
                )
        );
        return facts.ToArray();
    }

    private static FareSummaryFact Fact(
        string id,
        string kind,
        FareCondition condition,
        FareSummaryScope scope
    ) =>
        new(
            id,
            kind,
            kind,
            condition.Permission.ToString(),
            condition.Penalty?.Amount.ToString(
                "0.############################",
                CultureInfo.InvariantCulture
            ),
            condition.Penalty?.Currency.Value,
            null,
            [scope]
        );

    public static FareSummaryResult Current(
        QuotedFareSnapshot snapshot,
        DateTimeOffset serverObservedAt,
        bool hasSelectedServices = false,
        bool aiAvailable = false,
        string aiStatus = "Disabled",
        FareSummaryPlanSection[]? plan = null
    ) =>
        Render(
            snapshot,
            serverObservedAt,
            "QuotedOffer",
            hasSelectedServices,
            aiAvailable,
            aiStatus,
            plan
        );

    public static FareSummaryResult Historical(
        QuotedFareSnapshot snapshot,
        DateTimeOffset serverObservedAt,
        bool hasSelectedServices = false
    ) =>
        Render(
            snapshot,
            serverObservedAt,
            "Historical",
            hasSelectedServices,
            false,
            "NotRequested",
            null
        );

    private static FareSummaryResult Render(
        QuotedFareSnapshot snapshot,
        DateTimeOffset serverObservedAt,
        string provenance,
        bool extras,
        bool aiAvailable,
        string aiStatus,
        FareSummaryPlanSection[]? plan
    )
    {
        if (!Usable(snapshot))
            return Unavailable(
                snapshot?.AggregateId ?? Guid.Empty,
                serverObservedAt,
                passengerCount: snapshot?.Evidence?.PassengerCount is >= 1 and <= 9
                    ? snapshot.Evidence.PassengerCount
                    : 1,
                provenance: provenance,
                reason: "InvalidSnapshot",
                offerId: snapshot?.OfferId,
                quoteRevision: snapshot?.QuoteRevision,
                hasSelectedServices: extras
            );
        var evidence = snapshot.Evidence;
        if (evidence.ObservedAt > serverObservedAt)
            return Unavailable(
                snapshot.AggregateId,
                serverObservedAt,
                evidence.PassengerCount,
                provenance,
                "SourceStale",
                snapshot.OfferId,
                snapshot.QuoteRevision,
                evidence.SegmentCounts.ToArray(),
                evidence.OfferExpiresAt,
                evidence.FirstDepartureAt,
                hasSelectedServices: extras
            ) with
            {
                AiStatus = "SourceStale",
            };
        var facts = BuildFacts(snapshot);
        var expired =
            serverObservedAt >= evidence.OfferExpiresAt
            || serverObservedAt >= evidence.FirstDepartureAt;
        var organized =
            !expired
            && provenance != "Historical"
            && plan is not null
            && !FareSummaryPlanValidator.ValidateSections(facts, plan).IsError;
        var sections = organized ? plan! : Baseline(facts);
        var lookup = facts.ToDictionary(x => x.Id, StringComparer.Ordinal);
        var lines = sections
            .Select(section => new FareSummarySection(
                section.Kind,
                section
                    .Groups.Select(group => Line(group.FactIds.Select(id => lookup[id]).ToArray()))
                    .ToArray()
            ))
            .ToArray();
        return new FareSummaryResult(
            1,
            snapshot.AggregateId,
            snapshot.OfferId,
            snapshot.QuoteRevision,
            snapshot.Fingerprint,
            provenance == "Historical" || expired ? "Stale" : "Current",
            provenance == "Historical" ? "Historical"
                : expired ? "Expired"
                : null,
            provenance,
            evidence.ObservedAt,
            evidence.OfferExpiresAt,
            evidence.FirstDepartureAt,
            serverObservedAt,
            evidence.PassengerCount,
            evidence.SegmentCounts.ToArray(),
            aiAvailable,
            organized ? "Ready"
                : plan is not null
                    ? expired ? "SourceStale"
                        : "InvalidOutput"
                : aiStatus,
            organized ? "AiOrganized" : "Deterministic",
            evidence.ChangeBeforeDeparture,
            evidence.RefundBeforeDeparture,
            lines,
            evidence.Legs.ToArray(),
            evidence.Passengers.ToArray(),
            Missing(evidence),
            Notices(extras, provenance)
        );
    }

    public static FareSummaryResult Unavailable(
        Guid aggregateId,
        DateTimeOffset serverObservedAt,
        int passengerCount = 1,
        string provenance = "QuotedOffer",
        string reason = "NotProvided",
        Guid? offerId = null,
        Guid? quoteRevision = null,
        int[]? segmentCounts = null,
        DateTimeOffset? offerExpiresAt = null,
        DateTimeOffset? firstDepartureAt = null,
        bool hasSelectedServices = false
    ) =>
        new(
            1,
            aggregateId,
            offerId,
            quoteRevision,
            null,
            "Unavailable",
            reason,
            provenance,
            null,
            offerExpiresAt,
            firstDepartureAt,
            serverObservedAt,
            passengerCount,
            segmentCounts?.ToArray() ?? [],
            false,
            "NotRequested",
            "Deterministic",
            null,
            null,
            [],
            [],
            [],
            ["Подтверждённые сведения об условиях тарифа не предоставлены.", .. Completeness],
            Notices(hasSelectedServices, provenance)
        );

    private static FareSummaryPlanSection[] Baseline(FareSummaryFact[] facts) =>
        facts
            .GroupBy(x => x.Section)
            .Select(section => new FareSummaryPlanSection(
                section.Key,
                section
                    .GroupBy(FareSummaryPlanValidator.ClaimKey)
                    .Select(group => new FareSummaryPlanGroup(group.Select(x => x.Id).ToArray()))
                    .ToArray()
            ))
            .ToArray();

    private static FareSummaryLine Line(FareSummaryFact[] facts)
    {
        var fact = facts[0];
        var scope =
            fact.Scopes[0].Kind == "offer"
                ? "Всё предложение, все взрослые"
                : "Участки "
                    + string.Join(
                        ", ",
                        facts.SelectMany(x => x.Scopes).Select(x => x.Leg!.Value + 1).Order()
                    )
                    + ", все взрослые";
        var action = fact.Kind == "change" ? "Изменение до вылета" : "Возврат до вылета";
        var permission = fact.State switch
        {
            "Allowed" => "разрешён",
            "NotAllowed" => "не разрешён",
            _ => "возможность неизвестна",
        };
        var penalty = fact.Amount is null
            ? "Сбор неизвестен."
            : $"Указанный сбор{(fact.Scopes[0].Kind == "leg" ? " для каждого указанного участка" : "")}: {fact.Amount} {fact.Currency}.";
        return new FareSummaryLine(
            facts.Select(x => x.Id).ToArray(),
            scope,
            $"{action}: {permission}. {penalty}"
        );
    }

    private static readonly string[] Completeness =
    [
        "Полный текст правил тарифа не предоставлен.",
        "Условия после вылета не предоставлены.",
        "Условия при неявке на рейс не предоставлены.",
        "Условия при вынужденном изменении или отмене не предоставлены.",
    ];

    private static string[] Missing(FareRuleEvidence evidence)
    {
        var missing = new List<string>(Completeness);
        if (evidence.SupplierDataInvalid)
            missing.Add("Часть сведений поставщика некорректна и недоступна.");
        if (evidence.ChangeBeforeDeparture.Permission == FarePermission.Unknown)
            missing.Add("Возможность изменения всего предложения неизвестна.");
        if (evidence.RefundBeforeDeparture.Permission == FarePermission.Unknown)
            missing.Add("Возможность возврата всего предложения неизвестна.");
        if (
            evidence.Passengers.Any(x =>
                x.CheckedBaggageQuantity is null || x.CarryOnBaggageQuantity is null
            )
        )
            missing.Add(
                "Часть норм включённого багажа неизвестна; отсутствие данных не означает нулевую норму."
            );
        return missing.ToArray();
    }

    private static string[] Notices(bool extras, string provenance)
    {
        var notices = new List<string>
        {
            "Это ограниченные структурированные сведения, а не полный текст правил тарифа.",
            "Даже нулевой сбор не исключает доплату разницы тарифа при изменении.",
            "Разрешённый возврат не определяет сумму возврата, возврат полной цены или зачисление денег в банк. Финансовые операции проверяются отдельно.",
        };
        if (extras)
            notices.Add(
                "Условия изменения и возврата выбранных мест и дополнительного багажа неизвестны. Сбор по тарифу не определяет возврат дополнительных услуг или итоговой цены."
            );
        if (provenance == "Historical")
            notices.Add(
                "Это условия при расчёте предложения; они не являются текущими условиями созданного заказа."
            );
        return notices.ToArray();
    }

    private static bool Usable(QuotedFareSnapshot? snapshot) =>
        snapshot is not null
        && snapshot.AggregateId != Guid.Empty
        && snapshot.OfferId != Guid.Empty
        && snapshot.QuoteRevision != Guid.Empty
        && snapshot.FingerprintVersion == 1
        && snapshot.Fingerprint is { Length: 64 }
        && snapshot.Fingerprint.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f')
        && snapshot.Evidence is not null
        && !snapshot.Evidence.Validate().IsError;
}
