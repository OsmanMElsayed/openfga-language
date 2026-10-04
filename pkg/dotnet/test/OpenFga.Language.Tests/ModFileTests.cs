using System.Text.Json.Nodes;
using OpenFga.Language.Errors;
using OpenFga.Language.Tests.Fixtures;
using OpenFga.Language.Transformation;

namespace OpenFga.Language.Tests;

public class ModFileTests
{
    private static readonly IReadOnlyList<ModFileCase> Cases = TestData.ModFileCases();

    public static TheoryData<int, string> CaseData => TestData.Indexes(Cases, c => c.Name);

    [Theory]
    [MemberData(nameof(CaseData))]
    public void ParsesModFiles(int index, string name)
    {
        _ = name;
        var testCase = Cases[index];

        if (testCase.ExpectedErrors is null)
        {
            var modFile = ModFile.Parse(testCase.ModFile);
            var expected = JsonNode.Parse(testCase.Json!)!;
            Assert.Equal(expected["schema"]!["value"]!.GetValue<string>(), modFile.Schema);
            Assert.Equal(expected["contents"]!["value"]!.AsArray().Select(n => n!["value"]!.GetValue<string>()), modFile.Contents);
            return;
        }

        var exception = Assert.Throws<ModFileValidationException>(() => ModFile.Parse(testCase.ModFile));
        Assert.Equal(ErrorAssert.ExpectedAggregate(testCase.ExpectedErrors, _ => "validation", includeLocation: true), exception.Message);
        ErrorAssert.AllMatch(testCase.ExpectedErrors, exception.Errors);
    }

    [Fact]
    public void NonMappingDocumentReportsMissingFields()
    {
        var exception = Assert.Throws<ModFileValidationException>(() => ModFile.Parse("- core.fga\n"));

        Assert.Equal(["missing schema field", "missing contents field"], exception.Errors.Select(e => e.Message));
    }

    [Fact]
    public void EmptyValuesAreNotStrings()
    {
        var exception = Assert.Throws<ModFileValidationException>(() => ModFile.Parse("schema:\ncontents:\n"));

        Assert.Equal(
            ["unexpected schema type, expected string got value null", "unexpected contents type, expected list of strings got value null"],
            exception.Errors.Select(e => e.Message));
    }

    [Fact]
    public void MalformedYamlThrowsModFileValidationException()
    {
        var exception = Assert.Throws<ModFileValidationException>(() => ModFile.Parse("schema: [\n"));

        Assert.NotNull(Assert.Single(exception.Errors).Range);
    }

    [Fact]
    public void NullThrowsArgumentNullException()
        => Assert.Equal("text", Assert.Throws<ArgumentNullException>(() => ModFile.Parse(null!)).ParamName);

    [Fact]
    public void OversizedHexSchemaIsReportedNotThrown()
    {
        var exception = Assert.Throws<ModFileValidationException>(() => ModFile.Parse("schema: 0xFFFFFFFFFFFFFFFFF\ncontents:\n  - core.fga\n"));

        Assert.Equal("unexpected schema type, expected string got value 0xFFFFFFFFFFFFFFFFF", Assert.Single(exception.Errors).Message);
    }

    [Fact]
    public void OverflowingDoubleSchemaIsReportedNotThrown()
    {
        var exception = Assert.Throws<ModFileValidationException>(() => ModFile.Parse("schema: 1e400\ncontents:\n  - core.fga\n"));

        Assert.StartsWith("unexpected schema type, expected string got value ", Assert.Single(exception.Errors).Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(@"C:\x\a.fga")]
    [InlineData("C:/x/a.fga")]
    [InlineData(@"\\srv\a.fga")]
    [InlineData(@"\a.fga")]
    public void WindowsAbsolutePathsAreRejected(string path)
    {
        var exception = Assert.Throws<ModFileValidationException>(() => ModFile.Parse($"schema: '1.2'\ncontents:\n  - '{path}'\n"));

        Assert.Equal($"invalid contents item {path}", Assert.Single(exception.Errors).Message);
    }
}
