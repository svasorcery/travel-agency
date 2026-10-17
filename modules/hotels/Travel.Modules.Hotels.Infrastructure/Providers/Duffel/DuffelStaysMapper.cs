using System.Globalization;
using Travel.Modules.Hotels.Core;

namespace Travel.Modules.Hotels.Infrastructure.Providers.Duffel;

internal static class DuffelStaysMapper
{
    internal static HotelProperty Property(DuffelStaysAccommodation accommodation)
    {
        var address = accommodation.Location?.Address;
        var displayAddress = address is null
            ? null
            : string.Join(
                ", ",
                new[] { address.LineOne, address.CityName, address.CountryCode }.Where(x =>
                    !string.IsNullOrWhiteSpace(x)
                )
            );
        return new(
            Reference(accommodation.Id),
            Text(accommodation.Name),
            string.IsNullOrEmpty(displayAddress) ? null : Text(displayAddress),
            accommodation.Rating?.ToString(CultureInfo.InvariantCulture),
            accommodation.Rating is null ? null : "Duffel accommodation rating"
        );
    }

    internal static HotelOffer Indicative(DuffelStaysResult result, StaySearch request)
    {
        if (result is null || result.Accommodation is null)
            throw new FormatException();
        ValidateScope(
            result.CheckInDate,
            result.CheckOutDate,
            result.Rooms,
            result.Guests,
            request
        );
        var terms = new StayTerms(
            Property(result.Accommodation),
            "Номер уточняется при выборе тарифа",
            "indicative",
            request.Dates,
            request.Occupancy,
            Money(result.CheapestRateTotalAmount, result.CheapestRateCurrency),
            null,
            [],
            new("Unknown", []),
            [],
            false,
            "Unknown"
        );
        return new(
            "duffel-stays",
            "Test",
            Reference(result.Id),
            terms,
            result.ExpiresAt,
            false,
            "LoadRatesRequired"
        );
    }

    internal static HotelOffer Rate(
        DuffelStaysAccommodation accommodation,
        DuffelStaysRoom room,
        DuffelStaysRate rate,
        StayDates dates,
        RoomOccupancy occupancy,
        DateTimeOffset? expiry = null
    ) =>
        ProjectRate(accommodation, room, rate, dates, occupancy, expiry)
        ?? throw new FormatException();

    internal static void ValidateRateFacts(
        DuffelStaysAccommodation accommodation,
        DuffelStaysRoom room,
        DuffelStaysRate rate,
        StayDates dates
    ) => _ = ProjectRate(accommodation, room, rate, dates, null);

    private static HotelOffer? ProjectRate(
        DuffelStaysAccommodation accommodation,
        DuffelStaysRoom room,
        DuffelStaysRate rate,
        StayDates dates,
        RoomOccupancy? occupancy,
        DateTimeOffset? expiry = null
    )
    {
        if (accommodation is null || room is null || rate is null)
            throw new FormatException();
        // These complete material facts must fit the public decoder. Never truncate accepted terms.
        if (
            rate.Benefits is { Length: > 99 }
            || rate.Conditions is { Length: > 99 }
            || rate.CancellationTimeline is { Length: > 100 }
            || rate.Benefits?.Any(b => b is null) == true
            || rate.Conditions?.Any(c => c is null) == true
            || rate.CancellationTimeline?.Any(c =>
                c is null || c.Before is null || c.Before == DateTimeOffset.MinValue
            ) == true
        )
            throw new FormatException();
        var local =
            rate.DueAtAccommodationAmount is null || rate.DueAtAccommodationCurrency is null
                ? null
                : new[] { Money(rate.DueAtAccommodationAmount, rate.DueAtAccommodationCurrency) };
        var board = new List<ScopedFact>
        {
            new(
                rate.BoardType switch
                {
                    "room_only" => "Без питания",
                    "breakfast" => "Завтрак",
                    "half_board" => "Полупансион",
                    "full_board" => "Полный пансион",
                    "all_inclusive" => "Всё включено",
                    _ => "Питание неизвестно",
                },
                "Факт тарифа; число гостей, которым предоставляется питание, не указано"
            ),
        };
        foreach (var benefit in rate.Benefits ?? [])
            board.Add(
                new(
                    Text(benefit.Description ?? benefit.Title ?? benefit.Type ?? "Unknown"),
                    "Условия поставщика: область действия указана в описании"
                )
            );
        var cancellation =
            rate.CancellationTimeline is null ? new CancellationTerms("Unknown", [])
            : rate.CancellationTimeline.Length == 0 ? new("NonRefundable", [])
            : new(
                "Conditional",
                rate.CancellationTimeline.Select(c => new ScopedFact(
                        $"Возвратность тарифа до {c.Before:O}: {Money(c.RefundAmount, c.Currency).Amount.ToString(CultureInfo.InvariantCulture)} {c.Currency}",
                        "Весь тариф; время и смещение указаны поставщиком; не обещание выплаты клиенту"
                    ))
                    .ToArray()
            );
        var conditions = (rate.Conditions ?? [])
            .Select(c => new ScopedFact(
                Text((c.Title ?? "") + ": " + (c.Description ?? "")),
                "Тариф, всё проживание"
            ))
            .ToList();
        if (accommodation.CheckInInformation is { } checkIn)
            conditions.Add(
                new(
                    Text(
                        $"Заезд после {checkIn.CheckInAfterTime ?? "неизвестно"}, до {checkIn.CheckInBeforeTime ?? "неизвестно"}; выезд до {checkIn.CheckOutBeforeTime ?? "неизвестно"}"
                    ),
                    "Локальное время объекта размещения"
                )
            );
        if (accommodation.KeyCollection?.Instructions is { } instructions)
            conditions.Add(new(Text(instructions), "Получение ключей"));
        if (conditions.Count > 100)
            throw new FormatException();
        var eligible = rate.LoyaltyProgrammeRequired == false && rate.DealTypes is { Length: 0 };
        var immediate =
            rate.PaymentType == "pay_now"
            && rate.AvailablePaymentMethods?.Any(m => m is ["balance"]) == true;
        var property = Property(accommodation);
        var roomName = Text(room.Name);
        var rateId = Reference(rate.Id);
        var total = Money(rate.TotalAmount, rate.TotalCurrency);
        // A nullable source guest scope permits validation, but cannot produce accepted/actual occupancy.
        if (occupancy is null)
            return null;
        var terms = new StayTerms(
            property,
            roomName,
            rateId,
            dates,
            occupancy,
            total,
            local,
            board.ToArray(),
            cancellation,
            conditions.ToArray(),
            eligible,
            immediate ? "ImmediateTest" : "Unsupported"
        );
        var purchasable =
            eligible && immediate && local is not null && cancellation.Kind != "Unknown";
        var expires = new[]
        {
            expiry ?? DateTimeOffset.MaxValue,
            rate.ExpiresAt ?? DateTimeOffset.MaxValue,
        }.Min();
        return new(
            "duffel-stays",
            "Test",
            rate.Id,
            terms,
            expires == DateTimeOffset.MaxValue ? null : expires,
            purchasable,
            purchasable ? null
                : !eligible ? "EligibilityUnknownOrRestricted"
                : !immediate ? "PaymentUnsupported"
                : local is null ? "LocalAmountsUnknown"
                : "CancellationUnknown"
        );
    }

    internal static StayMoney Money(string? amount, string? currency)
    {
        if (
            !decimal.TryParse(
                amount,
                NumberStyles.AllowDecimalPoint,
                CultureInfo.InvariantCulture,
                out var value
            )
        )
            throw new FormatException();
        var money = StayMoney.Create(value, currency);
        return money.IsError ? throw new FormatException() : money.Value;
    }

    internal static StayDates Dates(string checkIn, string checkOut) =>
        new(
            DateOnly.ParseExact(checkIn, "yyyy-MM-dd", CultureInfo.InvariantCulture),
            DateOnly.ParseExact(checkOut, "yyyy-MM-dd", CultureInfo.InvariantCulture)
        );

    internal static void ValidateScope(
        string checkIn,
        string checkOut,
        int rooms,
        DuffelStaysGuestType[]? guests,
        StaySearch request
    )
    {
        if (
            Dates(checkIn, checkOut) != request.Dates
            || rooms != 1
            || guests is null
            || guests.Length != request.Occupancy.Adults
            || guests.Any(g => g is null || g.Type != "adult")
        )
            throw new FormatException();
    }

    internal static (DuffelStaysRoom Room, DuffelStaysRate Rate)[] Rates(
        DuffelStaysAccommodation? accommodation
    )
    {
        if (
            accommodation?.Rooms is null
            || accommodation.Rooms.Any(r =>
                r is null || r.Rates is null || r.Rates.Any(t => t is null)
            )
        )
            throw new FormatException();
        return accommodation
            .Rooms.SelectMany(room => room.Rates.Select(rate => (room, rate)))
            .ToArray();
    }

    internal static string Reference(string value) =>
        PathReference(value) ? value : throw new FormatException();

    internal static bool PathReference(string? value) =>
        ReservationState.SafeReference(value) && value is not ("." or "..");

    internal static string Text(string value) =>
        value is { Length: > 0 and <= 4096 }
        && !value.Any(c => char.IsControl(c) && c is not ('\n' or '\r' or '\t'))
            ? value
            : throw new FormatException();
}
