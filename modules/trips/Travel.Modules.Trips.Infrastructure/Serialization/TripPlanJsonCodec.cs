using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using ErrorOr;
using Travel.Modules.Trips.Application;
using Travel.Modules.Trips.Core;

namespace Travel.Modules.Trips.Infrastructure.Serialization;

public sealed class TripPlanJsonCodec : ITripPlanCodec
{
    public const int MaximumBytes = 128 * 1024;
    private static readonly UTF8Encoding Utf8 = new(false, true);
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        PropertyNameCaseInsensitive = false,
        MaxDepth = 16,
    };
    private const string TimestampFormat = "yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'";

    private sealed record BriefDto(
        string DestinationId,
        string? OriginId,
        string StartDate,
        string EndDate,
        int Adults,
        string Currency,
        string? TotalBudget,
        string[] Interests,
        string Pace
    );

    private sealed record MoneyDto(string Amount, string Currency);

    private sealed record BlockDto(
        Guid BlockId,
        string Title,
        string? Note,
        string? StartTime,
        string? EndTime,
        MoneyDto? Estimate
    );

    private sealed record DayDto(string Date, BlockDto[] Blocks);

    private sealed record DestinationDto(
        string DestinationId,
        string DisplayName,
        string TimeZone,
        string CatalogueVersion
    );

    private sealed record DetailDto(
        Guid TripId,
        string Revision,
        string Title,
        BriefDto Brief,
        DestinationDto Destination,
        string CreatedAt,
        string UpdatedAt,
        DayDto[] Days,
        string KnownUserEstimateSubtotal,
        int UnknownEstimateCount,
        string[] Warnings
    );

    private static string Date(DateOnly value) =>
        value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static string Timestamp(DateTimeOffset value) =>
        value.ToUniversalTime().ToString(TimestampFormat, CultureInfo.InvariantCulture);

    private static DetailDto Detail(TripPlan plan, bool reserve = false) =>
        new(
            plan.TripId,
            (reserve ? long.MaxValue : plan.Revision).ToString(CultureInfo.InvariantCulture),
            plan.Title,
            new(
                plan.Brief.DestinationId,
                plan.Brief.OriginId,
                Date(plan.Brief.StartDate),
                Date(plan.Brief.EndDate),
                plan.Brief.Adults,
                plan.Brief.Currency,
                plan.Brief.TotalBudget?.Amount,
                plan.Brief.Interests.ToArray(),
                plan.Brief.Pace
            ),
            new(
                plan.Destination.DestinationId,
                plan.Destination.DisplayName,
                plan.Destination.TimeZone,
                plan.Destination.CatalogueVersion
            ),
            Timestamp(reserve ? DateTimeOffset.MaxValue : plan.CreatedAt),
            Timestamp(reserve ? DateTimeOffset.MaxValue : plan.UpdatedAt),
            plan.Days.Select(day => new DayDto(
                    Date(day.Date),
                    day.Blocks.Select(block => new BlockDto(
                            block.BlockId,
                            block.Title,
                            block.Note,
                            block.StartTime,
                            block.EndTime,
                            block.Estimate is null
                                ? null
                                : new(block.Estimate.Amount, block.Estimate.Currency)
                        ))
                        .ToArray()
                ))
                .ToArray(),
            plan.KnownUserEstimateSubtotal,
            plan.UnknownEstimateCount,
            plan.Warnings.ToArray()
        );

    public ErrorOr<string> Serialize(TripPlan plan) => Encode(Detail(plan));

    public ErrorOr<int> MeasureDetailBytes(TripPlan plan, bool reserveMaximumServerFields) =>
        Measure(Encode(Detail(plan, reserveMaximumServerFields)));

    public ErrorOr<string> SerializePage(TripPlanPage page) =>
        Encode(
            new
            {
                items = page
                    .Items.Select(item => new
                    {
                        tripId = item.TripId,
                        revision = item.Revision.ToString(CultureInfo.InvariantCulture),
                        item.Title,
                        item.DestinationId,
                        item.DisplayName,
                        startDate = Date(item.StartDate),
                        endDate = Date(item.EndDate),
                        updatedAt = Timestamp(item.UpdatedAt),
                    })
                    .ToArray(),
                page.NextCursor,
            }
        );

    public ErrorOr<int> MeasurePageBytes(TripPlanPage page) => Measure(SerializePage(page));

    private static ErrorOr<int> Measure(ErrorOr<string> text) =>
        text.IsError ? text.Errors : Utf8.GetByteCount(text.Value);

    private static ErrorOr<string> Encode<T>(T value)
    {
        try
        {
            var result = JsonSerializer.Serialize(value, Json);
            Utf8.GetByteCount(result);
            return result;
        }
        catch (Exception ex)
            when (ex is JsonException or ArgumentException or EncoderFallbackException)
        {
            return TripErrors.InvalidInput;
        }
    }

    public ErrorOr<TripPlan> Deserialize(string plaintext, TripPlanRecord record)
    {
        try
        {
            if (Utf8.GetByteCount(plaintext) > MaximumBytes)
                return TripErrors.Unavailable;
            var dto = JsonSerializer.Deserialize<DetailDto>(plaintext, Json);
            if (
                dto is null
                || dto.TripId != record.TripId
                || dto.Revision != record.Revision.ToString(CultureInfo.InvariantCulture)
                || dto.CreatedAt != Timestamp(record.CreatedAt)
                || dto.UpdatedAt != Timestamp(record.UpdatedAt)
            )
                return TripErrors.Unavailable;
            var brief = new TripBriefInput(
                dto.Brief.DestinationId,
                dto.Brief.OriginId,
                ParseDate(dto.Brief.StartDate),
                ParseDate(dto.Brief.EndDate),
                dto.Brief.Adults,
                dto.Brief.Currency,
                dto.Brief.TotalBudget,
                dto.Brief.Interests,
                dto.Brief.Pace
            );
            var days = dto
                .Days.Select(day => new TripDayInput(
                    ParseDate(day.Date),
                    day.Blocks.Select(block => new TripBlockInput(
                            block.BlockId,
                            block.Title,
                            block.Note,
                            block.StartTime,
                            block.EndTime,
                            block.Estimate is null
                                ? null
                                : new PlanningMoneyInput(
                                    block.Estimate.Amount,
                                    block.Estimate.Currency
                                )
                        ))
                        .ToArray()
                ))
                .ToArray();
            var restored = TripPlan.Restore(
                record.TripId,
                record.Owner,
                record.Revision,
                dto.Title,
                brief,
                new DestinationSelection(
                    dto.Destination.DestinationId,
                    dto.Destination.DisplayName,
                    dto.Destination.TimeZone,
                    dto.Destination.CatalogueVersion
                ),
                days,
                record.CreatedAt,
                record.UpdatedAt
            );
            if (restored.IsError)
                return TripErrors.Unavailable;
            var canonical = Serialize(restored.Value);
            return canonical.IsError || canonical.Value != plaintext
                ? TripErrors.Unavailable
                : restored;
        }
        catch (Exception ex)
            when (ex
                    is JsonException
                        or ArgumentException
                        or FormatException
                        or InvalidOperationException
                        or NullReferenceException
                        or EncoderFallbackException
            )
        {
            return TripErrors.Unavailable;
        }
    }

    private static DateOnly ParseDate(string value) =>
        DateOnly.ParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture);
}
