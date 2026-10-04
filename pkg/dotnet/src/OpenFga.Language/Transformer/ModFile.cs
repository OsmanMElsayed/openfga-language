using System;
using System.Collections.Generic;
using OpenFga.Language.Transformer;

namespace OpenFga.Language.Transformer;

/// <summary>The contents of an <c>fga.mod</c> file, which lists the module files of a modular model.</summary>
public sealed class ModFile
{
    internal ModFile(string schema, IReadOnlyList<string> contents)
    {
        Schema = schema;
        Contents = contents;
    }

    /// <summary>Gets the schema version. Always <c>1.2</c>, the only version <c>fga.mod</c> supports.</summary>
    public string Schema { get; }

    /// <summary>Gets the relative paths of the module files, in file order.</summary>
    public IReadOnlyList<string> Contents { get; }

    /// <summary>Parses and validates the text of an <c>fga.mod</c> file.</summary>
    /// <param name="text">The YAML text of the file.</param>
    /// <returns>The parsed file.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="text"/> is null.</exception>
    /// <exception cref="ModFileValidationException">The file is not valid YAML or does not describe a valid module list.</exception>
    public static ModFile Parse(string text)
    {
        if (text is null)
        {
            throw new ArgumentNullException(nameof(text));
        }

        return ModFileReader.Read(text);
    }
}
