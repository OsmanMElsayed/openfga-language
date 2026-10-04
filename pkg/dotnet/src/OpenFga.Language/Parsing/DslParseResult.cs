using System.Collections.Generic;
using OpenFga.Language.Errors;
using OpenFga.Sdk.Model;

namespace OpenFga.Language.Parsing;

internal sealed class DslParseResult
{
    public DslParseResult(AuthorizationModel model, IReadOnlyDictionary<string, TypeDefinition> typeDefExtensions, IReadOnlyList<SyntaxError> errors)
    {
        Model = model;
        TypeDefExtensions = typeDefExtensions;
        Errors = errors;
    }

    public AuthorizationModel Model { get; }

    public IReadOnlyDictionary<string, TypeDefinition> TypeDefExtensions { get; }

    public IReadOnlyList<SyntaxError> Errors { get; }
}
