using System;
using System.Text.RegularExpressions;

namespace OpenFga.Language.Internal;

/// <summary>Finds definition lines in DSL text (JS util/line-numbers.ts). Names are matched literally.</summary>
internal static class LineNumbers
{
    private static readonly TimeSpan MatchTimeout = TimeSpan.FromSeconds(1);
    private static readonly Regex OptionalComment = new(@"^(\s+#.*)?$", RegexOptions.CultureInvariant, MatchTimeout);
    private static readonly Regex StartsWithParen = new(@"^\s*\(", RegexOptions.CultureInvariant, MatchTimeout);
    private static readonly Regex StartsWithColon = new(@"^\s*:", RegexOptions.CultureInvariant, MatchTimeout);
    private static readonly Regex RepeatedSpaces = new(" {2,}", RegexOptions.CultureInvariant, MatchTimeout);

    public static int? GetTypeLine(string typeName, string[]? lines, int? skipIndex = null, bool extension = false)
    {
        var prefix = (extension ? "extend " : string.Empty) + "type " + typeName;
        return Find(lines, skipIndex, line =>
        {
            var trimmed = line.Trim();
            return trimmed.StartsWith(prefix, StringComparison.Ordinal) && OptionalComment.IsMatch(trimmed.Substring(prefix.Length));
        });
    }

    public static int? GetRelationLine(string relation, string[]? lines, int? skipIndex = null)
    {
        var prefix = "define " + relation;
        return Find(lines, skipIndex, line =>
        {
            var normalized = RepeatedSpaces.Replace(line.Trim(), " ");
            return normalized.StartsWith(prefix, StringComparison.Ordinal) && StartsWithColon.IsMatch(normalized.Substring(prefix.Length));
        });
    }

    public static int? GetConditionLine(string conditionName, string[]? lines, int? skipIndex = null)
    {
        // `(` must follow the name, so `less` cannot match `less_than`.
        var prefix = "condition " + conditionName;
        return Find(lines, skipIndex, line =>
        {
            var trimmed = line.Trim();
            return trimmed.StartsWith(prefix, StringComparison.Ordinal) && StartsWithParen.IsMatch(trimmed.Substring(prefix.Length));
        });
    }

    /// <summary>Returns the schema line, or 0 when it is not found (as JS does).</summary>
    public static int? GetSchemaLine(string schema, string[]? lines)
    {
        var prefix = "schema " + schema;
        var index = Find(lines, 0, line =>
        {
            var normalized = RepeatedSpaces.Replace(line.Trim(), " ");
            return normalized.StartsWith(prefix, StringComparison.Ordinal) && OptionalComment.IsMatch(normalized.Substring(prefix.Length));
        });

        return index is null ? null : Math.Max(index.Value, 0);
    }

    private static int? Find(string[]? lines, int? skipIndex, Func<string, bool> predicate)
    {
        if (lines is null)
        {
            return null;
        }

        var start = skipIndex is > 0 ? skipIndex.Value : 0;
        for (var i = start; i < lines.Length; i++)
        {
            if (predicate(lines[i]))
            {
                return i;
            }
        }

        return -1;
    }
}
