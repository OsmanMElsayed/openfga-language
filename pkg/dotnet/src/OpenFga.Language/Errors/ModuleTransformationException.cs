using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace OpenFga.Language.Errors;

/// <summary>
/// Thrown when module files cannot be combined into a valid model. <see cref="Errors"/> mixes
/// <see cref="SyntaxError"/>, <see cref="ModelValidationError"/> and <see cref="ModuleTransformationError"/> items.
/// </summary>
public sealed class ModuleTransformationException : OpenFgaLanguageException
{
    internal ModuleTransformationException(IEnumerable<LanguageError> errors)
        : this(new ReadOnlyCollection<LanguageError>(errors.ToList()))
    {
    }

    private ModuleTransformationException(ReadOnlyCollection<LanguageError> errors)
        : base(ErrorMessages.Aggregate(errors))
    {
        Errors = errors;
    }

    /// <summary>Gets the problems, in the order they were found.</summary>
    public IReadOnlyList<LanguageError> Errors { get; }
}
