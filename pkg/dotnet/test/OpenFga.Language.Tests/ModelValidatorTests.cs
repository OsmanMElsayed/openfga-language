using System.Text.Json;
using OpenFga.Language.Errors;
using OpenFga.Language.Tests.Fixtures;
using OpenFga.Language.Transformation;
using OpenFga.Language.Validation;
using OpenFga.Sdk.Model;

namespace OpenFga.Language.Tests;

public class ModelValidatorTests
{
    private static readonly IReadOnlyList<DslCase> Syntax = TestData.DslSyntaxCases();
    private static readonly IReadOnlyList<DslCase> Semantic = TestData.DslSemanticCases();
    private static readonly IReadOnlyList<JsonCase> Json = TestData.JsonValidationCases();

    public static TheoryData<int, string> SyntaxData => TestData.Indexes(Syntax, c => c.Name);

    public static TheoryData<int, string> SemanticData => TestData.Indexes(Semantic, c => c.Name);

    public static TheoryData<int, string> JsonData => TestData.Indexes(Json, c => c.Name);

    [Theory]
    [MemberData(nameof(SyntaxData))]
    public void ValidateDslReportsSyntaxErrors(int index, string name)
    {
        _ = name;
        var testCase = Syntax[index];

        if (testCase.ExpectedErrors.Count == 0)
        {
            ModelValidator.Validate(testCase.Dsl);
            return;
        }

        var exception = Assert.Throws<DslSyntaxException>(() => ModelValidator.Validate(testCase.Dsl));
        Assert.Equal(ErrorAssert.ExpectedAggregate(testCase.ExpectedErrors, _ => "syntax", includeLocation: true), exception.Message);
        ErrorAssert.AllMatch(testCase.ExpectedErrors, exception.Errors);
    }

    [Theory]
    [MemberData(nameof(SemanticData))]
    public void ValidateDslReportsSemanticErrors(int index, string name)
    {
        _ = name;
        var testCase = Semantic[index];

        if (testCase.ExpectedErrors.Count == 0)
        {
            ModelValidator.Validate(testCase.Dsl);
            return;
        }

        var exception = Assert.Throws<ModelValidationException>(() => ModelValidator.Validate(testCase.Dsl));
        Assert.Equal(ErrorAssert.ExpectedAggregate(testCase.ExpectedErrors, e => e.Metadata?.ErrorType ?? "validation", includeLocation: true), exception.Message);
        ErrorAssert.AllMatch(testCase.ExpectedErrors, exception.Errors);
    }

    [Theory]
    [MemberData(nameof(SemanticData))]
    public void ValidateModelFromDslReportsSemanticErrorsWithoutLocations(int index, string name)
    {
        _ = name;
        var testCase = Semantic[index];
        var model = ModelTransformer.TransformDslToModel(testCase.Dsl);

        if (testCase.ExpectedErrors.Count == 0)
        {
            ModelValidator.Validate(model);
            return;
        }

        var exception = Assert.Throws<ModelValidationException>(() => ModelValidator.Validate(model));
        Assert.Equal(ErrorAssert.ExpectedAggregate(testCase.ExpectedErrors, e => e.Metadata?.ErrorType ?? "validation", includeLocation: false), exception.Message);
        Assert.Equal(testCase.ExpectedErrors.Count, exception.Errors.Count);
        for (var i = 0; i < exception.Errors.Count; i++)
        {
            Assert.Null(exception.Errors[i].Range);
            if (testCase.ExpectedErrors[i].Metadata?.Symbol is { } symbol)
            {
                Assert.Equal(symbol, exception.Errors[i].Symbol);
            }
        }
    }

    [Theory]
    [MemberData(nameof(JsonData))]
    public void ValidateModelReportsErrors(int index, string name)
    {
        _ = name;
        var testCase = Json[index];
        var model = AuthorizationModel.FromJson(testCase.Json);

        if (testCase.ExpectedErrors.Count == 0)
        {
            ModelValidator.Validate(model);
            return;
        }

        var exception = Assert.Throws<ModelValidationException>(() => ModelValidator.Validate(model));
        Assert.Equal(ErrorAssert.ExpectedAggregate(testCase.ExpectedErrors, e => e.Metadata?.ErrorType ?? "validation", includeLocation: false), exception.Message);
        ErrorAssert.AllMatch(testCase.ExpectedErrors, exception.Errors);
    }

    [Theory]
    [InlineData("")]
    [InlineData("type user")]
    [InlineData("model\n  schema 1.1\ntype user\n  relations\n    define viewer: [user] or")]
    [InlineData("model\n  schema 1.1\ntype user\n  relations\n    define viewer: ([user] or (a and")]
    [InlineData("model\n  schema 1.1\ntype user\ncondition c(x: int) {")]
    public void ValidateMalformedDslThrowsOnlyDslSyntaxException(string dsl)
        => Assert.IsType<DslSyntaxException>(Record.Exception(() => ModelValidator.Validate(dsl)));

    [Theory]
    // A restriction with no type.
    [InlineData("""{"schema_version":"1.1","type_definitions":[{"type":"user"},{"type":"doc","relations":{"viewer":{"this":{}}},"metadata":{"relations":{"viewer":{"directly_related_user_types":[{}]}}}}]}""")]
    // A null entry in type_definitions.
    [InlineData("""{"schema_version":"1.1","type_definitions":[{"type":"user"},null,{"type":"doc","relations":{"viewer":{"this":{}}},"metadata":{"relations":{"viewer":{"directly_related_user_types":[{"type":"user"}]}}}}]}""")]
    // A null child inside a union.
    [InlineData("""{"schema_version":"1.1","type_definitions":[{"type":"user"},{"type":"doc","relations":{"viewer":{"union":{"child":[{"this":{}},null]}}},"metadata":{"relations":{"viewer":{"directly_related_user_types":[{"type":"user"}]}}}}]}""")]
    // A null entry in directly_related_user_types.
    [InlineData("""{"schema_version":"1.1","type_definitions":[{"type":"user"},{"type":"doc","relations":{"viewer":{"this":{}}},"metadata":{"relations":{"viewer":{"directly_related_user_types":[null,{"type":"user"}]}}}}]}""")]
    public void MalformedModelReportsOnlyDocumentedExceptions(string json)
    {
        var model = AuthorizationModel.FromJson(json);

        var exception = Record.Exception(() => ModelValidator.Validate(model));

        Assert.True(
            exception is null or ArgumentException or ModelValidationException,
            $"Unexpected exception: {exception}");
    }

    [Theory]
    [InlineData("user\n", "viewer")]
    [InlineData("doc", "viewer\n")]
    public void TrailingNewlineInNameIsInvalid(string typeName, string relationName)
    {
        var type = JsonSerializer.Serialize(typeName);
        var relation = JsonSerializer.Serialize(relationName);
        var json = "{\"schema_version\":\"1.1\",\"type_definitions\":[{\"type\":\"user\"},{\"type\":" + type
            + ",\"relations\":{" + relation + ":{\"this\":{}}},\"metadata\":{\"relations\":{" + relation
            + ":{\"directly_related_user_types\":[{\"type\":\"user\"}]}}}}]}";
        var model = AuthorizationModel.FromJson(json);

        var exception = Assert.Throws<ModelValidationException>(() => ModelValidator.Validate(model));

        Assert.Contains(exception.Errors, error => error.ErrorType == ValidationErrorType.InvalidName);
    }

    [Fact]
    public void ConditionedTuplesetRestrictionDoesNotCrashEntryPointAnalysis()
    {
        // JS throws a TypeError here (it indexes types by "folder with c"); .NET resolves the restriction's type.
        const string dsl = """
            model
              schema 1.1
            type user
            type folder
              relations
                define viewer: [user]
            type document
              relations
                define parent: [folder with c]
                define viewer: viewer from parent
            condition c(x: int) {
              x < 100
            }
            """;

        ModelValidator.Validate(dsl);
    }

    [Fact]
    public void CustomNamingRuleAppearsInMessage()
    {
        var options = new ModelValidationOptions { TypePattern = "^[a-z]{1,3}$" };

        var exception = Assert.Throws<ModelValidationException>(() => ModelValidator.Validate("model\n  schema 1.1\ntype document\n", options));

        var error = Assert.Single(exception.Errors);
        Assert.Equal(ValidationErrorType.InvalidName, error.ErrorType);
        Assert.Equal("type 'document' does not match naming rule: '^[a-z]{1,3}$'.", error.Message);
        Assert.Equal(new SourceRange(2, 2, 5, 13), error.Range);
    }

    [Fact]
    public void DefaultOptionsUseSharedRules()
    {
        var options = new ModelValidationOptions();

        Assert.Equal(@"^[^:#@\*\s]{1,254}$", options.TypePattern);
        Assert.Equal(@"^[^:#@\*\s]{1,50}$", options.RelationPattern);
    }

    [Fact]
    public void InvalidRegexThrowsArgumentException()
    {
        var exception = Assert.Throws<ArgumentException>(() => ModelValidator.Validate("model\n  schema 1.1\ntype user\n", new ModelValidationOptions { RelationPattern = "(" }));

        Assert.Equal("options", exception.ParamName);
        Assert.StartsWith("Incorrect relation regex specification for (", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void NullArgumentsThrowArgumentNullException()
    {
        var model = new AuthorizationModel { SchemaVersion = "1.1", TypeDefinitions = [] };

        Assert.Equal("dsl", Assert.Throws<ArgumentNullException>(() => ModelValidator.Validate((string)null!)).ParamName);
        Assert.Equal("model", Assert.Throws<ArgumentNullException>(() => ModelValidator.Validate((AuthorizationModel)null!)).ParamName);
        Assert.Equal("options", Assert.Throws<ArgumentNullException>(() => ModelValidator.Validate("model\n  schema 1.1\n", null!)).ParamName);
        Assert.Equal("options", Assert.Throws<ArgumentNullException>(() => ModelValidator.Validate(model, null!)).ParamName);
    }
}
