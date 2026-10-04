using System;
using System.Collections.Generic;
using System.Linq;
using Antlr4.Runtime;
using Antlr4.Runtime.Tree;
using OpenFga.Language.Antlr;
using OpenFga.Language.Errors;
using OpenFga.Sdk.Model;

namespace OpenFga.Language.Parsing;

internal sealed class DslParseResult
{
    public DslParseResult(AuthorizationModel model, IReadOnlyDictionary<string, TypeDefinition> typeDefExtensions, IReadOnlyList<SyntaxError> errors)
    {
        Model = model;
        TypeDefExtensions = typeDefExtensions;
        Errors = errors;
    }

    public AuthorizationModel Model { get; }

    public IReadOnlyDictionary<string, TypeDefinition> TypeDefExtensions { get; }

    public IReadOnlyList<SyntaxError> Errors { get; }
}

internal static class DslParser
{
    public static DslParseResult Parse(string dsl)
    {
        var cleaned = string.Join("\n", dsl.Split('\n').Select(CleanLine));

        var errorListener = new DslErrorListener();
        var lexer = new OpenFGALexer(new AntlrInputStream(cleaned));
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
    private static string CleanLine(string line)
    {
        if (line.TrimStart().StartsWith("#", StringComparison.Ordinal))
        {
            return string.Empty;
        }

        var commentStart = line.IndexOf(" #", StringComparison.Ordinal);
        return (commentStart >= 0 ? line.Substring(0, commentStart) : line).TrimEnd();
    }
}
