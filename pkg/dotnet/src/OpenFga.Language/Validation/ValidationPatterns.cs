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
            Compile(typeRule, "type", paramName),
            relationRule,
            Compile(relationRule, "relation", paramName));
    }

    private static Regex Compile(string pattern, string kind, string paramName)
    {
        try
        {
            return new Regex(pattern, RegexOptions.CultureInvariant, MatchTimeout);
        }
        catch (ArgumentException ex)
        {
            throw new ArgumentException($"Incorrect {kind} regex specification for {pattern}", paramName, ex);
        }
    }
}
