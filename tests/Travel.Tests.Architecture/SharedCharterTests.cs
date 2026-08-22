using ArchUnitNET.Domain;
using ArchUnitNET.Fluent;
using ArchUnitNET.Loader;
using ArchUnitNET.xUnitV3;
using Shouldly;
using Travel.Tests.Architecture.Support;
using Xunit;
using static ArchUnitNET.Fluent.ArchRuleDefinition;
using ReflectionAssembly = System.Reflection.Assembly;

namespace Travel.Tests.Architecture;

[Trait("Category", "Architecture")]
public sealed class SharedCharterTests
{
    private static readonly global::ArchUnitNET.Domain.Architecture Arch =
        ArchitectureTestBase.Architecture;
    private static readonly string RepositoryRoot = FindRepositoryRoot();
    private static readonly string[] Configurations = ["Debug", "Release"];
    private static readonly string[] SharedProjects =
    [
        "shared/dotnet/Travel.Shared.Abstractions/Travel.Shared.Abstractions.csproj",
        "shared/dotnet/Travel.Shared.Domain/Travel.Shared.Domain.csproj",
        "shared/dotnet/Travel.Shared.Infrastructure/Travel.Shared.Infrastructure.csproj",
        "shared/dotnet/Travel.Shared.TestInfrastructure/Travel.Shared.TestInfrastructure.csproj",
        "shared/dotnet/Travel.Shared.Web/Travel.Shared.Web.csproj",
    ];
    private static readonly string[] SharedAssemblies =
    [
        "Travel.Shared.Abstractions",
        "Travel.Shared.Domain",
        "Travel.Shared.Infrastructure",
        "Travel.Shared.TestInfrastructure",
        "Travel.Shared.Web",
    ];
    private static readonly string[] ForbiddenSharedWebCompositionTypeNames =
    [
        "Microsoft.AspNetCore.Builder.WebApplication",
        "Microsoft.AspNetCore.Builder.WebApplicationBuilder",
        "Microsoft.AspNetCore.Builder.IApplicationBuilder",
        "Microsoft.AspNetCore.Routing.IEndpointRouteBuilder",
        "Microsoft.AspNetCore.Routing.EndpointDataSource",
        "Microsoft.Extensions.DependencyInjection.IServiceCollection",
        "Microsoft.Extensions.Hosting.IHost",
        "Microsoft.Extensions.Hosting.IHostBuilder",
        "Microsoft.Extensions.Hosting.IHostApplicationBuilder",
        "Microsoft.Extensions.Hosting.IHostEnvironment",
        "Microsoft.AspNetCore.Hosting.IWebHostBuilder",
        "Microsoft.AspNetCore.Hosting.IWebHostEnvironment",
        "Microsoft.AspNetCore.Hosting.IStartupFilter",
        "Travel.ServiceDefaults.Hosting.TestOnlyGuard",
        "Travel.ServiceDefaults.Web.PlatformExceptionHandler",
    ];

    public static TheoryData<string> ForbiddenSharedWebCompositionTypes
    {
        get
        {
            var data = new TheoryData<string>();
            foreach (var typeName in ForbiddenSharedWebCompositionTypeNames)
                data.Add(typeName);
            return data;
        }
    }

    [Fact]
    public async Task Shared_projects_do_not_reference_modules_in_Debug_or_Release()
    {
        SharedProjects.ShouldNotBeEmpty();

        foreach (var relativeProjectPath in SharedProjects)
        {
            var projectPath = Project(relativeProjectPath);
            File.Exists(projectPath).ShouldBeTrue($"Shared project must exist: {projectPath}");

            foreach (var configuration in Configurations)
            {
                var references = await EvaluatedProjectReferences.ForProjectAsync(
                    projectPath,
                    configuration
                );

                references
                    .Any(IsModuleProject)
                    .ShouldBeFalse(
                        $"{relativeProjectPath} must not reference a module in {configuration}"
                    );
            }
        }
    }

    [Theory]
    [InlineData("Debug")]
    [InlineData("Release")]
    public async Task Project_reference_reader_detects_imported_unused_module_bypass(
        string configuration
    )
    {
        var fixture = Project(
            "tests/Travel.Tests.Architecture/Fixtures/SharedCharter/ImportedUnusedModuleProjectReference.proj"
        );

        var projectReferences = await EvaluatedProjectReferences.ForProjectAsync(
            fixture,
            configuration
        );

        projectReferences.Any(IsModuleProject).ShouldBeTrue();
    }

    [Fact]
    public async Task Shared_projects_evaluate_FrameworkReference_in_Debug_and_Release()
    {
        foreach (var relativeProjectPath in SharedProjects)
        {
            var projectPath = Project(relativeProjectPath);
            foreach (var configuration in Configurations)
            {
                var frameworkReferences = await EvaluatedProjectReferences.ForItemIdentitiesAsync(
                    projectPath,
                    configuration,
                    "FrameworkReference"
                );

                if (
                    relativeProjectPath.Contains(
                        "Travel.Shared.Infrastructure/",
                        StringComparison.Ordinal
                    )
                )
                    frameworkReferences.ShouldNotContain("Microsoft.AspNetCore.App");

                if (relativeProjectPath.Contains("Travel.Shared.Web/", StringComparison.Ordinal))
                    frameworkReferences.ShouldContain("Microsoft.AspNetCore.App");
            }
        }
    }

    [Theory]
    [InlineData("Debug")]
    [InlineData("Release")]
    public async Task Framework_reference_reader_detects_imported_unused_AspNetCore_bypass(
        string configuration
    )
    {
        var fixture = Project(
            "tests/Travel.Tests.Architecture/Fixtures/SharedCharter/ImportedUnusedAspNetCoreFrameworkReference.proj"
        );

        var frameworkReferences = await EvaluatedProjectReferences.ForItemIdentitiesAsync(
            fixture,
            configuration,
            "FrameworkReference"
        );

        frameworkReferences.ShouldContain("Microsoft.AspNetCore.App");
    }

    [Theory]
    [InlineData("{")]
    [InlineData("""{}""")]
    [InlineData("""{"Items":{}}""")]
    [InlineData("""{"Items":{"FrameworkReference":{}}}""")]
    [InlineData("""{"Items":{"FrameworkReference":[{}]}}""")]
    [InlineData("""{"Items":{"FrameworkReference":[{"Identity":" "}]}}""")]
    public void Evaluated_item_identity_parser_fails_closed_on_malformed_or_missing_JSON(
        string json
    )
    {
        Should.Throw<InvalidOperationException>(() =>
            EvaluatedProjectReferences.ParseItemIdentitiesForTesting(
                json,
                "fixture.proj",
                "Debug",
                "FrameworkReference"
            )
        );
    }

    [Fact]
    public void Populated_Shared_assembly_selectors_are_non_empty_and_have_no_module_IL_dependencies()
    {
        SharedAssemblies.ShouldNotBeEmpty();

        foreach (var assemblyName in SharedAssemblies)
        {
            var assembly = LoadAssembly(assemblyName);
            assembly.GetTypes().ShouldNotBeEmpty($"{assemblyName} must remain populated");

            var sharedClasses = Classes().That().ResideInAssembly(assembly);
            sharedClasses.Should().Exist().Check(Arch);
            sharedClasses
                .Should()
                .NotDependOnAnyTypesThat()
                .ResideInNamespaceMatching(@"^Travel\.Modules(?:\.|$).*")
                .Check(Arch);

            assembly
                .GetReferencedAssemblies()
                .Select(reference => reference.Name ?? string.Empty)
                .ShouldNotContain(
                    reference => reference.StartsWith("Travel.Modules.", StringComparison.Ordinal),
                    $"{assemblyName} must have no module assembly reference"
                );
        }
    }

    [Fact]
    public void Shared_Infrastructure_has_no_AspNetCore_framework_or_type_dependency()
    {
        var assembly = LoadAssembly("Travel.Shared.Infrastructure");
        var infrastructureClasses = Classes().That().ResideInAssembly(assembly);

        infrastructureClasses.Should().Exist().Check(Arch);
        infrastructureClasses
            .Should()
            .NotDependOnAnyTypesThat()
            .ResideInNamespaceMatching(@"^Microsoft\.AspNetCore(?:\.|$).*")
            .Check(Arch);
        assembly
            .GetReferencedAssemblies()
            .Select(reference => reference.Name ?? string.Empty)
            .ShouldNotContain(
                reference => reference.StartsWith("Microsoft.AspNetCore", StringComparison.Ordinal),
                "Shared.Infrastructure must not carry an ASP.NET Core framework assembly"
            );
    }

    [Fact]
    public void Module_type_selector_rejects_controlled_Shared_IL_bypass()
    {
        var mutationArchitecture = LoadSharedWebMutationArchitecture();
        var fixtureClasses = Classes()
            .That()
            .HaveFullName(typeof(SharedModuleDependencyBypassFixture).FullName!);
        var moduleClasses = Classes()
            .That()
            .ResideInNamespaceMatching(@"^Travel\.Modules(?:\.|$).*");

        fixtureClasses.Should().Exist().Check(mutationArchitecture);
        moduleClasses.Should().Exist().Check(mutationArchitecture);
        Should.Throw<FailedArchRuleException>(() =>
            fixtureClasses.Should().NotDependOnAny(moduleClasses).Check(mutationArchitecture)
        );
    }

    [Theory]
    [MemberData(nameof(ForbiddenSharedWebCompositionTypes))]
    public void Explicit_forbidden_type_selector_rejects_controlled_Shared_Web_bypass(
        string forbiddenTypeName
    )
    {
        var mutationArchitecture = LoadSharedWebMutationArchitecture();
        var fixtureClasses = Classes()
            .That()
            .HaveFullName(typeof(SharedWebCharterBypassFixture).FullName!);
        var forbiddenTypes = Types().That().HaveFullName(forbiddenTypeName);

        fixtureClasses.Should().Exist().Check(mutationArchitecture);
        forbiddenTypes.Should().Exist().Check(mutationArchitecture);
        Should.Throw<FailedArchRuleException>(() =>
            fixtureClasses.Should().NotDependOnAny(forbiddenTypes).Check(mutationArchitecture)
        );
    }

    [Fact]
    public void ServiceDefaults_assembly_selector_rejects_controlled_Shared_Web_bypass()
    {
        var mutationArchitecture = LoadSharedWebMutationArchitecture();
        var fixtureClasses = Classes()
            .That()
            .HaveFullName(typeof(SharedWebCharterBypassFixture).FullName!);
        var serviceDefaultsTypes = Types()
            .That()
            .ResideInAssembly(typeof(Travel.ServiceDefaults.Hosting.TestOnlyGuard).Assembly);

        fixtureClasses.Should().Exist().Check(mutationArchitecture);
        serviceDefaultsTypes.Should().Exist().Check(mutationArchitecture);
        Should.Throw<FailedArchRuleException>(() =>
            fixtureClasses.Should().NotDependOnAny(serviceDefaultsTypes).Check(mutationArchitecture)
        );
    }

    [Fact]
    public void Shared_Web_has_no_module_ServiceDefaults_hosting_startup_or_TestOnlyGuard_dependency()
    {
        var assembly = LoadAssembly("Travel.Shared.Web");
        var webClasses = Classes().That().ResideInAssembly(assembly);

        var compositionArchitecture = LoadSharedWebCompositionArchitecture();
        webClasses.Should().Exist().Check(compositionArchitecture);
        AssertNoSharedWebCompositionDependencies(webClasses, compositionArchitecture);

        var referencedAssemblies = assembly
            .GetReferencedAssemblies()
            .Select(reference => reference.Name ?? string.Empty)
            .ToArray();
        referencedAssemblies.ShouldNotContain(reference =>
            reference.StartsWith("Travel.Modules.", StringComparison.Ordinal)
            || reference.StartsWith("Travel.ServiceDefaults", StringComparison.Ordinal)
            || reference.StartsWith("Travel.Host", StringComparison.Ordinal)
            || reference.StartsWith("Microsoft.AspNetCore.Hosting", StringComparison.Ordinal)
            || reference.StartsWith("Microsoft.Extensions.Hosting", StringComparison.Ordinal)
        );
        assembly
            .GetTypes()
            .ShouldNotContain(type =>
                string.Equals(type.Name, "TestOnlyGuard", StringComparison.Ordinal)
                || type.Name.Contains("Startup", StringComparison.Ordinal)
            );
    }

    private static void AssertNoSharedWebCompositionDependencies(
        IObjectProvider<Class> sourceClasses,
        global::ArchUnitNET.Domain.Architecture architecture
    )
    {
        var selectedClasses = Classes().That().Are(sourceClasses);
        selectedClasses.Should().Exist().Check(architecture);

        foreach (var forbiddenTypeName in ForbiddenSharedWebCompositionTypeNames)
        {
            var forbiddenTypes = Types().That().HaveFullName(forbiddenTypeName);
            forbiddenTypes.Should().Exist().Check(architecture);
            selectedClasses.Should().NotDependOnAny(forbiddenTypes).Check(architecture);
        }

        var serviceDefaultsTypes = Types()
            .That()
            .ResideInAssembly(LoadAssembly("Travel.ServiceDefaults"));
        serviceDefaultsTypes.Should().Exist().Check(architecture);
        selectedClasses.Should().NotDependOnAny(serviceDefaultsTypes).Check(architecture);
    }

    private static global::ArchUnitNET.Domain.Architecture LoadSharedWebCompositionArchitecture()
    {
        var assemblies = new[]
        {
            LoadAssembly("Travel.Shared.Web"),
            LoadAssembly("Travel.ServiceDefaults"),
            typeof(Microsoft.AspNetCore.Builder.WebApplication).Assembly,
            typeof(Microsoft.AspNetCore.Builder.IApplicationBuilder).Assembly,
            typeof(Microsoft.AspNetCore.Routing.IEndpointRouteBuilder).Assembly,
            typeof(Microsoft.Extensions.DependencyInjection.IServiceCollection).Assembly,
            typeof(Microsoft.Extensions.Hosting.IHost).Assembly,
            typeof(Microsoft.AspNetCore.Hosting.IWebHostEnvironment).Assembly,
        }.Distinct();

        return new ArchLoader().LoadAssembliesIncludingDependencies(assemblies.ToArray()).Build();
    }

    private static global::ArchUnitNET.Domain.Architecture LoadSharedWebMutationArchitecture()
    {
        var assemblies = new[]
        {
            typeof(SharedWebCharterBypassFixture).Assembly,
            typeof(Microsoft.AspNetCore.Builder.WebApplication).Assembly,
            typeof(Microsoft.AspNetCore.Builder.IApplicationBuilder).Assembly,
            typeof(Microsoft.AspNetCore.Routing.IEndpointRouteBuilder).Assembly,
            typeof(Microsoft.Extensions.DependencyInjection.IServiceCollection).Assembly,
            typeof(Microsoft.Extensions.Hosting.IHost).Assembly,
            typeof(Microsoft.AspNetCore.Hosting.IWebHostEnvironment).Assembly,
            typeof(Travel.ServiceDefaults.Hosting.TestOnlyGuard).Assembly,
        }.Distinct();

        return new ArchLoader().LoadAssembliesIncludingDependencies(assemblies.ToArray()).Build();
    }

    private static ReflectionAssembly LoadAssembly(string assemblyName)
    {
        var path = Path.Combine(AppContext.BaseDirectory, $"{assemblyName}.dll");
        File.Exists(path).ShouldBeTrue($"Architecture output must contain {assemblyName}.dll");
        return ReflectionAssembly.LoadFrom(path);
    }

    private static string Project(string relativePath) =>
        Path.GetFullPath(Path.Combine(RepositoryRoot, relativePath));

    private static bool IsModuleProject(string path) =>
        path.StartsWith(
            Path.Combine(RepositoryRoot, "modules") + Path.DirectorySeparatorChar,
            StringComparison.OrdinalIgnoreCase
        );

    private static string FindRepositoryRoot()
    {
        for (
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            directory is not null;
            directory = directory.Parent
        )
        {
            if (File.Exists(Path.Combine(directory.FullName, "Travel.slnx")))
                return directory.FullName;
        }

        throw new DirectoryNotFoundException(
            "Could not locate the repository root containing Travel.slnx."
        );
    }
}

internal sealed class SharedWebCharterBypassFixture
{
    public Microsoft.AspNetCore.Builder.WebApplication? Application { get; init; }
    public Microsoft.AspNetCore.Builder.WebApplicationBuilder? ApplicationBuilder { get; init; }
    public Microsoft.AspNetCore.Builder.IApplicationBuilder? Pipeline { get; init; }
    public Microsoft.AspNetCore.Routing.IEndpointRouteBuilder? Endpoints { get; init; }
    public Microsoft.AspNetCore.Routing.EndpointDataSource? EndpointDataSource { get; init; }
    public Microsoft.Extensions.DependencyInjection.IServiceCollection? Services { get; init; }
    public Microsoft.Extensions.Hosting.IHost? Host { get; init; }
    public Microsoft.Extensions.Hosting.IHostBuilder? HostBuilder { get; init; }
    public Microsoft.Extensions.Hosting.IHostApplicationBuilder? HostApplicationBuilder { get; init; }
    public Microsoft.Extensions.Hosting.IHostEnvironment? HostEnvironment { get; init; }
    public Microsoft.AspNetCore.Hosting.IWebHostBuilder? WebHostBuilder { get; init; }
    public Microsoft.AspNetCore.Hosting.IWebHostEnvironment? WebHostEnvironment { get; init; }
    public Microsoft.AspNetCore.Hosting.IStartupFilter? StartupFilter { get; init; }
    public Travel.ServiceDefaults.Web.PlatformExceptionHandler? PlatformExceptionHandler { get; init; }

    public void InvokeTestOnlyGuard(
        Microsoft.Extensions.DependencyInjection.IServiceCollection services,
        Microsoft.Extensions.Hosting.IHostEnvironment environment
    ) => Travel.ServiceDefaults.Hosting.TestOnlyGuard.Verify(services, environment);
}

internal sealed class SharedModuleDependencyBypassFixture
{
    public Travel.Modules.Flights.Application.Queries.SearchResult? SearchResult { get; init; }
}
