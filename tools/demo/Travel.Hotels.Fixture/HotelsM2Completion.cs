namespace Travel.Hotels.Fixture;

// Cleanup and a failure receipt remain independent of all other evidence writes.
internal static class HotelsM2Completion
{
    internal static async Task<bool> FinishAsync(
        Func<Func<string, Func<Task>, Task>, Task> evidence,
        Func<Task> cleanup,
        Func<bool, IReadOnlyList<string>, Task> receipt,
        Func<bool> eligible
    )
    {
        var errors = new List<string>();
        async Task Attempt(string stage, Func<Task> action)
        {
            try
            {
                await action();
            }
            catch (Exception ex)
            {
                errors.Add(stage + ":" + ex.GetType().Name);
            }
        }
        try
        {
            await Attempt("Evidence", () => evidence(Attempt));
        }
        finally
        {
            await Attempt("Cleanup", cleanup);
        }
        var passed = errors.Count == 0 && eligible();
        await Attempt("Receipt", () => receipt(passed, errors.ToArray()));
        if (passed && (errors.Count != 0 || !eligible()))
        {
            passed = false;
            errors.Add("AcceptanceDeadlineOrReceiptFailure");
            await Attempt("FailureReceipt", () => receipt(false, errors.ToArray()));
        }
        return passed && errors.Count == 0 && eligible();
    }
}
