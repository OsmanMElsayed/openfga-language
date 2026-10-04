namespace OpenFga.Language.Validation;

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
