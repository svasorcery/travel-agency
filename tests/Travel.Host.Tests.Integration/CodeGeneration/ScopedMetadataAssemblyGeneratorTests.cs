using System.Collections.Concurrent;
using JasperFx.CodeGeneration;
using JasperFx.RuntimeCompiler;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Travel.Host.CodeGeneration;
using Wolverine;
using Xunit;

namespace Travel.Host.Tests.Integration.CodeGeneration;

[Collection(HostIntegrationCollection.Name)]
public sealed class ScopedMetadataAssemblyGeneratorTests
{
    private const string Source = "public static class Answer { public static int Value => 42; }";

    private sealed class ObservedOwners
    {
        public ConcurrentBag<ModuleMetadata[]> Modules { get; } = [];
        public ConcurrentBag<string> Paths { get; } = [];

        public AssemblyMetadata Open(string path)
        {
            var owner = AssemblyMetadata.CreateFromFile(path);
            Modules.Add(owner.GetModules().ToArray());
            Paths.Add(path);
            return owner;
        }

        public void AssertClosed()
        {
            Modules.ShouldNotBeEmpty();
            Modules.SelectMany(m => m).ShouldAllBe(m => m.IsDisposed);
        }
    }

    [Fact]
    public void Successful_emit_releases_metadata_while_generated_code_stays_usable()
    {
        var observed = new ObservedOwners();
        IAssemblyGenerator generator = new ScopedMetadataAssemblyGenerator(observed.Open);
        generator.ReferenceAssemblyContainingType<IAssemblyGenerator>();
        generator.ReferenceAssembly(typeof(IAssemblyGenerator).Assembly);
        var result = generator.Generate(Source);
        observed.AssertClosed();
        observed.Paths.GroupBy(p => p).ShouldAllBe(g => g.Count() == 1);
        result.GetType("Answer")!.GetProperty("Value")!.GetValue(null).ShouldBe(42);
    }

    [Fact]
    public void Compiler_error_disposes_all_acquired_owners()
    {
        var observed = new ObservedOwners();
        var generator = new ScopedMetadataAssemblyGenerator(observed.Open);
        var error = Should.Throw<InvalidOperationException>(() =>
            generator.Generate("public class Broken {")
        );
        error.Message.ShouldContain("Compilation failures");
        observed.AssertClosed();
    }

    [Fact]
    public void Reference_open_failure_disposes_previously_acquired_owners()
    {
        var observed = new ObservedOwners();
        var count = 0;
        var generator = new ScopedMetadataAssemblyGenerator(p =>
            ++count == 2 ? throw new IOException("metadata-test-failure") : observed.Open(p)
        );
        Should
            .Throw<IOException>(() => generator.Generate(Source))
            .Message.ShouldBe("metadata-test-failure");
        observed.Modules.Count.ShouldBe(1);
        observed.AssertClosed();
    }

    [Fact]
    public void Repeated_compilations_do_not_keep_previous_owners_alive()
    {
        var observed = new ObservedOwners();
        var generator = new ScopedMetadataAssemblyGenerator(observed.Open);
        var first = generator.Generate(Source);
        observed.AssertClosed();
        var count = observed.Modules.Count;
        var second = generator.Generate(Source);
        observed.Modules.Count.ShouldBe(count * 2);
        observed.AssertClosed();
        first.ShouldNotBeSameAs(second);
        first.GetType("Answer")!.GetProperty("Value")!.GetValue(null).ShouldBe(42);
        second.GetType("Answer")!.GetProperty("Value")!.GetValue(null).ShouldBe(42);
    }

    [Fact]
    public async Task Concurrent_calls_have_separate_disposable_metadata()
    {
        var observed = new ObservedOwners();
        var generator = new ScopedMetadataAssemblyGenerator(observed.Open);
        var results = await Task.WhenAll(
            Task.Run(() => generator.Generate(Source)),
            Task.Run(() => generator.Generate(Source))
        );
        observed.AssertClosed();
        foreach (var result in results)
            result.GetType("Answer")!.GetProperty("Value")!.GetValue(null).ShouldBe(42);
    }

    [Fact]
    public void Writer_overload_and_explicit_assembly_name_are_preserved()
    {
        var observed = new ObservedOwners();
        var generator = new ScopedMetadataAssemblyGenerator(observed.Open)
        {
            AssemblyName = "Travel.Lifetime.Test",
        };
        var result = generator.Generate(writer => writer.Write(Source));
        result.GetName().Name.ShouldBe("Travel.Lifetime.Test");
        observed.AssertClosed();
    }

    [Fact]
    public void Existing_generated_assembly_contract_attaches_types_and_source_after_release()
    {
        var observed = new ObservedOwners();
        IAssemblyGenerator generator = new ScopedMetadataAssemblyGenerator(observed.Open);
        var generated = GeneratedAssembly.Empty();
        generated.AddType("OwnedMetadataObject", typeof(object));
        generator.Compile(generated, null, out var code);
        observed.AssertClosed();
        code.ShouldContain("OwnedMetadataObject");
        generated.Assembly.ShouldNotBeNull();
        Activator
            .CreateInstance(generated.Assembly.GetType("LamarGenerated.OwnedMetadataObject")!)
            .ShouldNotBeNull();
    }

    [Fact]
    public void Compile_failure_does_not_attach_an_assembly_or_retain_owners()
    {
        var observed = new ObservedOwners();
        var count = 0;
        IAssemblyGenerator generator = new ScopedMetadataAssemblyGenerator(p =>
            ++count == 2 ? throw new IOException("metadata-test-failure") : observed.Open(p)
        );
        var generated = GeneratedAssembly.Empty();
        generated.AddType("FailedMetadataObject", typeof(object));
        Should.Throw<IOException>(() => generator.Compile(generated));
        generated.Assembly.ShouldBeNull();
        observed.AssertClosed();
    }

    [Fact]
    public void Wolverine_builder_uses_the_owned_metadata_compiler_without_starting_a_host()
    {
        var builder = WebApplication.CreateEmptyBuilder(
            new WebApplicationOptions
            {
                EnvironmentName = "Test",
                ApplicationName = typeof(ScopedMetadataAssemblyGenerator).Assembly.GetName().Name,
            }
        );
        builder.Host.UseWolverine(options =>
            options.ApplicationAssembly = typeof(ScopedMetadataAssemblyGenerator).Assembly
        );
        builder.Services.AddScopedMetadataCompilation();
        using var provider = builder.Services.BuildServiceProvider();
        var compiler = provider.GetRequiredService<IAssemblyGenerator>();
        compiler.ShouldBeOfType<ScopedMetadataAssemblyGenerator>();
        compiler
            .Generate(Source)
            .GetType("Answer")!
            .GetProperty("Value")!
            .GetValue(null)
            .ShouldBe(42);
    }

    [Fact]
    public void Host_replaces_default_registration_once_and_consumer_compiles_through_same_interface()
    {
        var services = new ServiceCollection();
        services.AddRuntimeCompilation();
        services.AddRuntimeCompilation();
        services.AddScopedMetadataCompilation();
        services.AddScopedMetadataCompilation();
        services.Count(d => d.ServiceType == typeof(IAssemblyGenerator)).ShouldBe(1);
        using var provider = services.BuildServiceProvider();
        var generator = provider.GetRequiredService<IAssemblyGenerator>();
        generator.ShouldBeOfType<ScopedMetadataAssemblyGenerator>();
        generator.ShouldBeSameAs(provider.GetRequiredService<IAssemblyGenerator>());
        var generated = GeneratedAssembly.Empty();
        generated.AddType("RegisteredMetadataObject", typeof(object));
        generator.Compile(generated);
        Activator
            .CreateInstance(generated.Assembly!.GetType("LamarGenerated.RegisteredMetadataObject")!)
            .ShouldNotBeNull();
    }
}
