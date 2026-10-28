namespace Travel.Trips.Evaluation;

public static class ToolProgram
{
    public static async Task<int> Main(string[] args)
    {
        using var stop = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            stop.Cancel();
        };
        if (args.Length == 1 && args[0].StartsWith("__worker-", StringComparison.Ordinal))
            return await TripsBriefEnvelopeWorker.RunAsync(args[0], stop.Token);
        if (args.Length == 1 && args[0] == "prepare-source")
        {
            try
            {
                var prepared = await TripsBriefEvaluationEnvelope.PrepareAsync(
                    Directory.GetCurrentDirectory(),
                    stop.Token
                );
                Console.WriteLine(
                    System.Text.Json.JsonSerializer.Serialize(
                        new
                        {
                            prepared.ImageId,
                            prepared.WorkerClosure,
                            prepared.SourceKey,
                            ReadyForExecution = false,
                            ProviderCalls = 0,
                        }
                    )
                );
                return 0;
            }
            catch (Exception e)
                when (e is InvalidOperationException or IOException or OperationCanceledException)
            {
                Console.WriteLine(
                    "{\"ReadyForExecution\":false,\"ProviderCalls\":0,\"Reason\":\"SourcePreparationFailed\"}"
                );
                return 2;
            }
        }
        await using var environment = new TripsBriefEvaluationEnvironment();
        return await TripsBriefEvaluationCommand.RunAsync(
            args,
            environment,
            Console.Out,
            stop.Token
        );
    }
}
