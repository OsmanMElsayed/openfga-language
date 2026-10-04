using OpenFga.Language.Parsing;
using OpenFga.Sdk.Model;

namespace OpenFga.Language.Tests;

public class ConditionTypeNamesTests
{
    [Theory]
    [InlineData("int", TypeName.TYPENAMEINT)]
    [InlineData("string", TypeName.TYPENAMESTRING)]
    [InlineData("ipaddress", TypeName.TYPENAMEIPADDRESS)]
    [InlineData("map", TypeName.TYPENAMEMAP)]
    [InlineData("list", TypeName.TYPENAMELIST)]
    public void FromDslMapsDslNamesToSdkEnum(string dsl, TypeName expected)
    {
        Assert.Equal(expected, ConditionTypeNames.FromDsl(dsl));
    }

    [Theory]
    [InlineData("INT")]
    [InlineData("Int")]
    public void FromDslIsCaseInsensitiveOnInput(string dsl)
    {
        Assert.Equal(TypeName.TYPENAMEINT, ConditionTypeNames.FromDsl(dsl));
    }

    [Theory]
    [InlineData("")]
    [InlineData("notatype")]
    [InlineData("unspecified")]
    public void FromDslFallsBackToUnspecified(string dsl)
    {
        Assert.Equal(TypeName.TYPENAMEUNSPECIFIED, ConditionTypeNames.FromDsl(dsl));
    }

    [Theory]
    [InlineData(TypeName.TYPENAMEINT, "int")]
    [InlineData(TypeName.TYPENAMEIPADDRESS, "ipaddress")]
    [InlineData(TypeName.TYPENAMEMAP, "map")]
    public void ToDslLowercasesAndStripsPrefix(TypeName typeName, string expected)
    {
        Assert.Equal(expected, ConditionTypeNames.ToDsl(typeName));
    }

    [Fact]
    public void EverySdkTypeNameRoundTrips()
    {
        foreach (var typeName in Enum.GetValues<TypeName>())
        {
            Assert.Equal(typeName, ConditionTypeNames.FromDsl(ConditionTypeNames.ToDsl(typeName)));
        }
    }
}
