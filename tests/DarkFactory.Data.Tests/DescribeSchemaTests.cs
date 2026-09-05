using System.Text.Json;
using DarkFactory.Contracts;

namespace DarkFactory.Data.Tests;

/// <summary>
/// docs/conventions/describe.md: df.describe is a published schema, and the
/// factory validates every response against it before storing anything.
/// These tests are the schema's own tests — no database, no transport.
/// </summary>
public sealed class DescribeSchemaTests
{
    private static string Json(object value) => JsonSerializer.Serialize(value);

    [Fact]
    public void AWellFormedDescribeValidates()
    {
        var result = DescribeSchema.Validate(Json(FakeServerProbe.Describe()));
        Assert.True(result.IsValid, result.Summarize());
    }

    [Theory]
    [InlineData("name")]
    [InlineData("convention_version")]
    [InlineData("domain")]
    [InlineData("capabilities")]
    [InlineData("requires")]
    [InlineData("effective_config")]
    public void EveryRequiredFieldIsActuallyRequired(string field)
    {
        var document = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(Json(FakeServerProbe.Describe()))!;
        document.Remove(field);

        var result = DescribeSchema.Validate(JsonSerializer.Serialize(document));

        Assert.False(result.IsValid);
        // The point of validating is telling the author what's wrong, so
        // the missing field has to be named in the message they get.
        Assert.Contains(field, result.Summarize(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("0.2")]
    [InlineData("v0.2.0")]
    [InlineData("latest")]
    [InlineData("")]
    public void ConventionVersionMustBeSemver(string version)
    {
        var result = DescribeSchema.Validate(Json(FakeServerProbe.Describe(conventionVersion: version)));
        Assert.False(result.IsValid);
    }

    [Theory]
    [InlineData("0.1.0")]
    [InlineData("1.0.0")]
    [InlineData("2.3.4-rc.1")]
    public void SemverConventionVersionsAreAccepted(string version)
    {
        var result = DescribeSchema.Validate(Json(FakeServerProbe.Describe(conventionVersion: version)));
        Assert.True(result.IsValid, result.Summarize());
    }

    [Theory]
    [InlineData("files.list")]          // not rooted at df.
    [InlineData("workspace.describe")]  // the v0.1 name
    [InlineData("df.")]                 // root with nothing after it
    [InlineData("df.Files.List")]       // capabilities are lower-case
    [InlineData("")]
    public void CapabilitiesMustBeDottedDfNames(string capability)
    {
        var result = DescribeSchema.Validate(Json(FakeServerProbe.Describe(capabilities: [capability])));
        Assert.False(result.IsValid);
    }

    [Theory]
    [InlineData("df.describe")]
    [InlineData("df.files.read_many")]
    [InlineData("df.vcs.open_pr")]
    [InlineData("df.agent.run")]
    public void DottedDfNamesAreAccepted(string capability)
    {
        var result = DescribeSchema.Validate(Json(FakeServerProbe.Describe(capabilities: [capability])));
        Assert.True(result.IsValid, result.Summarize());
    }

    [Fact]
    public void RequiresUsesTheSameCapabilityForm()
    {
        Assert.False(DescribeSchema.Validate(Json(FakeServerProbe.Describe(requires: ["exec.run"]))).IsValid);
        Assert.True(DescribeSchema.Validate(Json(FakeServerProbe.Describe(requires: ["df.exec.run"]))).IsValid);
    }

    [Fact]
    public void AnUnknownDomainIsRejected()
    {
        var result = DescribeSchema.Validate(Json(FakeServerProbe.Describe(domain: "whatever")));
        Assert.False(result.IsValid);
    }

    [Fact]
    public void UnknownTopLevelPropertiesAreRejected()
    {
        // Catches the typo case — a server sending `capabilties` would
        // otherwise register with an empty capability list and look fine.
        var document = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(Json(FakeServerProbe.Describe()))!;
        document["capabilties"] = JsonSerializer.SerializeToElement(new[] { "df.files.list" });

        Assert.False(DescribeSchema.Validate(JsonSerializer.Serialize(document)).IsValid);
    }

    [Fact]
    public void ANewerServerIsNotRejectedByAnOlderFactory()
    {
        // docs/adr/0020's overlap window, from the other direction: a
        // server built against a later convention version adds capability
        // under `extensions`, which is open, so a factory that has never
        // heard of those keys still validates the response instead of
        // refusing to register a server that is strictly newer than it.
        var document = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(Json(FakeServerProbe.Describe()))!;
        document["extensions"] = JsonSerializer.SerializeToElement(new
        {
            rate_limit_per_minute = 600,
            something_invented_in_1_1 = new { nested = true },
        });

        var result = DescribeSchema.Validate(JsonSerializer.Serialize(document));
        Assert.True(result.IsValid, result.Summarize());
    }

    [Fact]
    public void ExtensionsIsTheOnlyPlaceUnknownFieldsAreTolerated()
    {
        // The rule is narrow on purpose: forward compatibility goes in
        // `extensions`, not anywhere a server feels like putting it, or
        // the typo protection above evaporates.
        var document = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(Json(FakeServerProbe.Describe()))!;
        document["rate_limit_per_minute"] = JsonSerializer.SerializeToElement(600);

        Assert.False(DescribeSchema.Validate(JsonSerializer.Serialize(document)).IsValid);
    }

    [Fact]
    public void ExtensionsMustStillBeAnObject()
    {
        var document = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(Json(FakeServerProbe.Describe()))!;
        document["extensions"] = JsonSerializer.SerializeToElement("not an object");

        Assert.False(DescribeSchema.Validate(JsonSerializer.Serialize(document)).IsValid);
    }

    [Fact]
    public void EchoingTheEnvelopeBackFailsValidation()
    {
        // Every other convention tool echoes the envelope; df.describe must
        // not, and this is why the reference server special-cases it.
        var document = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(Json(FakeServerProbe.Describe()))!;
        document["envelope"] = JsonSerializer.SerializeToElement(new { run_id = "r1" });

        Assert.False(DescribeSchema.Validate(JsonSerializer.Serialize(document)).IsValid);
    }

    [Fact]
    public void NonJsonIsAValidationFailureNotAnException()
    {
        var result = DescribeSchema.Validate("this is not json");

        Assert.False(result.IsValid);
        Assert.Contains("not valid JSON", result.Summarize(), StringComparison.Ordinal);
    }

    [Fact]
    public void DeserializationAloneIsNotValidation()
    {
        // System.Text.Json does enforce `required` members, so a document
        // missing a field fails to bind. What it cannot enforce is shape:
        // this one has every field, of the right JSON type, and is still
        // not a valid describe response — non-semver version, a capability
        // outside the df. root, and a domain that doesn't exist. Binding
        // accepts all three, which is why the schema is the gate.
        const string json = """
            {
              "name": "x",
              "convention_version": "whenever",
              "domain": "vibes",
              "capabilities": ["files.list"],
              "requires": [],
              "effective_config": {}
            }
            """;

        Assert.Null(Record.Exception(() => JsonSerializer.Deserialize<DescribeResponse>(json)));

        var result = DescribeSchema.Validate(json);
        Assert.False(result.IsValid);
        Assert.Contains("convention_version", result.Summarize(), StringComparison.Ordinal);
        Assert.Contains("domain", result.Summarize(), StringComparison.Ordinal);
        Assert.Contains("capabilities", result.Summarize(), StringComparison.Ordinal);
    }
}
