using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Shouldly;
using Travel.Modules.Identity.Api.Composition;
using Xunit;

namespace Travel.Modules.Identity.Tests.Unit.Authentication;

public sealed class NormalizedIdentityClaimsTransformationTests
{
    [Fact]
    public async Task Valid_subject_adds_one_canonical_name_identifier()
    {
        const string subject = "bfb631f9-c340-4f2a-bcd7-9427d17d8c59";

        var principal = await TransformAsync(new Claim("sub", subject));

        principal
            .FindAll(ClaimTypes.NameIdentifier)
            .Select(claim => claim.Value)
            .ShouldBe(new[] { subject });
    }

    [Fact]
    public async Task Equal_existing_name_identifier_is_not_duplicated()
    {
        const string subject = "d662c1c9-e6cc-4a51-9d15-e4e9aa846a5d";

        var principal = await TransformAsync(
            new Claim("sub", subject),
            new Claim(ClaimTypes.NameIdentifier, subject)
        );

        principal.FindAll(ClaimTypes.NameIdentifier).Count().ShouldBe(1);
    }

    [Fact]
    public async Task Repeated_transformation_keeps_the_canonical_identity_idempotent()
    {
        const string subject = "fd445941-dba8-446c-807b-4df0b9a729ae";
        var transformer = CreateTransformer();
        var principal = CreatePrincipal(new Claim("sub", subject));

        var afterFirstTransform = await transformer.TransformAsync(principal);
        var afterSecondTransform = await transformer.TransformAsync(afterFirstTransform);

        afterSecondTransform
            .FindAll(ClaimTypes.NameIdentifier)
            .Select(claim => claim.Value)
            .ShouldBe(new[] { subject });
    }

    [Theory]
    [InlineData("not-a-guid")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    public async Task Malformed_or_empty_subject_does_not_add_a_canonical_name_identifier(
        string subject
    )
    {
        var principal = await TransformAsync(new Claim("sub", subject));

        principal.FindAll(ClaimTypes.NameIdentifier).ShouldBeEmpty();
    }

    [Fact]
    public async Task Valid_and_malformed_subjects_do_not_add_a_canonical_name_identifier()
    {
        const string validSubject = "a7b5e058-5532-4aef-905b-4af3d31d5762";

        var principal = await TransformAsync(
            new Claim("sub", validSubject),
            new Claim("sub", "not-a-guid")
        );

        principal.FindAll(ClaimTypes.NameIdentifier).ShouldBeEmpty();
    }

    [Fact]
    public async Task Valid_and_empty_subjects_do_not_add_a_canonical_name_identifier()
    {
        const string validSubject = "f38d6d9c-c50f-491f-aa28-2d3d17e6d6c2";

        var principal = await TransformAsync(
            new Claim("sub", validSubject),
            new Claim("sub", string.Empty)
        );

        principal.FindAll(ClaimTypes.NameIdentifier).ShouldBeEmpty();
    }

    [Fact]
    public async Task Conflicting_valid_subject_and_name_identifier_remain_ambiguous()
    {
        const string subject = "09dc7aa7-612e-4ad3-ab62-caac1bb7474a";
        const string existingIdentifier = "73d0ba9a-803d-4fb9-8e45-2677de9f6c37";

        var principal = await TransformAsync(
            new Claim("sub", subject),
            new Claim(ClaimTypes.NameIdentifier, existingIdentifier)
        );

        principal
            .FindAll(ClaimTypes.NameIdentifier)
            .Select(claim => claim.Value)
            .ShouldBe(new[] { existingIdentifier });
    }

    [Fact]
    public async Task Scope_and_scp_claims_become_distinct_canonical_scopes_idempotently()
    {
        var transformer = CreateTransformer();
        var principal = CreatePrincipal(
            new Claim("scope", " flights:book  profile "),
            new Claim("scp", "profile admin flights:book")
        );

        var afterFirstTransform = await transformer.TransformAsync(principal);
        var afterSecondTransform = await transformer.TransformAsync(afterFirstTransform);

        afterSecondTransform
            .FindAll("scope")
            .Select(claim => claim.Value)
            .ShouldBe(new[] { "flights:book", "profile", "admin" });
        afterSecondTransform.FindAll("scp").ShouldBeEmpty();
    }

    private static async Task<ClaimsPrincipal> TransformAsync(params Claim[] claims) =>
        await CreateTransformer().TransformAsync(CreatePrincipal(claims));

    private static IClaimsTransformation CreateTransformer()
    {
        var builder = new HostApplicationBuilder();
        builder.AddIdentityModule();
        using var provider = builder.Services.BuildServiceProvider();
        return provider.GetRequiredService<IClaimsTransformation>();
    }

    private static ClaimsPrincipal CreatePrincipal(params Claim[] claims) =>
        new(new ClaimsIdentity(claims, "test"));
}
