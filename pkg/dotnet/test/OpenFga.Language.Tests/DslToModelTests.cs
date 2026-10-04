using OpenFga.Language.Errors;
using OpenFga.Language.Tests.Fixtures;
using OpenFga.Language.Transformation;

namespace OpenFga.Language.Tests;

public class DslToModelTests
{
    private static readonly IReadOnlyList<TransformerCase> Transformer = TestData.TransformerCases();
    private static readonly IReadOnlyList<DslCase> Syntax = TestData.DslSyntaxCases();

    public static TheoryData<int, string> TransformerData => TestData.Indexes(Transformer, c => c.Name);

    public static TheoryData<int, string> SyntaxData => TestData.Indexes(Syntax, c => c.Name);

    [Theory]
    [MemberData(nameof(TransformerData))]
    public void TransformsDslToJson(int index, string name)
    {
        _ = name;
        var testCase = Transformer[index];

        JsonAssert.Equivalent(testCase.Json, ModelTransformer.TransformDslToJson(testCase.Dsl));
    }

    [Theory]
    [MemberData(nameof(TransformerData))]
    public void TransformsDslToModel(int index, string name)
    {
        _ = name;
        var testCase = Transformer[index];

        var model = ModelTransformer.TransformDslToModel(testCase.Dsl);

        Assert.Null(model.Id);
        Assert.NotNull(model.TypeDefinitions);
        JsonAssert.Equivalent(testCase.Json, model.ToJson());
    }

    [Theory]
    [MemberData(nameof(TransformerData))]
    public void CrlfDslTransformsLikeLf(int index, string name)
    {
        _ = name;
        var testCase = Transformer[index];

        Assert.Equal(
            ModelTransformer.TransformDslToJson(testCase.Dsl),
            ModelTransformer.TransformDslToJson(testCase.Dsl.Replace("\n", "\r\n", StringComparison.Ordinal)));
    }

    [Theory]
    [MemberData(nameof(SyntaxData))]
    public void ReportsSyntaxErrors(int index, string name)
    {
        _ = name;
        var testCase = Syntax[index];

        if (testCase.ExpectedErrors.Count == 0)
        {
            ModelTransformer.TransformDslToModel(testCase.Dsl);
            return;
        }

        var exception = Assert.Throws<DslSyntaxException>(() => ModelTransformer.TransformDslToModel(testCase.Dsl));
        Assert.Equal(ErrorAssert.ExpectedAggregate(testCase.ExpectedErrors, _ => "syntax", includeLocation: true), exception.Message);
        ErrorAssert.AllMatch(testCase.ExpectedErrors, exception.Errors);
    }

    [Theory]
    [InlineData("")]
    [InlineData("type")]
    [InlineData("type user")]
    [InlineData("model")]
    [InlineData("model\n  schema")]
    [InlineData("model\n  schema 1.1\ntype")]
    [InlineData("model\n  schema 1.1\ntype user\n  relations")]
    [InlineData("model\n  schema 1.1\ntype user\n  relations\n    define")]
    [InlineData("model\n  schema 1.1\ntype user\n  relations\n    define viewer: [user] or")]
    [InlineData("model\n  schema 1.1\ntype user\n  relations\n    define viewer: ([user] or (a and")]
    [InlineData("model\n  schema 1.1\ntype user\n  relations\n    define viewer: [user with")]
    [InlineData("model\n  schema 1.1\ntype user\ncondition c(x: int) {")]
    [InlineData("model\n  schema 1.1\ntype user\ncondition (x: int) {\n  x < 1\n}")]
    [InlineData("module\ntype user")]
    [InlineData("module m\nextend type")]
    [InlineData("model\n  schema 1.1\ntype user\n  relations\n    define a: b from")]
    public void MalformedDslThrowsOnlyDslSyntaxException(string dsl)
    {
        var exception = Record.Exception(() => ModelTransformer.TransformDslToModel(dsl));

        Assert.IsType<DslSyntaxException>(exception);
    }

    [Fact]
    public void ExtendOutsideModuleIsASyntaxError()
    {
        var exception = Assert.Throws<DslSyntaxException>(() => ModelTransformer.TransformDslToModel("model\n  schema 1.2\nextend type user"));

        Assert.Contains(exception.Errors, e => e.Message == "extend can only be used in a modular model");
    }

    [Fact]
    public void NullDslThrowsArgumentNullException()
    {
        Assert.Equal("dsl", Assert.Throws<ArgumentNullException>(() => ModelTransformer.TransformDslToModel(null!)).ParamName);
        Assert.Equal("dsl", Assert.Throws<ArgumentNullException>(() => ModelTransformer.TransformDslToJson(null!)).ParamName);
    }
}
