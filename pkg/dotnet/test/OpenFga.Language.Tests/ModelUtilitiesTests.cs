using OpenFga.Language.Utilities;
using OpenFga.Sdk.Model;

namespace OpenFga.Language.Tests;

public class ModelUtilitiesTests
{
    private static readonly TypeDefinition WithModule = new()
    {
        Type = "type1",
        Relations = new() { ["relation1"] = new(), ["relation2"] = new(), ["relation3"] = new(), ["relation4"] = new() },
        Metadata = new Metadata
        {
            Module = "type_module1",
            Relations = new()
            {
                ["relation1"] = new RelationMetadata { Module = "module1" },
                ["relation2"] = new RelationMetadata { Module = "" },
                ["relation3"] = new RelationMetadata(),
                ["relation5"] = new RelationMetadata(),
            },
        },
    };

    private static readonly TypeDefinition WithoutModule = new() { Type = "type2", Relations = new() { ["relation7"] = new() } };

    [Theory]
    [InlineData("relation1", "module1")]
    [InlineData("relation2", "type_module1")]
    [InlineData("relation3", "type_module1")]
    [InlineData("relation4", "type_module1")]
    public void GetsModuleForRelation(string relation, string expected)
        => Assert.Equal(expected, ModelUtilities.GetModuleForObjectTypeRelation(WithModule, relation));

    [Fact]
    public void ReturnsNullWhenNeitherRelationNorTypeHasAModule()
        => Assert.Null(ModelUtilities.GetModuleForObjectTypeRelation(WithoutModule, "relation7"));

    [Theory]
    [InlineData("relation5")]
    [InlineData("relation6")]
    public void MissingRelationThrowsArgumentException(string relation)
    {
        var exception = Assert.Throws<ArgumentException>(() => ModelUtilities.GetModuleForObjectTypeRelation(WithModule, relation));

        Assert.StartsWith($"relation {relation} does not exist in type type1", exception.Message, StringComparison.Ordinal);
        Assert.Equal("relation", exception.ParamName);
    }

    [Fact]
    public void DetectsAssignableRelations()
    {
        Assert.True(ModelUtilities.IsRelationAssignable(new Userset { This = new object() }));
        Assert.True(ModelUtilities.IsRelationAssignable(new Userset { Union = new Usersets { Child = [new Userset { This = new object() }] } }));
        Assert.True(ModelUtilities.IsRelationAssignable(new Userset { Intersection = new Usersets { Child = [new Userset { This = new object() }] } }));
        Assert.True(ModelUtilities.IsRelationAssignable(new Userset { Difference = new Difference { Base = new Userset { This = new object() }, Subtract = new Userset() } }));
        Assert.True(ModelUtilities.IsRelationAssignable(new Userset { Difference = new Difference { Base = new Userset(), Subtract = new Userset { This = new object() } } }));
        Assert.False(ModelUtilities.IsRelationAssignable(new Userset { Union = new Usersets { Child = [new Userset { Intersection = new Usersets { Child = [new Userset()] } }] } }));
        Assert.False(ModelUtilities.IsRelationAssignable(new Userset { ComputedUserset = new ObjectRelation { Relation = "a" } }));
    }

    [Fact]
    public void NullArgumentsThrowArgumentNullException()
    {
        Assert.Equal("relationDefinition", Assert.Throws<ArgumentNullException>(() => ModelUtilities.IsRelationAssignable(null!)).ParamName);
        Assert.Equal("typeDefinition", Assert.Throws<ArgumentNullException>(() => ModelUtilities.GetModuleForObjectTypeRelation(null!, "a")).ParamName);
        Assert.Equal("relation", Assert.Throws<ArgumentNullException>(() => ModelUtilities.GetModuleForObjectTypeRelation(WithModule, null!)).ParamName);
    }
}
