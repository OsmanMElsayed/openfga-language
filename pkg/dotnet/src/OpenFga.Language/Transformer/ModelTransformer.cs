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

    /// <summary>Writes a model as DSL.</summary>
    /// <param name="model">The model; it is not modified.</param>
    /// <returns>The DSL text.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="model"/> is null.</exception>
    /// <exception cref="DslTransformationException">The model cannot be expressed in the DSL.</exception>
    public static string TransformModelToDsl(AuthorizationModel model) => TransformModelToDsl(model, new TransformOptions());

    /// <summary>Writes a model as DSL.</summary>
    /// <param name="model">The model; it is not modified.</param>
    /// <param name="options">Output options.</param>
    /// <returns>The DSL text.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="model"/> or <paramref name="options"/> is null.</exception>
    /// <exception cref="DslTransformationException">The model cannot be expressed in the DSL.</exception>
    public static string TransformModelToDsl(AuthorizationModel model, TransformOptions options)
    {
        if (model is null)
        {
            throw new ArgumentNullException(nameof(model));
        }

        if (options is null)
        {
            throw new ArgumentNullException(nameof(options));
        }

        return DslWriter.Write(ModelJson.Clone(model), options.IncludeSourceInformation);
    }

    /// <summary>Writes a JSON authorization model as DSL.</summary>
    /// <param name="json">The model in the JSON syntax accepted by the OpenFGA API.</param>
    /// <returns>The DSL text.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="json"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="json"/> is not a JSON authorization model.</exception>
    /// <exception cref="DslTransformationException">The model cannot be expressed in the DSL.</exception>
    public static string TransformJsonToDsl(string json) => TransformJsonToDsl(json, new TransformOptions());

    /// <summary>Writes a JSON authorization model as DSL.</summary>
    /// <param name="json">The model in the JSON syntax accepted by the OpenFGA API.</param>
    /// <param name="options">Output options.</param>
    /// <returns>The DSL text.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="json"/> or <paramref name="options"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="json"/> is not a JSON authorization model.</exception>
    /// <exception cref="DslTransformationException">The model cannot be expressed in the DSL.</exception>
    public static string TransformJsonToDsl(string json, TransformOptions options)
    {
        if (json is null)
        {
            throw new ArgumentNullException(nameof(json));
        }

        if (options is null)
        {
            throw new ArgumentNullException(nameof(options));
        }

        return DslWriter.Write(ModelJson.Deserialize(json, nameof(json)), options.IncludeSourceInformation);
    }
}
