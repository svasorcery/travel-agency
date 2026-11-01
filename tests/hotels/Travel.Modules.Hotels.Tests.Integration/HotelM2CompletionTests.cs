using Shouldly;
using Travel.Hotels.Fixture;

namespace Travel.Modules.Hotels.Tests.Integration;

public sealed class HotelM2CompletionTests
{
    [Theory]
    [InlineData("first-evidence")]
    [InlineData("final-facts")]
    [InlineData("cleanup")]
    [InlineData("receipt")]
    [InlineData("cancel-cleanup")]
    [InlineData("cancel-receipt")]
    public async Task Failure_or_late_cancellation_never_skips_cleanup_or_accepts(string fault)
    {
        var cleanupCalls = 0;
        var allowed = true;
        var attempts = new List<string>();
        var receipts = new List<bool>();
        var passed = await HotelsM2Completion.FinishAsync(
            async save =>
            {
                await save(
                    "first-evidence",
                    () =>
                    {
                        attempts.Add("first-evidence");
                        if (fault == "first-evidence")
                            throw new IOException();
                        return Task.CompletedTask;
                    }
                );
                await save(
                    "final-facts",
                    () =>
                    {
                        attempts.Add("final-facts");
                        if (fault == "final-facts")
                            throw new IOException();
                        return Task.CompletedTask;
                    }
                );
            },
            () =>
            {
                cleanupCalls++;
                if (fault == "cleanup")
                    throw new IOException();
                if (fault == "cancel-cleanup")
                    allowed = false;
                return Task.CompletedTask;
            },
            (value, errors) =>
            {
                receipts.Add(value);
                if (fault == "receipt")
                    throw new IOException();
                if (fault == "cancel-receipt")
                    allowed = false;
                return Task.CompletedTask;
            },
            () => allowed
        );
        cleanupCalls.ShouldBe(1);
        attempts.ShouldContain("final-facts");
        receipts.Count.ShouldBeGreaterThan(0);
        passed.ShouldBeFalse();
        if (fault != "receipt")
            receipts.Last().ShouldBeFalse();
    }
}
