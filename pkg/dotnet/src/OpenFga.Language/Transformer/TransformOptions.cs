namespace OpenFga.Language.Transformer;

/// <summary>Options for writing a model as DSL.</summary>
public sealed class TransformOptions
{
    /// <summary>
    /// Gets or sets a value indicating whether types, relations and conditions of modular models are followed by a
    /// <c># module: …, file: …</c> comment.
    /// </summary>
    public bool IncludeSourceInformation { get; set; }
}
