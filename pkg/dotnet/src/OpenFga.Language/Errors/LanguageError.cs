using System.Globalization;

namespace OpenFga.Language.Errors;

/// <summary>A single problem found in DSL, JSON, module or <c>fga.mod</c> input.</summary>
public class LanguageError
{
    internal LanguageError(string kind, string message, string? file, SourceRange? range)
    {
        Kind = kind;
        Message = message;
        File = file;
        Range = range;
    }

    /// <summary>Gets the human-readable description of the problem.</summary>
    public string Message { get; }

    /// <summary>Gets the name of the file the problem is in, when known.</summary>
    public string? File { get; internal set; }

    /// <summary>Gets the location of the problem, or <see langword="null"/> when there is no source text to point at.</summary>
    public SourceRange? Range { get; internal set; }

    /// <summary>Gets the error kind used as the message prefix (e.g. <c>syntax</c>, <c>invalid-name</c>).</summary>
    internal string Kind { get; }

    /// <summary>Formats the error as <c>"{kind} error at line=L, column=C: {message}"</c>.</summary>
    public override string ToString()
        => Range is { } range
            ? string.Format(CultureInfo.InvariantCulture, "{0} error at line={1}, column={2}: {3}", Kind, range.StartLine, range.StartColumn, Message)
            : string.Format(CultureInfo.InvariantCulture, "{0} error: {1}", Kind, Message);
}

internal static class LanguageErrors
{
    /// <summary>An <c>fga.mod</c> problem; JS reports these with kind <c>validation</c>.</summary>
    public static LanguageError ModFile(string message, SourceRange range) => new("validation", message, null, range);
}
