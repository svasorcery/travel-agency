using System.Reflection;

namespace Travel.AI.NlSearch;

/// <summary>
/// Provides prompt text and pricing constants for the NL-search feature.
/// </summary>
public static class NlSearchPrompts
{
    // Estimated standard-token cost, verified 2026-10-16 against
    // https://platform.claude.com/docs/en/about-claude/pricing . No cache/tariff modifiers or billing proof.
    public const decimal InputCostPer1M = 5m;
    public const decimal OutputCostPer1M = 25m;

    private static readonly Lazy<string> _nlSearchSystem = new(
        LoadNlSearchSystem,
        isThreadSafe: true
    );

    /// <summary>
    /// The system prompt used to extract structured flight-search criteria from free-form user input.
    /// Loaded once from the embedded resource on first access.
    /// </summary>
    public static string NlSearchSystem => _nlSearchSystem.Value;

    private static string LoadNlSearchSystem()
    {
        var assembly = Assembly.GetExecutingAssembly();
        using var stream =
            assembly.GetManifestResourceStream("prompts.v1.flights.nl-search.system.md")
            ?? throw new InvalidOperationException(
                "Embedded resource 'prompts.v1.flights.nl-search.system.md' not found. "
                    + "Ensure the file is included as an EmbeddedResource in Travel.AI.csproj."
            );
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
