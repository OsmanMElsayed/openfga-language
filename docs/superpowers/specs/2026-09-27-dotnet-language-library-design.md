# .NET OpenFGA Language Library — Design

- **Date:** 2026-09-27
- **Status:** Draft for review
- **Package:** `OpenFga.Language` (NuGet), source in `pkg/dotnet/`

## 1. Goal

Provide a .NET library equivalent to the Go, JS and Java packages in this repo, so that:

1. **.NET apps using `OpenFga.Sdk`** can author models in the OpenFGA DSL (single-file or modular) and turn them into an `AuthorizationModel` ready for `OpenFgaClient.WriteAuthorizationModel`, and vice versa.
2. **.NET IDE / CI tooling** (VS extensions, build-time model linting) can validate models and get rich, located errors (line/column, file, error type, symbol metadata).

The library follows the [.NET Framework Design Guidelines](https://learn.microsoft.com/en-us/dotnet/standard/design-guidelines/) and exposes only what these two use cases need.

### Success criteria

- All in-scope fixtures in `tests/data/` pass (same fixtures used by Go/JS/Java).
- Public API is exactly the surface in §4.
- `make build-dotnet`, `test-dotnet`, `lint-dotnet`, `audit-dotnet` work from the repo root, like the other packages.

## 2. Scope

| Capability | In v1 |
|---|---|
| DSL → `AuthorizationModel` / JSON | ✓ |
| `AuthorizationModel` / JSON → DSL | ✓ |
| `fga.mod` parsing | ✓ |
| Modular models (module files → one model) | ✓ |
| Syntactic validators (object, user, relation, …) | ✓ |
| Semantic model validation | ✓ |
| Model utilities (`IsRelationAssignable`, `GetModuleForObjectTypeRelation`) | ✓ |
| Store / tuple YAML validation (JS-only) | ✗ |
| Graph utilities (Go-only, server-specific) | ✗ |
| Public parser / listener / keyword / line-number helper APIs | ✗ (internal) |

## 3. Key decisions

### 3.1 Dependency on `OpenFga.Sdk` for model types

How the existing libraries obtain `AuthorizationModel`:

| Library | Source of model types | Runtime dependency |
|---|---|---|
| Java | `dev.openfga:openfga-sdk` (`dev.openfga.sdk.api.model.*`), used in public signatures | Yes (declared `implementation`, which under-exposes a type used publicly) |
| JS | `@openfga/sdk` as **devDependency**, `import type` only; returns structurally-typed plain objects | No |
| Go | `github.com/openfga/api/proto` (`openfgav1.*`), **not** the Go SDK | Yes, on API protos |

**Decision:** reference `OpenFga.Sdk` (currently 0.10.4) and use its `OpenFga.Sdk.Model` types (`AuthorizationModel`, `TypeDefinition`, `Userset`, …) in public signatures — same approach as Java. The `PackageReference` flows transitively (equivalent of Gradle `api`), so consumers compile against those types without an extra reference. Rationale: use case 1 hands the result straight to `OpenFgaClient`.

Verified facts about `OpenFga.Sdk` 0.10.4:
- Targets `netstandard2.0`, `net48`, `net8.0`, `net9.0`.
- **Not strong-named** → this library is not strong-named either (a strong-named assembly cannot reference an unsigned one).
- Nullable-annotated.
- `AuthorizationModel.Id` is optional and omitted from JSON when null (`JsonIgnore(WhenWritingNull)`), so DSL-derived models (no id) serialize as `{"schema_version":…,"type_definitions":…}`.
- Provides `ToJson()` / `FromJson()` using System.Text.Json with `[JsonPropertyName]`.

### 3.2 Target framework

- Library: **`netstandard2.0` only.** Runs on .NET Framework 4.6.1+, .NET 8/9/10, VS extensions and analyzers; matches `Antlr4.Runtime.Standard` and the SDK. Adding a TFM later is non-breaking; removing one is breaking.
- Tests: **`net10.0`** (current LTS) only. We rely on the .NET Standard compatibility contract rather than a .NET Framework test leg. The one real behavioural divergence between runtimes (NLS vs ICU globalization) is eliminated at compile time: culture-sensitivity analyzers CA1304, CA1307, CA1309, CA1310, CA1311 are errors, so all string operations are ordinal/invariant.

### 3.3 Error reporting: exceptions only

Mirrors JS and Java. Every operation either succeeds or throws an aggregate exception carrying the full list of errors. Tooling catches the exception and reads `Errors`. Invalid arguments use standard BCL exceptions.

### 3.4 Minimal public surface

Anything not listed in §4 is `internal`. Generated ANTLR code is made `internal`.

## 4. Public API

Stateless operations are static classes (cf. `System.Text.Json.JsonSerializer`). Options are passed through explicit overloads rather than optional parameters (Framework Design Guidelines member overloading). All constructors of exception and error-item types are `internal` (consumers read them, never create them).

### 4.1 `OpenFga.Language`

```csharp
namespace OpenFga.Language;

public static class ModelTransformer
{
    public static AuthorizationModel TransformDslToModel(string dsl);
    public static string TransformDslToJson(string dsl);
    public static string TransformModelToDsl(AuthorizationModel model);
    public static string TransformModelToDsl(AuthorizationModel model, TransformOptions options);
    public static string TransformJsonToDsl(string json);
    public static string TransformJsonToDsl(string json, TransformOptions options);
    public static AuthorizationModel TransformModulesToModel(IEnumerable<ModuleFile> files, string schemaVersion);
}

public sealed class TransformOptions
{
    public bool IncludeSourceInformation { get; set; }
}

public sealed class ModuleFile
{
    public ModuleFile(string name, string contents);
    public string Name { get; }
    public string Contents { get; }
}

public sealed class ModFile
{
    public static ModFile Parse(string text);          // throws ModFileValidationException
    public string Schema { get; }
    public IReadOnlyList<string> Contents { get; }
}
```

Behaviour:
- `TransformDslToModel` / `TransformDslToJson` throw `DslSyntaxException` on parse errors. They perform the same checks as the JS `transformDSLToJSONObject` (syntax only, no semantic validation).
- `TransformModelToDsl` / `TransformJsonToDsl` throw `DslTransformationException` when the model cannot be expressed in DSL (unsupported nesting, direct assignment not in first position, condition key not matching the nested condition name). `TransformModelToDsl` does not mutate the caller's model. `TransformJsonToDsl` throws `ArgumentException` (inner `JsonException`) on malformed JSON.
- `TransformModulesToModel` throws `ModuleTransformationException`; its errors are a mix of `SyntaxError` (a module file does not parse), `ModelValidationError` (the combined model is invalid, positions resolved back into the module file) and `ModuleTransformationError` (duplicate conditions/relations, unknown extended types, non-module files). All carry `File` where known.
- Models returned by `TransformDslToModel` / `TransformModulesToModel` always have a non-null `TypeDefinitions` list (the SDK annotates it non-nullable); a model with no types serializes `"type_definitions":[]` where JS omits the key.
- `ModFile.Parse` throws `ModFileValidationException`. YAML is parsed with YamlDotNet; the fga.mod validation messages match JS, but a malformed-YAML error carries YamlDotNet's message and stops at the first YAML error (JS `yaml` collects several). Unlike JS/Go/Java, `ModFile` exposes plain values rather than value+position wrappers; positions are only reported through errors.
- `JSON` output uses the SDK's System.Text.Json contract.
- All `string` / `IEnumerable` / model parameters throw `ArgumentNullException` when null.

### 4.2 `OpenFga.Language.Validation`

```csharp
namespace OpenFga.Language.Validation;

public static class ModelValidator
{
    public static void Validate(string dsl);
    public static void Validate(string dsl, ModelValidationOptions options);
    public static void Validate(AuthorizationModel model);
    public static void Validate(AuthorizationModel model, ModelValidationOptions options);
}

public sealed class ModelValidationOptions
{
    public string TypePattern { get; set; }       // default: ^[^:#@\*\s]{1,254}$
    public string RelationPattern { get; set; }   // default: ^[^:#@\*\s]{1,50}$
}

public static class TupleSyntax
{
    public static bool IsValidObject(string value);
    public static bool IsValidObjectId(string value);
    public static bool IsValidType(string value);
    public static bool IsValidRelation(string value);
    public static bool IsValidUser(string value);
    public static bool IsValidUserset(string value);
    public static bool IsValidUserObject(string value);
    public static bool IsValidUserWildcard(string value);
    public static bool IsValidConditionName(string value);
}
```

- `Validate(string dsl, …)` throws `DslSyntaxException` on parse errors, then `ModelValidationException` for semantic errors (with ranges).
- `Validate(AuthorizationModel, …)` throws `ModelValidationException` (errors have no `Range`).
- Invalid regex in options → `ArgumentException`.
- Default patterns are the existing implementations' `Rules` (`type`, `relation`), anchored.

### 4.3 `OpenFga.Language.Utilities`

```csharp
namespace OpenFga.Language.Utilities;

public static class ModelUtilities
{
    public static bool IsRelationAssignable(Userset relationDefinition);
    public static string? GetModuleForObjectTypeRelation(TypeDefinition typeDefinition, string relation);
}
```

### 4.4 Errors (`OpenFga.Language`)

```csharp
public abstract class OpenFgaLanguageException : Exception { }

public sealed class DslSyntaxException : OpenFgaLanguageException
{ public IReadOnlyList<SyntaxError> Errors { get; } }

public sealed class ModelValidationException : OpenFgaLanguageException
{ public IReadOnlyList<ModelValidationError> Errors { get; } }

public sealed class ModFileValidationException : OpenFgaLanguageException
{ public IReadOnlyList<LanguageError> Errors { get; } }

public sealed class ModuleTransformationException : OpenFgaLanguageException
{ public IReadOnlyList<LanguageError> Errors { get; } }   // SyntaxError | ModelValidationError | ModuleTransformationError

public sealed class DslTransformationException : OpenFgaLanguageException { }

public class LanguageError
{
    public string Message { get; }
    public string? File { get; }
    public SourceRange? Range { get; }
}

public sealed class SyntaxError : LanguageError { public string? Symbol { get; } }
public sealed class ModuleTransformationError : LanguageError { public string? Symbol { get; } }

public sealed class ModelValidationError : LanguageError
{
    public ValidationErrorType ErrorType { get; }
    public string? Symbol { get; }
    public string? TypeName { get; }
    public string? RelationName { get; }
    public string? ConditionName { get; }
    public string? Module { get; }
    public string? OffendingType { get; }
}

public readonly struct SourceRange : IEquatable<SourceRange>
{
    public int StartLine { get; }
    public int EndLine { get; }
    public int StartColumn { get; }
    public int EndColumn { get; }
    // Equals / GetHashCode / == / != / ToString
}

public enum ValidationErrorType
{
    SchemaVersionRequired, SchemaVersionUnsupported, ReservedTypeKeywords, ReservedRelationKeywords,
    SelfError, InvalidName, MissingDefinition, InvalidRelationType, InvalidRelationOnTupleset,
    InvalidType, RelationNoEntrypoint, TuplesetNotDirect, DuplicatedError, RequireSchema1_0,
    AssignableRelationsMustHaveType, AllowedTypesNotValidOnSchema1_0, InvalidSchema, InvalidSyntax,
    TypeRestrictionCannotHaveWildcardAndRelation, ConditionNotDefined, ConditionNotUsed,
    DifferentNestedConditionName, MultipleModulesInFile,
}
```

- Line/column values are **zero-based**, matching the shared fixtures and JS.
- `ValidationErrorType` maps internally to the shared string codes (`"invalid-name"`, `"relation-no-entry-point"`, …) used by fixtures and other libraries. Member names match the JS/Java names exactly, for parity. `RequireSchema1_0` (`"allowed-type-schema-10"`) and `AllowedTypesNotValidOnSchema1_0` (`"allowed-type-not-valid-on-schema-1_0"`) are kept even though no implementation currently raises them. Version numbers in identifiers use an underscore between major and minor (`1_0`), following Roslyn `LanguageVersion.CSharp7_3` and Microsoft.OpenApi `OpenApiSpecVersion.OpenApi3_0`, because concatenated digits (BCL style, e.g. `SslProtocols.Tls12`) would be ambiguous with future versions (1.0 vs 10). CA1707 is suppressed for these two members only, via `[SuppressMessage]` with a justification, not project-wide.
- Aggregate exception `Message` format matches JS: `"{n} error(s) occurred:\n\t* {error}\n\t* …\n\n"`. Each errors `ToString()` is `"{kind} error at line={StartLine}, column={StartColumn}: {Message}"`, or `"{kind} error: {Message}"` when `Range` is null, where `{kind}` is `syntax` (SyntaxError), the error-type code (ModelValidationError), `validation` (fga.mod errors) or `transformation-error` (ModuleTransformationError). Divergence: JS freezes the message before module positions are resolved, so it omits the location for module-resolved validation errors; .NET includes it whenever `Range` is set.
- No `[Serializable]` / serialization constructors (BinaryFormatter is obsolete).
- The three JS JSON→DSL errors (`UnsupportedDSLNestingError`, `ConditionNameDoesntMatchError`, `UnsupportedModularModules`) collapse into `DslTransformationException`; JS `ConfigurationError` becomes `ArgumentException`.

## 5. Project layout and internals

```
pkg/dotnet/
├── OpenFga.Language.slnx
├── Directory.Build.props        # LangVersion latest, Nullable enable, TreatWarningsAsErrors,
│                                # AnalysisLevel latest-recommended, culture analyzers as errors,
│                                # GenerateDocumentationFile, Deterministic, SourceLink
├── Makefile                     # build / test / lint / audit / format / all-tests
├── README.md
├── CHANGELOG.md
├── src/OpenFga.Language/
│   ├── OpenFga.Language.csproj  # netstandard2.0; OpenFga.Sdk 0.10.4, Antlr4.Runtime.Standard 4.13.1, YamlDotNet (fga.mod parsing)
│   ├── Gen/                     # ANTLR output (committed, like Go & Java), namespace OpenFga.Language.Antlr
│   ├── Transformation/            # ModelTransformer, ModFile, ModuleFile, TransformOptions
│   │                            # + internal DSL listener, error listener, model→DSL writer, module combiner
│   ├── Utilities/               # ModelUtilities
│   ├── Validation/              # ModelValidator, ModelValidationOptions, TupleSyntax + internal rules
│   └── Errors/                  # exceptions, error items, SourceRange, ValidationErrorType
└── test/OpenFga.Language.Tests/
    └── OpenFga.Language.Tests.csproj  # net10.0; xUnit, YamlDotNet; reads ../../../../tests/data
```

- **ANTLR generation:** root `Makefile` target `antlr-gen-dotnet` runs the existing ANTLR container with `-Dlanguage=CSharp -package OpenFga.Language.Antlr -o pkg/dotnet/src/OpenFga.Language/Gen`, then post-processes generated files so `public` types become `internal` (the C# target always emits `public partial`).
- **Internals:** all non-API types are `internal sealed`. Tests get `InternalsVisibleTo` only where needed.
- **Port source:** logic is ported primarily from the JS implementation (most complete: modules, validation), cross-checked against Java for statically-typed idioms.
- **Strings:** all comparisons/case conversions ordinal or invariant (enforced by analyzers). Where JS uses `localeCompare` for sorting, .NET uses ordinal comparison (as Go and Java do; all share the same fixtures).
- **Intentional divergences from JS bugs** (JS throws a raw `TypeError` in these cases):
  - Entry-point analysis of `a from b` where `b` has a conditioned type restriction (`[folder with cond]`) looks up the restriction's type (`folder`) instead of the full restriction string.
  - `TupleSyntax` anchors patterns with `\z` (end of string), so a trailing `
` is rejected; .NET `$` would otherwise accept it.

## 6. Testing

xUnit `[Theory]` tests driven by the shared fixtures; each case's `name` is the display name.

| Fixture | Covers |
|---|---|
| `tests/data/transformer/*/authorization-model.{fga,json}` | DSL→JSON, JSON→DSL |
| `tests/data/transformer-module/*` | `ModFile.Parse` + `TransformModulesToModel` |
| `dsl-syntax-validation-cases.yaml` | `DslSyntaxException.Errors` |
| `dsl-semantic-validation-cases.yaml` | `ModelValidator.Validate(string)` |
| `json-validation-cases.yaml` | `ModelValidator.Validate(AuthorizationModel)` |
| `json-syntax-transformer-validation-cases.yaml` | JSON→DSL failures |
| `fga-mod-transformer-cases.yaml` | `ModFileValidationException` |
| `object-id-validation-cases.yaml` | `TupleSyntax` |

- JSON compared semantically (normalized `JsonNode` deep equality), not byte-for-byte.
- Errors compared on message, error-type string code, ranges and metadata, as in JS/Java.
- Hand-written tests for .NET-specific behaviour: null arguments, invalid regex options, exception `Message` format, `ModelUtilities`, `SourceRange` equality.
- `tests/data/stores` is out of scope.

## 7. Repo integration, CI and release

- **Root `Makefile`:** `antlr-gen-dotnet`, `build-dotnet`, `test-dotnet`, `lint-dotnet` (`dotnet format --verify-no-changes` + analyzers), `audit-dotnet` (`dotnet list package --vulnerable --include-transitive`), `format-dotnet`, `all-tests-dotnet`; aggregate `antlr-gen`, `build`, `test`, `lint` include them.
- **Workflows** (mirroring Java/JS):
  - `pkg-dotnet-pr.yaml` — triggers on `pkg/dotnet/**`, `*.g4`, `tests/**`, its own workflow files; calls the build workflow.
  - `pkg-dotnet-build.yaml` — single `ubuntu-latest` job: `setup-dotnet` (10.0.x), ANTLR gen, lint, test, audit.
  - `pkg-dotnet-release.yaml` — on `pkg/dotnet/v*` tag: pack, verify tag/manifest/csproj versions, push `.nupkg` + `.snupkg` to NuGet (OIDC) and GitHub Packages — same pattern as `openfga/dotnet-sdk`.
- **release-please:** add `pkg/dotnet` to `release-please-config.json` and `.release-please-manifest.json`, starting at `0.1.0`.
- **Package metadata:** mirrors `openfga/dotnet-sdk` (authors, company, copyright, icon, repository, release notes, tags), README, `Apache-2.0` license expression, SourceLink, deterministic build, symbols package.
- **Docs:** add a .NET column to the feature-parity table in `AGENTS.md` and the root `README.md`; add .NET commands to `AGENTS.md`.

## 8. Out of scope (v1)

Graph utilities; store/tuple YAML validation; additional TFMs (`net8.0`+); a .NET Framework test leg; public parser/listener/keyword/line-number APIs; position information on successfully parsed `ModFile` values; strong naming (blocked while the SDK is unsigned).
