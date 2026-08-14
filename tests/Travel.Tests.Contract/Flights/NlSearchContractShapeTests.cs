using System.Reflection;
using System.Text.Json;
using Shouldly;
using Travel.AI.NlSearch;
using Travel.Modules.Flights.Application.Handlers.NlSearch;
using Xunit;

namespace Travel.Tests.Contract.Flights;

[Trait("Category", "Contract")]
public sealed class NlSearchContractShapeTests
{
    private const string ContractAssemblyName = "Travel.IntegrationContracts.AI";
    private const string ContractNamespace = "Travel.IntegrationContracts.AI.NlSearch";
    private const string RequestedAlias = "travel.ai.nl-search.requested";
    private const string ParsedAlias = "travel.ai.nl-search.parsed";
    private const int ContractVersion = 1;

    private static readonly JsonSerializerOptions WebJson = new(JsonSerializerDefaults.Web);

    [Theory]
    [InlineData(true, RequestedAlias, "Requested")]
    [InlineData(false, ParsedAlias, "Parsed")]
    public void Messages_have_stable_wolverine_v1_identity_and_public_constants(
        bool requested,
        string expectedAlias,
        string aliasFieldName
    )
    {
        var messageType = requested ? GetMessageTypes().Requested : GetMessageTypes().Parsed;
        var identity = messageType
            .GetCustomAttributesData()
            .SingleOrDefault(attribute =>
                attribute.AttributeType.FullName == "Wolverine.Attributes.MessageIdentityAttribute"
            );

        identity.ShouldNotBeNull($"{messageType.Name} must declare a Wolverine message identity");
        identity.ConstructorArguments.Single().Value.ShouldBe(expectedAlias);
        identity
            .NamedArguments.Single(argument => argument.MemberName == "Version")
            .TypedValue.Value.ShouldBe(ContractVersion);

        var constants = messageType.Assembly.GetType(
            $"{ContractNamespace}.NlSearchMessageIdentity"
        );
        constants.ShouldNotBeNull("the contract assembly must own its identity constants");
        GetPublicConstant(constants, aliasFieldName).ShouldBe(expectedAlias);
        GetPublicConstant(constants, "Version").ShouldBe(ContractVersion);
    }

    [Fact]
    public void Requested_message_has_exact_web_json_shape_and_round_trips_correlation()
    {
        var correlationId = Guid.Parse("a1b2c3d4-e5f6-7890-abcd-ef1234567890");
        var locale = GetDefaultValue<string>(GetMessageTypes().Requested, "Locale");
        var requested = CreateRequested("Moscow to Saint Petersburg", correlationId, locale);

        var json = Serialize(requested);

        json.ShouldBe(
            "{\"query\":\"Moscow to Saint Petersburg\",\"correlationId\":\"a1b2c3d4-e5f6-7890-abcd-ef1234567890\",\"locale\":\"ru\"}"
        );
        var roundTrip = Deserialize(json, requested.GetType());
        GetProperty<string>(roundTrip, "Query").ShouldBe("Moscow to Saint Petersburg");
        GetProperty<Guid>(roundTrip, "CorrelationId").ShouldBe(correlationId);
        GetProperty<string>(roundTrip, "Locale").ShouldBe("ru");
    }

    [Fact]
    public void Parsed_message_has_exact_web_json_shape_including_defaults_and_round_trips()
    {
        var correlationId = Guid.Parse("a1b2c3d4-e5f6-7890-abcd-ef1234567890");
        var parsedType = GetMessageTypes().Parsed;
        var parsed = CreateParsed(
            correlationId,
            origin: "DME",
            destination: "LED",
            departureDate: new DateOnly(2026, 6, 25),
            returnDate: null,
            passengerCount: 1,
            cabinClass: "economy",
            currency: "RUB",
            inputTokens: GetDefaultValue<int>(parsedType, "InputTokens"),
            outputTokens: GetDefaultValue<int>(parsedType, "OutputTokens"),
            costUsd: GetDefaultValue<decimal>(parsedType, "CostUsd"),
            modelId: GetDefaultValue<string>(parsedType, "ModelId")
        );

        var json = Serialize(parsed);

        json.ShouldBe(
            "{\"correlationId\":\"a1b2c3d4-e5f6-7890-abcd-ef1234567890\",\"origin\":\"DME\",\"destination\":\"LED\",\"departureDate\":\"2026-06-25\",\"returnDate\":null,\"passengerCount\":1,\"cabinClass\":\"economy\",\"currency\":\"RUB\",\"inputTokens\":0,\"outputTokens\":0,\"costUsd\":0,\"modelId\":\"\"}"
        );
        var roundTrip = Deserialize(json, parsed.GetType());
        GetProperty<Guid>(roundTrip, "CorrelationId").ShouldBe(correlationId);
        GetProperty<string>(roundTrip, "Origin").ShouldBe("DME");
        GetProperty<string>(roundTrip, "Destination").ShouldBe("LED");
        GetProperty<DateOnly>(roundTrip, "DepartureDate").ShouldBe(new DateOnly(2026, 6, 25));
        GetProperty<DateOnly?>(roundTrip, "ReturnDate").ShouldBeNull();
        GetProperty<int>(roundTrip, "PassengerCount").ShouldBe(1);
        GetProperty<string>(roundTrip, "CabinClass").ShouldBe("economy");
        GetProperty<string>(roundTrip, "Currency").ShouldBe("RUB");
        GetProperty<int>(roundTrip, "InputTokens").ShouldBe(0);
        GetProperty<int>(roundTrip, "OutputTokens").ShouldBe(0);
        GetProperty<decimal>(roundTrip, "CostUsd").ShouldBe(0m);
        GetProperty<string>(roundTrip, "ModelId").ShouldBe(string.Empty);
    }

    [Fact]
    public void Reply_for_a_request_keeps_the_same_correlation_id()
    {
        var correlationId = Guid.Parse("b2c3d4e5-f6a7-8901-bcde-f12345678901");
        var requested = CreateRequested("LED to DME", correlationId, "ru");
        var parsed = CreateParsed(
            correlationId,
            origin: "LED",
            destination: "DME",
            departureDate: new DateOnly(2026, 8, 15),
            returnDate: new DateOnly(2026, 8, 22),
            passengerCount: 2,
            cabinClass: "business",
            currency: "RUB",
            inputTokens: 1200,
            outputTokens: 340,
            costUsd: 0.0435m,
            modelId: "claude-opus-4-7"
        );

        var requestedRoundTrip = Deserialize(Serialize(requested), requested.GetType());
        var parsedRoundTrip = Deserialize(Serialize(parsed), parsed.GetType());

        GetProperty<Guid>(parsedRoundTrip, "CorrelationId")
            .ShouldBe(GetProperty<Guid>(requestedRoundTrip, "CorrelationId"));
    }

    [Fact]
    public void AI_handler_uses_the_shared_request_and_reply_types()
    {
        var messageTypes = GetMessageTypes();

        messageTypes.Requested.Assembly.GetName().Name.ShouldBe(ContractAssemblyName);
        messageTypes.Requested.FullName.ShouldBe($"{ContractNamespace}.NlSearchRequested");
        messageTypes.Parsed.Assembly.GetName().Name.ShouldBe(ContractAssemblyName);
        messageTypes.Parsed.FullName.ShouldBe($"{ContractNamespace}.NlSearchParsed");
    }

    [Fact]
    public void Flights_and_AI_reference_one_contract_assembly_and_own_no_mirrors()
    {
        var flightsAssembly = typeof(NlSearchHandler).Assembly;
        var aiAssembly = typeof(NlSearchAiHandler).Assembly;

        flightsAssembly
            .GetReferencedAssemblies()
            .ShouldContain(reference => reference.Name == ContractAssemblyName);
        aiAssembly
            .GetReferencedAssemblies()
            .ShouldContain(reference => reference.Name == ContractAssemblyName);
        flightsAssembly
            .GetType("Travel.Modules.Flights.Application.Contracts.NlSearchRequested")
            .ShouldBeNull();
        flightsAssembly
            .GetType("Travel.Modules.Flights.Application.Contracts.NlSearchParsed")
            .ShouldBeNull();
        aiAssembly.GetType("Travel.AI.NlSearch.Contracts.NlSearchRequested").ShouldBeNull();
        aiAssembly.GetType("Travel.AI.NlSearch.Contracts.NlSearchParsed").ShouldBeNull();
    }

    [Theory]
    [InlineData("NlSearchRequested")]
    [InlineData("NlSearchParsed")]
    public void Each_message_name_resolves_to_one_runtime_type(string messageName)
    {
        var messageTypes = GetMessageTypes();
        var assemblies = new[]
        {
            typeof(NlSearchHandler).Assembly,
            typeof(NlSearchAiHandler).Assembly,
            messageTypes.Requested.Assembly,
            messageTypes.Parsed.Assembly,
        };
        var matchingTypes = assemblies
            .Distinct()
            .SelectMany(assembly => assembly.GetTypes())
            .Where(type => type.Name == messageName)
            .ToArray();

        matchingTypes.Length.ShouldBe(1, $"{messageName} must have one canonical runtime Type");
        matchingTypes[0].Assembly.GetName().Name.ShouldBe(ContractAssemblyName);
    }

    private static (Type Requested, Type Parsed) GetMessageTypes()
    {
        var handle = typeof(NlSearchAiHandler).GetMethod(
            "Handle",
            BindingFlags.Public | BindingFlags.Static
        );
        handle.ShouldNotBeNull();
        var requested = handle.GetParameters()[0].ParameterType;
        var returnType = handle.ReturnType;
        returnType.IsGenericType.ShouldBeTrue();
        returnType.GetGenericTypeDefinition().ShouldBe(typeof(Task<>));

        return (requested, returnType.GetGenericArguments().Single());
    }

    private static object CreateRequested(string query, Guid correlationId, string locale)
    {
        return Activator.CreateInstance(GetMessageTypes().Requested, query, correlationId, locale)
            ?? throw new InvalidOperationException("Could not construct NlSearchRequested.");
    }

    private static object CreateParsed(
        Guid correlationId,
        string origin,
        string destination,
        DateOnly departureDate,
        DateOnly? returnDate,
        int passengerCount,
        string cabinClass,
        string currency,
        int inputTokens = 0,
        int outputTokens = 0,
        decimal costUsd = 0m,
        string modelId = ""
    )
    {
        return Activator.CreateInstance(
                GetMessageTypes().Parsed,
                correlationId,
                origin,
                destination,
                departureDate,
                returnDate,
                passengerCount,
                cabinClass,
                currency,
                inputTokens,
                outputTokens,
                costUsd,
                modelId
            ) ?? throw new InvalidOperationException("Could not construct NlSearchParsed.");
    }

    private static string Serialize(object message) =>
        JsonSerializer.Serialize(message, message.GetType(), WebJson);

    private static object Deserialize(string json, Type messageType) =>
        JsonSerializer.Deserialize(json, messageType, WebJson)
        ?? throw new InvalidOperationException($"Could not deserialize {messageType.Name}.");

    private static T GetProperty<T>(object instance, string propertyName)
    {
        var property = instance.GetType().GetProperty(propertyName);
        property.ShouldNotBeNull($"{instance.GetType().Name}.{propertyName} must exist");
        return (T)property.GetValue(instance)!;
    }

    private static object? GetPublicConstant(Type declaringType, string fieldName)
    {
        var field = declaringType.GetField(fieldName, BindingFlags.Public | BindingFlags.Static);
        field.ShouldNotBeNull($"{declaringType.FullName}.{fieldName} must be public");
        field.IsLiteral.ShouldBeTrue($"{field.Name} must be a compile-time constant");
        return field.GetRawConstantValue();
    }

    private static T GetDefaultValue<T>(Type messageType, string parameterName)
    {
        var parameter = messageType
            .GetConstructors(BindingFlags.Public | BindingFlags.Instance)
            .Single()
            .GetParameters()
            .Single(candidate =>
                string.Equals(candidate.Name, parameterName, StringComparison.OrdinalIgnoreCase)
            );
        parameter.HasDefaultValue.ShouldBeTrue(
            $"{messageType.Name}.{parameterName} must keep its positional default"
        );
        return (T)parameter.DefaultValue!;
    }
}
