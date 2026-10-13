using Shouldly;
using Travel.Host.Commands;
using Xunit;

namespace Travel.Host.Tests.Integration.Support;

public sealed class SupportTextKeysCommandTests
{
    [Theory]
    [InlineData("")]
    [InlineData("initialize")]
    [InlineData("initialize --execute extra")]
    [InlineData("initialize --execute --password=private-marker")]
    public async Task Only_explicit_exact_key_initialization_arguments_are_accepted(string raw)
    {
        using var output = new StringWriter();
        var exit = await SupportTextKeysCommand.RunAsync(
            raw.Split(' ', StringSplitOptions.RemoveEmptyEntries),
            output
        );
        exit.ShouldBe(2);
        output.ToString().ShouldStartWith("InvalidArguments:");
        output.ToString().ShouldNotContain("private-marker");
    }
}
