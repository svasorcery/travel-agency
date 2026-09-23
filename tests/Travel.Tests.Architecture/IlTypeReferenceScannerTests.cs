using Mono.Cecil;
using Mono.Cecil.Cil;
using Shouldly;
using Travel.Tests.Architecture.Support;
using Xunit;

namespace Travel.Tests.Architecture;

[Trait("Category", "Architecture")]
public sealed class IlTypeReferenceScannerTests
{
    [Fact]
    public void Scanner_finds_external_types_used_only_inside_method_bodies()
    {
        var edges = IlTypeReferenceScanner.Scan(
            typeof(Travel.Modules.Flights.Api.Endpoints.ApiContractMethodBodyBypassFixture)
                .Assembly
                .Location
        );

        edges.ShouldContain(edge =>
            edge.OriginType.Contains("ApiContractMethodBodyBypassFixture", StringComparison.Ordinal)
            && edge.TargetAssembly == "Travel.IntegrationContracts.AI"
            && edge.TargetType.Contains("NlSearchRequested", StringComparison.Ordinal)
        );
        edges.ShouldContain(edge =>
            edge.OriginType.Contains("ForeignModuleMethodBodyFixture", StringComparison.Ordinal)
            && edge.TargetAssembly == "Travel.Modules.Flights.Core"
            && edge.TargetType.Contains("IataCode", StringComparison.Ordinal)
        );
    }

    [Fact]
    public void Scanner_finds_local_and_catch_types_without_type_operands()
    {
        var path = Path.Combine(Path.GetTempPath(), "ws5-il-" + Guid.NewGuid() + ".dll");
        try
        {
            using (
                var assembly = AssemblyDefinition.CreateAssembly(
                    new AssemblyNameDefinition("Travel.Modules.Hotels.Core", new Version(1, 0)),
                    "ScannerFixture",
                    ModuleKind.Dll
                )
            )
            {
                var module = assembly.MainModule;
                var type = new TypeDefinition(
                    "Travel.Modules.Hotels.Core",
                    "LocalAndCatchFixture",
                    TypeAttributes.Public | TypeAttributes.Class,
                    module.TypeSystem.Object
                );
                module.Types.Add(type);
                var method = new MethodDefinition(
                    "Probe",
                    MethodAttributes.Public | MethodAttributes.Static,
                    module.TypeSystem.Void
                );
                type.Methods.Add(method);
                method.Body.InitLocals = true;
                method.Body.Variables.Add(
                    new VariableDefinition(
                        module.ImportReference(
                            typeof(Travel.Modules.Flights.Core.ValueObjects.IataCode)
                        )
                    )
                );
                var start = Instruction.Create(OpCodes.Nop);
                var end = Instruction.Create(OpCodes.Ret);
                var leave = Instruction.Create(OpCodes.Leave_S, end);
                var handler = Instruction.Create(OpCodes.Pop);
                var handlerLeave = Instruction.Create(OpCodes.Leave_S, end);
                method.Body.Instructions.Add(start);
                method.Body.Instructions.Add(leave);
                method.Body.Instructions.Add(handler);
                method.Body.Instructions.Add(handlerLeave);
                method.Body.Instructions.Add(end);
                method.Body.ExceptionHandlers.Add(
                    new ExceptionHandler(ExceptionHandlerType.Catch)
                    {
                        TryStart = start,
                        TryEnd = handler,
                        HandlerStart = handler,
                        HandlerEnd = end,
                        CatchType = module.ImportReference(
                            typeof(Travel.Modules.Flights.Application.ReadModels.BookingProjectionTransientException)
                        ),
                    }
                );
                assembly.Write(path);
            }

            var edges = IlTypeReferenceScanner.Scan(path);
            edges.ShouldContain(edge =>
                edge.TargetType.Contains("IataCode", StringComparison.Ordinal)
            );
            edges.ShouldContain(edge =>
                edge.TargetType.Contains(
                    "BookingProjectionTransientException",
                    StringComparison.Ordinal
                )
            );
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }

    [Fact]
    public void Method_body_edges_are_rejected_by_module_and_contract_policies()
    {
        var edges = IlTypeReferenceScanner.Scan(
            typeof(Travel.Modules.Flights.Api.Endpoints.ApiContractMethodBodyBypassFixture)
                .Assembly
                .Location
        );
        ModuleArchitectureInventory
            .FindCrossModuleIlViolations(
                edges.Where(edge =>
                    edge.OriginType.Contains(
                        "ForeignModuleMethodBodyFixture",
                        StringComparison.Ordinal
                    )
                )
            )
            .ShouldContain(violation =>
                violation.Contains("Hotels", StringComparison.Ordinal)
                && violation.Contains("Flights", StringComparison.Ordinal)
            );
        LayerArchitecturePolicy
            .FindContractConsumerIlViolations(
                edges.Where(edge =>
                    edge.OriginType.Contains(
                        "ApiContractMethodBodyBypassFixture",
                        StringComparison.Ordinal
                    )
                )
            )
            .ShouldContain(violation =>
                violation.Contains("ApiContractMethodBodyBypassFixture", StringComparison.Ordinal)
            );
    }
}
