using Travel.Shared.Infrastructure.Initialization;
using Wolverine;
using Wolverine.Http;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

builder.Host.UseWolverine();

builder.Services.AddAppInitialization();  // hosted service that runs IInitializer impls at startup

var app = builder.Build();

app.MapDefaultEndpoints();
app.MapWolverineEndpoints();  // discovers endpoint methods via [WolverinePost]/[WolverineGet] attributes

await app.RunAsync();
