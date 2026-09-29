namespace OpenFga.Language.Validation;

/// <summary>Regex fragments shared with the Go, JS and Java libraries (JS validate-rules.ts Rules).</summary>
internal static class Rules
{
    public const string Type = @"[^:#@\*\s]{1,254}";
    public const string Relation = @"[^:#@\*\s]{1,50}";
    public const string Condition = @"[^\*\s]{1,50}";
    public const string Id = @"[^#:\s*][a-zA-Z0-9_|*@.+/\-]*";
    public const string Object = @"[^\s]{2,256}";

    // Kept textually identical to JS: they appear verbatim in invalid-name messages.
    public const string DefaultTypePattern = "^" + Type + "$";
    public const string DefaultRelationPattern = "^" + Relation + "$";
}
