using System;

namespace OpenFga.Language.Errors;

/// <summary>Base class for every problem this library reports about its input.</summary>
public abstract class OpenFgaLanguageException : Exception
{
    private protected OpenFgaLanguageException(string message)
        : base(message)
    {
    }
}
