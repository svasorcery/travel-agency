using Travel.Shared.Infrastructure.Telemetry;

namespace Travel.ServiceDefaults.Telemetry;

internal sealed class HttpUrlRedactor(IEnumerable<IHttpUrlRedactionContributor> contributors)
{
    private readonly HashSet<string> _sensitiveQueryParameterNames = contributors
        .SelectMany(contributor => contributor.SensitiveQueryParameterNames)
        .Where(name => !string.IsNullOrWhiteSpace(name))
        .Select(name => name.Trim())
        .ToHashSet(StringComparer.OrdinalIgnoreCase);

    public string Redact(string url)
    {
        if (_sensitiveQueryParameterNames.Count == 0)
            return url;

        var queryStart = url.IndexOf('?');
        if (queryStart < 0)
            return url;

        var fragmentStart = url.IndexOf('#', queryStart + 1);
        var queryEnd = fragmentStart < 0 ? url.Length : fragmentStart;
        var query = url[(queryStart + 1)..queryEnd];
        var parameters = query.Split('&', StringSplitOptions.None);
        var changed = false;

        for (var index = 0; index < parameters.Length; index++)
        {
            var separator = parameters[index].IndexOf('=');
            var encodedName = separator < 0 ? parameters[index] : parameters[index][..separator];
            var name = Uri.UnescapeDataString(encodedName.Replace('+', ' '));
            if (!_sensitiveQueryParameterNames.Contains(name))
                continue;

            parameters[index] = $"{encodedName}=REDACTED";
            changed = true;
        }

        if (!changed)
            return url;

        return string.Concat(
            url.AsSpan(0, queryStart + 1),
            string.Join('&', parameters),
            url.AsSpan(queryEnd)
        );
    }
}
