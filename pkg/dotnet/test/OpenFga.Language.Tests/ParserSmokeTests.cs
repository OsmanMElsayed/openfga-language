using System.IO;
using Antlr4.Runtime;
using OpenFga.Language.Antlr;

namespace OpenFga.Language.Tests;

public class ParserSmokeTests
{
    [Fact]
    public void GeneratedParserParsesMinimalModel()
    {
        var errors = new List<string>();
        var parser = new OpenFGAParser(new CommonTokenStream(new OpenFGALexer(new AntlrInputStream("model\n  schema 1.1\ntype user\n"))));
        parser.RemoveErrorListeners();
        parser.AddErrorListener(new CollectingErrorListener(errors));

        parser.main();

        Assert.Empty(errors);
    }

    [Fact]
    public void GeneratedParserReportsErrors()
    {
        var errors = new List<string>();
        var parser = new OpenFGAParser(new CommonTokenStream(new OpenFGALexer(new AntlrInputStream("type user"))));
        parser.RemoveErrorListeners();
        parser.AddErrorListener(new CollectingErrorListener(errors));

        parser.main();

        Assert.NotEmpty(errors);
    }

    private sealed class CollectingErrorListener(List<string> errors) : BaseErrorListener
    {
        public override void SyntaxError(TextWriter output, IRecognizer recognizer, IToken offendingSymbol, int line, int charPositionInLine, string msg, RecognitionException e)
            => errors.Add(msg);
    }
}
