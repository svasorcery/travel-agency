using Travel.Modules.Identity.Infrastructure;
using Travel.Shared.Infrastructure.Initialization;
using Wolverine;
using Wolverine.Http;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

builder.Services.AddIdentityModule(builder.Configuration);

builder.Host.UseWolverine();

builder.Services.AddAppInitialization();  // hosted service that runs IInitializer impls at startup

var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();

app.MapDefaultEndpoints();
app.MapWolverineEndpoints();  // discovers endpoint methods via [WolverinePost]/[WolverineGet] attributes

await app.RunAsync();
