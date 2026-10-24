using System.Text.Json;

namespace Travel.Trips.LocalStack;

public static class ToolProgram
{
    public static async Task<int> Main(string[] args)
    {
        var stage = "arguments";
        try
        {
            if (args.Length == 0 || (args.Length - 1) % 2 != 0)
                throw new LocalStackRefusal("Expected a command and named arguments.");
            var options = new Dictionary<string, string>(StringComparer.Ordinal);
            for (var index = 1; index < args.Length; index += 2)
                if (!options.TryAdd(args[index], args[index + 1]))
                    throw new LocalStackRefusal("Duplicate command option.");
            var expected = args[0] switch
            {
                "provision" => new[] { "--state", "--repo" },
                "serve-e2e" => ["--state", "--host"],
                "snapshot" => ["--state", "--trip-id"],
                "schema" or "probe" => ["--state"],
                "package-check" => ["--directory", "--version", "--manifest-sha", "--as-of"],
                _ => throw new LocalStackRefusal("Unknown local tool command."),
            };
            if (options.Count != expected.Length || expected.Any(key => !options.ContainsKey(key)))
                throw new LocalStackRefusal("Unexpected command options.");
            stage = args[0];
            if (args[0] == "package-check")
            {
                using var checkTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                Write(
                    await PackageVerification.CheckAsync(
                        options["--directory"],
                        options["--version"],
                        options["--manifest-sha"],
                        options["--as-of"],
                        checkTimeout.Token
                    )
                );
                return 0;
            }
            if (args[0] == "provision")
            {
                var local = LocalStackState.ReadLocal(options["--state"], options["--repo"]);
                LocalProvisioning.Provision(local, options["--repo"], DateTimeOffset.UtcNow);
                Write(
                    new
                    {
                        provisioned = true,
                        wallUtc = DateTimeOffset.UtcNow,
                        applicationAsOfUtc = local.ApplicationAsOfUtc,
                    }
                );
                return 0;
            }
            var state = LocalStackState.ReadContainer(options["--state"]);
            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
            switch (args[0])
            {
                case "schema":
                    await LocalDatabase.ApplySchemaAsync(state, timeout.Token);
                    Write(new { schemaApplied = true });
                    return 0;
                case "snapshot":
                    if (
                        !Guid.TryParseExact(options["--trip-id"], "D", out var id)
                        || id == Guid.Empty
                    )
                        throw new LocalStackRefusal("A nonempty trip id is required.");
                    Write(await LocalDatabase.CaptureAsync(state, id, timeout.Token));
                    return 0;
                case "probe":
                    return await ProbeAsync(state, timeout.Token);
                case "serve-e2e":
                    await E2eHostFactory.ServeAsync(
                        state,
                        options["--host"],
                        CancellationToken.None
                    );
                    return 0;
                default:
                    throw new LocalStackRefusal("Unknown local tool command.");
            }
        }
        catch (LocalStackRefusal refusal)
        {
            // Only fixed, tool-authored refusal messages are public. Never print nested exception details.
            Write(
                new
                {
                    ok = false,
                    stage,
                    errorClass = nameof(LocalStackRefusal),
                    error = refusal.Message,
                }
            );
            return 1;
        }
        catch (Exception exception)
        {
            // Provider/IO/configuration exceptions may contain connection strings, paths or private content.
            Write(
                new
                {
                    ok = false,
                    stage,
                    errorClass = exception.GetType().Name,
                    error = "Local tool operation failed; inspect the owned stack stage and readiness.",
                }
            );
            return 1;
        }
    }

    private static async Task<int> ProbeAsync(LocalStackState state, CancellationToken ct)
    {
        using var handler = new HttpClientHandler
        {
            AllowAutoRedirect = false,
            UseCookies = false,
            UseProxy = false,
        };
        using var client = new HttpClient(handler)
        {
            BaseAddress = new Uri("http://127.0.0.1:5098"),
            Timeout = TimeSpan.FromSeconds(10),
            MaxResponseContentBufferSize = 65536,
        };
        var checks = new Dictionary<string, int>();
        var dependencies = new Dictionary<string, string>();
        foreach (var check in new[] { "ready", "dependencies" })
        {
            using var response = await client.GetAsync("/health/" + check, ct);
            checks.Add(check, (int)response.StatusCode);
            // TCP checks belong to /ready; Trips belongs to /dependencies. Keep only bounded status facts from both.
            foreach (
                var fact in LocalHealth.ParseDependencies(
                    await response.Content.ReadAsStringAsync(ct)
                )
            )
                if (!dependencies.TryAdd(fact.Key, fact.Value))
                    throw new LocalStackRefusal(
                        "A health check appeared in both endpoint reports."
                    );
        }
        var ready =
            checks.Values.All(status => status == 200) && LocalHealth.RequiredHealthy(dependencies);
        Write(
            new
            {
                ready,
                wallUtc = DateTimeOffset.UtcNow,
                applicationAsOfUtc = state.ApplicationAsOfUtc,
                checks,
                dependencies,
            }
        );
        return ready ? 0 : 1;
    }

    private static void Write<T>(T value) =>
        Console.WriteLine(JsonSerializer.Serialize(value, LocalStackJson.Options));
}
