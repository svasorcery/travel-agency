using Microsoft.EntityFrameworkCore;
using Travel.Host.Persistence;
using Travel.Modules.Identity.Infrastructure;
using Travel.Shared.Infrastructure.Initialization;
using Wolverine;
using Wolverine.Http;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

builder.AddNpgsqlDbContext<HostDbContext>("travel", configureDbContextOptions: opts =>
{
    opts.UseSnakeCaseNamingConvention(); // PostgreSQL convention via EFCore.NamingConventions package
});

builder.Services.AddIdentityModule(builder.Configuration);

builder.Host.UseWolverine();

builder.Services.AddAppInitialization();  // hosted service that runs IInitializer impls at startup

var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();

app.MapDefaultEndpoints();
app.MapWolverineEndpoints();  // discovers endpoint methods via [WolverinePost]/[WolverineGet] attributes

await app.RunAsync();
