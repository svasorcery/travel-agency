using JasperFx.CodeGeneration;
using Microsoft.CodeAnalysis;
using Shouldly;
using Xunit;

namespace Travel.Host.Tests.Integration.CodeGeneration;

// Library contract characterization only: no Host, database, tracing, RSS assertion or forced GC.
public sealed class MetadataOwnershipTests
{
    private static string AssemblyPath => typeof(IAssemblyGenerator).Assembly.Location;

    [Fact]
    public void Repeated_file_references_have_independent_metadata_owners()
    {
        var first = MetadataReference.CreateFromFile(AssemblyPath);
        var second = MetadataReference.CreateFromFile(AssemblyPath);
        first.GetMetadataId().ShouldNotBe(second.GetMetadataId());
        using var a = (AssemblyMetadata)first.GetMetadata();
        using var b = (AssemblyMetadata)second.GetMetadata();
        a.GetModules()[0].Name.ShouldBe(b.GetModules()[0].Name);
    }

    [Fact]
    public void Disposing_a_reference_metadata_copy_does_not_dispose_its_owner()
    {
        var reference = MetadataReference.CreateFromFile(AssemblyPath);
        var copy = (AssemblyMetadata)reference.GetMetadata();
        var module = copy.GetModules()[0];
        var name = module.Name;
        copy.Dispose();
        using var anotherCopy = (AssemblyMetadata)reference.GetMetadata();
        anotherCopy.GetModules()[0].IsDisposed.ShouldBeFalse();
        anotherCopy.GetModules()[0].Name.ShouldBe(name);
    }

    [Fact]
    public void Explicit_owner_shares_references_and_disposes_every_view_deterministically()
    {
        using var owner = AssemblyMetadata.CreateFromFile(AssemblyPath);
        var first = owner.GetReference(filePath: AssemblyPath);
        var second = owner.GetReference(filePath: AssemblyPath);
        first.GetMetadataId().ShouldBe(second.GetMetadataId());
        using var copy = (AssemblyMetadata)first.GetMetadata();
        var module = copy.GetModules()[0];
        module.Name.ShouldNotBeNullOrEmpty();
        owner.Dispose();
        module.IsDisposed.ShouldBeTrue();
        Should.Throw<ObjectDisposedException>(() => module.Name);
    }
}
