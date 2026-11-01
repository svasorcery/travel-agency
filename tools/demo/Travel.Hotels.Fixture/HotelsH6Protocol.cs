using System.Text.Json;

namespace Travel.Hotels.Fixture;

internal sealed record HotelH6Handoff(HotelH6TraceReceipt Trace, string Fingerprint);

internal sealed class HotelsH6Protocol(
    HotelsH6Trace trace,
    Func<HotelH6TraceReceipt, Task<string>> persist,
    Action<string> release
)
{
    private int sequence;
    private bool failed,
        finished,
        released;
    private string? fingerprint;

    internal async Task<HotelH6Reply> ApplyAsync(HotelH6Message message, CancellationToken ct)
    {
        try
        {
            if (
                message.Sequence != ++sequence
                || released
                || message.Operation is not ("attach" or "A" or "B" or "finish" or "release")
                || (message.Operation == "attach") != (message.Target is not null)
                || (message.Operation == "release") != (message.Fingerprint is not null)
                || failed && message.Operation is not ("finish" or "release")
                || finished && message.Operation != "release"
            )
                throw new InvalidDataException("H6 invalid protocol transition.");
            JsonElement? data = null;
            switch (message.Operation)
            {
                case "attach":
                    await trace.AttachAsync(message.Target!, ct);
                    break;
                case "A":
                case "B":
                    await trace.SnapshotAsync(message.Operation, ct);
                    break;
                case "finish":
                    finished = true;
                    var receipt = await trace.FinishAsync(ct);
                    fingerprint = await persist(receipt);
                    data = JsonSerializer.SerializeToElement(
                        new HotelH6Handoff(receipt, fingerprint),
                        HotelsH6Wire.Json
                    );
                    break;
                case "release":
                    if (!finished || fingerprint is null || message.Fingerprint != fingerprint)
                        throw new InvalidDataException("H6 output acknowledgement mismatch.");
                    release(fingerprint);
                    released = true;
                    break;
            }
            return new(message.Sequence, true, data);
        }
        catch (Exception ex)
        {
            failed = true;
            return new(message.Sequence, false, Error: ex.GetType().Name);
        }
    }
}
