using System;
using System.Text.RegularExpressions;

namespace OpenFga.Language.Validation;

/// <summary>Checks the syntax of the parts of a relationship tuple (objects, users, relations, conditions).</summary>
/// <remarks>Patterns end with <c>\z</c>, so unlike .NET's <c>$</c> a trailing newline is never accepted.</remarks>
public static class TupleSyntax
{
    private static readonly Regex TypeAndId = Create("^" + Rules.Type + ":" + Rules.Id + @"\z");
    private static readonly Regex ObjectLength = Create("^" + Rules.Object + @"\z");
    private static readonly Regex Userset = Create("^" + Rules.Type + ":" + Rules.Id + "#" + Rules.Relation + @"\z");
    private static readonly Regex Wildcard = Create("^" + Rules.Type + @":\*\z");
    private static readonly Regex RelationName = Create("^" + Rules.Relation + @"\z");
    private static readonly Regex ConditionName = Create("^" + Rules.Condition + @"\z");
    private static readonly Regex TypeName = Create("^" + Rules.Type + @"\z");
    private static readonly Regex ObjectId = Create("^" + Rules.Id + @"\z");

    /// <summary>Determines whether <paramref name="value"/> is an object, <c>type:id</c> (e.g. <c>document:1</c>).</summary>
    /// <param name="value">The value to check.</param>
    /// <returns><see langword="true"/> if the value is a valid object.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="value"/> is null.</exception>
    public static bool IsValidObject(string value)
    {
        ThrowIfNull(value);
        return TypeAndId.IsMatch(value) && ObjectLength.IsMatch(value);
    }

    /// <summary>Determines whether <paramref name="value"/> is an object identifier (the part after <c>:</c>).</summary>
    /// <param name="value">The value to check.</param>
    /// <returns><see langword="true"/> if the value is a valid object id.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="value"/> is null.</exception>
    public static bool IsValidObjectId(string value)
    {
        ThrowIfNull(value);
        return ObjectId.IsMatch(value);
    }

    /// <summary>Determines whether <paramref name="value"/> is a type name.</summary>
    /// <param name="value">The value to check.</param>
    /// <returns><see langword="true"/> if the value is a valid type name.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="value"/> is null.</exception>
    public static bool IsValidType(string value)
    {
        ThrowIfNull(value);
        return TypeName.IsMatch(value);
    }

    /// <summary>Determines whether <paramref name="value"/> is a relation name.</summary>
    /// <param name="value">The value to check.</param>
    /// <returns><see langword="true"/> if the value is a valid relation name.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="value"/> is null.</exception>
    public static bool IsValidRelation(string value)
    {
        ThrowIfNull(value);
        return RelationName.IsMatch(value);
    }

    /// <summary>Determines whether <paramref name="value"/> is a user: a userset, a user object or a user wildcard.</summary>
    /// <param name="value">The value to check.</param>
    /// <returns><see langword="true"/> if the value is a valid user.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="value"/> is null.</exception>
    public static bool IsValidUser(string value)
    {
        ThrowIfNull(value);
        return IsValidUserset(value) || IsValidUserObject(value) || IsValidUserWildcard(value);
    }

    /// <summary>Determines whether <paramref name="value"/> is a userset, <c>type:id#relation</c> (e.g. <c>group:eng#member</c>).</summary>
    /// <param name="value">The value to check.</param>
    /// <returns><see langword="true"/> if the value is a valid userset.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="value"/> is null.</exception>
    public static bool IsValidUserset(string value)
    {
        ThrowIfNull(value);
        return Userset.IsMatch(value);
    }

    /// <summary>Determines whether <paramref name="value"/> is a user object, <c>type:id</c> (e.g. <c>user:anne</c>).</summary>
    /// <param name="value">The value to check.</param>
    /// <returns><see langword="true"/> if the value is a valid user object.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="value"/> is null.</exception>
    public static bool IsValidUserObject(string value)
    {
        ThrowIfNull(value);
        return TypeAndId.IsMatch(value) && ObjectLength.IsMatch(value);
    }

    /// <summary>Determines whether <paramref name="value"/> is a user wildcard, <c>type:*</c>.</summary>
    /// <param name="value">The value to check.</param>
    /// <returns><see langword="true"/> if the value is a valid user wildcard.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="value"/> is null.</exception>
    public static bool IsValidUserWildcard(string value)
    {
        ThrowIfNull(value);
        return Wildcard.IsMatch(value);
    }

    /// <summary>Determines whether <paramref name="value"/> is a condition name.</summary>
    /// <param name="value">The value to check.</param>
    /// <returns><see langword="true"/> if the value is a valid condition name.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="value"/> is null.</exception>
    public static bool IsValidConditionName(string value)
    {
        ThrowIfNull(value);
        return ConditionName.IsMatch(value);
    }

    private static Regex Create(string pattern) => new(pattern, RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    private static void ThrowIfNull(string value)
    {
        if (value is null)
        {
            throw new ArgumentNullException(nameof(value));
        }
    }
}
