using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace OpenFga.Language.Errors;

/// <summary>Thrown when DSL text does not parse. <see cref="Errors"/> lists every syntax error found.</summary>
public sealed class DslSyntaxException : OpenFgaLanguageException
{
    internal DslSyntaxException(IEnumerable<SyntaxError> errors)
        : this(new ReadOnlyCollection<SyntaxError>(errors.ToList()))
    {
    }

    private DslSyntaxException(ReadOnlyCollection<SyntaxError> errors)
        : base(ErrorMessages.Aggregate(errors))
    {
        Errors = errors;
    }

    /// <summary>Gets the syntax errors, in the order they were found.</summary>
    public IReadOnlyList<SyntaxError> Errors { get; }
}
