using System;
using System.Collections.Generic;
using System.Linq;
using OpenFga.Language.Utilities;
using OpenFga.Sdk.Model;

namespace OpenFga.Language.Validation;

internal readonly struct Origin
{
    public Origin(string? file, string? module)
    {
        File = file;
        Module = module;
    }

    public string? File { get; }

    public string? Module { get; }
}

internal readonly struct DecodedRestriction
{
    public DecodedRestriction(string type, string? relation, bool isWildcard, string? condition)
    {
        Type = type;
        Relation = relation;
        IsWildcard = isWildcard;
        Condition = condition;
    }

    public string Type { get; }

    public string? Relation { get; }

    public bool IsWildcard { get; }

    public string? Condition { get; }
}

/// <summary>String form of type restrictions used by validation (JS getTypeRestrictionString / destructTupleToUserset).</summary>
internal static class TypeRestrictions
{
    /// <summary>Reserved condition name for inline expressions; it needs no entry in the model's conditions.</summary>
    public const string InlineExpressionCondition = "$expression";

    private static readonly string[] WithSeparator = [" with "];

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

    public static List<string> ToTexts(IEnumerable<RelationReference>? restrictions) => restrictions.OrEmpty().Where(r => r != null).Select(ToText).ToList();

    public static DecodedRestriction Decode(string restriction)
    {
        var parts = restriction.Split(WithSeparator, StringSplitOptions.None);
        var tuple = parts[0];
        var condition = parts.Length > 1 && parts[1].Length > 0 ? parts[1] : null;
        var wildcardAt = tuple.IndexOf(":*", StringComparison.Ordinal);
        var withoutWildcard = wildcardAt < 0 ? tuple : tuple.Remove(wildcardAt, 2);
        var words = withoutWildcard.Split('#');
        var relation = words.Length > 1 && words[1].Length > 0 ? words[1] : null;
        return new DecodedRestriction(words[0], relation, wildcardAt >= 0, condition);
    }
}
