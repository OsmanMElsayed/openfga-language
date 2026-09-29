using System.Text.Json;
using YamlDotNet.Serialization;

namespace OpenFga.Language.Tests.Fixtures;

internal static class TestData
{
    private static readonly IDeserializer Yaml = new DeserializerBuilder().IgnoreUnmatchedProperties().Build();

    public static IReadOnlyList<TransformerCase> TransformerCases() =>
        Directories("transformer")
            .Select(name => new TransformerCase(
                name,
                FixturePaths.Read("transformer", name, "authorization-model.fga"),
                FixturePaths.Read("transformer", name, "authorization-model.json")))
            .ToList();

    public static IReadOnlyList<DslCase> DslSyntaxCases() => LoadYaml<DslCase>("dsl-syntax-validation-cases.yaml").Where(c => !c.Skip).ToList();

    public static IReadOnlyList<DslCase> DslSemanticCases() => LoadYaml<DslCase>("dsl-semantic-validation-cases.yaml").Where(c => !c.Skip).ToList();

    public static IReadOnlyList<JsonCase> JsonValidationCases() => LoadYaml<JsonCase>("json-validation-cases.yaml").Where(c => !c.Skip).ToList();

    public static IReadOnlyList<JsonCase> JsonSyntaxTransformerCases() => LoadYaml<JsonCase>("json-syntax-transformer-validation-cases.yaml").Where(c => !c.Skip).ToList();

    public static IReadOnlyList<ModFileCase> ModFileCases() => LoadYaml<ModFileCase>("fga-mod-transformer-cases.yaml").Where(c => !c.Skip).ToList();

    public static IReadOnlyList<ObjectIdCase> ObjectIdCases() => LoadYaml<ObjectIdCase>("object-id-validation-cases.yaml");

    public static IReadOnlyList<ModuleCase> ModuleCases() =>
        Directories("transformer-module").Select(LoadModuleCase).ToList();

    /// <summary>Theory rows of (index, name): the name makes failures readable, the index finds the case.</summary>
    public static TheoryData<int, string> Indexes<T>(IReadOnlyList<T> cases, Func<T, string> name)
    {
        var data = new TheoryData<int, string>();
        for (var i = 0; i < cases.Count; i++)
        {
            data.Add(i, name(cases[i]));
        }

        return data;
    }

    private static List<T> LoadYaml<T>(string file) => Yaml.Deserialize<List<T>>(FixturePaths.Read(file));

    private static IEnumerable<string> Directories(string folder) =>
        Directory.GetDirectories(Path.Combine(FixturePaths.DataDirectory, folder))
            .Where(d => !File.Exists(Path.Combine(d, "test.skip")))
            .Select(Path.GetFileName)
            .OfType<string>()
            .OrderBy(n => n, StringComparer.Ordinal);

    private static ModuleCase LoadModuleCase(string name)
    {
        string? Optional(string file) =>
            File.Exists(Path.Combine(FixturePaths.DataDirectory, "transformer-module", name, file))
                ? FixturePaths.Read("transformer-module", name, file)
                : null;

        var moduleDir = Path.Combine(FixturePaths.DataDirectory, "transformer-module", name, "module");
        var modules = Directory.Exists(moduleDir)
            ? Directory.GetFiles(moduleDir, "*.fga")
                .Select(Path.GetFileName)
                .OfType<string>()
                .OrderBy(f => f, StringComparer.Ordinal)
                .Select(f => new FixtureModule(f, FixturePaths.Read("transformer-module", name, "module", f)))
                .ToList()
            : [];

        var errorsJson = Optional("expected_errors.json");
        return new ModuleCase(
            name,
            modules,
            Optional("authorization-model.json"),
            Optional("combined.fga"),
            Optional("combined-sourceinfo.fga"),
            errorsJson is null ? null : JsonSerializer.Deserialize<List<ExpectedError>>(errorsJson));
    }
}
