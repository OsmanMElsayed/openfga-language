namespace OpenFga.Language.Errors;

internal static class LanguageErrors
{
    /// <summary>An <c>fga.mod</c> problem; JS reports these with kind <c>validation</c>.</summary>
    public static LanguageError ModFile(string message, SourceRange range) => new("validation", message, null, range);
}
