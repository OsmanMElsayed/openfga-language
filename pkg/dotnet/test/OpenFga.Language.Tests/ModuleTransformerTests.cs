using System.Text.RegularExpressions;
using OpenFga.Language.Errors;
using OpenFga.Language.Tests.Fixtures;
using OpenFga.Language.Transformer;

namespace OpenFga.Language.Tests;

public class ModuleTransformerTests
{
    private static readonly IReadOnlyList<ModuleCase> Modules = TestData.ModuleCases().Where(c => c.Modules.Count > 0).ToList();

    // Mirrors the JS test: semantic cases rewritten from `model` to `module test`, only for schema 1.1 models.
    private static readonly IReadOnlyList<DslCase> Semantic = TestData.DslSemanticCases()
        .Where(c => !c.Dsl.TrimStart().StartsWith("module", StringComparison.Ordinal) && c.Dsl.Contains("schema 1.1", StringComparison.Ordinal))
        .ToList();

    public static TheoryData<int, string> ModuleData => TestData.Indexes(Modules, c => c.Name);

    public static TheoryData<int, string> SemanticData => TestData.Indexes(Semantic, c => c.Name);

    [Theory]
    [MemberData(nameof(ModuleData))]
    public void CombinesModules(int index, string name)
    {
        _ = name;
        var testCase = Modules[index];
        var files = testCase.Modules.Select(m => new ModuleFile(m.Name, m.Contents)).ToList();

        if (testCase.ExpectedErrors is null)
        {
            JsonAssert.Equivalent(testCase.Json!, ModelTransformer.TransformModulesToModel(files, "1.2").ToJson());
            return;
        }

        var exception = Assert.Throws<ModuleTransformationException>(() => ModelTransformer.TransformModulesToModel(files, "1.2"));
        Assert.Equal(ErrorAssert.ExpectedAggregate(testCase.ExpectedErrors, Kind, includeLocation: true), exception.Message);
        ErrorAssert.AllMatch(testCase.ExpectedErrors, exception.Errors);
    }

    [Theory]
    [MemberData(nameof(SemanticData))]
    public void ReportsSemanticErrorsInModules(int index, string name)
    {
        _ = name;
        var testCase = Semantic[index];
        var dsl = new Regex("model\n {2}schema 1\\.1").Replace(testCase.Dsl, "module test\n", 1);

        // Like the JS test, a case that combines without errors is not a failure: some checks only apply to whole models.
        var exception = Record.Exception(() => ModelTransformer.TransformModulesToModel([new ModuleFile("test.fga", dsl)], "1.2"));
        if (exception is null)
        {
            return;
        }

        var moduleException = Assert.IsType<ModuleTransformationException>(exception);
        var expected = testCase.ExpectedErrors.Select(e => new ExpectedError
        {
            Msg = e.Msg,
            Line = e.Line,
            Column = e.Column,
            Metadata = new ExpectedMetadata { Symbol = e.Metadata?.Symbol, ErrorType = e.Metadata?.ErrorType, Module = "test" },
        }).ToList();
        Assert.Equal(ErrorAssert.ExpectedAggregate(expected, Kind, includeLocation: true), moduleException.Message);
        ErrorAssert.AllMatch(expected, moduleException.Errors);
    }

    [Fact]
    public void AcceptsCustomSchemaVersion()
    {
        var model = ModelTransformer.TransformModulesToModel([new ModuleFile("core.fga", "module core\n  type user")], "1.1");

        Assert.Equal("1.1", model.SchemaVersion);
    }

    [Fact]
    public void NullArgumentsThrow()
    {
        Assert.Equal("files", Assert.Throws<ArgumentNullException>(() => ModelTransformer.TransformModulesToModel(null!, "1.2")).ParamName);
        Assert.Equal("schemaVersion", Assert.Throws<ArgumentNullException>(() => ModelTransformer.TransformModulesToModel([], null!)).ParamName);
        Assert.Equal("files", Assert.Throws<ArgumentException>(() => ModelTransformer.TransformModulesToModel([null!], "1.2")).ParamName);
        Assert.Equal("name", Assert.Throws<ArgumentNullException>(() => new ModuleFile(null!, "")).ParamName);
        Assert.Equal("contents", Assert.Throws<ArgumentNullException>(() => new ModuleFile("a.fga", null!)).ParamName);
    }

    private static string Kind(ExpectedError error)
        => error.Type == "syntax" ? "syntax" : error.Metadata?.ErrorType ?? "transformation-error";
}
