using System;
using OpenFga.Sdk.Model;

namespace OpenFga.Language.Parsing;

/// <summary>
/// Maps DSL condition parameter types (<c>int</c>, <c>ipaddress</c>, ...) to the SDK enum, whose members are
/// named <c>TYPENAMEINT</c> etc. and serialize as <c>TYPE_NAME_INT</c>.
/// </summary>
internal static class ConditionTypeNames
{
    private const string Prefix = "TYPENAME";

    public static TypeName FromDsl(string dslTypeName)
        => Enum.TryParse<TypeName>(Prefix + dslTypeName.ToUpperInvariant(), ignoreCase: false, out var typeName)
            ? typeName
            : TypeName.TYPENAMEUNSPECIFIED;

    public static string ToDsl(TypeName typeName)
    {
        var name = typeName.ToString();
        return (name.StartsWith(Prefix, StringComparison.Ordinal) ? name.Substring(Prefix.Length) : name).ToLowerInvariant();
    }
}
