using Shouldly;
using Travel.Modules.Support.Core;

namespace Travel.Modules.Support.Tests.Unit;

public sealed class SupportValidationTests
{
    [Theory]
    [InlineData("")]
    [InlineData(" \r\n\t")]
    public void Invalid_title_is_rejected_without_echo(string text)
    {
        var result = SupportTitle.Create(text);
        result.IsError.ShouldBeTrue();
        result.FirstError.Code.ShouldBe("Support.InvalidInput");
        result.FirstError.Description.ShouldBe("The support request is invalid.");
    }

    [Fact]
    public void Title_counts_unicode_scalars_and_preserves_exact_text()
    {
        var text = string.Concat(Enumerable.Repeat("😀", 120));
        SupportTitle.Create(text).Value.Value.ShouldBe(text);
        SupportTitle.Create(text + "x").IsError.ShouldBeTrue();
    }

    [Fact]
    public void Malformed_utf16_is_rejected_before_encoding()
    {
        var malformed = new string((char)0xd800, 1);
        SupportTitle.Create(malformed).FirstError.Code.ShouldBe("Support.InvalidInput");
        SupportBody.Create(malformed).FirstError.Code.ShouldBe("Support.InvalidInput");
    }

    [Fact]
    public void Body_rejects_scalar_overflow_and_preserves_markup_as_text()
    {
        SupportBody.Create(new string('x', 4001)).IsError.ShouldBeTrue();
        SupportBody
            .Create(" <script>doNothing()</script> ")
            .Value.Value.ShouldBe(" <script>doNothing()</script> ");
        SupportBody.Create(string.Concat(Enumerable.Repeat("😀", 4000))).IsError.ShouldBeFalse();
    }

    [Fact]
    public void Empty_ids_and_nonpositive_revisions_are_rejected()
    {
        CaseId.Create(Guid.Empty).IsError.ShouldBeTrue();
        MessageId.Create(Guid.Empty).IsError.ShouldBeTrue();
        SupportRevision.Create(0).IsError.ShouldBeTrue();
        SupportRevision.Create(-1).IsError.ShouldBeTrue();
        SupportRevision.Create(1).Value.Value.ShouldBe(1);
    }
}
