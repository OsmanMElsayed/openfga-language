using OpenFga.Sdk.Model;

namespace OpenFga.Language.Validation;

/// <summary>String form of type restrictions used by validation (JS getTypeRestrictionString / destructTupleToUserset).</summary>
internal static class TypeRestrictions
{
    /// <summary>Reserved condition name for inline expressions; it needs no entry in the model's conditions.</summary>
    public const string InlineExpressionCondition = "$expression";

    public static string ToText(RelationReference restriction)
    {
        var text = restriction.Type ?? string.Empty;
        if (restriction.Wildcard != null)
        {
            text += ":*";
        }
        else if (!string.IsNullOrEmpty(restriction.Relation))
        {
            text += "#" + restriction.Relation;
        }

        if (!string.IsNullOrEmpty(restriction.Condition))
        {
            text += " with " + restriction.Condition;
        }

        return text;
    }

}
