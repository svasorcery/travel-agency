using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Travel.Modules.Rail.Application;

namespace Travel.Modules.Rail.Infrastructure;

public static class YandexRailRouteLocatorParser
{
    public static RailLocatorFact Parse(string key, string uid, string? value, bool missing = false)
    {
        if (missing)
            return new(key, "missing", null);
        if (
            value is null
            || value.Length == 0
            || Encoding.UTF8.GetByteCount(value) > 2048
            || value.Any(c => char.IsWhiteSpace(c) || char.IsControl(c))
            || value.Contains('\\')
            || value.Contains('#')
            || value.Contains('+')
        )
            return new(key, "invalid", null);
        var match = Regex.Match(
            value,
            @"^(?:https://)?(api\.rasp\.yandex-net\.ru|api\.rasp\.yandex\.net)/v3\.0/thread/\?([^#]+)$",
            RegexOptions.CultureInvariant
        );
        if (!match.Success)
            return new(key, "invalid", null);
        var fields = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var pair in match.Groups[2].Value.Split('&'))
        {
            var parts = pair.Split('=', 2);
            if (
                parts.Length != 2
                || Regex.IsMatch(pair, "%(?![0-9A-Fa-f]{2})", RegexOptions.CultureInvariant)
            )
                return new(key, "invalid", null);
            var name = Uri.UnescapeDataString(parts[0]);
            var decoded = Uri.UnescapeDataString(parts[1]);
            if (name is not ("uid" or "date") || !fields.TryAdd(name, decoded))
                return new(key, "invalid", null);
        }
        return
            fields.Count == 2
            && fields.GetValueOrDefault("uid") == uid
            && DateOnly.TryParseExact(
                fields.GetValueOrDefault("date"),
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out _
            )
            ? new(key, "reported", new("yandex-rasp", uid, fields["date"]))
            : new(key, "invalid", null);
    }
}
