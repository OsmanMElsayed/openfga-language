using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace OpenFga.Language;

/// <summary>Thrown when an <c>fga.mod</c> file is invalid. <see cref="Errors"/> lists every problem found.</summary>
public sealed class ModFileValidationException : OpenFgaLanguageException
{
    internal ModFileValidationException(IEnumerable<LanguageError> errors)
        : this(new ReadOnlyCollection<LanguageError>(errors.ToList()))
    {
    }

    private ModFileValidationException(ReadOnlyCollection<LanguageError> errors)
        : base(ErrorMessages.Aggregate(errors))
    {
        Errors = errors;
    }

    /// <summary>Gets the problems, in the order they were found.</summary>
    public IReadOnlyList<LanguageError> Errors { get; }
}
