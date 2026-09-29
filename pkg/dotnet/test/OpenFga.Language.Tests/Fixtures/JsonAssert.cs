using System.Text.Json;
using System.Text.Json.Nodes;

namespace OpenFga.Language.Tests.Fixtures;

internal static class JsonAssert
{
    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    /// <summary>
    /// Compares JSON documents ignoring property order. Null-valued properties and empty arrays count as absent:
    /// the SDK omits nulls, while the JS fixtures sometimes spell out <c>"metadata": null</c>.
    /// </summary>
    public static void Equivalent(string expectedJson, string actualJson)
    {
        var expected = Normalize(JsonNode.Parse(expectedJson));
        var actual = Normalize(JsonNode.Parse(actualJson));

        Assert.True(
            JsonNode.DeepEquals(expected, actual),
            $"JSON differs.\nExpected:\n{expected?.ToJsonString(Indented)}\nActual:\n{actual?.ToJsonString(Indented)}");
    }

    private static JsonNode? Normalize(JsonNode? node)
    {
        switch (node)
        {
            case JsonObject obj:
                var result = new JsonObject();
                foreach (var (key, value) in obj)
                {
                    var normalized = Normalize(value);
                    if (normalized is null || normalized is JsonArray { Count: 0 })
                    {
                        continue;
                    }

                    result[key] = normalized;
                }

                return result;
            case JsonArray array:
                return new JsonArray(array.Select(Normalize).ToArray());
            default:
                return node?.DeepClone();
        }
    }
}
