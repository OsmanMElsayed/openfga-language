using OpenFga.Language.Tests.Fixtures;
using OpenFga.Language.Validation;

namespace OpenFga.Language.Tests.Validation;

public class TupleSyntaxTests
{
    private static readonly IReadOnlyList<ObjectIdCase> ObjectIds = TestData.ObjectIdCases();

    private static readonly Dictionary<string, Func<string, bool>> Validators = new()
    {
        ["object"] = TupleSyntax.IsValidObject,
        ["user"] = TupleSyntax.IsValidUser,
        ["userset"] = TupleSyntax.IsValidUserset,
        ["userObject"] = TupleSyntax.IsValidUserObject,
        ["userWildcard"] = TupleSyntax.IsValidUserWildcard,
        ["type"] = TupleSyntax.IsValidType,
    };

    private static readonly string[] BadStructures =
    [
        "item::1", ":item:1", "item:1:", "item#relation", "item:1##relation", "#item:1", "item:1#",
        "ite#m:1", "it*em:1", "*:1", "item*thing", "item:*thing", "item:**",
    ];

    public static TheoryData<string, string> BadStructureData
    {
        get
        {
            var data = new TheoryData<string, string>();
            foreach (var validator in Validators.Keys)
            {
                foreach (var value in BadStructures)
                {
                    data.Add(validator, value);
                }
            }

            return data;
        }
    }

    public static TheoryData<int, string> ObjectIdData => TestData.Indexes(ObjectIds, c => c.Name);

    [Theory]
    [MemberData(nameof(BadStructureData))]
    public void RejectsBadStructure(string validator, string value) => Assert.False(Validators[validator](value));

    [Theory]
    [InlineData("object", "document:1", true)]
    [InlineData("object", "document1", false)]
    [InlineData("object", "document:1#relation", false)]
    [InlineData("object", "document:*", false)]
    [InlineData("user", "group:engineering#member", true)]
    [InlineData("user", "group:engineering", true)]
    [InlineData("user", "group:*", true)]
    [InlineData("user", "group", false)]
    [InlineData("userset", "group:engineering#member", true)]
    [InlineData("userset", "group:engineering", false)]
    [InlineData("userset", "group:*", false)]
    [InlineData("userset", "group", false)]
    [InlineData("userObject", "group:engineering", true)]
    [InlineData("userObject", "group:engineering#member", false)]
    [InlineData("userObject", "group:*", false)]
    [InlineData("userObject", "group", false)]
    [InlineData("userWildcard", "group:*", true)]
    [InlineData("userWildcard", "group:organization", false)]
    [InlineData("userWildcard", "group:engineering#member", false)]
    [InlineData("userWildcard", "group", false)]
    [InlineData("type", "folder", true)]
    [InlineData("type", "folder:1", false)]
    [InlineData("type", "folder:1#relation", false)]
    public void ValidatesTupleParts(string validator, string value, bool expected) => Assert.Equal(expected, Validators[validator](value));

    [Theory]
    [MemberData(nameof(ObjectIdData))]
    public void ValidatesObjectIds(int index, string name)
    {
        _ = name;
        Assert.Equal(ObjectIds[index].Valid, TupleSyntax.IsValidObjectId(ObjectIds[index].ObjectId));
    }

    [Fact]
    public void ValidatesRelationAndConditionNames()
    {
        Assert.True(TupleSyntax.IsValidRelation("viewer"));
        Assert.False(TupleSyntax.IsValidRelation("view#er"));
        Assert.False(TupleSyntax.IsValidRelation(new string('a', 51)));
        Assert.True(TupleSyntax.IsValidConditionName("is_valid_ip"));
        Assert.False(TupleSyntax.IsValidConditionName("is valid"));
    }

    [Fact]
    public void TrailingNewlineIsInvalid()
    {
        Assert.False(TupleSyntax.IsValidObject("document:1\n"));
        Assert.False(TupleSyntax.IsValidType("document\n"));
        Assert.False(TupleSyntax.IsValidRelation("viewer\n"));
        Assert.False(TupleSyntax.IsValidObjectId("1\n"));
        Assert.False(TupleSyntax.IsValidUser("user:anne\n"));
    }

    [Fact]
    public void NullThrowsArgumentNullException()
    {
        foreach (var validator in Validators.Values.Append(TupleSyntax.IsValidObjectId).Append(TupleSyntax.IsValidRelation).Append(TupleSyntax.IsValidConditionName))
        {
            Assert.Equal("value", Assert.Throws<ArgumentNullException>(() => validator(null!)).ParamName);
        }
    }
}
