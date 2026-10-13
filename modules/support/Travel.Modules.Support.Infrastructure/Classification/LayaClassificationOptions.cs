namespace Travel.Modules.Support.Infrastructure.Classification;

public sealed class LayaClassificationOptions
{
    public bool Enabled { get; set; }
    public string? BaseUrl { get; set; }
    public string? ApiKey { get; set; }
    public string? ServerSourceSha { get; set; }
    public string? ImageDigest { get; set; }
    public string? CheckpointRevision { get; set; }
    public string? ExpectedDevice { get; set; }
    public string? ExpectedPrecision { get; set; }
    public double MinAnswerConfidence { get; set; } = 0.75;
    public double MinEntropyConfidence { get; set; } = 0.60;
    public bool AllowCpuFallback { get; set; }
}
