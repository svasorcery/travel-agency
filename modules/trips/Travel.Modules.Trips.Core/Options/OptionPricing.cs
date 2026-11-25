namespace Travel.Modules.Trips.Core.Options;

internal sealed record OptionPricing(long Subtotal, OptionPrice Price)
{
    internal static OptionPricing Calculate(TripBrief brief, FlightOffer flight, StayOffer hotel)
    {
        long subtotal = 0;
        var complete = true;
        var lines = new List<OptionPriceLine>();
        Add(flight.Charges, "flightBase");
        Add(hotel.Charges, "hotelBase");
        var price = new OptionPrice(
            brief.Currency,
            PlanningMoney.FormatMinorUnits(subtotal),
            complete,
            brief.TotalBudget is null ? "notSpecified" : "indeterminate",
            lines,
            ["meals", "transfers"]
        );
        return new(subtotal, price);

        void Add(IReadOnlyList<OptionCharge> charges, string baseComponent)
        {
            var hasComparableBase = false;
            foreach (var charge in charges.OrderBy(c => c.Component, StringComparer.Ordinal))
            {
                var kind = charge.Kind switch
                {
                    OptionChargeKind.TravelerMandatory => "travelerMandatory",
                    OptionChargeKind.Included => "included",
                    OptionChargeKind.UnknownMandatory => "unknownMandatory",
                    OptionChargeKind.SupplierNet => "supplierNet",
                    _ => throw new ArgumentOutOfRangeException(nameof(charges)),
                };
                lines.Add(
                    new(charge.Component, kind, charge.Money?.Amount, charge.Money?.Currency)
                );
                if (charge.Kind == OptionChargeKind.Included)
                    continue;
                if (
                    charge.Kind == OptionChargeKind.TravelerMandatory
                    && charge.Money?.Currency == brief.Currency
                )
                {
                    subtotal += charge.Money.MinorUnits;
                    hasComparableBase |= charge.Component == baseComponent;
                }
                else if (charge.Kind != OptionChargeKind.SupplierNet)
                    complete = false;
            }
            complete &= hasComparableBase;
        }
    }
}
