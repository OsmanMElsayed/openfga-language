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

    // The default MaxDepth (64) is too shallow for deeply nested union/intersection/difference DSL relations;
    // raise it for serialize, deserialize and clone alike so all three agree on what is representable.
    private static readonly JsonSerializerOptions Options = new() { MaxDepth = 1024 };

    public static string Serialize(AuthorizationModel model) => JsonSerializer.Serialize(model, Options);

    public static AuthorizationModel Deserialize(string json, string paramName)
    {
        AuthorizationModel? model;
        try
        {
            model = JsonSerializer.Deserialize<AuthorizationModel>(json, Options);
        }
        catch (JsonException ex)
        {
            throw new ArgumentException(InvalidJson, paramName, ex);
        }

        return model ?? throw new ArgumentException(InvalidJson, paramName);
    }

    public static AuthorizationModel Clone(AuthorizationModel model) => JsonSerializer.Deserialize<AuthorizationModel>(Serialize(model), Options)!;
}
