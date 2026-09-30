using System;
using System.Text.RegularExpressions;

namespace OpenFga.Language.Validation;

/// <summary>Compiled naming rules. An empty pattern falls back to the default, as JS does with <c>||</c>.</summary>
internal sealed class ValidationPatterns
{
    private static readonly TimeSpan MatchTimeout = TimeSpan.FromSeconds(1);

    private ValidationPatterns(string typeRule, Regex type, string relationRule, Regex relation)
    {
        TypeRule = typeRule;
        Type = type;
        RelationRule = relationRule;
        Relation = relation;
    }

    public static ValidationPatterns Default { get; } = From(new ModelValidationOptions(), "options");

    public string TypeRule { get; }

    public Regex Type { get; }

    public string RelationRule { get; }

    public Regex Relation { get; }

    public static ValidationPatterns From(ModelValidationOptions options, string paramName)
    {
        var typeRule = string.IsNullOrEmpty(options.TypePattern) ? Rules.DefaultTypePattern : options.TypePattern;
        var relationRule = string.IsNullOrEmpty(options.RelationPattern) ? Rules.DefaultRelationPattern : options.RelationPattern;
        return new ValidationPatterns(
            typeRule,
            Compile(typeRule, Rules.DefaultTypePattern, "type", paramName),
            relationRule,
            Compile(relationRule, Rules.DefaultRelationPattern, "relation", paramName));
    }

    // .NET's `$` also matches before a final newline (JS's does not), so the default patterns are compiled with `\z`.
    private static Regex Compile(string pattern, string defaultPattern, string kind, string paramName)
    {
        try
        {
            var effective = string.Equals(pattern, defaultPattern, StringComparison.Ordinal) ? pattern.Substring(0, pattern.Length - 1) + @"\z" : pattern;
            return new Regex(effective, RegexOptions.CultureInvariant, MatchTimeout);
        }
        catch (ArgumentException ex)
        {
            throw new ArgumentException($"Incorrect {kind} regex specification for {pattern}", paramName, ex);
        }
    }
}
