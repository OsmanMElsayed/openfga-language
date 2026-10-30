using OpenFga.Language.Errors;
using OpenFga.Language.Utilities;
using OpenFga.Language.Validation;
using OpenFga.Sdk.Model;

namespace OpenFga.Language.Tests.Validation;

public class ValidationBuildingBlocksTests
{
    private static readonly string[] Lines =
    [
        "model",
        "  schema 1.1",
        "type user # module: core, file: core.fga",
        "type document",
        "  relations",
        "    define   viewer: [user, group#member] or editor",
        "    define parent: [folder]",
        "    define can_view: viewer from parent",
        "extend type document",
        "condition less(x: int) {",
        "condition less_than(x: int) {",
    ];

    [Fact]
    public void FindWordMatchesWholeWordsLikeJs()
    {
        Assert.Equal(22, TextSearch.FindWord(Lines[5], "user"));
        Assert.Equal(-1, TextSearch.FindWord(Lines[5], "use"));
        Assert.Equal(-1, TextSearch.FindWord(null, "user"));
    }

    [Fact]
    public void FindWordReturnsMinusOneForInvalidPattern()
    {
        Assert.Equal(-1, TextSearch.FindWord("define a: [b]", "a("));
        Assert.Equal(0, TextSearch.FindWordOrZero("define a: [b]", "[unclosed"));
    }

    [Fact]
    public void ResolversMarkTheValueNotTheKey()
    {
        Assert.Equal(22, TextSearch.ResolveAfterColon(Lines[5], "user", fallback: 99));
        Assert.Equal(99, TextSearch.ResolveAfterColon("no colon here", "user", fallback: 99));
        Assert.Equal(33, TextSearch.ResolveAfterFrom(Lines[7], "parent"));
    }

    [Fact]
    public void ResolveAfterFromMatchesWholeWordsAndClampsMisses()
    {
        const string line = "    define can_view: viewer from parent_org or parent";
        Assert.Equal(line.LastIndexOf("parent", StringComparison.Ordinal), TextSearch.ResolveAfterFrom(line, "parent"));
        Assert.Equal(0, TextSearch.ResolveAfterFrom("    define can_view: viewer from parent", "missing"));
        Assert.Equal(0, TextSearch.ResolveAfterFrom("    define can_view: viewer", "viewer"));
    }

    [Fact]
    public void LineNumbersFindDefinitions()
    {
        Assert.Equal(2, LineNumbers.GetTypeLine("user", Lines));
        Assert.Equal(3, LineNumbers.GetTypeLine("document", Lines));
        Assert.Equal(8, LineNumbers.GetTypeLine("document", Lines, extension: true));
        Assert.Equal(-1, LineNumbers.GetTypeLine("doc", Lines));
        Assert.Equal(5, LineNumbers.GetRelationLine("viewer", Lines, 3));
        Assert.Equal(-1, LineNumbers.GetRelationLine("viewer", Lines, 6));
        Assert.Equal(9, LineNumbers.GetConditionLine("less", Lines));
        Assert.Equal(10, LineNumbers.GetConditionLine("less_than", Lines));
        Assert.Equal(1, LineNumbers.GetSchemaLine("1.1", Lines));
        Assert.Equal(0, LineNumbers.GetSchemaLine("1.2", Lines));
        Assert.Null(LineNumbers.GetTypeLine("user", null));
    }

    [Fact]
    public void TypeRestrictionsToText()
    {
        Assert.Equal("user:* with c", TypeRestrictions.ToText(new RelationReference { Type = "user", Wildcard = new object(), Condition = "c" }));
        Assert.Equal("group#member", TypeRestrictions.ToText(new RelationReference { Type = "group", Relation = "member" }));
        Assert.Equal("group#member with cond", TypeRestrictions.ToText(new RelationReference { Type = "group", Relation = "member", Condition = "cond" }));
        Assert.Equal("user", TypeRestrictions.ToText(new RelationReference { Type = "user", Relation = string.Empty }));
    }

    [Fact]
    public void CollectorComputesRangesFromLines()
    {
        var errors = new List<ModelValidationError>();
        var collector = new ValidationErrorCollector(errors, Lines);

        collector.RaiseInvalidType("folder", "document", "parent", new Origin("a.fga", "m"), 6);

        var error = Assert.Single(errors);
        Assert.Equal("`folder` is not a valid type.", error.Message);
        Assert.Equal(new SourceRange(6, 6, 20, 26), error.Range);
        Assert.Equal("a.fga", error.File);
        Assert.Equal("m", error.Module);
        Assert.Equal("document", error.TypeName);
        Assert.Equal("parent", error.RelationName);
        Assert.Equal(ValidationErrorType.InvalidType, error.ErrorType);
    }

    [Fact]
    public void CollectorOmitsRangeWithoutLines()
    {
        var errors = new List<ModelValidationError>();
        new ValidationErrorCollector(errors, null).RaiseNoEntryPoint("viewer", "document", default, 3);

        var error = Assert.Single(errors);
        Assert.Equal("`viewer` is an impossible relation for `document` (no entrypoint).", error.Message);
        Assert.Null(error.Range);
        Assert.Equal("viewer", error.RelationName);
    }

    [Fact]
    public void CollectorAssignableRelationUsesBracketText()
    {
        var errors = new List<ModelValidationError>();
        new ValidationErrorCollector(errors, ["    define viewer: []"]).RaiseAssignableRelationMustHaveTypes(0);

        Assert.Equal("assignable relation '[' must have types", Assert.Single(errors).Message);
    }

    [Fact]
    public void CollectorSkipsMissingDefinitionWhenRelationIsValid()
    {
        var errors = new List<ModelValidationError>();
        new ValidationErrorCollector(errors, null).RaiseInvalidRelationError("viewer", "doc", "can_view", ["viewer"], null, default);

        Assert.Empty(errors);
    }
}
