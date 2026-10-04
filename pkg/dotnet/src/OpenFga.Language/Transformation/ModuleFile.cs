using System;

namespace OpenFga.Language.Transformation;

/// <summary>One file of a modular model.</summary>
public sealed class ModuleFile
{
    /// <summary>Initializes a new <see cref="ModuleFile"/>.</summary>
    /// <param name="name">The file name, as listed in <c>fga.mod</c>; used in errors and source information.</param>
    /// <param name="contents">The DSL text of the module.</param>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> or <paramref name="contents"/> is null.</exception>
    public ModuleFile(string name, string contents)
    {
        Name = name ?? throw new ArgumentNullException(nameof(name));
        Contents = contents ?? throw new ArgumentNullException(nameof(contents));
    }

    /// <summary>Gets the file name.</summary>
    public string Name { get; }

    /// <summary>Gets the DSL text of the module.</summary>
    public string Contents { get; }
}
