using OpenFga.Language.Errors;

namespace OpenFga.Language.Tests;

public class ErrorTypesTests
{
    [Fact]
    public void SourceRangeHasValueEquality()
    {
        var a = new SourceRange(1, 1, 2, 5);
        var b = new SourceRange(1, 1, 2, 5);

        Assert.Equal(a, b);
        Assert.True(a == b);
        Assert.False(a != b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
        Assert.NotEqual(a, new SourceRange(1, 1, 2, 6));
    }

    [Fact]
    public void SyntaxErrorToStringIncludesLocation()
    {
        var error = new SyntaxError("mismatched input 'x'", new SourceRange(3, 3, 7, 8), "x");

        Assert.Equal("syntax error at line=3, column=7: mismatched input 'x'", error.ToString());
    }

    [Fact]
    public void ValidationErrorWithoutRangeOmitsLocation()
    {
        var error = new ModelValidationError("invalid schema 1.3", null, null, ValidationErrorType.InvalidSchema, "1.3", null, null, null, null, null);

        Assert.Equal("invalid-schema error: invalid schema 1.3", error.ToString());
    }

    [Fact]
    public void AggregateMessageMatchesJsFormat()
    {
        var exception = new DslSyntaxException(
        [
            new SyntaxError("first", new SourceRange(0, 0, 1, 2), "a"),
            new SyntaxError("second", new SourceRange(4, 4, 0, 3), "b"),
        ]);

        Assert.Equal(
            "2 errors occurred:\n\t* syntax error at line=0, column=1: first\n\t* syntax error at line=4, column=0: second\n\n",
            exception.Message);
        Assert.Equal(2, exception.Errors.Count);
    }

    [Fact]
    public void SingleErrorMessageIsSingular()
    {
        var exception = new ModFileValidationException([LanguageErrors.ModFile("missing schema field", default)]);

        Assert.Equal("1 error occurred:\n\t* validation error at line=0, column=0: missing schema field\n\n", exception.Message);
    }

    [Fact]
    public void ModuleTransformationErrorKind()
    {
        var error = new ModuleTransformationError("duplicate condition a", "org.fga", new SourceRange(12, 12, 10, 17), "a");

        Assert.Equal("transformation-error error at line=12, column=10: duplicate condition a", error.ToString());
        Assert.Equal("org.fga", error.File);
    }

    [Theory]
    [InlineData(ValidationErrorType.SchemaVersionRequired, "schema-version-required")]
    [InlineData(ValidationErrorType.SchemaVersionUnsupported, "schema-version-unsupported")]
    [InlineData(ValidationErrorType.ReservedTypeKeywords, "reserved-type-keywords")]
    [InlineData(ValidationErrorType.ReservedRelationKeywords, "reserved-relation-keywords")]
    [InlineData(ValidationErrorType.SelfError, "self-error")]
    [InlineData(ValidationErrorType.InvalidName, "invalid-name")]
    [InlineData(ValidationErrorType.MissingDefinition, "missing-definition")]
    [InlineData(ValidationErrorType.InvalidRelationType, "invalid-relation-type")]
    [InlineData(ValidationErrorType.InvalidRelationOnTupleset, "invalid-relation-on-tupleset")]
    [InlineData(ValidationErrorType.InvalidType, "invalid-type")]
    [InlineData(ValidationErrorType.RelationNoEntrypoint, "relation-no-entry-point")]
    [InlineData(ValidationErrorType.TuplesetNotDirect, "tupleuserset-not-direct")]
    [InlineData(ValidationErrorType.DuplicatedError, "duplicated-error")]
    [InlineData(ValidationErrorType.RequireSchema1_0, "allowed-type-schema-10")]
    [InlineData(ValidationErrorType.AssignableRelationsMustHaveType, "assignable-relation-must-have-type")]
    [InlineData(ValidationErrorType.AllowedTypesNotValidOnSchema1_0, "allowed-type-not-valid-on-schema-1_0")]
    [InlineData(ValidationErrorType.InvalidSchema, "invalid-schema")]
    [InlineData(ValidationErrorType.InvalidSyntax, "invalid-syntax")]
    [InlineData(ValidationErrorType.TypeRestrictionCannotHaveWildcardAndRelation, "type-wildcard-relation")]
    [InlineData(ValidationErrorType.ConditionNotDefined, "condition-not-defined")]
    [InlineData(ValidationErrorType.ConditionNotUsed, "condition-not-used")]
    [InlineData(ValidationErrorType.DifferentNestedConditionName, "different-nested-condition-name")]
    [InlineData(ValidationErrorType.MultipleModulesInFile, "multiple-modules-in-file")]
    public void ErrorTypeCodesMatchSharedFixtures(ValidationErrorType type, string code)
        => Assert.Equal(code, ValidationErrorTypeCodes.ToCode(type));

    [Fact]
    public void AllExceptionsDeriveFromCommonBase()
    {
        Assert.IsAssignableFrom<OpenFgaLanguageException>(new DslTransformationException("x"));
        Assert.IsAssignableFrom<OpenFgaLanguageException>(new ModelValidationException([]));
        Assert.IsAssignableFrom<OpenFgaLanguageException>(new ModuleTransformationException([]));
    }
}
