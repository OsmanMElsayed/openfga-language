using System;
using System.Text.Json;
using OpenFga.Sdk.Model;

namespace OpenFga.Language.Transformation;

/// <summary>
/// JSON goes through the SDK's own <c>ToJson</c>/<c>FromJson</c>, so output is what OpenFgaClient sends.
/// </summary>
internal static class ModelJson
{
    private const string InvalidJson = "The value is not a valid authorization model JSON document.";

    public static string Serialize(AuthorizationModel model) => model.ToJson();

    public static AuthorizationModel Deserialize(string json, string paramName)
    {
        try
        {
            return AuthorizationModel.FromJson(json);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
        {
            // FromJson throws InvalidOperationException for the literal null.
            throw new ArgumentException(InvalidJson, paramName, ex);
        }
    }

    public static AuthorizationModel Clone(AuthorizationModel model) => AuthorizationModel.FromJson(model.ToJson());
}
