namespace Travel.AI.NlSearch;

public sealed record NlSearchOptions
{
    public const string ModelId = "claude-opus-4-7";
    public int MaxQueryCharacters { get; init; } = 1000;
    public int MaxInputTokens { get; init; } = 4096;
    public int MaxOutputTokens { get; init; } = 512;
    public int MaxResponseBytes { get; init; } = 16 * 1024;
    public int InferenceTimeoutMilliseconds { get; init; } = 3500;
    public int HandlerTimeoutMilliseconds { get; init; } = 5000;
    public int MaxConcurrentRequests { get; init; } = 1;
    public int RequestsPerMinute { get; init; } = 6;
    public decimal MaxCostUsd { get; init; } = 0.10m;
    public decimal HourlyBudgetUsd { get; init; } = 1m;

    public void Validate()
    {
        if (
            MaxQueryCharacters is < 1 or > 1000
            || MaxInputTokens is < 1 or > 4096
            || MaxOutputTokens is < 1 or > 512
            || MaxResponseBytes is < 1 or > 16 * 1024
            || InferenceTimeoutMilliseconds is < 1 or > 3500
            || HandlerTimeoutMilliseconds is < 1 or > 5000
            || InferenceTimeoutMilliseconds > HandlerTimeoutMilliseconds
            || MaxConcurrentRequests != 1
            || RequestsPerMinute is < 1 or > 6
            || MaxCostUsd is <= 0 or > 0.10m
            || HourlyBudgetUsd is <= 0 or > 1m
            || HourlyBudgetUsd < MaxCostUsd
        )
            throw new InvalidOperationException(
                "NL-search bounds must be positive and within the approved ceilings."
            );
    }

    public static decimal EstimateCost(int input, int output) =>
        (input * NlSearchPrompts.InputCostPer1M + output * NlSearchPrompts.OutputCostPer1M)
        / 1_000_000m;
}
