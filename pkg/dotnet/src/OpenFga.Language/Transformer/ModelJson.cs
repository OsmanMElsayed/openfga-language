using System;
using System.Text.Json;
using OpenFga.Sdk.Model;

namespace OpenFga.Language.Transformer;

/// <summary>
/// JSON uses the SDK's own System.Text.Json contract (attributes on the model types), so output is what OpenFgaClient
/// sends. Deserialization calls JsonSerializer directly because the SDK's FromJson throws InvalidOperationException
/// for the literal <c>null</c>.
/// </summary>
internal static class ModelJson
{
    private const string InvalidJson = "The value is not a valid authorization model JSON document.";

    public static string Serialize(AuthorizationModel model) => model.ToJson();

    public static AuthorizationModel Deserialize(string json, string paramName)
    {
        AuthorizationModel? model;
        try
        {
            model = JsonSerializer.Deserialize<AuthorizationModel>(json);
        }
        catch (JsonException ex)
        {
            throw new ArgumentException(InvalidJson, paramName, ex);
        }

        return model ?? throw new ArgumentException(InvalidJson, paramName);
    }

    public static AuthorizationModel Clone(AuthorizationModel model) => JsonSerializer.Deserialize<AuthorizationModel>(model.ToJson())!;
}
