namespace OpenFga.Language.Errors;

/// <summary>Thrown when an authorization model cannot be expressed in the DSL.</summary>
public sealed class DslTransformationException : OpenFgaLanguageException
{
    internal DslTransformationException(string message)
        : base(message)
    {
    }
}
