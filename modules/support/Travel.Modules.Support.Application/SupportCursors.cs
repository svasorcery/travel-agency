using System.Text.Json;
using System.Text.Json.Serialization;
using ErrorOr;
using Travel.Modules.Support.Core;

namespace Travel.Modules.Support.Application;

internal static class SupportCursors
{
    private sealed record CaseCursor(
        int Version,
        SupportCaseFilters Filters,
        SupportCasePosition Position
    );

    private sealed record MessageCursor(int Version, Guid CaseId, SupportMessagePosition Position);

    private static readonly JsonSerializerOptions Options = new()
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    public static bool ValidFilters(SupportCaseFilters filters) =>
        (filters.State is null || Enum.IsDefined(filters.State.Value))
        && (filters.Category is null || Enum.IsDefined(filters.Category.Value))
        && (filters.Priority is null || Enum.IsDefined(filters.Priority.Value));

    public static bool Matches(SupportCase row, SupportCaseFilters filters) =>
        (filters.State is null || row.State == filters.State)
        && (filters.Category is null || row.Category == filters.Category)
        && (filters.Priority is null || row.Priority == filters.Priority);

    public static bool After(SupportCase row, SupportCasePosition? position) =>
        position is null
        || row.CreatedAt > position.CreatedAt
        || row.CreatedAt == position.CreatedAt && row.CaseId.CompareTo(position.CaseId) > 0;

    public static bool After(SupportMessage row, SupportMessagePosition? position) =>
        position is null
        || row.WrittenAtRevision > position.WrittenAtRevision
        || row.WrittenAtRevision == position.WrittenAtRevision
            && row.MessageId.CompareTo(position.MessageId) > 0;

    public static ErrorOr<SupportCasePosition?> ReadCase(string? cursor, SupportCaseFilters filters)
    {
        if (cursor is null)
            return (SupportCasePosition?)null;
        var decoded = Decode<CaseCursor>(cursor);
        if (
            decoded is null
            || decoded.Version != 1
            || decoded.Filters != filters
            || decoded.Position is null
            || decoded.Position.CaseId == Guid.Empty
            || decoded.Position.CreatedAt.Offset != TimeSpan.Zero
        )
            return SupportErrors.InvalidInput;
        return decoded.Position;
    }

    public static ErrorOr<SupportMessagePosition?> ReadMessage(string? cursor, Guid caseId)
    {
        if (cursor is null)
            return (SupportMessagePosition?)null;
        var decoded = Decode<MessageCursor>(cursor);
        if (
            decoded is null
            || decoded.Version != 1
            || decoded.CaseId != caseId
            || decoded.Position is null
            || decoded.Position.WrittenAtRevision <= 0
            || decoded.Position.MessageId == Guid.Empty
        )
            return SupportErrors.InvalidInput;
        return decoded.Position;
    }

    public static string WriteCase(SupportCaseFilters filters, SupportCasePosition position) =>
        Encode(new CaseCursor(1, filters, position));

    public static string WriteMessage(Guid caseId, SupportMessagePosition position) =>
        Encode(new MessageCursor(1, caseId, position));

    private static string Encode<T>(T value) =>
        Convert
            .ToBase64String(JsonSerializer.SerializeToUtf8Bytes(value, Options))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

    private static T? Decode<T>(string cursor)
        where T : class
    {
        if (
            cursor.Length is 0 or > 1024
            || cursor.Any(character =>
                !char.IsAsciiLetterOrDigit(character) && character is not ('-' or '_')
            )
        )
            return null;
        try
        {
            var base64 = cursor.Replace('-', '+').Replace('_', '/');
            var bytes = Convert.FromBase64String(base64.PadRight((base64.Length + 3) / 4 * 4, '='));
            using var document = JsonDocument.Parse(bytes);
            return UniqueProperties(document.RootElement)
                ? JsonSerializer.Deserialize<T>(bytes, Options)
                : null;
        }
        catch (Exception exception)
            when (exception is FormatException or JsonException or NotSupportedException)
        {
            return null;
        }
    }

    private static bool UniqueProperties(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object)
            return false;
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in element.EnumerateObject())
            if (
                !names.Add(property.Name)
                || property.Value.ValueKind == JsonValueKind.Object
                    && !UniqueProperties(property.Value)
            )
                return false;
        return true;
    }
}
