using Microsoft.EntityFrameworkCore;
using Travel.AI.Persistence;
using Wolverine;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

builder.AddNpgsqlDbContext<AiDbContext>(
    "travel",
    configureDbContextOptions: opts =>
    {
        opts.UseSnakeCaseNamingConvention();
    }
);

builder.Host.UseWolverine();

var app = builder.Build();
app.MapDefaultEndpoints();
app.MapGet("/", () => "Travel.AI service running");

await app.RunAsync();
