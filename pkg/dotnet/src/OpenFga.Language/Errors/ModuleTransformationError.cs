namespace OpenFga.Language;

/// <summary>A problem combining module files into one model (duplicate definitions, unknown extended types, ...).</summary>
public sealed class ModuleTransformationError : LanguageError
{
    internal ModuleTransformationError(string message, string? file, SourceRange? range, string? symbol)
        : base("transformation-error", message, file, range)
    {
        Symbol = symbol;
    }

    /// <summary>Gets the name the error refers to, when there is one.</summary>
    public string? Symbol { get; }
}
