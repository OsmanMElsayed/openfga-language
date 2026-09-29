using System;
using OpenFga.Language.Parsing;
using OpenFga.Language.Transformer;
using OpenFga.Sdk.Model;

namespace OpenFga.Language;

/// <summary>Converts OpenFGA models between the DSL, JSON and <see cref="AuthorizationModel"/>.</summary>
public static class ModelTransformer
{
    /// <summary>Parses DSL text into an <see cref="AuthorizationModel"/>. Only syntax is checked; use <c>ModelValidator</c> for semantic checks.</summary>
    /// <param name="dsl">The model in OpenFGA DSL syntax.</param>
    /// <returns>The model, with no <see cref="AuthorizationModel.Id"/> and a non-null <see cref="AuthorizationModel.TypeDefinitions"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="dsl"/> is null.</exception>
    /// <exception cref="DslSyntaxException">The DSL does not parse.</exception>
    public static AuthorizationModel TransformDslToModel(string dsl)
    {
        if (dsl is null)
        {
            throw new ArgumentNullException(nameof(dsl));
        }

        var result = DslParser.Parse(dsl);
        if (result.Errors.Count > 0)
        {
            throw new DslSyntaxException(result.Errors);
        }

        return result.Model;
    }

    /// <summary>Parses DSL text into the JSON accepted by the OpenFGA API.</summary>
    /// <param name="dsl">The model in OpenFGA DSL syntax.</param>
    /// <returns>The model serialized with the OpenFga.Sdk JSON contract.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="dsl"/> is null.</exception>
    /// <exception cref="DslSyntaxException">The DSL does not parse.</exception>
    public static string TransformDslToJson(string dsl) => ModelJson.Serialize(TransformDslToModel(dsl));
}
