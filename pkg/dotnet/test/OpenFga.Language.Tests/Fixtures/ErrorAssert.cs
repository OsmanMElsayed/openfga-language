using OpenFga.Language.Errors;

namespace OpenFga.Language.Tests.Fixtures;

internal static class ErrorAssert
{
    /// <summary>Asserts each actual error matches the fixture fields that are present (like Jest's toMatchObject).</summary>
    public static void AllMatch(IReadOnlyList<ExpectedError> expected, IReadOnlyList<LanguageError> actual)
    {
        Assert.Equal(expected.Count, actual.Count);
        for (var i = 0; i < expected.Count; i++)
        {
            Matches(expected[i], actual[i], i);
        }
    }

    /// <summary>
    /// Builds the aggregate exception message the fixture implies. <paramref name="kind"/> gives the prefix per error;
    /// the location is included when <paramref name="includeLocation"/> is true and the error has a line.
    /// </summary>
    public static string ExpectedAggregate(IReadOnlyList<ExpectedError> expected, Func<ExpectedError, string> kind, bool includeLocation)
    {
        var items = expected.Select(e => includeLocation && e.Line is not null
            ? $"{kind(e)} error at line={e.Line.Start}, column={e.Column?.Start}: {e.Msg}"
            : $"{kind(e)} error: {e.Msg}");
        return $"{expected.Count} error{(expected.Count == 1 ? "" : "s")} occurred:\n\t* {string.Join("\n\t* ", items)}\n\n";
    }

    private static void Matches(ExpectedError expected, LanguageError actual, int index)
    {
        var at = $"error[{index}] ({actual})";
        Assert.True(expected.Msg == actual.Message, $"{at}: message expected '{expected.Msg}'");
        if (expected.File is not null)
        {
            Assert.True(expected.File == actual.File, $"{at}: file expected '{expected.File}' but was '{actual.File}'");
        }

        if (expected.Line is not null)
        {
            Assert.True(actual.Range.HasValue, $"{at}: expected a range");
            Assert.True(expected.Line.Start == actual.Range.Value.StartLine && expected.Line.End == actual.Range.Value.EndLine, $"{at}: line expected {expected.Line.Start}-{expected.Line.End}");
        }

        if (expected.Column is not null)
        {
            Assert.True(actual.Range.HasValue, $"{at}: expected a range");
            Assert.True(expected.Column.Start == actual.Range.Value.StartColumn && expected.Column.End == actual.Range.Value.EndColumn, $"{at}: column expected {expected.Column.Start}-{expected.Column.End}");
        }

        if (expected.Type == "syntax")
        {
            Assert.IsType<SyntaxError>(actual);
        }

        if (expected.Metadata is { } metadata)
        {
            if (metadata.Symbol is not null)
            {
                Assert.True(metadata.Symbol == Symbol(actual), $"{at}: symbol expected '{metadata.Symbol}' but was '{Symbol(actual)}'");
            }

            if (metadata.ErrorType is not null)
            {
                var validation = Assert.IsType<ModelValidationError>(actual);
                Assert.True(metadata.ErrorType == ValidationErrorTypeCodes.ToCode(validation.ErrorType), $"{at}: errorType expected '{metadata.ErrorType}'");
            }

            if (metadata.Module is not null)
            {
                Assert.True(metadata.Module == Assert.IsType<ModelValidationError>(actual).Module, $"{at}: module expected '{metadata.Module}'");
            }
        }
    }

    private static string? Symbol(LanguageError error) => error switch
    {
        SyntaxError s => s.Symbol,
        ModelValidationError v => v.Symbol,
        ModuleTransformationError m => m.Symbol,
        _ => null,
    };
}
