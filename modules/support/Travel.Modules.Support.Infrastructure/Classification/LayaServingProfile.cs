using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using Travel.Modules.Support.Application;

namespace Travel.Modules.Support.Infrastructure.Classification;

// Immutable per-client configuration receipt. Never infer these pins from a provider response.
internal sealed class LayaServingProfile
{
    internal const string SourceSha = "c73908d96ff73c0062c340a11bf7c54ec7d8bc4d";
    internal const string BundleRevision = "55cf4c4ebb4ebe31b2550e8bdf3bd21b99753851";
    internal Uri Origin { get; }
    internal string ApiKey { get; }
    internal string ExpectedDevice { get; }
    internal double MinAnswerConfidence { get; }
    internal double MinEntropyConfidence { get; }
    internal bool AllowCpuFallback { get; }
    internal string Id { get; }

    private LayaServingProfile(Uri origin, LayaClassificationOptions options)
    {
        Origin = origin;
        ApiKey = options.ApiKey!;
        ExpectedDevice = options.ExpectedDevice!;
        MinAnswerConfidence = options.MinAnswerConfidence;
        MinEntropyConfidence = options.MinEntropyConfidence;
        AllowCpuFallback = options.AllowCpuFallback;
        // Order and numbers are deterministic under System.Text.Json; credentials and input are excluded.
        var canonical = JsonSerializer.SerializeToUtf8Bytes(
            new
            {
                protocol = "laya-full-v1",
                release = "0.4.6",
                serverSourceSha = SourceSha,
                imageDigest = options.ImageDigest!.ToLowerInvariant(),
                origin = origin.GetLeftPart(UriPartial.Authority),
                checkpointRevision = BundleRevision,
                model = "multilingual",
                repo = "convaiinnovations/laya/multilingual",
                questionVersion = SupportClassificationInput.QuestionVersion,
                inputVersion = "support-category-v1",
                instructions = LayaProtocol.Instructions,
                criteria = LayaProtocol.Criteria,
                max_len = 512,
                head_max_len = 512,
                stateScalars = 8000,
                stateBytes = 24576,
                requestBytes = 32768,
                responseBytes = 65536,
                minAnswerConfidence = MinAnswerConfidence,
                minEntropyConfidence = MinEntropyConfidence,
                device = ExpectedDevice,
                precision = options.ExpectedPrecision,
                allowCpuFallback = AllowCpuFallback,
            }
        );
        Id = Convert.ToHexStringLower(SHA256.HashData(canonical));
    }

    internal static LayaServingProfile? Create(LayaClassificationOptions options)
    {
        if (
            !options.Enabled
            || options.ServerSourceSha != SourceSha
            || options.CheckpointRevision != BundleRevision
            || options.ImageDigest is not { Length: 71 } image
            || !image.StartsWith("sha256:", StringComparison.Ordinal)
            || !image.AsSpan(7).ToString().All(Uri.IsHexDigit)
            || options.ApiKey is not { Length: > 0 and <= 4096 } key
            || key.Any(c => c > 126 || c < 33 || char.IsWhiteSpace(c))
            || options.ExpectedPrecision is not ("fp32" or "fp16" or "bf16")
            || !ValidDevice(options.ExpectedDevice)
            || !ValidConfidence(options.MinAnswerConfidence)
            || !ValidConfidence(options.MinEntropyConfidence)
            || options.BaseUrl is not { Length: > 0 and <= 2048 } baseUrl
            || baseUrl.Any(c => char.IsWhiteSpace(c) || char.IsControl(c))
            || baseUrl.Contains('\\')
            || baseUrl.Contains('?')
            || baseUrl.Contains('#')
            || !Uri.TryCreate(baseUrl, UriKind.Absolute, out var origin)
            || origin.Scheme != Uri.UriSchemeHttps
            || string.IsNullOrEmpty(origin.Host)
            || origin.UserInfo.Length != 0
            || origin.AbsolutePath != "/"
            || origin.Query.Length != 0
            || origin.Fragment.Length != 0
            || baseUrl.IndexOf('/', 8) is var pathStart
                && pathStart >= 0
                && pathStart != baseUrl.Length - 1
        )
            return null;
        return new(origin, options);
    }

    private static bool ValidConfidence(double value) =>
        double.IsFinite(value) && value > 0 && value <= 1;

    private static bool ValidDevice(string? device) =>
        device == "cpu"
        || device is not null
            && device.StartsWith("cuda:", StringComparison.Ordinal)
            && int.TryParse(
                device.AsSpan(5),
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var index
            )
            && index >= 0
            && device[5..] == index.ToString(CultureInfo.InvariantCulture);
}
