using OpenFga.Language.Tests.Fixtures;
using OpenFga.Sdk.Model;

namespace OpenFga.Language.Tests;

public class ModelToDslTests
{
    private static readonly IReadOnlyList<TransformerCase> Transformer = TestData.TransformerCases();
    private static readonly IReadOnlyList<JsonCase> Invalid = TestData.JsonSyntaxTransformerCases();
    private static readonly IReadOnlyList<ModuleCase> Modules = TestData.ModuleCases().Where(c => c.Dsl is not null && c.Json is not null).ToList();

    public static TheoryData<int, string> TransformerData => TestData.Indexes(Transformer, c => c.Name);

    public static TheoryData<int, string> InvalidData => TestData.Indexes(Invalid, c => c.Name);

    public static TheoryData<int, string> ModuleData => TestData.Indexes(Modules, c => c.Name);

    [Theory]
    [MemberData(nameof(TransformerData))]
    public void TransformsJsonToDsl(int index, string name)
    {
        _ = name;
        var testCase = Transformer[index];

        Assert.Equal(testCase.Dsl, ModelTransformer.TransformJsonToDsl(testCase.Json));
    }

    [Theory]
    [MemberData(nameof(TransformerData))]
    public void TransformsModelToDsl(int index, string name)
    {
        _ = name;
        var testCase = Transformer[index];

        Assert.Equal(testCase.Dsl, ModelTransformer.TransformModelToDsl(AuthorizationModel.FromJson(testCase.Json)));
    }

    [Theory]
    [MemberData(nameof(InvalidData))]
    public void ReportsModelsTheDslCannotExpress(int index, string name)
    {
        _ = name;
        var testCase = Invalid[index];

        if (string.IsNullOrEmpty(testCase.ErrorMessage))
        {
            ModelTransformer.TransformJsonToDsl(testCase.Json);
            return;
        }

        var exception = Assert.Throws<DslTransformationException>(() => ModelTransformer.TransformJsonToDsl(testCase.Json));
        Assert.Equal(testCase.ErrorMessage, exception.Message);
    }

    [Theory]
    [MemberData(nameof(ModuleData))]
    public void TransformsModularJsonToDsl(int index, string name)
    {
        _ = name;
        var testCase = Modules[index];

        Assert.Equal(testCase.Dsl, ModelTransformer.TransformJsonToDsl(testCase.Json!));
    }

    [Theory]
    [MemberData(nameof(ModuleData))]
    public void TransformsModularJsonToDslWithSourceInformation(int index, string name)
    {
        _ = name;
        var testCase = Modules[index];

        Assert.Equal(
            testCase.DslWithSourceInfo,
            ModelTransformer.TransformJsonToDsl(testCase.Json!, new TransformOptions { IncludeSourceInformation = true }));
    }

    [Fact]
    public void TransformModelToDslDoesNotMutateInput()
    {
        const string json = """{"schema_version":"1.1","type_definitions":[{"type":"user"},{"type":"doc","relations":{"viewer":{"union":{"child":[{"computedUserset":{"relation":"owner"}},{"this":{}}]}},"owner":{"this":{}}},"metadata":{"relations":{"viewer":{"directly_related_user_types":[{"type":"user"}]},"owner":{"directly_related_user_types":[{"type":"user"}]}}}}]}""";
        var model = AuthorizationModel.FromJson(json);
        var before = model.ToJson();

        var dsl = ModelTransformer.TransformModelToDsl(model);

        Assert.Contains("define viewer: [user] or owner", dsl, StringComparison.Ordinal);
        Assert.NotNull(model.TypeDefinitions[1].Relations!["viewer"].Union!.Child[0].ComputedUserset);
        Assert.Equal(before, model.ToJson());
    }

    [Theory]
    // A null entry in type_definitions.
    [InlineData("""{"schema_version":"1.1","type_definitions":[{"type":"user"},null,{"type":"doc","relations":{"viewer":{"this":{}}},"metadata":{"relations":{"viewer":{"directly_related_user_types":[{"type":"user"}]}}}}]}""")]
    // A null child inside a union.
    [InlineData("""{"schema_version":"1.1","type_definitions":[{"type":"user"},{"type":"doc","relations":{"viewer":{"union":{"child":[{"this":{}},null]}}},"metadata":{"relations":{"viewer":{"directly_related_user_types":[{"type":"user"}]}}}}]}""")]
    // A null entry in directly_related_user_types.
    [InlineData("""{"schema_version":"1.1","type_definitions":[{"type":"user"},{"type":"doc","relations":{"viewer":{"this":{}}},"metadata":{"relations":{"viewer":{"directly_related_user_types":[null,{"type":"user"}]}}}}]}""")]
    // A null value in conditions.
    [InlineData("""{"schema_version":"1.1","type_definitions":[{"type":"user"},{"type":"doc","relations":{"viewer":{"this":{}}},"metadata":{"relations":{"viewer":{"directly_related_user_types":[{"type":"user"}]}}}}],"conditions":{"c":null}}""")]
    public void MalformedJsonModelReportsOnlyDocumentedExceptions(string json)
    {
        var exception = Record.Exception(() => ModelTransformer.TransformJsonToDsl(json));

        Assert.True(
            exception is null or ArgumentException or DslTransformationException,
            $"Unexpected exception: {exception}");
    }

    [Fact]
    public void InvalidJsonThrowsArgumentException()
    {
        var exception = Assert.Throws<ArgumentException>(() => ModelTransformer.TransformJsonToDsl("{not json"));

        Assert.Equal("json", exception.ParamName);
        Assert.IsAssignableFrom<System.Text.Json.JsonException>(exception.InnerException);
        Assert.Equal("json", Assert.Throws<ArgumentException>(() => ModelTransformer.TransformJsonToDsl("null")).ParamName);
    }

    [Fact]
    public void NullArgumentsThrowArgumentNullException()
    {
        var model = new AuthorizationModel { SchemaVersion = "1.1", TypeDefinitions = [] };

        Assert.Equal("model", Assert.Throws<ArgumentNullException>(() => ModelTransformer.TransformModelToDsl(null!)).ParamName);
        Assert.Equal("options", Assert.Throws<ArgumentNullException>(() => ModelTransformer.TransformModelToDsl(model, null!)).ParamName);
        Assert.Equal("json", Assert.Throws<ArgumentNullException>(() => ModelTransformer.TransformJsonToDsl(null!)).ParamName);
        Assert.Equal("options", Assert.Throws<ArgumentNullException>(() => ModelTransformer.TransformJsonToDsl("{}", null!)).ParamName);
    }

    [Fact]
    public void DslRoundTripsThroughModel()
    {
        foreach (var testCase in Transformer)
        {
            Assert.Equal(testCase.Dsl, ModelTransformer.TransformModelToDsl(ModelTransformer.TransformDslToModel(testCase.Dsl)));
        }
    }
}
