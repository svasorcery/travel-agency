using System.Diagnostics.CodeAnalysis;
using JasperFx.CodeGeneration;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Travel.Host.CodeGeneration;

public static class RuntimeCompilationServices
{
    [RequiresDynamicCode("Registers runtime Roslyn compilation.")]
    [RequiresUnreferencedCode("Registers runtime-generated type loading.")]
    public static IServiceCollection AddScopedMetadataCompilation(this IServiceCollection services)
    {
        services.RemoveAll<IAssemblyGenerator>();
        services.AddSingleton<IAssemblyGenerator, ScopedMetadataAssemblyGenerator>();
        return services;
    }
}
