using System.Buffers;
using System.Text;
using ErrorOr;

namespace Travel.Modules.Support.Core;

public sealed record CaseId
{
    public Guid Value { get; }

    private CaseId(Guid value) => Value = value;

    public static ErrorOr<CaseId> Create(Guid value) =>
        value == Guid.Empty ? SupportErrors.InvalidInput : new CaseId(value);
}

public sealed record MessageId
{
    public Guid Value { get; }

    private MessageId(Guid value) => Value = value;

    public static ErrorOr<MessageId> Create(Guid value) =>
        value == Guid.Empty ? SupportErrors.InvalidInput : new MessageId(value);
}

public sealed record SupportRevision
{
    public int Value { get; }

    private SupportRevision(int value) => Value = value;

    public static ErrorOr<SupportRevision> Create(int value) =>
        value <= 0 ? SupportErrors.InvalidInput : new SupportRevision(value);
}

public sealed record SupportTitle
{
    public string Value { get; }

    private SupportTitle(string value) => Value = value;

    public static ErrorOr<SupportTitle> Create(string? value) =>
        SupportTextValidation.IsValid(value, 120, 16 * 1024)
            ? new SupportTitle(value!)
            : SupportErrors.InvalidInput;

    public override string ToString() => nameof(SupportTitle);
}

public sealed record SupportBody
{
    public string Value { get; }

    private SupportBody(string value) => Value = value;

    public static ErrorOr<SupportBody> Create(string? value) =>
        SupportTextValidation.IsValid(value, 4000, 16 * 1024)
            ? new SupportBody(value!)
            : SupportErrors.InvalidInput;

    public override string ToString() => nameof(SupportBody);
}

internal static class SupportTextValidation
{
    public static bool IsValid(string? value, int scalarLimit, int byteLimit)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > scalarLimit * 2)
            return false;
        var remaining = value.AsSpan();
        var scalars = 0;
        var bytes = 0;
        while (!remaining.IsEmpty)
        {
            if (
                Rune.DecodeFromUtf16(remaining, out var rune, out var consumed)
                != OperationStatus.Done
            )
                return false;
            scalars++;
            bytes += rune.Utf8SequenceLength;
            if (scalars > scalarLimit || bytes > byteLimit)
                return false;
            remaining = remaining[consumed..];
        }
        return true;
    }
}
