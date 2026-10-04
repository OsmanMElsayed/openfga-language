using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace OpenFga.Language.Errors;

/// <summary>Thrown when an authorization model is semantically invalid. <see cref="Errors"/> lists every problem found.</summary>
public sealed class ModelValidationException : OpenFgaLanguageException
{
    internal ModelValidationException(IEnumerable<ModelValidationError> errors)
        : this(new ReadOnlyCollection<ModelValidationError>(errors.ToList()))
    {
    }

    private ModelValidationException(ReadOnlyCollection<ModelValidationError> errors)
        : base(ErrorMessages.Aggregate(errors))
    {
        Errors = errors;
    }

    /// <summary>Gets the validation errors, in the order they were found.</summary>
    public IReadOnlyList<ModelValidationError> Errors { get; }
}
