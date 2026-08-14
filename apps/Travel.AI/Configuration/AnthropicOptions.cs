using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Travel.AI.Configuration;

public sealed class AnthropicOptions
{
    public const string SectionName = "Anthropic";

    public string ApiKey { get; set; } = string.Empty;
}

internal sealed class AnthropicOptionsValidator(IHostEnvironment environment)
    : IValidateOptions<AnthropicOptions>
{
    public ValidateOptionsResult Validate(string? name, AnthropicOptions options) =>
        environment.IsProduction() && string.IsNullOrWhiteSpace(options.ApiKey)
            ? ValidateOptionsResult.Fail("Production Anthropic API key is required.")
            : ValidateOptionsResult.Success;
}
