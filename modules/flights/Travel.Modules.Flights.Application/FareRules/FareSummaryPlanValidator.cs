using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using ErrorOr;
using Travel.IntegrationContracts.AI.FareSummary;

namespace Travel.Modules.Flights.Application.FareRules;

public static class FareSummaryPlanValidator
{
    public static ErrorOr<Success> Validate(
        FareSummaryRequested request,
        FareSummaryPlanned response
    )
    {
        if (
            request is null
            || response is null
            || request.CorrelationId == Guid.Empty
            || request.PromptVersion != FareSummaryMessageIdentity.PromptVersion
            || request.Locale != "ru"
            || response.Status != "Ready"
            || response.CorrelationId != request.CorrelationId
            || response.Fingerprint != request.Fingerprint
            || response.PromptVersion != request.PromptVersion
            || response.Locale != request.Locale
            || response.Sections is null
            || JsonSerializer.SerializeToUtf8Bytes(response).Length > 16 * 1024
        )
            return Invalid();
        return ValidateSections(request.Facts, response.Sections);
    }

    public static ErrorOr<Success> ValidateSections(
        FareSummaryFact[] facts,
        FareSummaryPlanSection[] sections
    )
    {
        if (
            facts is null
            || facts.Length is < 1 or > 64
            || sections is null
            || sections.Length is < 1 or > 3
            || JsonSerializer.SerializeToUtf8Bytes(sections).Length > 16 * 1024
            || facts.Any(x =>
                x is null
                || x.Id is null
                || !Regex.IsMatch(x.Id, "^f[0-9]{1,4}$", RegexOptions.CultureInvariant)
                || x.Kind is not ("change" or "refund")
                || x.Section != x.Kind
                || x.State is not ("Allowed" or "NotAllowed" or "Unknown")
                || x.Quantity is not null
                || !MoneyValid(x.Amount, x.Currency)
                || x.Scopes is null
                || x.Scopes.Length != 1
                || x.Scopes[0] is null
                || x.Scopes[0].Kind is not ("offer" or "leg")
            )
            || facts.Select(x => x.Id).Distinct(StringComparer.Ordinal).Count() != facts.Length
        )
            return Invalid();
        var lookup = facts.ToDictionary(x => x.Id, StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var groups = 0;
        var sectionKinds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var section in sections)
        {
            if (
                section is null
                || section.Kind is not ("change" or "refund" or "baggage")
                || !sectionKinds.Add(section.Kind)
                || section.Groups is null
                || section.Groups.Length == 0
            )
                return Invalid();
            foreach (var group in section.Groups)
            {
                if (
                    ++groups > 64
                    || group is null
                    || group.FactIds is null
                    || group.FactIds.Length is < 1 or > 64
                )
                    return Invalid();
                string? key = null;
                foreach (var id in group.FactIds)
                {
                    if (
                        id is null
                        || !lookup.TryGetValue(id, out var fact)
                        || !seen.Add(id)
                        || fact.Section != section.Kind
                    )
                        return Invalid();
                    var claim = ClaimKey(fact);
                    if (key is not null && claim != key)
                        return Invalid();
                    key = claim;
                }
            }
        }
        return seen.Count == facts.Length ? Result.Success : Invalid();
    }

    private static bool MoneyValid(string? amount, string? currency) =>
        amount is null
            ? currency is null
            : currency is not null
                && amount.Length <= 40
                && Regex.IsMatch(amount, "^[0-9]+(?:[.][0-9]+)?$", RegexOptions.CultureInvariant)
                && decimal.TryParse(
                    amount,
                    NumberStyles.AllowDecimalPoint,
                    CultureInfo.InvariantCulture,
                    out var value
                )
                && value >= 0
                && Regex.IsMatch(currency, "^[A-Z]{3}$", RegexOptions.CultureInvariant);

    internal static string ClaimKey(FareSummaryFact fact) =>
        JsonSerializer.Serialize(
            new
            {
                fact.Section,
                fact.Kind,
                fact.State,
                fact.Amount,
                fact.Currency,
                fact.Quantity,
                ScopeKind = fact.Scopes[0].Kind,
            }
        );

    private static Error Invalid() =>
        Error.Validation(
            "Flights.FareSummaryPlanInvalid",
            "Fare summary plan must cover each fact exactly once and group only equal claims with their original scopes."
        );
}
