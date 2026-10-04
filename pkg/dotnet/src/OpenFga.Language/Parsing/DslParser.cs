using System;
using System.Linq;
using Antlr4.Runtime;
using Antlr4.Runtime.Tree;
using OpenFga.Language.Antlr;

namespace OpenFga.Language.Parsing;

internal static class DslParser
{
    public static DslParseResult Parse(string dsl)
    {
        var dslWithCommentsStrippedOut = string.Join("\n", dsl.Split('\n').Select(StripCommentsOut));

        var errorListener = new DslErrorListener();
        var lexer = new OpenFGALexer(new AntlrInputStream(dslWithCommentsStrippedOut));
        lexer.RemoveErrorListeners();
        lexer.AddErrorListener(errorListener);

        var parser = new OpenFGAParser(new CommonTokenStream(lexer));
        parser.RemoveErrorListeners();
        parser.AddErrorListener(errorListener);

        var listener = new DslListener(parser);
        ParseTreeWalker.Default.Walk(listener, parser.main());

        return new DslParseResult(listener.Model, listener.TypeDefExtensions, errorListener.Errors);
    }

    // Whole-line comments become empty lines (keeping line numbers); trailing " #" comments and whitespace are removed.
    private static string StripCommentsOut(string line)
    {
        if (line.TrimStart().StartsWith("#", StringComparison.Ordinal))
        {
            return string.Empty;
        }

        var commentStart = line.IndexOf(" #", StringComparison.Ordinal);
        return (commentStart >= 0 ? line.Substring(0, commentStart) : line).TrimEnd();
    }
}
