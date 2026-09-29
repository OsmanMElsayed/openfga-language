using System.Text.Json.Serialization;
using YamlDotNet.Serialization;

namespace OpenFga.Language.Tests.Fixtures;

public sealed class ExpectedSpan
{
    [YamlMember(Alias = "start")][JsonPropertyName("start")] public int Start { get; set; }
    [YamlMember(Alias = "end")][JsonPropertyName("end")] public int End { get; set; }
}

public sealed class ExpectedMetadata
{
    [YamlMember(Alias = "symbol")][JsonPropertyName("symbol")] public string? Symbol { get; set; }
    [YamlMember(Alias = "errorType")][JsonPropertyName("errorType")] public string? ErrorType { get; set; }
    [YamlMember(Alias = "module")][JsonPropertyName("module")] public string? Module { get; set; }
}

public sealed class ExpectedError
{
    [YamlMember(Alias = "msg")][JsonPropertyName("msg")] public string Msg { get; set; } = "";
    [YamlMember(Alias = "file")][JsonPropertyName("file")] public string? File { get; set; }
    [YamlMember(Alias = "type")][JsonPropertyName("type")] public string? Type { get; set; }
    [YamlMember(Alias = "line")][JsonPropertyName("line")] public ExpectedSpan? Line { get; set; }
    [YamlMember(Alias = "column")][JsonPropertyName("column")] public ExpectedSpan? Column { get; set; }
    [YamlMember(Alias = "metadata")][JsonPropertyName("metadata")] public ExpectedMetadata? Metadata { get; set; }
}

public sealed class DslCase
{
    [YamlMember(Alias = "name")] public string Name { get; set; } = "";
    [YamlMember(Alias = "dsl")] public string Dsl { get; set; } = "";
    [YamlMember(Alias = "skip")] public bool Skip { get; set; }
    private List<ExpectedError> _expectedErrors = [];

    // An empty `expected_errors:` key deserializes as null.
    [YamlMember(Alias = "expected_errors")] public List<ExpectedError> ExpectedErrors { get => _expectedErrors; set => _expectedErrors = value ?? []; }
}

public sealed class JsonCase
{
    [YamlMember(Alias = "name")] public string Name { get; set; } = "";
    [YamlMember(Alias = "json")] public string Json { get; set; } = "";
    [YamlMember(Alias = "skip")] public bool Skip { get; set; }
    [YamlMember(Alias = "error_message")] public string? ErrorMessage { get; set; }
    private List<ExpectedError> _expectedErrors = [];

    // An empty `expected_errors:` key deserializes as null.
    [YamlMember(Alias = "expected_errors")] public List<ExpectedError> ExpectedErrors { get => _expectedErrors; set => _expectedErrors = value ?? []; }
}

public sealed class ModFileCase
{
    [YamlMember(Alias = "name")] public string Name { get; set; } = "";
    [YamlMember(Alias = "modFile")] public string ModFile { get; set; } = "";
    [YamlMember(Alias = "json")] public string? Json { get; set; }
    [YamlMember(Alias = "skip")] public bool Skip { get; set; }
    [YamlMember(Alias = "expected_errors")] public List<ExpectedError>? ExpectedErrors { get; set; }
}

public sealed class ObjectIdCase
{
    [YamlMember(Alias = "name")] public string Name { get; set; } = "";
    [YamlMember(Alias = "object_id")] public string ObjectId { get; set; } = "";
    [YamlMember(Alias = "valid")] public bool Valid { get; set; }
}

public sealed record TransformerCase(string Name, string Dsl, string Json);

/// <summary>A module file of a fixture; kept separate from the library's ModuleFile so fixtures load before Task 9.</summary>
public sealed record FixtureModule(string Name, string Contents);

public sealed record ModuleCase(
    string Name,
    IReadOnlyList<FixtureModule> Modules,
    string? Json,
    string? Dsl,
    string? DslWithSourceInfo,
    IReadOnlyList<ExpectedError>? ExpectedErrors);
