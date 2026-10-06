using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Travel.Modules.Flights.Core.Cancellation;

public static class CancellationTermsFingerprint
{
    internal static string Compute(CancellationTermsInput input)
    {
        using var bytes = new MemoryStream();
        using (var writer = new BinaryWriter(bytes, Encoding.UTF8, leaveOpen: true))
        {
            // BinaryWriter strings are UTF-8 length-prefixed; opaque IDs cannot escape fields.
            writer.Write("travel-cancellation-terms-v1");
            writer.Write(input.Revision.ToString(CultureInfo.InvariantCulture));
            writer.Write(input.AggregateId.ToString("N"));
            writer.Write(input.OwnerId.ToString("N"));
            writer.Write(input.OperationId.ToString("N"));
            writer.Write(input.ProviderOrderRef);
            writer.Write(input.ProviderCancellationRef);
            writer.Write(input.ItineraryPartyHash.ToLowerInvariant());
            writer.Write(input.Refund!.Amount.ToString("G29", CultureInfo.InvariantCulture));
            writer.Write(input.Refund.Currency.Value);
            writer.Write((int)input.Destination);
            writer.Write((int)input.Settlement.Composition);
            writer.Write(input.Settlement.CreditsKnownEmpty);
            writer.Write((int)input.Settlement.Provenance);
            writer.Write(input.Settlement.OriginalCashOnlyVerified);
            writer.Write(input.Settlement.UnpaidOrderVerified);
            writer.Write(input.ExpiresAt!.Value.UtcTicks);
            writer.Write(input.NoticeVersion);
        }
        return Convert.ToHexString(SHA256.HashData(bytes.ToArray())).ToLowerInvariant();
    }
}
