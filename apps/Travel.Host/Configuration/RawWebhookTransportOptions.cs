namespace Travel.Host.Configuration;

public sealed class RawWebhookTransportOptions
{
    public int MaximumBytes { get; set; } = 2 * 1024 * 1024;

    public bool IsValid() => MaximumBytes is >= 256 and <= 2 * 1024 * 1024;
}
