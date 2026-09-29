namespace OpenFga.Language;

/// <summary>A DSL syntax error reported by the parser.</summary>
public sealed class SyntaxError : LanguageError
{
    internal SyntaxError(string message, SourceRange? range, string? symbol)
        : base("syntax", message, null, range)
    {
        Symbol = symbol;
    }

    /// <summary>Gets the offending token text, when the parser identified one.</summary>
    public string? Symbol { get; }
}
