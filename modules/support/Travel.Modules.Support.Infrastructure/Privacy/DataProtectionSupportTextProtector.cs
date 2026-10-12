using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ErrorOr;
using Travel.Modules.Support.Application;
using Travel.Modules.Support.Core;

namespace Travel.Modules.Support.Infrastructure.Privacy;

public sealed class DataProtectionSupportTextProtector(SupportTextProtectionProvider provider)
    : ISupportTextProtector
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private const int MaxEnvelopeLength = 32768;

    private sealed record Envelope(int Format, string Ciphertext);

    private static bool ValidBinding(SupportTextBinding binding) =>
        binding.OwnerId != Guid.Empty
        && binding.CaseId != Guid.Empty
        && binding.MessageId != Guid.Empty
        && binding.WrittenAtRevision > 0;

    private static string[] Purpose(SupportTextBinding binding) =>
        [
            "support-text.v1",
            binding.OwnerId.ToString("N"),
            binding.CaseId.ToString("N"),
            binding.MessageId?.ToString("N") ?? "title",
            binding.WrittenAtRevision.ToString(System.Globalization.CultureInfo.InvariantCulture),
        ];

    public Task<ErrorOr<string>> ProtectAsync(
        string plaintext,
        SupportTextBinding binding,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!ValidBinding(binding) || string.IsNullOrWhiteSpace(plaintext))
            return Task.FromResult<ErrorOr<string>>(SupportErrors.InvalidInput);
        byte[] bytes;
        try
        {
            bytes = StrictUtf8.GetBytes(plaintext);
        }
        catch (EncoderFallbackException)
        {
            return Task.FromResult<ErrorOr<string>>(SupportErrors.InvalidInput);
        }
        try
        {
            if (bytes.Length > 16384)
                return Task.FromResult<ErrorOr<string>>(SupportErrors.InvalidInput);
            var encrypted = provider.Protect(bytes, Purpose(binding));
            return Task.FromResult<ErrorOr<string>>(
                encrypted.IsError
                    ? SupportErrors.Unavailable
                    : JsonSerializer.Serialize(
                        new Envelope(1, Convert.ToBase64String(encrypted.Value))
                    )
            );
        }
        finally
        {
            CryptographicOperations.ZeroMemory(bytes);
        }
    }

    public Task<ErrorOr<string>> UnprotectAsync(
        string envelope,
        SupportTextBinding binding,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (
            !ValidBinding(binding)
            || string.IsNullOrEmpty(envelope)
            || envelope.Length > MaxEnvelopeLength
        )
            return Task.FromResult<ErrorOr<string>>(SupportErrors.Unavailable);
        try
        {
            var parsed = JsonSerializer.Deserialize<Envelope>(envelope);
            if (parsed is null || parsed.Format != 1 || string.IsNullOrEmpty(parsed.Ciphertext))
                return Task.FromResult<ErrorOr<string>>(SupportErrors.Unavailable);
            var decrypted = provider.Unprotect(
                Convert.FromBase64String(parsed.Ciphertext),
                Purpose(binding)
            );
            if (decrypted.IsError)
                return Task.FromResult<ErrorOr<string>>(SupportErrors.Unavailable);
            try
            {
                return Task.FromResult<ErrorOr<string>>(StrictUtf8.GetString(decrypted.Value));
            }
            finally
            {
                CryptographicOperations.ZeroMemory(decrypted.Value);
            }
        }
        catch (Exception exception)
            when (exception is JsonException or FormatException or ArgumentException)
        {
            return Task.FromResult<ErrorOr<string>>(SupportErrors.Unavailable);
        }
    }
}
