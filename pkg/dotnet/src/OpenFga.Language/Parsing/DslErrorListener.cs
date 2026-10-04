using System.Collections.Generic;
using System.IO;
using Antlr4.Runtime;
using OpenFga.Language.Errors;

namespace OpenFga.Language.Parsing;

/// <summary>Collects lexer and parser errors as <see cref="Errors.SyntaxError"/> (JS OpenFgaDslErrorListener).</summary>
internal sealed class DslErrorListener : IAntlrErrorListener<IToken>, IAntlrErrorListener<int>
{
    public List<SyntaxError> Errors { get; } = [];

    public void SyntaxError(TextWriter output, IRecognizer recognizer, IToken offendingSymbol, int line, int charPositionInLine, string msg, RecognitionException e)
        => Add(offendingSymbol?.Text, line, charPositionInLine, msg);

    public void SyntaxError(TextWriter output, IRecognizer recognizer, int offendingSymbol, int line, int charPositionInLine, string msg, RecognitionException e)
        => Add(null, line, charPositionInLine, msg);

    // ANTLR lines are one-based; columns are already zero-based.
    private void Add(string? symbol, int line, int column, string message)
        => Errors.Add(new SyntaxError(message, new SourceRange(line - 1, line - 1, column, column + (symbol?.Length ?? 0)), symbol));
}
