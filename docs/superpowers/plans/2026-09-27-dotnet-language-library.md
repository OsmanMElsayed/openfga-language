# .NET OpenFGA Language Library Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ship `OpenFga.Language`, a `netstandard2.0` library in `pkg/dotnet/` that transforms OpenFGA DSL ↔ `OpenFga.Sdk` `AuthorizationModel`/JSON, parses `fga.mod`, combines modular models, and validates models, passing the shared fixtures in `tests/data/`.

**Architecture:** A port of the JS implementation (`pkg/js`), which is the most complete one, onto the ANTLR C# target and the `OpenFga.Sdk` model types. A small public surface of static entry points (`ModelTransformer`, `ModFile.Parse`, `ModelValidator`, `TupleSyntax`, `ModelUtilities`) sits over internal parsing, writing, validation and module-combining units. Failures are reported through aggregate exceptions whose `Errors` carry zero-based source ranges.

**Tech Stack:** C# (latest), `netstandard2.0` library, `net10.0` xUnit tests, ANTLR 4.13.1 (Docker-generated C# target), `OpenFga.Sdk` 0.10.4, YamlDotNet, System.Text.Json (via the SDK), `Microsoft.CodeAnalysis.PublicApiAnalyzers`.

**Spec:** `docs/superpowers/specs/2026-09-27-dotnet-language-library-design.md`

## Global Constraints

- Library targets `netstandard2.0` only; the test project targets `net10.0` only.
- Library package references (exact): `OpenFga.Sdk` 0.10.4, `Antlr4.Runtime.Standard` 4.13.1, `YamlDotNet` 18.1.0, `Microsoft.CodeAnalysis.PublicApiAnalyzers` 5.6.0 (`PrivateAssets="all"`).
- Test package references (exact): `Microsoft.NET.Test.Sdk` 18.10.1, `xunit` 2.9.3, `xunit.runner.visualstudio` 4.0.0, `YamlDotNet` 18.1.0.
- NuGet package ID `OpenFga.Language`, root namespace `OpenFga.Language`, version `0.1.0`, license `Apache-2.0`. Not strong-named.
- The public API is exactly spec §4. Everything else is `internal` (and `sealed` where it's a class). Every task that adds public members updates `PublicAPI.Unshipped.txt` in the same commit.
- Every public member has XML documentation (`GenerateDocumentationFile` + `TreatWarningsAsErrors` makes CS1591 a build error).
- All string comparison, searching and case conversion is ordinal or invariant. CA1304, CA1305, CA1307, CA1309, CA1310 and CA1311 are errors. Sorting that JS does with `localeCompare` uses `StringComparer.Ordinal`.
- Line and column numbers are zero-based everywhere.
- Error messages and error-type codes are byte-for-byte the JS strings quoted in this plan; the fixtures assert them.
- A `null` argument to any public method throws `ArgumentNullException` with the parameter's name.
- `OpenFga.Sdk` model properties annotated non-nullable (`AuthorizationModel.TypeDefinitions`, `.SchemaVersion`, `RelationReference.Type`, `Usersets.Child`, `Difference.Base`/`.Subtract`, `TupleToUserset.Tupleset`/`.ComputedUserset`, `Condition.Name`/`.Expression`) can still be `null` at runtime when a model came from JSON. Read them defensively (`?.`, `?? empty`). Assign `null!` only where this plan does.
- Never hand-edit `pkg/dotnet/src/OpenFga.Language/Gen/`. It is produced by `make antlr-gen-dotnet`.
- If an analyzer rule not mentioned in this plan fires, change the code to satisfy it. Suppress a rule (in `pkg/dotnet/.editorconfig`, with a comment giving the reason) only when satisfying it would change the public API defined in the spec.
- Commits use Conventional Commits and end with the two attribution lines shown in each commit step. Commits are signed through the user's 1Password agent. If signing fails, stop and ask the user; never pass `--no-gpg-sign` or `--no-verify`.
- Commands below are bash (Git Bash on Windows, as the repo's Makefiles already require) and run from `pkg/dotnet/` unless they start with `make`, which runs from the repo root.

## Review Focus

1. **Malformed DSL input.** Truncated or garbage DSL (for example `"type"`, `""`, `"model\n  schema 1.1\ntype user\n  relations\n    define viewer: [user] or"`) must surface as `DslSyntaxException`, never as `NullReferenceException` from listener callbacks on an error-recovered parse tree. Test added in Task 3 (`MalformedDslThrowsOnlyDslSyntaxException`) and Task 7 (`ValidateMalformedDslThrowsOnlyDslSyntaxException`).
2. **Windows line endings.** DSL files authored on Windows (`\r\n`) must transform exactly like their `\n` equivalents. Test added in Task 3 (`CrlfDslTransformsLikeLf`).
3. **Caller's model is not mutated.** `TransformModelToDsl` must not reorder the caller's union/intersection children, even though the JS algorithm moves direct assignments to the front in place. Test added in Task 4 (`TransformModelToDslDoesNotMutateInput`).
4. **Symbols containing regex metacharacters.** Location resolution builds `\b{symbol}\b` patterns exactly like JS. A symbol that makes an invalid pattern (for example `a(`) must yield "not found" rather than throwing `ArgumentException`. Test added in Task 6 (`FindWordReturnsMinusOneForInvalidPattern`).
5. **Trailing newline in tuple values.** `TupleSyntax.IsValidObject("document:1\n")` must be `false`. .NET's `$` matches before a final `\n` while JS's does not. Test added in Task 5 (`TrailingNewlineIsInvalid`).

---

## File Map

```
Makefile                                         (modify: dotnet targets; aggregates in Task 11)
.gitattributes                                   (modify, Task 11)
.github/workflows/pkg-dotnet-{pr,build,release}.yaml   (create, Task 11)
release-please-config.json, .release-please-manifest.json (modify, Task 11)
AGENTS.md, README.md                             (modify, Task 11)
pkg/dotnet/
├── .editorconfig  .gitignore  Directory.Build.props  Makefile  OpenFga.Language.sln
├── README.md  CHANGELOG.md
├── src/OpenFga.Language/
│   ├── OpenFga.Language.csproj  PublicAPI.Shipped.txt  PublicAPI.Unshipped.txt
│   ├── Gen/                                     generated ANTLR lexer/parser/listener (internal)
│   ├── Errors/
│   │   ├── SourceRange.cs                       public struct: zero-based line/column span
│   │   ├── LanguageError.cs                     public base error item (Message, File, Range)
│   │   ├── SyntaxError.cs  ModuleTransformationError.cs  ModelValidationError.cs
│   │   ├── ValidationErrorType.cs               public enum
│   │   ├── ValidationErrorTypeCodes.cs          internal enum <-> JS string code map
│   │   ├── ErrorMessages.cs                     internal aggregate "n errors occurred" formatter
│   │   └── *Exception.cs                        OpenFgaLanguageException + 5 sealed exceptions
│   ├── Internal/
│   │   ├── DictionaryExtensions.cs              GetOrNull helper (netstandard2.0 lacks GetValueOrDefault)
│   │   ├── TextSearch.cs                        JS-compatible \bword\b search + column resolvers
│   │   └── LineNumbers.cs                       type/relation/condition/schema line lookup
│   ├── Parsing/
│   │   ├── DslParser.cs                         comment stripping + ANTLR pipeline -> DslParseResult
│   │   ├── DslErrorListener.cs                  ANTLR errors -> SyntaxError
│   │   ├── DslListener.cs                       parse tree -> AuthorizationModel
│   │   └── ConditionTypeNames.cs                DSL param type <-> SDK TypeName enum
│   ├── Transformation/
│   │   ├── ModelTransformer.cs                  public static entry points
│   │   ├── TransformOptions.cs  ModuleFile.cs  ModFile.cs   public
│   │   ├── ModelJson.cs                         SDK JSON (de)serialization + clone
│   │   ├── DslWriter.cs                         AuthorizationModel -> DSL text
│   │   └── ModuleCombiner.cs                    module files -> one AuthorizationModel
│   ├── Validation/
│   │   ├── ModelValidator.cs  ModelValidationOptions.cs  TupleSyntax.cs   public
│   │   ├── Rules.cs                             shared regex fragments
│   │   ├── TypeRestrictions.cs                  restriction string helpers
│   │   ├── ValidationErrorCollector.cs          builds ModelValidationError (JS ExceptionCollector)
│   │   ├── VisitedRelations.cs  EntryPointAnalyzer.cs
│   │   └── ModelValidationEngine.cs             JS validateJSON / modelValidation / populateRelations
│   └── Utilities/ModelUtilities.cs              public
└── test/OpenFga.Language.Tests/
    ├── OpenFga.Language.Tests.csproj
    ├── Fixtures/ FixturePaths.cs  FixtureModels.cs  TestData.cs  JsonAssert.cs  ErrorAssert.cs
    └── *Tests.cs
```

---

### Task 1: Scaffold the .NET package and generate the ANTLR C# parser

**Files:**
- Create: `pkg/dotnet/.gitignore`, `pkg/dotnet/.editorconfig`, `pkg/dotnet/Directory.Build.props`, `pkg/dotnet/Makefile`, `pkg/dotnet/README.md`, `pkg/dotnet/OpenFga.Language.sln`
- Create: `pkg/dotnet/src/OpenFga.Language/OpenFga.Language.csproj`, `PublicAPI.Shipped.txt`, `PublicAPI.Unshipped.txt`
- Create (generated): `pkg/dotnet/src/OpenFga.Language/Gen/*`
- Create: `pkg/dotnet/test/OpenFga.Language.Tests/OpenFga.Language.Tests.csproj`, `ParserSmokeTests.cs`
- Modify: `Makefile` (repo root)

**Interfaces:**
- Consumes: nothing.
- Produces: internal generated types in namespace `OpenFga.Language.Antlr`: `OpenFGALexer`, `OpenFGAParser` (with nested `*Context` classes), `OpenFGAParserBaseListener`, `IOpenFGAParserListener`. Root make target `antlr-gen-dotnet`. `InternalsVisibleTo("OpenFga.Language.Tests")`.

- [ ] **Step 1: Create the build scaffolding**

`pkg/dotnet/.gitignore`:
```gitignore
bin/
obj/
TestResults/
*.user
audit.log
```

`pkg/dotnet/.editorconfig`:
```ini
[*.cs]
# Behaviour must be identical on every runtime (NLS vs ICU), so culture-sensitive string operations are errors (spec §3.2).
dotnet_diagnostic.CA1304.severity = error
dotnet_diagnostic.CA1305.severity = error
dotnet_diagnostic.CA1307.severity = error
dotnet_diagnostic.CA1309.severity = error
dotnet_diagnostic.CA1310.severity = error
dotnet_diagnostic.CA1311.severity = error
# Exceptions are only ever created by this library; their constructors are internal by design (spec §4.4).
dotnet_diagnostic.CA1032.severity = none

[src/OpenFga.Language/Gen/**.cs]
generated_code = true

[test/**.cs]
# xUnit requires public test classes.
dotnet_diagnostic.CA1515.severity = none
# Inline arrays in test data are clearer than hoisted static fields.
dotnet_diagnostic.CA1861.severity = none
# Test fixtures favour readable interface types over micro-optimisations.
dotnet_diagnostic.CA1859.severity = none
```

`pkg/dotnet/Directory.Build.props`:
```xml
<Project>
  <PropertyGroup>
    <LangVersion>latest</LangVersion>
    <Nullable>enable</Nullable>
    <ImplicitUsings>disable</ImplicitUsings>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <AnalysisLevel>latest-recommended</AnalysisLevel>
    <EnforceCodeStyleInBuild>true</EnforceCodeStyleInBuild>
    <Deterministic>true</Deterministic>
    <ContinuousIntegrationBuild Condition="'$(CI)' == 'true'">true</ContinuousIntegrationBuild>
  </PropertyGroup>
</Project>
```

`pkg/dotnet/README.md` (expanded in Task 11):
```markdown
# OpenFGA Language - .NET

.NET implementation of the OpenFGA DSL transformer and validator. See the [repository README](../../README.md).
```

- [ ] **Step 2: Create the library project**

`pkg/dotnet/src/OpenFga.Language/OpenFga.Language.csproj`:
```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>netstandard2.0</TargetFramework>
    <RootNamespace>OpenFga.Language</RootNamespace>
    <AssemblyName>OpenFga.Language</AssemblyName>
    <PackageId>OpenFga.Language</PackageId>
    <Version>0.1.0</Version> <!-- x-release-please-version -->
    <Authors>OpenFGA</Authors>
    <Description>OpenFGA DSL parser, DSL/JSON transformer and model validator for .NET.</Description>
    <PackageLicenseExpression>Apache-2.0</PackageLicenseExpression>
    <PackageReadmeFile>README.md</PackageReadmeFile>
    <PackageProjectUrl>https://github.com/openfga/language</PackageProjectUrl>
    <RepositoryUrl>https://github.com/openfga/language</RepositoryUrl>
    <PackageTags>openfga;authorization;fga;fine-grained-authorization;rebac;zanzibar</PackageTags>
    <GenerateDocumentationFile>true</GenerateDocumentationFile>
    <IncludeSymbols>true</IncludeSymbols>
    <SymbolPackageFormat>snupkg</SymbolPackageFormat>
    <PublishRepositoryUrl>true</PublishRepositoryUrl>
    <EmbedUntrackedSources>true</EmbedUntrackedSources>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="OpenFga.Sdk" Version="0.10.4" />
    <PackageReference Include="Antlr4.Runtime.Standard" Version="4.13.1" />
    <PackageReference Include="YamlDotNet" Version="18.1.0" />
    <PackageReference Include="Microsoft.CodeAnalysis.PublicApiAnalyzers" Version="5.6.0" PrivateAssets="all" />
  </ItemGroup>

  <ItemGroup>
    <AdditionalFiles Include="PublicAPI.Shipped.txt" />
    <AdditionalFiles Include="PublicAPI.Unshipped.txt" />
    <None Include="..\..\README.md" Pack="true" PackagePath="\" />
    <InternalsVisibleTo Include="OpenFga.Language.Tests" />
  </ItemGroup>

</Project>
```

`PublicAPI.Shipped.txt` and `PublicAPI.Unshipped.txt` (same content, one line each):
```
#nullable enable
```

- [ ] **Step 3: Add the ANTLR generation targets**

In the repo-root `Makefile`, add this section immediately before `#### Test data #####`:
```make
#### .NET #####

.PHONY: antlr-gen-dotnet
antlr-gen-dotnet: build-antlr-container
	${ANTLR_CMD} -Dlanguage=CSharp -o pkg/dotnet/src/OpenFga.Language/Gen -package OpenFga.Language.Antlr /app/OpenFGALexer.g4 /app/OpenFGAParser.g4
	$(MAKE) -C pkg/dotnet post-antlr-gen

.PHONY: build-dotnet
build-dotnet: antlr-gen-dotnet
	$(MAKE) -C pkg/dotnet build

.PHONY: clean-dotnet
clean-dotnet:
	$(MAKE) -C pkg/dotnet clean

.PHONY: test-dotnet
test-dotnet: antlr-gen-dotnet
	$(MAKE) -C pkg/dotnet test

.PHONY: lint-dotnet
lint-dotnet: antlr-gen-dotnet
	$(MAKE) -C pkg/dotnet lint

.PHONY: audit-dotnet
audit-dotnet: antlr-gen-dotnet
	$(MAKE) -C pkg/dotnet audit

.PHONY: format-dotnet
format-dotnet: antlr-gen-dotnet
	$(MAKE) -C pkg/dotnet format

.PHONY: all-tests-dotnet
all-tests-dotnet: antlr-gen-dotnet
	$(MAKE) -C pkg/dotnet all-tests

```

`pkg/dotnet/Makefile` (recipe lines are indented with a TAB):
```make
.PHONY: build clean test lint audit format all-tests post-antlr-gen

build:
	dotnet build -c Release

clean:
	dotnet clean

test:
	dotnet test -c Release

lint:
	dotnet format --verify-no-changes
	dotnet build -c Release

audit:
	dotnet restore
	dotnet list package --vulnerable --include-transitive | tee audit.log
	! grep -q "has the following vulnerable packages" audit.log
	rm -f audit.log

format:
	dotnet format

all-tests: build audit lint test

# The ANTLR C# target always emits public types; the library exposes none of them (spec §3.4).
# Every warning in generated code is also silenced, since the code can't be edited.
post-antlr-gen:
	sed -i -E 's/^(\s*)public (partial class|class|interface) /\1internal \2 /' src/OpenFga.Language/Gen/*.cs
	sed -i '1i #pragma warning disable' src/OpenFga.Language/Gen/*.cs
```

- [ ] **Step 4: Generate the parser**

Run (repo root, Docker running): `make antlr-gen-dotnet`
Expected: `pkg/dotnet/src/OpenFga.Language/Gen/` contains `OpenFGALexer.cs`, `OpenFGAParser.cs`, `OpenFGAParserListener.cs`, `OpenFGAParserBaseListener.cs` plus `.interp`/`.tokens` files.

Run: `grep -n "^\s*public \(partial class\|class\|interface\) " pkg/dotnet/src/OpenFga.Language/Gen/*.cs`
Expected: no output (every generated type is now `internal`).

Run: `head -1 pkg/dotnet/src/OpenFga.Language/Gen/OpenFGAParser.cs`
Expected: `#pragma warning disable`

- [ ] **Step 5: Create the test project, solution and a failing smoke test**

`pkg/dotnet/test/OpenFga.Language.Tests/OpenFga.Language.Tests.csproj`:
```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <IsPackable>false</IsPackable>
    <IsTestProject>true</IsTestProject>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="18.10.1" />
    <PackageReference Include="xunit" Version="2.9.3" />
    <PackageReference Include="xunit.runner.visualstudio" Version="4.0.0" PrivateAssets="all" />
    <PackageReference Include="YamlDotNet" Version="18.1.0" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\src\OpenFga.Language\OpenFga.Language.csproj" />
  </ItemGroup>

  <ItemGroup>
    <Using Include="Xunit" />
  </ItemGroup>

</Project>
```

Run (in `pkg/dotnet`):
```bash
dotnet new sln -n OpenFga.Language --format sln
dotnet sln OpenFga.Language.sln add src/OpenFga.Language/OpenFga.Language.csproj test/OpenFga.Language.Tests/OpenFga.Language.Tests.csproj
```

`pkg/dotnet/test/OpenFga.Language.Tests/ParserSmokeTests.cs`:
```csharp
using System.IO;
using Antlr4.Runtime;
using OpenFga.Language.Antlr;

namespace OpenFga.Language.Tests;

public class ParserSmokeTests
{
    [Fact]
    public void GeneratedParserParsesMinimalModel()
    {
        var errors = new List<string>();
        var parser = new OpenFGAParser(new CommonTokenStream(new OpenFGALexer(new AntlrInputStream("model\n  schema 1.1\ntype user\n"))));
        parser.RemoveErrorListeners();
        parser.AddErrorListener(new CollectingErrorListener(errors));

        parser.main();

        Assert.Empty(errors);
    }

    [Fact]
    public void GeneratedParserReportsErrors()
    {
        var errors = new List<string>();
        var parser = new OpenFGAParser(new CommonTokenStream(new OpenFGALexer(new AntlrInputStream("type user"))));
        parser.RemoveErrorListeners();
        parser.AddErrorListener(new CollectingErrorListener(errors));

        parser.main();

        Assert.NotEmpty(errors);
    }

    private sealed class CollectingErrorListener(List<string> errors) : BaseErrorListener
    {
        public override void SyntaxError(TextWriter output, IRecognizer recognizer, IToken offendingSymbol, int line, int charPositionInLine, string msg, RecognitionException e)
            => errors.Add(msg);
    }
}
```

- [ ] **Step 6: Build and run the tests**

Run: `dotnet test`
Expected: build succeeds with 0 warnings; `Passed!  - Failed: 0, Passed: 2`.

If the build fails with CS0122 ("inaccessible due to its protection level") the `InternalsVisibleTo` item is missing. If it fails with warnings from `Gen/`, check that Step 4's `#pragma` line is present.

- [ ] **Step 7: Commit**

```bash
git add Makefile pkg/dotnet
git commit -m "build(dotnet): scaffold OpenFga.Language package with generated ANTLR parser

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01U8M7PfSCNGaz27dr4xJPEu"
```

---

### Task 2: Error types and exceptions

**Files:**
- Create: `pkg/dotnet/src/OpenFga.Language/Errors/SourceRange.cs`, `LanguageError.cs`, `SyntaxError.cs`, `ModuleTransformationError.cs`, `ModelValidationError.cs`, `ValidationErrorType.cs`, `ValidationErrorTypeCodes.cs`, `ErrorMessages.cs`, `OpenFgaLanguageException.cs`, `DslSyntaxException.cs`, `ModelValidationException.cs`, `ModFileValidationException.cs`, `ModuleTransformationException.cs`, `DslTransformationException.cs`
- Modify: `pkg/dotnet/src/OpenFga.Language/PublicAPI.Unshipped.txt`
- Test: `pkg/dotnet/test/OpenFga.Language.Tests/ErrorTypesTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces (all namespace `OpenFga.Language`):
  - `public readonly struct SourceRange(int startLine, int endLine, int startColumn, int endColumn)` with `StartLine`, `EndLine`, `StartColumn`, `EndColumn`, equality operators.
  - `public class LanguageError { string Message; string? File {get; internal set;}; SourceRange? Range {get; internal set;} }` with `internal LanguageError(string kind, string message, string? file, SourceRange? range)` and `internal string Kind`.
  - `public sealed class SyntaxError : LanguageError` with `internal SyntaxError(string message, SourceRange? range, string? symbol)` and `string? Symbol`.
  - `public sealed class ModuleTransformationError : LanguageError` with `internal ModuleTransformationError(string message, string? file, SourceRange? range, string? symbol)` and `string? Symbol`.
  - `public sealed class ModelValidationError : LanguageError` with `internal ModelValidationError(string message, string? file, SourceRange? range, ValidationErrorType errorType, string symbol, string? typeName, string? relationName, string? conditionName, string? module, string? offendingType)`.
  - `public enum ValidationErrorType`; `internal static class ValidationErrorTypeCodes { static string ToCode(ValidationErrorType) }`.
  - `internal static class LanguageErrors { static LanguageError ModFile(string message, SourceRange range) }`, the kind-`validation` error used by `fga.mod` parsing.
  - Exceptions with internal constructors: `DslSyntaxException(IEnumerable<SyntaxError>)`, `ModelValidationException(IEnumerable<ModelValidationError>)`, `ModFileValidationException(IEnumerable<LanguageError>)`, `ModuleTransformationException(IEnumerable<LanguageError>)`, `DslTransformationException(string message)`.

- [ ] **Step 1: Write the failing tests**

`pkg/dotnet/test/OpenFga.Language.Tests/ErrorTypesTests.cs`:
```csharp
namespace OpenFga.Language.Tests;

public class ErrorTypesTests
{
    [Fact]
    public void SourceRangeHasValueEquality()
    {
        var a = new SourceRange(1, 1, 2, 5);
        var b = new SourceRange(1, 1, 2, 5);

        Assert.Equal(a, b);
        Assert.True(a == b);
        Assert.False(a != b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
        Assert.NotEqual(a, new SourceRange(1, 1, 2, 6));
    }

    [Fact]
    public void SyntaxErrorToStringIncludesLocation()
    {
        var error = new SyntaxError("mismatched input 'x'", new SourceRange(3, 3, 7, 8), "x");

        Assert.Equal("syntax error at line=3, column=7: mismatched input 'x'", error.ToString());
    }

    [Fact]
    public void ValidationErrorWithoutRangeOmitsLocation()
    {
        var error = new ModelValidationError("invalid schema 1.3", null, null, ValidationErrorType.InvalidSchema, "1.3", null, null, null, null, null);

        Assert.Equal("invalid-schema error: invalid schema 1.3", error.ToString());
    }

    [Fact]
    public void AggregateMessageMatchesJsFormat()
    {
        var exception = new DslSyntaxException(
        [
            new SyntaxError("first", new SourceRange(0, 0, 1, 2), "a"),
            new SyntaxError("second", new SourceRange(4, 4, 0, 3), "b"),
        ]);

        Assert.Equal(
            "2 errors occurred:\n\t* syntax error at line=0, column=1: first\n\t* syntax error at line=4, column=0: second\n\n",
            exception.Message);
        Assert.Equal(2, exception.Errors.Count);
    }

    [Fact]
    public void SingleErrorMessageIsSingular()
    {
        var exception = new ModFileValidationException([LanguageErrors.ModFile("missing schema field", default)]);

        Assert.Equal("1 error occurred:\n\t* validation error at line=0, column=0: missing schema field\n\n", exception.Message);
    }

    [Fact]
    public void ModuleTransformationErrorKind()
    {
        var error = new ModuleTransformationError("duplicate condition a", "org.fga", new SourceRange(12, 12, 10, 17), "a");

        Assert.Equal("transformation-error error at line=12, column=10: duplicate condition a", error.ToString());
        Assert.Equal("org.fga", error.File);
    }

    [Theory]
    [InlineData(ValidationErrorType.SchemaVersionRequired, "schema-version-required")]
    [InlineData(ValidationErrorType.SchemaVersionUnsupported, "schema-version-unsupported")]
    [InlineData(ValidationErrorType.ReservedTypeKeywords, "reserved-type-keywords")]
    [InlineData(ValidationErrorType.ReservedRelationKeywords, "reserved-relation-keywords")]
    [InlineData(ValidationErrorType.SelfError, "self-error")]
    [InlineData(ValidationErrorType.InvalidName, "invalid-name")]
    [InlineData(ValidationErrorType.MissingDefinition, "missing-definition")]
    [InlineData(ValidationErrorType.InvalidRelationType, "invalid-relation-type")]
    [InlineData(ValidationErrorType.InvalidRelationOnTupleset, "invalid-relation-on-tupleset")]
    [InlineData(ValidationErrorType.InvalidType, "invalid-type")]
    [InlineData(ValidationErrorType.RelationNoEntrypoint, "relation-no-entry-point")]
    [InlineData(ValidationErrorType.TuplesetNotDirect, "tupleuserset-not-direct")]
    [InlineData(ValidationErrorType.DuplicatedError, "duplicated-error")]
    [InlineData(ValidationErrorType.RequireSchema1_0, "allowed-type-schema-10")]
    [InlineData(ValidationErrorType.AssignableRelationsMustHaveType, "assignable-relation-must-have-type")]
    [InlineData(ValidationErrorType.AllowedTypesNotValidOnSchema1_0, "allowed-type-not-valid-on-schema-1_0")]
    [InlineData(ValidationErrorType.InvalidSchema, "invalid-schema")]
    [InlineData(ValidationErrorType.InvalidSyntax, "invalid-syntax")]
    [InlineData(ValidationErrorType.TypeRestrictionCannotHaveWildcardAndRelation, "type-wildcard-relation")]
    [InlineData(ValidationErrorType.ConditionNotDefined, "condition-not-defined")]
    [InlineData(ValidationErrorType.ConditionNotUsed, "condition-not-used")]
    [InlineData(ValidationErrorType.DifferentNestedConditionName, "different-nested-condition-name")]
    [InlineData(ValidationErrorType.MultipleModulesInFile, "multiple-modules-in-file")]
    public void ErrorTypeCodesMatchSharedFixtures(ValidationErrorType type, string code)
        => Assert.Equal(code, ValidationErrorTypeCodes.ToCode(type));

    [Fact]
    public void AllExceptionsDeriveFromCommonBase()
    {
        Assert.IsAssignableFrom<OpenFgaLanguageException>(new DslTransformationException("x"));
        Assert.IsAssignableFrom<OpenFgaLanguageException>(new ModelValidationException([]));
        Assert.IsAssignableFrom<OpenFgaLanguageException>(new ModuleTransformationException([]));
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test --filter FullyQualifiedName~ErrorTypesTests`
Expected: build FAILS with CS0246 (`SourceRange`, `SyntaxError`, ... could not be found).

- [ ] **Step 3: Implement the error items**

`Errors/SourceRange.cs`:
```csharp
using System;
using System.Globalization;

namespace OpenFga.Language;

/// <summary>A span of source text. All values are zero-based.</summary>
public readonly struct SourceRange : IEquatable<SourceRange>
{
    /// <summary>Initializes a new <see cref="SourceRange"/>.</summary>
    /// <param name="startLine">Zero-based line where the span starts.</param>
    /// <param name="endLine">Zero-based line where the span ends.</param>
    /// <param name="startColumn">Zero-based column where the span starts.</param>
    /// <param name="endColumn">Zero-based column where the span ends (exclusive).</param>
    public SourceRange(int startLine, int endLine, int startColumn, int endColumn)
    {
        StartLine = startLine;
        EndLine = endLine;
        StartColumn = startColumn;
        EndColumn = endColumn;
    }

    /// <summary>Gets the zero-based line where the span starts.</summary>
    public int StartLine { get; }

    /// <summary>Gets the zero-based line where the span ends.</summary>
    public int EndLine { get; }

    /// <summary>Gets the zero-based column where the span starts.</summary>
    public int StartColumn { get; }

    /// <summary>Gets the zero-based column where the span ends (exclusive).</summary>
    public int EndColumn { get; }

    /// <inheritdoc />
    public bool Equals(SourceRange other)
        => StartLine == other.StartLine && EndLine == other.EndLine && StartColumn == other.StartColumn && EndColumn == other.EndColumn;

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is SourceRange other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode()
    {
        unchecked
        {
            var hash = StartLine;
            hash = (hash * 397) ^ EndLine;
            hash = (hash * 397) ^ StartColumn;
            return (hash * 397) ^ EndColumn;
        }
    }

    /// <summary>Determines whether two ranges are equal.</summary>
    public static bool operator ==(SourceRange left, SourceRange right) => left.Equals(right);

    /// <summary>Determines whether two ranges differ.</summary>
    public static bool operator !=(SourceRange left, SourceRange right) => !left.Equals(right);

    /// <inheritdoc />
    public override string ToString()
        => string.Format(CultureInfo.InvariantCulture, "({0},{1})-({2},{3})", StartLine, StartColumn, EndLine, EndColumn);
}
```

`Errors/LanguageError.cs`:
```csharp
using System.Globalization;

namespace OpenFga.Language;

/// <summary>A single problem found in DSL, JSON, module or <c>fga.mod</c> input.</summary>
public class LanguageError
{
    internal LanguageError(string kind, string message, string? file, SourceRange? range)
    {
        Kind = kind;
        Message = message;
        File = file;
        Range = range;
    }

    /// <summary>Gets the human-readable description of the problem.</summary>
    public string Message { get; }

    /// <summary>Gets the name of the file the problem is in, when known.</summary>
    public string? File { get; internal set; }

    /// <summary>Gets the location of the problem, or <see langword="null"/> when there is no source text to point at.</summary>
    public SourceRange? Range { get; internal set; }

    /// <summary>Gets the error kind used as the message prefix (e.g. <c>syntax</c>, <c>invalid-name</c>).</summary>
    internal string Kind { get; }

    /// <summary>Formats the error as <c>"{kind} error at line=L, column=C: {message}"</c>.</summary>
    public override string ToString()
        => Range is { } range
            ? string.Format(CultureInfo.InvariantCulture, "{0} error at line={1}, column={2}: {3}", Kind, range.StartLine, range.StartColumn, Message)
            : string.Format(CultureInfo.InvariantCulture, "{0} error: {1}", Kind, Message);
}

internal static class LanguageErrors
{
    /// <summary>An <c>fga.mod</c> problem; JS reports these with kind <c>validation</c>.</summary>
    public static LanguageError ModFile(string message, SourceRange range) => new("validation", message, null, range);
}
```

`Errors/SyntaxError.cs`:
```csharp
namespace OpenFga.Language;

/// <summary>A DSL syntax error reported by the parser.</summary>
public sealed class SyntaxError : LanguageError
{
    internal SyntaxError(string message, SourceRange? range, string? symbol)
        : base("syntax", message, null, range)
    {
        Symbol = symbol;
    }

    /// <summary>Gets the offending token text, when the parser identified one.</summary>
    public string? Symbol { get; }
}
```

`Errors/ModuleTransformationError.cs`:
```csharp
namespace OpenFga.Language;

/// <summary>A problem combining module files into one model (duplicate definitions, unknown extended types, ...).</summary>
public sealed class ModuleTransformationError : LanguageError
{
    internal ModuleTransformationError(string message, string? file, SourceRange? range, string? symbol)
        : base("transformation-error", message, file, range)
    {
        Symbol = symbol;
    }

    /// <summary>Gets the name the error refers to, when there is one.</summary>
    public string? Symbol { get; }
}
```

`Errors/ModelValidationError.cs`:
```csharp
namespace OpenFga.Language;

/// <summary>A semantic problem in an authorization model.</summary>
public sealed class ModelValidationError : LanguageError
{
    internal ModelValidationError(
        string message,
        string? file,
        SourceRange? range,
        ValidationErrorType errorType,
        string symbol,
        string? typeName,
        string? relationName,
        string? conditionName,
        string? module,
        string? offendingType)
        : base(ValidationErrorTypeCodes.ToCode(errorType), message, file, range)
    {
        ErrorType = errorType;
        Symbol = symbol;
        TypeName = typeName;
        RelationName = relationName;
        ConditionName = conditionName;
        Module = module;
        OffendingType = offendingType;
    }

    /// <summary>Gets the category of the problem.</summary>
    public ValidationErrorType ErrorType { get; }

    /// <summary>Gets the text the error is anchored to (a type, relation, condition or restriction).</summary>
    public string? Symbol { get; }

    /// <summary>Gets the type the error relates to, when there is one.</summary>
    public string? TypeName { get; }

    /// <summary>Gets the relation the error relates to, when there is one.</summary>
    public string? RelationName { get; }

    /// <summary>Gets the condition the error relates to, when there is one.</summary>
    public string? ConditionName { get; }

    /// <summary>Gets the module the offending definition belongs to, for modular models.</summary>
    public string? Module { get; }

    /// <summary>Gets the type that holds the offending reference, for <see cref="ValidationErrorType.InvalidRelationType"/>.</summary>
    public string? OffendingType { get; }
}
```

`Errors/ValidationErrorType.cs`:
```csharp
using System.Diagnostics.CodeAnalysis;

namespace OpenFga.Language;

/// <summary>Categories of semantic model errors. Each maps to the error code shared with the Go, JS and Java libraries.</summary>
public enum ValidationErrorType
{
    /// <summary>The model has no schema version (<c>schema-version-required</c>).</summary>
    SchemaVersionRequired,

    /// <summary>The schema version is not supported (<c>schema-version-unsupported</c>).</summary>
    SchemaVersionUnsupported,

    /// <summary>A type uses a reserved name (<c>reserved-type-keywords</c>).</summary>
    ReservedTypeKeywords,

    /// <summary>A relation uses a reserved name (<c>reserved-relation-keywords</c>).</summary>
    ReservedRelationKeywords,

    /// <summary>Invalid use of <c>self</c> (<c>self-error</c>).</summary>
    SelfError,

    /// <summary>A type or relation name does not match the naming rule (<c>invalid-name</c>).</summary>
    InvalidName,

    /// <summary>A referenced relation does not exist (<c>missing-definition</c>).</summary>
    MissingDefinition,

    /// <summary>A type restriction references a relation that does not exist (<c>invalid-relation-type</c>).</summary>
    InvalidRelationType,

    /// <summary>A <c>from</c> clause references a relation missing on every tupleset type (<c>invalid-relation-on-tupleset</c>).</summary>
    InvalidRelationOnTupleset,

    /// <summary>A type restriction references a type that does not exist (<c>invalid-type</c>).</summary>
    InvalidType,

    /// <summary>A relation can never be satisfied (<c>relation-no-entry-point</c>).</summary>
    RelationNoEntrypoint,

    /// <summary>A tupleset relation is not directly assignable (<c>tupleuserset-not-direct</c>).</summary>
    TuplesetNotDirect,

    /// <summary>A duplicate type, restriction or partial definition (<c>duplicated-error</c>).</summary>
    DuplicatedError,

    /// <summary>Requires schema 1.0 (<c>allowed-type-schema-10</c>). Kept for parity with the other libraries.</summary>
    [SuppressMessage("Naming", "CA1707:Identifiers should not contain underscores", Justification = "Underscore separates major/minor version so 1.0 cannot be confused with 10 (cf. Roslyn LanguageVersion.CSharp7_3).")]
    RequireSchema1_0,

    /// <summary>An assignable relation declares no types (<c>assignable-relation-must-have-type</c>).</summary>
    AssignableRelationsMustHaveType,

    /// <summary>Allowed types are not valid on schema 1.0 (<c>allowed-type-not-valid-on-schema-1_0</c>). Kept for parity with the other libraries.</summary>
    [SuppressMessage("Naming", "CA1707:Identifiers should not contain underscores", Justification = "Underscore separates major/minor version so 1.0 cannot be confused with 10 (cf. Roslyn LanguageVersion.CSharp7_3).")]
    AllowedTypesNotValidOnSchema1_0,

    /// <summary>The schema version is not one OpenFGA accepts (<c>invalid-schema</c>).</summary>
    InvalidSchema,

    /// <summary>Invalid syntax (<c>invalid-syntax</c>).</summary>
    InvalidSyntax,

    /// <summary>A type restriction has both a wildcard and a relation (<c>type-wildcard-relation</c>).</summary>
    TypeRestrictionCannotHaveWildcardAndRelation,

    /// <summary>A type restriction references an undefined condition (<c>condition-not-defined</c>).</summary>
    ConditionNotDefined,

    /// <summary>A condition is defined but never used (<c>condition-not-used</c>).</summary>
    ConditionNotUsed,

    /// <summary>A condition's key differs from its nested name (<c>different-nested-condition-name</c>).</summary>
    DifferentNestedConditionName,

    /// <summary>One file would hold more than one module (<c>multiple-modules-in-file</c>).</summary>
    MultipleModulesInFile,
}
```

`Errors/ValidationErrorTypeCodes.cs`:
```csharp
using System;

namespace OpenFga.Language;

internal static class ValidationErrorTypeCodes
{
    public static string ToCode(ValidationErrorType type) => type switch
    {
        ValidationErrorType.SchemaVersionRequired => "schema-version-required",
        ValidationErrorType.SchemaVersionUnsupported => "schema-version-unsupported",
        ValidationErrorType.ReservedTypeKeywords => "reserved-type-keywords",
        ValidationErrorType.ReservedRelationKeywords => "reserved-relation-keywords",
        ValidationErrorType.SelfError => "self-error",
        ValidationErrorType.InvalidName => "invalid-name",
        ValidationErrorType.MissingDefinition => "missing-definition",
        ValidationErrorType.InvalidRelationType => "invalid-relation-type",
        ValidationErrorType.InvalidRelationOnTupleset => "invalid-relation-on-tupleset",
        ValidationErrorType.InvalidType => "invalid-type",
        ValidationErrorType.RelationNoEntrypoint => "relation-no-entry-point",
        ValidationErrorType.TuplesetNotDirect => "tupleuserset-not-direct",
        ValidationErrorType.DuplicatedError => "duplicated-error",
        ValidationErrorType.RequireSchema1_0 => "allowed-type-schema-10",
        ValidationErrorType.AssignableRelationsMustHaveType => "assignable-relation-must-have-type",
        ValidationErrorType.AllowedTypesNotValidOnSchema1_0 => "allowed-type-not-valid-on-schema-1_0",
        ValidationErrorType.InvalidSchema => "invalid-schema",
        ValidationErrorType.InvalidSyntax => "invalid-syntax",
        ValidationErrorType.TypeRestrictionCannotHaveWildcardAndRelation => "type-wildcard-relation",
        ValidationErrorType.ConditionNotDefined => "condition-not-defined",
        ValidationErrorType.ConditionNotUsed => "condition-not-used",
        ValidationErrorType.DifferentNestedConditionName => "different-nested-condition-name",
        ValidationErrorType.MultipleModulesInFile => "multiple-modules-in-file",
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, null),
    };
}
```

- [ ] **Step 4: Implement the exceptions**

`Errors/ErrorMessages.cs`:
```csharp
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace OpenFga.Language;

internal static class ErrorMessages
{
    /// <summary>JS BaseMultiError format: <c>"{n} error(s) occurred:\n\t* {e1}\n\t* {e2}\n\n"</c>.</summary>
    public static string Aggregate(IReadOnlyCollection<LanguageError> errors)
        => string.Format(
            CultureInfo.InvariantCulture,
            "{0} error{1} occurred:\n\t* {2}\n\n",
            errors.Count,
            errors.Count > 1 ? "s" : string.Empty,
            string.Join("\n\t* ", errors.Select(e => e.ToString())));
}
```

`Errors/OpenFgaLanguageException.cs`:
```csharp
using System;

namespace OpenFga.Language;

/// <summary>Base class for every problem this library reports about its input.</summary>
public abstract class OpenFgaLanguageException : Exception
{
    private protected OpenFgaLanguageException(string message)
        : base(message)
    {
    }
}
```

`Errors/DslSyntaxException.cs`:
```csharp
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace OpenFga.Language;

/// <summary>Thrown when DSL text does not parse. <see cref="Errors"/> lists every syntax error found.</summary>
public sealed class DslSyntaxException : OpenFgaLanguageException
{
    internal DslSyntaxException(IEnumerable<SyntaxError> errors)
        : this(new ReadOnlyCollection<SyntaxError>(errors.ToList()))
    {
    }

    private DslSyntaxException(ReadOnlyCollection<SyntaxError> errors)
        : base(ErrorMessages.Aggregate(errors))
    {
        Errors = errors;
    }

    /// <summary>Gets the syntax errors, in the order they were found.</summary>
    public IReadOnlyList<SyntaxError> Errors { get; }
}
```

`Errors/ModelValidationException.cs`:
```csharp
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace OpenFga.Language;

/// <summary>Thrown when an authorization model is semantically invalid. <see cref="Errors"/> lists every problem found.</summary>
public sealed class ModelValidationException : OpenFgaLanguageException
{
    internal ModelValidationException(IEnumerable<ModelValidationError> errors)
        : this(new ReadOnlyCollection<ModelValidationError>(errors.ToList()))
    {
    }

    private ModelValidationException(ReadOnlyCollection<ModelValidationError> errors)
        : base(ErrorMessages.Aggregate(errors))
    {
        Errors = errors;
    }

    /// <summary>Gets the validation errors, in the order they were found.</summary>
    public IReadOnlyList<ModelValidationError> Errors { get; }
}
```

`Errors/ModFileValidationException.cs`:
```csharp
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace OpenFga.Language;

/// <summary>Thrown when an <c>fga.mod</c> file is invalid. <see cref="Errors"/> lists every problem found.</summary>
public sealed class ModFileValidationException : OpenFgaLanguageException
{
    internal ModFileValidationException(IEnumerable<LanguageError> errors)
        : this(new ReadOnlyCollection<LanguageError>(errors.ToList()))
    {
    }

    private ModFileValidationException(ReadOnlyCollection<LanguageError> errors)
        : base(ErrorMessages.Aggregate(errors))
    {
        Errors = errors;
    }

    /// <summary>Gets the problems, in the order they were found.</summary>
    public IReadOnlyList<LanguageError> Errors { get; }
}
```

`Errors/ModuleTransformationException.cs`:
```csharp
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace OpenFga.Language;

/// <summary>
/// Thrown when module files cannot be combined into a valid model. <see cref="Errors"/> mixes
/// <see cref="SyntaxError"/>, <see cref="ModelValidationError"/> and <see cref="ModuleTransformationError"/> items.
/// </summary>
public sealed class ModuleTransformationException : OpenFgaLanguageException
{
    internal ModuleTransformationException(IEnumerable<LanguageError> errors)
        : this(new ReadOnlyCollection<LanguageError>(errors.ToList()))
    {
    }

    private ModuleTransformationException(ReadOnlyCollection<LanguageError> errors)
        : base(ErrorMessages.Aggregate(errors))
    {
        Errors = errors;
    }

    /// <summary>Gets the problems, in the order they were found.</summary>
    public IReadOnlyList<LanguageError> Errors { get; }
}
```

`Errors/DslTransformationException.cs`:
```csharp
namespace OpenFga.Language;

/// <summary>Thrown when an authorization model cannot be expressed in the DSL.</summary>
public sealed class DslTransformationException : OpenFgaLanguageException
{
    internal DslTransformationException(string message)
        : base(message)
    {
    }
}
```

- [ ] **Step 5: Record the public API**

Run:
```bash
printf '#nullable enable
' > src/OpenFga.Language/PublicAPI.Unshipped.txt
dotnet format analyzers src/OpenFga.Language/OpenFga.Language.csproj --diagnostics RS0016 --severity info
```
(The RS0016 fixer only fills an API file that has no entries yet, so the file is regenerated each time. Nothing has shipped, so `PublicAPI.Unshipped.txt` holds the whole public surface.)
Expected: `PublicAPI.Unshipped.txt` lists exactly the public types and members above, and nothing marked `internal` or `private protected`. Inspect with `git diff src/OpenFga.Language/PublicAPI.Unshipped.txt`.

- [ ] **Step 6: Run the tests to verify they pass**

Run: `dotnet test --filter FullyQualifiedName~ErrorTypesTests`
Expected: `Passed!  - Failed: 0, Passed: 30` (the Theory contributes 23 cases).

- [ ] **Step 7: Commit**

```bash
git add pkg/dotnet
git commit -m "feat(dotnet): add error types and exceptions

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01U8M7PfSCNGaz27dr4xJPEu"
```

---

### Task 3: DSL → AuthorizationModel / JSON

Port of `pkg/js/transformer/dsltojson.ts`. This task also builds the shared fixture helpers every later test task uses.

**Files:**
- Create: `pkg/dotnet/src/OpenFga.Language/Internal/DictionaryExtensions.cs`
- Create: `pkg/dotnet/src/OpenFga.Language/Parsing/DslParser.cs`, `DslErrorListener.cs`, `DslListener.cs`, `ConditionTypeNames.cs`
- Create: `pkg/dotnet/src/OpenFga.Language/Transformation/ModelTransformer.cs`, `ModelJson.cs`
- Modify: `pkg/dotnet/src/OpenFga.Language/PublicAPI.Unshipped.txt`
- Create: `pkg/dotnet/test/OpenFga.Language.Tests/Fixtures/FixturePaths.cs`, `FixtureModels.cs`, `TestData.cs`, `JsonAssert.cs`, `ErrorAssert.cs`
- Test: `pkg/dotnet/test/OpenFga.Language.Tests/DslToModelTests.cs`

**Interfaces:**
- Consumes: Task 1 generated parser; Task 2 `SyntaxError`, `SourceRange`, `DslSyntaxException`.
- Produces:
  - `public static class ModelTransformer` (namespace `OpenFga.Language`): `AuthorizationModel TransformDslToModel(string dsl)`, `string TransformDslToJson(string dsl)`.
  - `internal static class DslParser { static DslParseResult Parse(string dsl) }` (namespace `OpenFga.Language.Parsing`).
  - `internal sealed class DslParseResult { AuthorizationModel Model; IReadOnlyDictionary<string, TypeDefinition> TypeDefExtensions; IReadOnlyList<SyntaxError> Errors; }`.
  - `internal static class ConditionTypeNames { static TypeName FromDsl(string); static string ToDsl(TypeName) }`.
  - `internal static class ModelJson { static string Serialize(AuthorizationModel); static AuthorizationModel Deserialize(string json, string paramName); static AuthorizationModel Clone(AuthorizationModel) }` (namespace `OpenFga.Language.Transformation`).
  - `internal static class DictionaryExtensions { static TValue? GetOrNull<TValue>(this IDictionary<string, TValue>? d, string key) where TValue : class }` (namespace `OpenFga.Language.Internal`).
  - Test helpers (namespace `OpenFga.Language.Tests.Fixtures`): `FixturePaths.Read(params string[])`, `TestData.TransformerCases()`, `TestData.DslSyntaxCases()`, `TestData.DslSemanticCases()`, `TestData.JsonValidationCases()`, `TestData.JsonSyntaxTransformerCases()`, `TestData.ModFileCases()`, `TestData.ObjectIdCases()`, `TestData.ModuleCases()`, `TestData.Indexes<T>(IReadOnlyList<T>, Func<T,string>)`, `JsonAssert.Equivalent(string, string)`, `ErrorAssert.AllMatch(IReadOnlyList<ExpectedError>, IReadOnlyList<LanguageError>)`, `ErrorAssert.ExpectedAggregate(IReadOnlyList<ExpectedError>, Func<ExpectedError,string> kind, bool includeLocation)`.

- [ ] **Step 1: Write the fixture helpers**

`test/OpenFga.Language.Tests/Fixtures/FixturePaths.cs`:
```csharp
namespace OpenFga.Language.Tests.Fixtures;

internal static class FixturePaths
{
    public static string DataDirectory { get; } = Locate();

    /// <summary>Reads a fixture, normalising CRLF (git autocrlf on Windows) to LF as in the repository.</summary>
    public static string Read(params string[] parts)
        => File.ReadAllText(Path.Combine([DataDirectory, .. parts])).Replace("\r\n", "\n", StringComparison.Ordinal);

    private static string Locate()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "tests", "data");
            if (Directory.Exists(Path.Combine(candidate, "transformer")))
            {
                return candidate;
            }
        }

        throw new InvalidOperationException("Could not locate the shared tests/data directory above " + AppContext.BaseDirectory);
    }
}
```

`test/OpenFga.Language.Tests/Fixtures/FixtureModels.cs`:
```csharp
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
```

`test/OpenFga.Language.Tests/Fixtures/TestData.cs`:
```csharp
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
```

`test/OpenFga.Language.Tests/Fixtures/JsonAssert.cs`:
```csharp
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
```

`test/OpenFga.Language.Tests/Fixtures/ErrorAssert.cs`:
```csharp
namespace OpenFga.Language.Tests.Fixtures;

internal static class ErrorAssert
{
    /// <summary>Asserts each actual error matches the fixture fields that are present (like Jest's toMatchObject).</summary>
    public static void AllMatch(IReadOnlyList<ExpectedError> expected, IReadOnlyList<LanguageError> actual)
    {
        Assert.Equal(expected.Count, actual.Count);
        for (var i = 0; i < expected.Count; i++)
        {
            Matches(expected[i], actual[i], i);
        }
    }

    /// <summary>
    /// Builds the aggregate exception message the fixture implies. <paramref name="kind"/> gives the prefix per error;
    /// the location is included when <paramref name="includeLocation"/> is true and the error has a line.
    /// </summary>
    public static string ExpectedAggregate(IReadOnlyList<ExpectedError> expected, Func<ExpectedError, string> kind, bool includeLocation)
    {
        var items = expected.Select(e => includeLocation && e.Line is not null
            ? $"{kind(e)} error at line={e.Line.Start}, column={e.Column?.Start}: {e.Msg}"
            : $"{kind(e)} error: {e.Msg}");
        return $"{expected.Count} error{(expected.Count == 1 ? "" : "s")} occurred:\n\t* {string.Join("\n\t* ", items)}\n\n";
    }

    private static void Matches(ExpectedError expected, LanguageError actual, int index)
    {
        var at = $"error[{index}] ({actual})";
        Assert.True(expected.Msg == actual.Message, $"{at}: message expected '{expected.Msg}'");
        if (expected.File is not null)
        {
            Assert.True(expected.File == actual.File, $"{at}: file expected '{expected.File}' but was '{actual.File}'");
        }

        if (expected.Line is not null)
        {
            Assert.True(actual.Range.HasValue, $"{at}: expected a range");
            Assert.True(expected.Line.Start == actual.Range.Value.StartLine && expected.Line.End == actual.Range.Value.EndLine, $"{at}: line expected {expected.Line.Start}-{expected.Line.End}");
        }

        if (expected.Column is not null)
        {
            Assert.True(actual.Range.HasValue, $"{at}: expected a range");
            Assert.True(expected.Column.Start == actual.Range.Value.StartColumn && expected.Column.End == actual.Range.Value.EndColumn, $"{at}: column expected {expected.Column.Start}-{expected.Column.End}");
        }

        if (expected.Type == "syntax")
        {
            Assert.IsType<SyntaxError>(actual);
        }

        if (expected.Metadata is { } metadata)
        {
            if (metadata.Symbol is not null)
            {
                Assert.True(metadata.Symbol == Symbol(actual), $"{at}: symbol expected '{metadata.Symbol}' but was '{Symbol(actual)}'");
            }

            if (metadata.ErrorType is not null)
            {
                var validation = Assert.IsType<ModelValidationError>(actual);
                Assert.True(metadata.ErrorType == ValidationErrorTypeCodes.ToCode(validation.ErrorType), $"{at}: errorType expected '{metadata.ErrorType}'");
            }

            if (metadata.Module is not null)
            {
                Assert.True(metadata.Module == Assert.IsType<ModelValidationError>(actual).Module, $"{at}: module expected '{metadata.Module}'");
            }
        }
    }

    private static string? Symbol(LanguageError error) => error switch
    {
        SyntaxError s => s.Symbol,
        ModelValidationError v => v.Symbol,
        ModuleTransformationError m => m.Symbol,
        _ => null,
    };
}
```

- [ ] **Step 2: Write the failing tests**

`test/OpenFga.Language.Tests/DslToModelTests.cs`:
```csharp
using OpenFga.Language.Tests.Fixtures;

namespace OpenFga.Language.Tests;

public class DslToModelTests
{
    private static readonly IReadOnlyList<TransformerCase> Transformer = TestData.TransformerCases();
    private static readonly IReadOnlyList<DslCase> Syntax = TestData.DslSyntaxCases();

    public static TheoryData<int, string> TransformerData => TestData.Indexes(Transformer, c => c.Name);

    public static TheoryData<int, string> SyntaxData => TestData.Indexes(Syntax, c => c.Name);

    [Theory]
    [MemberData(nameof(TransformerData))]
    public void TransformsDslToJson(int index, string name)
    {
        _ = name;
        var testCase = Transformer[index];

        JsonAssert.Equivalent(testCase.Json, ModelTransformer.TransformDslToJson(testCase.Dsl));
    }

    [Theory]
    [MemberData(nameof(TransformerData))]
    public void TransformsDslToModel(int index, string name)
    {
        _ = name;
        var testCase = Transformer[index];

        var model = ModelTransformer.TransformDslToModel(testCase.Dsl);

        Assert.Null(model.Id);
        Assert.NotNull(model.TypeDefinitions);
        JsonAssert.Equivalent(testCase.Json, model.ToJson());
    }

    [Theory]
    [MemberData(nameof(TransformerData))]
    public void CrlfDslTransformsLikeLf(int index, string name)
    {
        _ = name;
        var testCase = Transformer[index];

        Assert.Equal(
            ModelTransformer.TransformDslToJson(testCase.Dsl),
            ModelTransformer.TransformDslToJson(testCase.Dsl.Replace("\n", "\r\n", StringComparison.Ordinal)));
    }

    [Theory]
    [MemberData(nameof(SyntaxData))]
    public void ReportsSyntaxErrors(int index, string name)
    {
        _ = name;
        var testCase = Syntax[index];

        if (testCase.ExpectedErrors.Count == 0)
        {
            ModelTransformer.TransformDslToModel(testCase.Dsl);
            return;
        }

        var exception = Assert.Throws<DslSyntaxException>(() => ModelTransformer.TransformDslToModel(testCase.Dsl));
        Assert.Equal(ErrorAssert.ExpectedAggregate(testCase.ExpectedErrors, _ => "syntax", includeLocation: true), exception.Message);
        ErrorAssert.AllMatch(testCase.ExpectedErrors, exception.Errors);
    }

    [Theory]
    [InlineData("")]
    [InlineData("type")]
    [InlineData("type user")]
    [InlineData("model")]
    [InlineData("model\n  schema")]
    [InlineData("model\n  schema 1.1\ntype")]
    [InlineData("model\n  schema 1.1\ntype user\n  relations")]
    [InlineData("model\n  schema 1.1\ntype user\n  relations\n    define")]
    [InlineData("model\n  schema 1.1\ntype user\n  relations\n    define viewer: [user] or")]
    [InlineData("model\n  schema 1.1\ntype user\n  relations\n    define viewer: ([user] or (a and")]
    [InlineData("model\n  schema 1.1\ntype user\n  relations\n    define viewer: [user with")]
    [InlineData("model\n  schema 1.1\ntype user\ncondition c(x: int) {")]
    [InlineData("model\n  schema 1.1\ntype user\ncondition (x: int) {\n  x < 1\n}")]
    [InlineData("module\ntype user")]
    [InlineData("module m\nextend type")]
    [InlineData("model\n  schema 1.1\ntype user\n  relations\n    define a: b from")]
    public void MalformedDslThrowsOnlyDslSyntaxException(string dsl)
    {
        var exception = Record.Exception(() => ModelTransformer.TransformDslToModel(dsl));

        Assert.IsType<DslSyntaxException>(exception);
    }

    [Fact]
    public void ExtendOutsideModuleIsASyntaxError()
    {
        var exception = Assert.Throws<DslSyntaxException>(() => ModelTransformer.TransformDslToModel("model\n  schema 1.2\nextend type user"));

        Assert.Contains(exception.Errors, e => e.Message == "extend can only be used in a modular model");
    }

    [Fact]
    public void NullDslThrowsArgumentNullException()
    {
        Assert.Equal("dsl", Assert.Throws<ArgumentNullException>(() => ModelTransformer.TransformDslToModel(null!)).ParamName);
        Assert.Equal("dsl", Assert.Throws<ArgumentNullException>(() => ModelTransformer.TransformDslToJson(null!)).ParamName);
    }
}
```

- [ ] **Step 3: Run the tests to verify they fail**

Run: `dotnet test --filter FullyQualifiedName~DslToModelTests`
Expected: build FAILS with CS0103 (`ModelTransformer` does not exist).

- [ ] **Step 4: Implement the internal helpers**

`src/OpenFga.Language/Internal/DictionaryExtensions.cs`:
```csharp
using System.Collections.Generic;

namespace OpenFga.Language.Internal;

internal static class DictionaryExtensions
{
    /// <summary>Returns the value for <paramref name="key"/>, or null when the dictionary or key is missing.</summary>
    public static TValue? GetOrNull<TValue>(this IDictionary<string, TValue>? dictionary, string key)
        where TValue : class
        => dictionary != null && dictionary.TryGetValue(key, out var value) ? value : null;
}
```

`src/OpenFga.Language/Parsing/ConditionTypeNames.cs`:
```csharp
using System;
using OpenFga.Sdk.Model;

namespace OpenFga.Language.Parsing;

/// <summary>
/// Maps DSL condition parameter types (<c>int</c>, <c>ipaddress</c>, ...) to the SDK enum, whose members are
/// named <c>TYPENAMEINT</c> etc. and serialize as <c>TYPE_NAME_INT</c>.
/// </summary>
internal static class ConditionTypeNames
{
    private const string Prefix = "TYPENAME";

    public static TypeName FromDsl(string dslTypeName)
        => Enum.TryParse<TypeName>(Prefix + dslTypeName.ToUpperInvariant(), ignoreCase: false, out var typeName)
            ? typeName
            : TypeName.TYPENAMEUNSPECIFIED;

    public static string ToDsl(TypeName typeName)
    {
        var name = typeName.ToString();
        return (name.StartsWith(Prefix, StringComparison.Ordinal) ? name.Substring(Prefix.Length) : name).ToLowerInvariant();
    }
}
```

`src/OpenFga.Language/Transformation/ModelJson.cs`:
```csharp
using System;
using System.Text.Json;
using OpenFga.Sdk.Model;

namespace OpenFga.Language.Transformation;

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
```

- [ ] **Step 5: Implement the parser pipeline**

`src/OpenFga.Language/Parsing/DslErrorListener.cs`:
```csharp
using System.Collections.Generic;
using System.IO;
using Antlr4.Runtime;

namespace OpenFga.Language.Parsing;

/// <summary>Collects lexer and parser errors as <see cref="OpenFga.Language.SyntaxError"/> (JS OpenFgaDslErrorListener).</summary>
internal sealed class DslErrorListener : IAntlrErrorListener<IToken>, IAntlrErrorListener<int>
{
    public List<SyntaxError> Errors { get; } = [];

    public void SyntaxError(TextWriter output, IRecognizer recognizer, IToken offendingSymbol, int line, int charPositionInLine, string msg, RecognitionException e)
        => Add(offendingSymbol?.Text, line, charPositionInLine, msg);

    public void SyntaxError(TextWriter output, IRecognizer recognizer, int offendingSymbol, int line, int charPositionInLine, string msg, RecognitionException e)
        => Add(null, line, charPositionInLine, msg);

    // ANTLR lines are one-based; columns are already zero-based.
    private void Add(string? symbol, int line, int column, string message)
        => Errors.Add(new SyntaxError(message, new SourceRange(line - 1, line - 1, column, column + (symbol?.Length ?? 0)), symbol));
}
```

`src/OpenFga.Language/Parsing/DslParser.cs`:
```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using Antlr4.Runtime;
using Antlr4.Runtime.Tree;
using OpenFga.Language.Antlr;
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

internal static class DslParser
{
    public static DslParseResult Parse(string dsl)
    {
        var cleaned = string.Join("\n", dsl.Split('\n').Select(CleanLine));

        var errorListener = new DslErrorListener();
        var lexer = new OpenFGALexer(new AntlrInputStream(cleaned));
        lexer.RemoveErrorListeners();
        lexer.AddErrorListener(errorListener);

        var parser = new OpenFGAParser(new CommonTokenStream(lexer));
        parser.RemoveErrorListeners();
        parser.AddErrorListener(errorListener);

        var listener = new DslListener(parser);
        ParseTreeWalker.Default.Walk(listener, parser.main());

        return new DslParseResult(listener.Model, listener.TypeDefExtensions, errorListener.Errors);
    }

    // Whole-line comments become empty lines (keeping line numbers); trailing " #" comments and whitespace are removed.
    private static string CleanLine(string line)
    {
        if (line.TrimStart().StartsWith("#", StringComparison.Ordinal))
        {
            return string.Empty;
        }

        var commentStart = line.IndexOf(" #", StringComparison.Ordinal);
        return (commentStart >= 0 ? line.Substring(0, commentStart) : line).TrimEnd();
    }
}
```

`src/OpenFga.Language/Parsing/DslListener.cs`. Every callback tolerates missing children: ANTLR error recovery produces partial trees, and a crash here would mask the `DslSyntaxException` (Review Focus 1).
```csharp
using System.Collections.Generic;
using Antlr4.Runtime;
using OpenFga.Language.Antlr;
using OpenFga.Sdk.Model;

namespace OpenFga.Language.Parsing;

/// <summary>Walks the parse tree and builds an AuthorizationModel (JS OpenFgaDslListener).</summary>
internal sealed class DslListener : OpenFGAParserBaseListener
{
    private readonly Parser _parser;
    private readonly Stack<StackRelation> _rewriteStack = new();
    private TypeDefinition? _currentTypeDef;
    private PartialRelation? _currentRelation;
    private Condition? _currentCondition;
    private bool _isModularModel;
    private string? _moduleName;

    public DslListener(Parser parser)
    {
        _parser = parser;
    }

    private enum RelationOperator
    {
        None,
        Or,
        And,
        ButNot,
    }

    // TypeDefinitions is annotated non-nullable by the SDK, so it starts (and stays) a list, even an empty one.
    public AuthorizationModel Model { get; } = new() { TypeDefinitions = [] };

    public Dictionary<string, TypeDefinition> TypeDefExtensions { get; } = [];

    public override void ExitModuleHeader(OpenFGAParser.ModuleHeaderContext context)
    {
        if (context.moduleName == null)
        {
            return;
        }

        _isModularModel = true;
        _moduleName = context.moduleName.GetText();
    }

    public override void ExitModelHeader(OpenFGAParser.ModelHeaderContext context)
    {
        if (context.schemaVersion != null)
        {
            Model.SchemaVersion = context.schemaVersion.Text;
        }
    }

    public override void EnterTypeDef(OpenFGAParser.TypeDefContext context)
    {
        if (context.typeName == null)
        {
            return;
        }

        if (context.EXTEND() != null && !_isModularModel)
        {
            _parser.NotifyErrorListeners(context.typeName.Start, "extend can only be used in a modular model", null);
        }

        _currentTypeDef = new TypeDefinition
        {
            Type = context.typeName.GetText(),
            Relations = [],
            Metadata = new Metadata { Relations = [] },
        };

        if (_isModularModel)
        {
            _currentTypeDef.Metadata.Module = _moduleName;
        }
    }

    public override void ExitTypeDef(OpenFGAParser.TypeDefContext context)
    {
        var typeDef = _currentTypeDef;
        if (typeDef?.Type == null)
        {
            return;
        }

        if (typeDef.Metadata?.Relations is { Count: 0 })
        {
            if (_isModularModel)
            {
                typeDef.Metadata.Relations = null;
            }
            else
            {
                typeDef.Metadata = null;
            }
        }

        Model.TypeDefinitions.Add(typeDef);

        if (context.EXTEND() != null && _isModularModel)
        {
            if (TypeDefExtensions.ContainsKey(typeDef.Type))
            {
                _parser.NotifyErrorListeners(context.typeName.Start, $"'{typeDef.Type}' is already extended in file.", null);
            }
            else
            {
                TypeDefExtensions[typeDef.Type] = typeDef;
            }
        }

        _currentTypeDef = null;
    }

    public override void EnterRelationDeclaration(OpenFGAParser.RelationDeclarationContext context)
    {
        _currentRelation = new PartialRelation();
        _rewriteStack.Clear();
    }

    public override void ExitRelationDeclaration(OpenFGAParser.RelationDeclarationContext context)
    {
        var relationNameContext = context.relationName();
        var relation = _currentRelation;
        _currentRelation = null;
        if (relationNameContext == null || relation == null || _currentTypeDef?.Relations == null)
        {
            return;
        }

        var relationName = relationNameContext.GetText();
        var relationDef = ParseExpression(relation.Rewrites, relation.Operator);
        if (relationDef == null)
        {
            return;
        }

        if (_currentTypeDef.Relations.ContainsKey(relationName))
        {
            _parser.NotifyErrorListeners(relationNameContext.Start, $"'{relationName}' is already defined in '{_currentTypeDef.Type}'", null);
        }

        _currentTypeDef.Relations[relationName] = relationDef;
        var metadata = new RelationMetadata { DirectlyRelatedUserTypes = relation.DirectlyRelatedUserTypes };
        _currentTypeDef.Metadata ??= new Metadata { Relations = [] };
        _currentTypeDef.Metadata.Relations ??= [];
        _currentTypeDef.Metadata.Relations[relationName] = metadata;

        // Only relations declared inside an `extend type` carry their own module.
        if (_isModularModel && (context.Parent as OpenFGAParser.TypeDefContext)?.EXTEND() != null)
        {
            metadata.Module = _moduleName;
        }
    }

    public override void EnterRelationDefDirectAssignment(OpenFGAParser.RelationDefDirectAssignmentContext context)
    {
        if (_currentRelation != null)
        {
            _currentRelation.DirectlyRelatedUserTypes = [];
        }
    }

    public override void ExitRelationDefDirectAssignment(OpenFGAParser.RelationDefDirectAssignmentContext context)
        => _currentRelation?.Rewrites.Add(new Userset { This = new object() });

    public override void ExitRelationDefTypeRestriction(OpenFGAParser.RelationDefTypeRestrictionContext context)
    {
        var baseRestriction = context.relationDefTypeRestrictionBase();
        if (baseRestriction == null || _currentRelation == null)
        {
            return;
        }

        var reference = new RelationReference { Type = baseRestriction.relationDefTypeRestrictionType?.GetText() ?? string.Empty };

        if (context.conditionName() != null)
        {
            reference.Condition = context.conditionName().GetText();
        }
        else if (context.DOLLAR_EXPRESSION() != null)
        {
            // `$expression` is the reserved inline-expression condition name; it is carried like a named condition.
            reference.Condition = context.DOLLAR_EXPRESSION().GetText();
        }

        if (baseRestriction.relationDefTypeRestrictionRelation != null)
        {
            reference.Relation = baseRestriction.relationDefTypeRestrictionRelation.GetText();
        }

        if (baseRestriction.relationDefTypeRestrictionWildcard != null)
        {
            reference.Wildcard = new object();
        }

        _currentRelation.DirectlyRelatedUserTypes.Add(reference);
    }

    public override void ExitRelationDefRewrite(OpenFGAParser.RelationDefRewriteContext context)
    {
        var computedUserset = new ObjectRelation { Relation = context.rewriteComputedusersetName?.GetText() };
        var rewrite = context.rewriteTuplesetName == null
            ? new Userset { ComputedUserset = computedUserset }
            : new Userset
            {
                TupleToUserset = new TupleToUserset
                {
                    ComputedUserset = computedUserset,
                    Tupleset = new ObjectRelation { Relation = context.rewriteTuplesetName.GetText() },
                },
            };

        _currentRelation?.Rewrites.Add(rewrite);
    }

    public override void ExitRelationRecurse(OpenFGAParser.RelationRecurseContext context)
    {
        if (_currentRelation == null)
        {
            return;
        }

        var relationDef = ParseExpression(_currentRelation.Rewrites, _currentRelation.Operator);
        if (relationDef != null)
        {
            _currentRelation.Rewrites = [relationDef];
        }
    }

    public override void EnterRelationRecurseNoDirect(OpenFGAParser.RelationRecurseNoDirectContext context)
    {
        if (_currentRelation == null)
        {
            return;
        }

        _rewriteStack.Push(new StackRelation(_currentRelation.Rewrites, _currentRelation.Operator));
        _currentRelation.Rewrites = [];
    }

    public override void ExitRelationRecurseNoDirect(OpenFGAParser.RelationRecurseNoDirectContext context)
    {
        if (_currentRelation == null)
        {
            return;
        }

        var relationDef = ParseExpression(_currentRelation.Rewrites, _currentRelation.Operator);
        var popped = _rewriteStack.Count > 0 ? _rewriteStack.Pop() : null;
        if (relationDef != null && popped != null)
        {
            _currentRelation.Operator = popped.Operator;
            _currentRelation.Rewrites = [.. popped.Rewrites, relationDef];
        }
    }

    public override void EnterRelationDefPartials(OpenFGAParser.RelationDefPartialsContext context)
    {
        if (_currentRelation == null)
        {
            return;
        }

        if (context.OR().Length > 0)
        {
            _currentRelation.Operator = RelationOperator.Or;
        }
        else if (context.AND().Length > 0)
        {
            _currentRelation.Operator = RelationOperator.And;
        }
        else if (context.BUT_NOT() != null)
        {
            _currentRelation.Operator = RelationOperator.ButNot;
        }
    }

    public override void EnterCondition(OpenFGAParser.ConditionContext context)
    {
        var conditionNameContext = context.conditionName();
        if (conditionNameContext == null)
        {
            return;
        }

        Model.Conditions ??= [];
        var conditionName = conditionNameContext.GetText();
        if (Model.Conditions.ContainsKey(conditionName))
        {
            _parser.NotifyErrorListeners(conditionNameContext.Start, $"condition '{conditionName}' is already defined in the model", null);
        }

        _currentCondition = new Condition { Name = conditionName, Expression = string.Empty, Parameters = [] };
        if (_isModularModel)
        {
            _currentCondition.Metadata = new ConditionMetadata { Module = _moduleName };
        }
    }

    public override void ExitConditionParameter(OpenFGAParser.ConditionParameterContext context)
    {
        var parameterNameContext = context.parameterName();
        var parameterType = context.parameterType();
        if (parameterNameContext == null || parameterType == null || _currentCondition == null)
        {
            return;
        }

        _currentCondition.Parameters ??= [];
        var parameterName = parameterNameContext.GetText();
        if (_currentCondition.Parameters.ContainsKey(parameterName))
        {
            _parser.NotifyErrorListeners(parameterNameContext.Start, $"parameter '{parameterName}' is already defined in the condition '{_currentCondition.Name}'", null);
        }

        var container = parameterType.CONDITION_PARAM_CONTAINER();
        var typeRef = new ConditionParamTypeRef();
        if (container != null)
        {
            typeRef.TypeName = ConditionTypeNames.FromDsl(container.GetText());
            var generic = parameterType.CONDITION_PARAM_TYPE();
            if (generic != null)
            {
                typeRef.GenericTypes = [new ConditionParamTypeRef { TypeName = ConditionTypeNames.FromDsl(generic.GetText()) }];
            }
        }
        else
        {
            typeRef.TypeName = ConditionTypeNames.FromDsl(parameterType.GetText());
        }

        _currentCondition.Parameters[parameterName] = typeRef;
    }

    public override void ExitConditionExpression(OpenFGAParser.ConditionExpressionContext context)
    {
        if (_currentCondition != null)
        {
            _currentCondition.Expression = context.GetText().Trim();
        }
    }

    public override void ExitCondition(OpenFGAParser.ConditionContext context)
    {
        if (_currentCondition == null)
        {
            return;
        }

        Model.Conditions ??= [];
        Model.Conditions[_currentCondition.Name] = _currentCondition;
        _currentCondition = null;
    }

    private static Userset? ParseExpression(List<Userset> rewrites, RelationOperator relationOperator)
    {
        if (rewrites.Count == 0)
        {
            return null;
        }

        if (rewrites.Count == 1)
        {
            return rewrites[0];
        }

        switch (relationOperator)
        {
            case RelationOperator.Or:
                return new Userset { Union = new Usersets { Child = rewrites } };
            case RelationOperator.And:
                return new Userset { Intersection = new Usersets { Child = rewrites } };
            case RelationOperator.ButNot:
                var difference = new Difference { Base = rewrites[0], Subtract = rewrites[1] };
                rewrites.RemoveRange(0, 2);
                return new Userset { Difference = difference };
            default:
                return null;
        }
    }

    private sealed class PartialRelation
    {
        public List<Userset> Rewrites { get; set; } = [];

        public RelationOperator Operator { get; set; }

        public List<RelationReference> DirectlyRelatedUserTypes { get; set; } = [];
    }

    private sealed class StackRelation(List<Userset> rewrites, RelationOperator relationOperator)
    {
        public List<Userset> Rewrites { get; } = rewrites;

        public RelationOperator Operator { get; } = relationOperator;
    }
}
```

- [ ] **Step 6: Implement the public entry points**

`src/OpenFga.Language/Transformation/ModelTransformer.cs`:
```csharp
using System;
using OpenFga.Language.Parsing;
using OpenFga.Language.Transformation;
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
```

- [ ] **Step 7: Record the public API**

Run:
```bash
printf '#nullable enable
' > src/OpenFga.Language/PublicAPI.Unshipped.txt
dotnet format analyzers src/OpenFga.Language/OpenFga.Language.csproj --diagnostics RS0016 --severity info
```
(The RS0016 fixer only fills an API file that has no entries yet, so the file is regenerated each time. Nothing has shipped, so `PublicAPI.Unshipped.txt` holds the whole public surface.)
Expected: `git diff src/OpenFga.Language/PublicAPI.Unshipped.txt` adds entries for `OpenFga.Language.ModelTransformer` and its two static methods, nothing else.

- [ ] **Step 8: Run the tests to verify they pass**

Run: `dotnet test --filter FullyQualifiedName~DslToModelTests`
Expected: all pass. If a `ReportsSyntaxErrors` case fails on message text only, compare with `cd pkg/js && npx jest tests/validator.test.ts -t "<case name>"`. The C# and JS ANTLR runtimes are expected to produce identical messages, and any difference is a porting bug in `DslErrorListener` or `DslParser.CleanLine`.

- [ ] **Step 9: Run the whole suite**

Run: `dotnet test`
Expected: all tests pass, 0 warnings.

- [ ] **Step 10: Commit**

```bash
git add pkg/dotnet
git commit -m "feat(dotnet): transform DSL to AuthorizationModel and JSON

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01U8M7PfSCNGaz27dr4xJPEu"
```

---

### Task 4: AuthorizationModel / JSON → DSL

Port of `pkg/js/transformer/jsontodsl.ts` (without `getModulesFromJSON`, which is out of scope).

**Files:**
- Create: `pkg/dotnet/src/OpenFga.Language/Transformation/TransformOptions.cs`, `DslWriter.cs`
- Modify: `pkg/dotnet/src/OpenFga.Language/Transformation/ModelTransformer.cs`, `PublicAPI.Unshipped.txt`
- Test: `pkg/dotnet/test/OpenFga.Language.Tests/ModelToDslTests.cs`

**Interfaces:**
- Consumes: `ModelJson` (Task 3), `ConditionTypeNames.ToDsl` (Task 3), `DslTransformationException` (Task 2), `DictionaryExtensions.GetOrNull` (Task 3), fixture helpers (Task 3).
- Produces:
  - `public sealed class TransformOptions { bool IncludeSourceInformation { get; set; } }`.
  - `ModelTransformer.TransformModelToDsl(AuthorizationModel)`, `TransformModelToDsl(AuthorizationModel, TransformOptions)`, `TransformJsonToDsl(string)`, `TransformJsonToDsl(string, TransformOptions)`.
  - `internal static class DslWriter { static string Write(AuthorizationModel model, bool includeSourceInformation) }`. It mutates the model it is given, so callers pass a copy.

- [ ] **Step 1: Write the failing tests**

`test/OpenFga.Language.Tests/ModelToDslTests.cs`:
```csharp
using OpenFga.Language.Tests.Fixtures;
using OpenFga.Sdk.Model;

namespace OpenFga.Language.Tests;

public class ModelToDslTests
{
    private static readonly IReadOnlyList<TransformerCase> Transformer = TestData.TransformerCases();
    private static readonly IReadOnlyList<JsonCase> Invalid = TestData.JsonSyntaxTransformerCases();
    private static readonly IReadOnlyList<ModuleCase> Modules = TestData.ModuleCases().Where(c => c.Dsl is not null && c.Json is not null).ToList();

    public static TheoryData<int, string> TransformerData => TestData.Indexes(Transformer, c => c.Name);

    public static TheoryData<int, string> InvalidData => TestData.Indexes(Invalid, c => c.Name);

    public static TheoryData<int, string> ModuleData => TestData.Indexes(Modules, c => c.Name);

    [Theory]
    [MemberData(nameof(TransformerData))]
    public void TransformsJsonToDsl(int index, string name)
    {
        _ = name;
        var testCase = Transformer[index];

        Assert.Equal(testCase.Dsl, ModelTransformer.TransformJsonToDsl(testCase.Json));
    }

    [Theory]
    [MemberData(nameof(TransformerData))]
    public void TransformsModelToDsl(int index, string name)
    {
        _ = name;
        var testCase = Transformer[index];

        Assert.Equal(testCase.Dsl, ModelTransformer.TransformModelToDsl(AuthorizationModel.FromJson(testCase.Json)));
    }

    [Theory]
    [MemberData(nameof(InvalidData))]
    public void ReportsModelsTheDslCannotExpress(int index, string name)
    {
        _ = name;
        var testCase = Invalid[index];

        if (string.IsNullOrEmpty(testCase.ErrorMessage))
        {
            ModelTransformer.TransformJsonToDsl(testCase.Json);
            return;
        }

        var exception = Assert.Throws<DslTransformationException>(() => ModelTransformer.TransformJsonToDsl(testCase.Json));
        Assert.Equal(testCase.ErrorMessage, exception.Message);
    }

    [Theory]
    [MemberData(nameof(ModuleData))]
    public void TransformsModularJsonToDsl(int index, string name)
    {
        _ = name;
        var testCase = Modules[index];

        Assert.Equal(testCase.Dsl, ModelTransformer.TransformJsonToDsl(testCase.Json!));
    }

    [Theory]
    [MemberData(nameof(ModuleData))]
    public void TransformsModularJsonToDslWithSourceInformation(int index, string name)
    {
        _ = name;
        var testCase = Modules[index];

        Assert.Equal(
            testCase.DslWithSourceInfo,
            ModelTransformer.TransformJsonToDsl(testCase.Json!, new TransformOptions { IncludeSourceInformation = true }));
    }

    [Fact]
    public void TransformModelToDslDoesNotMutateInput()
    {
        const string json = """{"schema_version":"1.1","type_definitions":[{"type":"user"},{"type":"doc","relations":{"viewer":{"union":{"child":[{"computedUserset":{"relation":"owner"}},{"this":{}}]}},"owner":{"this":{}}},"metadata":{"relations":{"viewer":{"directly_related_user_types":[{"type":"user"}]},"owner":{"directly_related_user_types":[{"type":"user"}]}}}}]}""";
        var model = AuthorizationModel.FromJson(json);
        var before = model.ToJson();

        var dsl = ModelTransformer.TransformModelToDsl(model);

        Assert.Contains("define viewer: [user] or owner", dsl, StringComparison.Ordinal);
        Assert.NotNull(model.TypeDefinitions[1].Relations!["viewer"].Union!.Child[0].ComputedUserset);
        Assert.Equal(before, model.ToJson());
    }

    [Fact]
    public void InvalidJsonThrowsArgumentException()
    {
        var exception = Assert.Throws<ArgumentException>(() => ModelTransformer.TransformJsonToDsl("{not json"));

        Assert.Equal("json", exception.ParamName);
        Assert.IsAssignableFrom<System.Text.Json.JsonException>(exception.InnerException);
        Assert.Equal("json", Assert.Throws<ArgumentException>(() => ModelTransformer.TransformJsonToDsl("null")).ParamName);
    }

    [Fact]
    public void NullArgumentsThrowArgumentNullException()
    {
        var model = new AuthorizationModel { SchemaVersion = "1.1", TypeDefinitions = [] };

        Assert.Equal("model", Assert.Throws<ArgumentNullException>(() => ModelTransformer.TransformModelToDsl(null!)).ParamName);
        Assert.Equal("options", Assert.Throws<ArgumentNullException>(() => ModelTransformer.TransformModelToDsl(model, null!)).ParamName);
        Assert.Equal("json", Assert.Throws<ArgumentNullException>(() => ModelTransformer.TransformJsonToDsl(null!)).ParamName);
        Assert.Equal("options", Assert.Throws<ArgumentNullException>(() => ModelTransformer.TransformJsonToDsl("{}", null!)).ParamName);
    }

    [Fact]
    public void DslRoundTripsThroughModel()
    {
        foreach (var testCase in Transformer)
        {
            Assert.Equal(testCase.Dsl, ModelTransformer.TransformModelToDsl(ModelTransformer.TransformDslToModel(testCase.Dsl)));
        }
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test --filter FullyQualifiedName~ModelToDslTests`
Expected: build FAILS with CS0117 (`ModelTransformer` has no `TransformJsonToDsl`).

- [ ] **Step 3: Implement `TransformOptions` and `DslWriter`**

`src/OpenFga.Language/Transformation/TransformOptions.cs`:
```csharp
namespace OpenFga.Language;

/// <summary>Options for writing a model as DSL.</summary>
public sealed class TransformOptions
{
    /// <summary>
    /// Gets or sets a value indicating whether types, relations and conditions of modular models are followed by a
    /// <c># module: …, file: …</c> comment.
    /// </summary>
    public bool IncludeSourceInformation { get; set; }
}
```

`src/OpenFga.Language/Transformation/DslWriter.cs`:
```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using OpenFga.Language.Internal;
using OpenFga.Language.Parsing;
using OpenFga.Sdk.Model;

namespace OpenFga.Language.Transformation;

/// <summary>
/// Writes an AuthorizationModel as DSL (JS jsontodsl.ts). Like JS, it reorders union/intersection children in place
/// to put direct assignments first, so it must be given a copy of the caller's model.
/// </summary>
internal static class DslWriter
{
    public static string Write(AuthorizationModel model, bool includeSourceInformation)
    {
        var schemaVersion = string.IsNullOrEmpty(model.SchemaVersion) ? "1.1" : model.SchemaVersion;
        var typeDefinitions = model.TypeDefinitions;
        var isModularModel = typeDefinitions?.Any(t => !string.IsNullOrEmpty(t.Metadata?.Module)) == true;

        var builder = new StringBuilder();
        builder.Append("model\n  schema ").Append(schemaVersion).Append('\n');

        if (typeDefinitions is { Count: > 0 })
        {
            IEnumerable<TypeDefinition> ordered = isModularModel
                ? typeDefinitions.OrderBy(t => t, Comparer<TypeDefinition>.Create((a, b) =>
                    SortByModule(a.Type, b.Type, a.Metadata?.Module, a.Metadata?.SourceInfo?.File, b.Metadata?.Module, b.Metadata?.SourceInfo?.File)))
                : typeDefinitions;
            builder.Append(string.Join("\n", ordered.Select(t => WriteType(t, isModularModel, includeSourceInformation)))).Append('\n');
        }

        builder.Append(WriteConditions(model.Conditions, isModularModel, includeSourceInformation));
        return builder.ToString();
    }

    private static string WriteType(TypeDefinition typeDef, bool isModularModel, bool includeSourceInformation)
    {
        var builder = new StringBuilder();
        builder.Append("\ntype ").Append(typeDef.Type)
            .Append(SourceComment(typeDef.Metadata?.Module, typeDef.Metadata?.SourceInfo?.File, string.Empty, includeSourceInformation));

        var relations = typeDef.Relations;
        if (relations is { Count: > 0 })
        {
            builder.Append("\n  relations");
            var relationsMetadata = typeDef.Metadata?.Relations;
            IEnumerable<KeyValuePair<string, Userset>> ordered = isModularModel
                ? relations.OrderBy(r => r, Comparer<KeyValuePair<string, Userset>>.Create((a, b) =>
                {
                    var aMeta = relationsMetadata.GetOrNull(a.Key);
                    var bMeta = relationsMetadata.GetOrNull(b.Key);
                    return SortByModule(a.Key, b.Key, aMeta?.Module, aMeta?.SourceInfo?.File, bMeta?.Module, bMeta?.SourceInfo?.File);
                }))
                : relations;

            foreach (var relation in ordered)
            {
                builder.Append('\n').Append(WriteRelation(typeDef.Type, relation.Key, relation.Value, relationsMetadata.GetOrNull(relation.Key), includeSourceInformation));
            }
        }

        return builder.ToString();
    }

    private static string WriteRelation(string typeName, string relationName, Userset? relationDefinition, RelationMetadata? relationMetadata, bool includeSourceInformation)
    {
        var definition = relationDefinition ?? new Userset();
        var typeRestrictions = relationMetadata?.DirectlyRelatedUserTypes ?? [];
        var counter = new DirectAssignmentCounter();

        var builder = new StringBuilder("    define ").Append(relationName).Append(": ");
        if (definition.Difference != null)
        {
            builder.Append(WriteDifference(typeName, relationName, definition, typeRestrictions, counter));
        }
        else if (definition.Union != null)
        {
            builder.Append(WriteUnion(typeName, relationName, definition, typeRestrictions, counter));
        }
        else if (definition.Intersection != null)
        {
            builder.Append(WriteIntersection(typeName, relationName, definition, typeRestrictions, counter));
        }
        else
        {
            builder.Append(WriteSubRelation(typeName, relationName, definition, typeRestrictions, counter));
        }

        builder.Append(SourceComment(relationMetadata?.Module, relationMetadata?.SourceInfo?.File, " extended by:", includeSourceInformation));

        // Either no direct assignment, or exactly one and in the first position.
        if (counter.Occurred == 0 || (counter.Occurred == 1 && IsFirstPosition(definition)))
        {
            return builder.ToString();
        }

        throw new DslTransformationException(UnsupportedNesting(typeName, relationName));
    }

    private static string WriteSubRelation(string typeName, string relationName, Userset definition, List<RelationReference> typeRestrictions, DirectAssignmentCounter counter)
    {
        if (definition.This != null)
        {
            counter.Occurred++;
            return "[" + string.Join(", ", typeRestrictions.Select(WriteTypeRestriction)) + "]";
        }

        if (definition.ComputedUserset != null)
        {
            return definition.ComputedUserset.Relation ?? string.Empty;
        }

        if (definition.TupleToUserset != null)
        {
            return $"{definition.TupleToUserset.ComputedUserset?.Relation} from {definition.TupleToUserset.Tupleset?.Relation}";
        }

        if (definition.Union != null)
        {
            return "(" + WriteUnion(typeName, relationName, definition, typeRestrictions, counter) + ")";
        }

        if (definition.Intersection != null)
        {
            return "(" + WriteIntersection(typeName, relationName, definition, typeRestrictions, counter) + ")";
        }

        if (definition.Difference != null)
        {
            return "(" + WriteDifference(typeName, relationName, definition, typeRestrictions, counter) + ")";
        }

        throw new DslTransformationException(UnsupportedNesting(typeName, relationName));
    }

    private static string WriteUnion(string typeName, string relationName, Userset definition, List<RelationReference> typeRestrictions, DirectAssignmentCounter counter)
        => string.Join(" or ", PrioritizeDirectAssignment(definition.Union!.Child).Select(c => WriteSubRelation(typeName, relationName, c, typeRestrictions, counter)));

    private static string WriteIntersection(string typeName, string relationName, Userset definition, List<RelationReference> typeRestrictions, DirectAssignmentCounter counter)
        => string.Join(" and ", PrioritizeDirectAssignment(definition.Intersection!.Child).Select(c => WriteSubRelation(typeName, relationName, c, typeRestrictions, counter)));

    private static string WriteDifference(string typeName, string relationName, Userset definition, List<RelationReference> typeRestrictions, DirectAssignmentCounter counter)
    {
        var baseText = WriteSubRelation(typeName, relationName, definition.Difference!.Base ?? new Userset(), typeRestrictions, counter);
        var subtractText = WriteSubRelation(typeName, relationName, definition.Difference.Subtract ?? new Userset(), typeRestrictions, counter);
        return $"{baseText} but not {subtractText}";
    }

    private static string WriteTypeRestriction(RelationReference restriction)
    {
        var text = restriction.Type;
        if (restriction.Wildcard != null)
        {
            text += ":*";
        }

        if (!string.IsNullOrEmpty(restriction.Relation))
        {
            text += "#" + restriction.Relation;
        }

        if (!string.IsNullOrEmpty(restriction.Condition))
        {
            text += " with " + restriction.Condition;
        }

        return text;
    }

    /// <summary>Moves the first direct assignment to the front, in place (JS prioritizeDirectAssignment).</summary>
    private static List<Userset> PrioritizeDirectAssignment(List<Userset>? usersets)
    {
        if (usersets is not { Count: > 0 })
        {
            return [];
        }

        var thisPosition = usersets.FindIndex(u => u.This != null);
        if (thisPosition > 0)
        {
            var direct = usersets[thisPosition];
            usersets.RemoveAt(thisPosition);
            usersets.Insert(0, direct);
        }

        return usersets;
    }

    private static bool IsFirstPosition(Userset userset)
    {
        if (userset.This != null)
        {
            return true;
        }

        if (userset.Difference?.Base != null)
        {
            return userset.Difference.Base.This != null || IsFirstPosition(userset.Difference.Base);
        }

        if (userset.Intersection?.Child is { Count: > 0 } intersection)
        {
            return intersection[0].This != null || IsFirstPosition(intersection[0]);
        }

        if (userset.Union?.Child is { Count: > 0 } union)
        {
            return union[0].This != null || IsFirstPosition(union[0]);
        }

        return false;
    }

    private static string WriteConditions(Dictionary<string, Condition>? conditions, bool isModularModel, bool includeSourceInformation)
    {
        if (conditions is not { Count: > 0 })
        {
            return string.Empty;
        }

        IEnumerable<KeyValuePair<string, Condition>> ordered = conditions.OrderBy(c => c, Comparer<KeyValuePair<string, Condition>>.Create((a, b) =>
            isModularModel
                ? SortByModule(a.Key, b.Key, a.Value.Metadata?.Module, a.Value.Metadata?.SourceInfo?.File, b.Value.Metadata?.Module, b.Value.Metadata?.SourceInfo?.File)
                : string.CompareOrdinal(a.Key, b.Key)));

        var builder = new StringBuilder();
        foreach (var condition in ordered)
        {
            builder.Append('\n').Append(WriteCondition(condition.Key, condition.Value, includeSourceInformation));
        }

        return builder.ToString();
    }

    private static string WriteCondition(string conditionName, Condition condition, bool includeSourceInformation)
    {
        if (conditionName != condition.Name)
        {
            throw new DslTransformationException($"the '{conditionName}' condition has a different nested condition name ('{condition.Name}')");
        }

        var parameters = (condition.Parameters ?? [])
            .OrderBy(p => p.Key, StringComparer.Ordinal)
            .Select(p => $"{p.Key}: {WriteParameterType(p.Value)}");
        var source = SourceComment(condition.Metadata?.Module, condition.Metadata?.SourceInfo?.File, string.Empty, includeSourceInformation);

        return $"condition {conditionName}({string.Join(", ", parameters)}) {{\n  {condition.Expression}\n}}{source}\n";
    }

    private static string WriteParameterType(ConditionParamTypeRef parameterType)
    {
        var text = ConditionTypeNames.ToDsl(parameterType.TypeName);
        if (text is "list" or "map")
        {
            var generic = parameterType.GenericTypes is { Count: > 0 } generics ? ConditionTypeNames.ToDsl(generics[0].TypeName) : string.Empty;
            text = $"{text}<{generic}>";
        }

        return text;
    }

    private static string SourceComment(string? module, string? file, string leading, bool includeSourceInformation)
        => !string.IsNullOrEmpty(module) && includeSourceInformation ? $" #{leading} module: {module}, file: {file}" : string.Empty;

    /// <summary>JS sortByModule, with ordinal comparison in place of localeCompare (as in Go and Java).</summary>
    private static int SortByModule(string aName, string bName, string? aModule, string? aFile, string? bModule, string? bFile)
    {
        if (string.IsNullOrEmpty(aModule) && string.IsNullOrEmpty(bModule))
        {
            return string.CompareOrdinal(aName, bName);
        }

        // No module means it lives in the same file as its type, so it sorts first.
        if (aModule is null)
        {
            return -1;
        }

        if (bModule is null)
        {
            return 1;
        }

        if (aModule != bModule)
        {
            return string.CompareOrdinal(aModule, bModule);
        }

        if (aFile != bFile)
        {
            return string.CompareOrdinal(aFile, bFile);
        }

        return string.CompareOrdinal(aName, bName);
    }

    private static string UnsupportedNesting(string typeName, string relationName)
        => $"the '{relationName}' relation definition under the '{typeName}' type is not supported by the OpenFGA DSL syntax yet";

    private sealed class DirectAssignmentCounter
    {
        public int Occurred { get; set; }
    }
}
```

- [ ] **Step 4: Add the public entry points**

Append to `ModelTransformer` (in `Transformation/ModelTransformer.cs`):
```csharp
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
```

- [ ] **Step 5: Record the public API**

Run:
```bash
printf '#nullable enable
' > src/OpenFga.Language/PublicAPI.Unshipped.txt
dotnet format analyzers src/OpenFga.Language/OpenFga.Language.csproj --diagnostics RS0016 --severity info
```
(The RS0016 fixer only fills an API file that has no entries yet, so the file is regenerated each time. Nothing has shipped, so `PublicAPI.Unshipped.txt` holds the whole public surface.)
Expected: `git diff src/OpenFga.Language/PublicAPI.Unshipped.txt` adds entries for `TransformOptions` (type, constructor, `IncludeSourceInformation` get/set) and the four `ModelTransformer` overloads only.

- [ ] **Step 6: Run the tests to verify they pass**

Run: `dotnet test --filter FullyQualifiedName~ModelToDslTests`
Expected: all pass. `TransformModelToDslDoesNotMutateInput` would fail if `ModelJson.Clone` were skipped. Temporarily replace `ModelJson.Clone(model)` with `model` to watch it fail, then restore.

- [ ] **Step 7: Run the whole suite and commit**

Run: `dotnet test`
Expected: all pass.

```bash
git add pkg/dotnet
git commit -m "feat(dotnet): transform AuthorizationModel and JSON to DSL

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01U8M7PfSCNGaz27dr4xJPEu"
```

---

### Task 5: `TupleSyntax` validators

Port of `pkg/js/validator/validate-rules.ts` (Go `validation`, Java `Validator.validate*`).

**Files:**
- Create: `pkg/dotnet/src/OpenFga.Language/Validation/Rules.cs`, `TupleSyntax.cs`
- Modify: `pkg/dotnet/src/OpenFga.Language/PublicAPI.Unshipped.txt`
- Test: `pkg/dotnet/test/OpenFga.Language.Tests/TupleSyntaxTests.cs`

**Interfaces:**
- Consumes: fixture helpers (Task 3).
- Produces:
  - `internal static class Rules` (namespace `OpenFga.Language.Validation`) with consts `Type`, `Relation`, `Condition`, `Id`, `Object`, `DefaultTypePattern` (`^[^:#@\*\s]{1,254}$`) and `DefaultRelationPattern` (`^[^:#@\*\s]{1,50}$`).
  - `public static class TupleSyntax` with `IsValidObject`, `IsValidObjectId`, `IsValidType`, `IsValidRelation`, `IsValidUser`, `IsValidUserset`, `IsValidUserObject`, `IsValidUserWildcard`, `IsValidConditionName`, each `bool (string value)`.

- [ ] **Step 1: Write the failing tests**

`test/OpenFga.Language.Tests/TupleSyntaxTests.cs`:
```csharp
using OpenFga.Language.Tests.Fixtures;
using OpenFga.Language.Validation;

namespace OpenFga.Language.Tests;

public class TupleSyntaxTests
{
    private static readonly IReadOnlyList<ObjectIdCase> ObjectIds = TestData.ObjectIdCases();

    private static readonly Dictionary<string, Func<string, bool>> Validators = new()
    {
        ["object"] = TupleSyntax.IsValidObject,
        ["user"] = TupleSyntax.IsValidUser,
        ["userset"] = TupleSyntax.IsValidUserset,
        ["userObject"] = TupleSyntax.IsValidUserObject,
        ["userWildcard"] = TupleSyntax.IsValidUserWildcard,
        ["type"] = TupleSyntax.IsValidType,
    };

    private static readonly string[] BadStructures =
    [
        "item::1", ":item:1", "item:1:", "item#relation", "item:1##relation", "#item:1", "item:1#",
        "ite#m:1", "it*em:1", "*:1", "item*thing", "item:*thing", "item:**",
    ];

    public static TheoryData<string, string> BadStructureData
    {
        get
        {
            var data = new TheoryData<string, string>();
            foreach (var validator in Validators.Keys)
            {
                foreach (var value in BadStructures)
                {
                    data.Add(validator, value);
                }
            }

            return data;
        }
    }

    public static TheoryData<int, string> ObjectIdData => TestData.Indexes(ObjectIds, c => c.Name);

    [Theory]
    [MemberData(nameof(BadStructureData))]
    public void RejectsBadStructure(string validator, string value) => Assert.False(Validators[validator](value));

    [Theory]
    [InlineData("object", "document:1", true)]
    [InlineData("object", "document1", false)]
    [InlineData("object", "document:1#relation", false)]
    [InlineData("object", "document:*", false)]
    [InlineData("user", "group:engineering#member", true)]
    [InlineData("user", "group:engineering", true)]
    [InlineData("user", "group:*", true)]
    [InlineData("user", "group", false)]
    [InlineData("userset", "group:engineering#member", true)]
    [InlineData("userset", "group:engineering", false)]
    [InlineData("userset", "group:*", false)]
    [InlineData("userset", "group", false)]
    [InlineData("userObject", "group:engineering", true)]
    [InlineData("userObject", "group:engineering#member", false)]
    [InlineData("userObject", "group:*", false)]
    [InlineData("userObject", "group", false)]
    [InlineData("userWildcard", "group:*", true)]
    [InlineData("userWildcard", "group:organization", false)]
    [InlineData("userWildcard", "group:engineering#member", false)]
    [InlineData("userWildcard", "group", false)]
    [InlineData("type", "folder", true)]
    [InlineData("type", "folder:1", false)]
    [InlineData("type", "folder:1#relation", false)]
    public void ValidatesTupleParts(string validator, string value, bool expected) => Assert.Equal(expected, Validators[validator](value));

    [Theory]
    [MemberData(nameof(ObjectIdData))]
    public void ValidatesObjectIds(int index, string name)
    {
        _ = name;
        Assert.Equal(ObjectIds[index].Valid, TupleSyntax.IsValidObjectId(ObjectIds[index].ObjectId));
    }

    [Fact]
    public void ValidatesRelationAndConditionNames()
    {
        Assert.True(TupleSyntax.IsValidRelation("viewer"));
        Assert.False(TupleSyntax.IsValidRelation("view#er"));
        Assert.False(TupleSyntax.IsValidRelation(new string('a', 51)));
        Assert.True(TupleSyntax.IsValidConditionName("is_valid_ip"));
        Assert.False(TupleSyntax.IsValidConditionName("is valid"));
    }

    [Fact]
    public void TrailingNewlineIsInvalid()
    {
        Assert.False(TupleSyntax.IsValidObject("document:1\n"));
        Assert.False(TupleSyntax.IsValidType("document\n"));
        Assert.False(TupleSyntax.IsValidRelation("viewer\n"));
        Assert.False(TupleSyntax.IsValidObjectId("1\n"));
        Assert.False(TupleSyntax.IsValidUser("user:anne\n"));
    }

    [Fact]
    public void NullThrowsArgumentNullException()
    {
        foreach (var validator in Validators.Values.Append(TupleSyntax.IsValidObjectId).Append(TupleSyntax.IsValidRelation).Append(TupleSyntax.IsValidConditionName))
        {
            Assert.Equal("value", Assert.Throws<ArgumentNullException>(() => validator(null!)).ParamName);
        }
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test --filter FullyQualifiedName~TupleSyntaxTests`
Expected: build FAILS with CS0234 (`OpenFga.Language.Validation` does not exist).

- [ ] **Step 3: Implement `Rules` and `TupleSyntax`**

`src/OpenFga.Language/Validation/Rules.cs`:
```csharp
namespace OpenFga.Language.Validation;

/// <summary>Regex fragments shared with the Go, JS and Java libraries (JS validate-rules.ts Rules).</summary>
internal static class Rules
{
    public const string Type = @"[^:#@\*\s]{1,254}";
    public const string Relation = @"[^:#@\*\s]{1,50}";
    public const string Condition = @"[^\*\s]{1,50}";
    public const string Id = @"[^#:\s*][a-zA-Z0-9_|*@.+/\-]*";
    public const string Object = @"[^\s]{2,256}";

    // Kept textually identical to JS: they appear verbatim in invalid-name messages.
    public const string DefaultTypePattern = "^" + Type + "$";
    public const string DefaultRelationPattern = "^" + Relation + "$";
}
```

`src/OpenFga.Language/Validation/TupleSyntax.cs`:
```csharp
using System;
using System.Text.RegularExpressions;

namespace OpenFga.Language.Validation;

/// <summary>Checks the syntax of the parts of a relationship tuple (objects, users, relations, conditions).</summary>
/// <remarks>Patterns end with <c>\z</c>, so unlike .NET's <c>$</c> a trailing newline is never accepted.</remarks>
public static class TupleSyntax
{
    private static readonly Regex TypeAndId = Create("^" + Rules.Type + ":" + Rules.Id + @"\z");
    private static readonly Regex ObjectLength = Create("^" + Rules.Object + @"\z");
    private static readonly Regex Userset = Create("^" + Rules.Type + ":" + Rules.Id + "#" + Rules.Relation + @"\z");
    private static readonly Regex Wildcard = Create("^" + Rules.Type + @":\*\z");
    private static readonly Regex RelationName = Create("^" + Rules.Relation + @"\z");
    private static readonly Regex ConditionName = Create("^" + Rules.Condition + @"\z");
    private static readonly Regex TypeName = Create("^" + Rules.Type + @"\z");
    private static readonly Regex ObjectId = Create("^" + Rules.Id + @"\z");

    /// <summary>Determines whether <paramref name="value"/> is an object, <c>type:id</c> (e.g. <c>document:1</c>).</summary>
    /// <param name="value">The value to check.</param>
    /// <returns><see langword="true"/> if the value is a valid object.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="value"/> is null.</exception>
    public static bool IsValidObject(string value)
    {
        ThrowIfNull(value);
        return TypeAndId.IsMatch(value) && ObjectLength.IsMatch(value);
    }

    /// <summary>Determines whether <paramref name="value"/> is an object identifier (the part after <c>:</c>).</summary>
    /// <param name="value">The value to check.</param>
    /// <returns><see langword="true"/> if the value is a valid object id.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="value"/> is null.</exception>
    public static bool IsValidObjectId(string value)
    {
        ThrowIfNull(value);
        return ObjectId.IsMatch(value);
    }

    /// <summary>Determines whether <paramref name="value"/> is a type name.</summary>
    /// <param name="value">The value to check.</param>
    /// <returns><see langword="true"/> if the value is a valid type name.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="value"/> is null.</exception>
    public static bool IsValidType(string value)
    {
        ThrowIfNull(value);
        return TypeName.IsMatch(value);
    }

    /// <summary>Determines whether <paramref name="value"/> is a relation name.</summary>
    /// <param name="value">The value to check.</param>
    /// <returns><see langword="true"/> if the value is a valid relation name.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="value"/> is null.</exception>
    public static bool IsValidRelation(string value)
    {
        ThrowIfNull(value);
        return RelationName.IsMatch(value);
    }

    /// <summary>Determines whether <paramref name="value"/> is a user: a userset, a user object or a user wildcard.</summary>
    /// <param name="value">The value to check.</param>
    /// <returns><see langword="true"/> if the value is a valid user.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="value"/> is null.</exception>
    public static bool IsValidUser(string value)
    {
        ThrowIfNull(value);
        return IsValidUserset(value) || IsValidUserObject(value) || IsValidUserWildcard(value);
    }

    /// <summary>Determines whether <paramref name="value"/> is a userset, <c>type:id#relation</c> (e.g. <c>group:eng#member</c>).</summary>
    /// <param name="value">The value to check.</param>
    /// <returns><see langword="true"/> if the value is a valid userset.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="value"/> is null.</exception>
    public static bool IsValidUserset(string value)
    {
        ThrowIfNull(value);
        return Userset.IsMatch(value);
    }

    /// <summary>Determines whether <paramref name="value"/> is a user object, <c>type:id</c> (e.g. <c>user:anne</c>).</summary>
    /// <param name="value">The value to check.</param>
    /// <returns><see langword="true"/> if the value is a valid user object.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="value"/> is null.</exception>
    public static bool IsValidUserObject(string value)
    {
        ThrowIfNull(value);
        return TypeAndId.IsMatch(value) && ObjectLength.IsMatch(value);
    }

    /// <summary>Determines whether <paramref name="value"/> is a user wildcard, <c>type:*</c>.</summary>
    /// <param name="value">The value to check.</param>
    /// <returns><see langword="true"/> if the value is a valid user wildcard.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="value"/> is null.</exception>
    public static bool IsValidUserWildcard(string value)
    {
        ThrowIfNull(value);
        return Wildcard.IsMatch(value);
    }

    /// <summary>Determines whether <paramref name="value"/> is a condition name.</summary>
    /// <param name="value">The value to check.</param>
    /// <returns><see langword="true"/> if the value is a valid condition name.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="value"/> is null.</exception>
    public static bool IsValidConditionName(string value)
    {
        ThrowIfNull(value);
        return ConditionName.IsMatch(value);
    }

    private static Regex Create(string pattern) => new(pattern, RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    private static void ThrowIfNull(string value)
    {
        if (value is null)
        {
            throw new ArgumentNullException(nameof(value));
        }
    }
}
```

- [ ] **Step 4: Record the public API, run the tests, commit**

Run:
```bash
printf '#nullable enable
' > src/OpenFga.Language/PublicAPI.Unshipped.txt
dotnet format analyzers src/OpenFga.Language/OpenFga.Language.csproj --diagnostics RS0016 --severity info
```
(The RS0016 fixer only fills an API file that has no entries yet, so the file is regenerated each time. Nothing has shipped, so `PublicAPI.Unshipped.txt` holds the whole public surface.)
Expected: `git diff src/OpenFga.Language/PublicAPI.Unshipped.txt` adds entries for `OpenFga.Language.Validation.TupleSyntax` and its nine methods only.

Run: `dotnet test --filter FullyQualifiedName~TupleSyntaxTests`
Expected: all pass.

```bash
git add pkg/dotnet
git commit -m "feat(dotnet): add tuple syntax validators

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01U8M7PfSCNGaz27dr4xJPEu"
```

---

### Task 6: Validation building blocks (restrictions, text search, line numbers, error collector)

Port of the helpers in `pkg/js/validator/validate-dsl.ts`, `pkg/js/util/line-numbers.ts` and `pkg/js/util/exceptions.ts` (`ExceptionCollector`, `constructValidationError`).

**Files:**
- Create: `pkg/dotnet/src/OpenFga.Language/Internal/CollectionExtensions.cs`, `TextSearch.cs`, `LineNumbers.cs`
- Create: `pkg/dotnet/src/OpenFga.Language/Validation/TypeRestrictions.cs`, `ValidationErrorCollector.cs`
- Test: `pkg/dotnet/test/OpenFga.Language.Tests/ValidationBuildingBlocksTests.cs`

**Interfaces:**
- Consumes: `ModelValidationError`, `ValidationErrorType`, `SourceRange` (Task 2).
- Produces (all internal):
  - `CollectionExtensions.OrEmpty<T>(this IEnumerable<T>?)` (namespace `OpenFga.Language.Internal`).
  - `TextSearch.FindWord(string? line, string symbol) -> int` (-1 when absent), `FindWordOrZero`, `ResolveAfterColon(string line, string symbol, int fallback)`, `ResolveAfterFrom(string line, string symbol)`.
  - `LineNumbers.GetTypeLine(string name, string[]? lines, int? skipIndex = null, bool extension = false) -> int?`, `GetRelationLine(string relation, string[]? lines, int? skipIndex = null) -> int?`, `GetConditionLine(string name, string[]? lines, int? skipIndex = null) -> int?`, `GetSchemaLine(string schema, string[]? lines) -> int?`. Each returns `null` when `lines` is null and `-1` when not found, except `GetSchemaLine`, which returns 0 when not found.
  - `readonly struct Origin(string? file, string? module)` and `readonly struct DecodedRestriction { Type, Relation?, IsWildcard, Condition? }` (namespace `OpenFga.Language.Validation`).
  - `TypeRestrictions.InlineExpressionCondition` (`"$expression"`), `ToText(RelationReference)`, `ToTexts(IEnumerable<RelationReference>?)`, `Decode(string)`.
  - `sealed class ValidationErrorCollector(List<ModelValidationError> errors, string[]? lines)` with the `Raise*` methods listed in Step 5.

- [ ] **Step 1: Write the failing tests**

`test/OpenFga.Language.Tests/ValidationBuildingBlocksTests.cs`:
```csharp
using OpenFga.Language.Internal;
using OpenFga.Language.Validation;
using OpenFga.Sdk.Model;

namespace OpenFga.Language.Tests;

public class ValidationBuildingBlocksTests
{
    private static readonly string[] Lines =
    [
        "model",
        "  schema 1.1",
        "type user # module: core, file: core.fga",
        "type document",
        "  relations",
        "    define   viewer: [user, group#member] or editor",
        "    define parent: [folder]",
        "    define can_view: viewer from parent",
        "extend type document",
        "condition less(x: int) {",
        "condition less_than(x: int) {",
    ];

    [Fact]
    public void FindWordMatchesWholeWordsLikeJs()
    {
        Assert.Equal(22, TextSearch.FindWord(Lines[5], "user"));
        Assert.Equal(-1, TextSearch.FindWord(Lines[5], "use"));
        Assert.Equal(-1, TextSearch.FindWord(null, "user"));
    }

    [Fact]
    public void FindWordReturnsMinusOneForInvalidPattern()
    {
        Assert.Equal(-1, TextSearch.FindWord("define a: [b]", "a("));
        Assert.Equal(0, TextSearch.FindWordOrZero("define a: [b]", "[unclosed"));
    }

    [Fact]
    public void ResolversMarkTheValueNotTheKey()
    {
        Assert.Equal(22, TextSearch.ResolveAfterColon(Lines[5], "user", fallback: 99));
        Assert.Equal(99, TextSearch.ResolveAfterColon("no colon here", "user", fallback: 99));
        Assert.Equal(33, TextSearch.ResolveAfterFrom(Lines[7], "parent"));
    }

    [Fact]
    public void LineNumbersFindDefinitions()
    {
        Assert.Equal(2, LineNumbers.GetTypeLine("user", Lines));
        Assert.Equal(3, LineNumbers.GetTypeLine("document", Lines));
        Assert.Equal(8, LineNumbers.GetTypeLine("document", Lines, extension: true));
        Assert.Equal(-1, LineNumbers.GetTypeLine("doc", Lines));
        Assert.Equal(5, LineNumbers.GetRelationLine("viewer", Lines, 3));
        Assert.Equal(-1, LineNumbers.GetRelationLine("viewer", Lines, 6));
        Assert.Equal(9, LineNumbers.GetConditionLine("less", Lines));
        Assert.Equal(10, LineNumbers.GetConditionLine("less_than", Lines));
        Assert.Equal(1, LineNumbers.GetSchemaLine("1.1", Lines));
        Assert.Equal(0, LineNumbers.GetSchemaLine("1.2", Lines));
        Assert.Null(LineNumbers.GetTypeLine("user", null));
    }

    [Fact]
    public void TypeRestrictionsRoundTrip()
    {
        Assert.Equal("user:* with c", TypeRestrictions.ToText(new RelationReference { Type = "user", Wildcard = new object(), Condition = "c" }));
        Assert.Equal("group#member", TypeRestrictions.ToText(new RelationReference { Type = "group", Relation = "member" }));

        var decoded = TypeRestrictions.Decode("group#member with cond");
        Assert.Equal("group", decoded.Type);
        Assert.Equal("member", decoded.Relation);
        Assert.False(decoded.IsWildcard);
        Assert.Equal("cond", decoded.Condition);

        var wildcard = TypeRestrictions.Decode("user:*");
        Assert.Equal("user", wildcard.Type);
        Assert.Null(wildcard.Relation);
        Assert.True(wildcard.IsWildcard);
        Assert.Null(wildcard.Condition);
    }

    [Fact]
    public void CollectorComputesRangesFromLines()
    {
        var errors = new List<ModelValidationError>();
        var collector = new ValidationErrorCollector(errors, Lines);

        collector.RaiseInvalidType("folder", "document", "parent", new Origin("a.fga", "m"), 6);

        var error = Assert.Single(errors);
        Assert.Equal("`folder` is not a valid type.", error.Message);
        Assert.Equal(new SourceRange(6, 6, 20, 26), error.Range);
        Assert.Equal("a.fga", error.File);
        Assert.Equal("m", error.Module);
        Assert.Equal("document", error.TypeName);
        Assert.Equal("parent", error.RelationName);
        Assert.Equal(ValidationErrorType.InvalidType, error.ErrorType);
    }

    [Fact]
    public void CollectorOmitsRangeWithoutLines()
    {
        var errors = new List<ModelValidationError>();
        new ValidationErrorCollector(errors, null).RaiseNoEntryPoint("viewer", "document", default, 3);

        var error = Assert.Single(errors);
        Assert.Equal("`viewer` is an impossible relation for `document` (no entrypoint).", error.Message);
        Assert.Null(error.Range);
        Assert.Equal("viewer", error.RelationName);
    }

    [Fact]
    public void CollectorAssignableRelationUsesBracketText()
    {
        var errors = new List<ModelValidationError>();
        new ValidationErrorCollector(errors, ["    define viewer: []"]).RaiseAssignableRelationMustHaveTypes(0);

        Assert.Equal("assignable relation '[' must have types", Assert.Single(errors).Message);
    }

    [Fact]
    public void CollectorSkipsMissingDefinitionWhenRelationIsValid()
    {
        var errors = new List<ModelValidationError>();
        new ValidationErrorCollector(errors, null).RaiseInvalidRelationError("viewer", "doc", "can_view", ["viewer"], null, default);

        Assert.Empty(errors);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test --filter FullyQualifiedName~ValidationBuildingBlocksTests`
Expected: build FAILS with CS0103 (`TextSearch` does not exist).

- [ ] **Step 3: Implement the text helpers**

`src/OpenFga.Language/Internal/CollectionExtensions.cs`:
```csharp
using System.Collections.Generic;
using System.Linq;

namespace OpenFga.Language.Internal;

internal static class CollectionExtensions
{
    public static IEnumerable<T> OrEmpty<T>(this IEnumerable<T>? source) => source ?? Enumerable.Empty<T>();
}
```

`src/OpenFga.Language/Internal/TextSearch.cs`:
```csharp
using System;
using System.Text.RegularExpressions;

namespace OpenFga.Language.Internal;

/// <summary>Column resolution shared by validation and module combining (JS <c>line.search(new RegExp("\\b" + symbol + "\\b"))</c>).</summary>
internal static class TextSearch
{
    private static readonly TimeSpan MatchTimeout = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Index of the first whole-word occurrence of <paramref name="symbol"/>, or -1. The symbol is deliberately not
    /// escaped, as in JS; ECMAScript mode gives JS <c>\b</c> semantics. An invalid pattern counts as not found.
    /// </summary>
    public static int FindWord(string? line, string symbol)
    {
        if (line is null)
        {
            return -1;
        }

        try
        {
            var match = Regex.Match(line, @"\b" + symbol + @"\b", RegexOptions.ECMAScript, MatchTimeout);
            return match.Success ? match.Index : -1;
        }
        catch (ArgumentException)
        {
            return -1;
        }
    }

    public static int FindWordOrZero(string? line, string symbol)
    {
        var index = FindWord(line, symbol);
        return index < 0 ? 0 : index;
    }

    /// <summary>Marks the symbol in the text after the first <c>:</c> (the value, not the key).</summary>
    public static int ResolveAfterColon(string line, string symbol, int fallback)
    {
        var colon = line.IndexOf(':');
        if (colon < 0)
        {
            return fallback;
        }

        var segment = line.Substring(colon + 1);
        var nextColon = segment.IndexOf(':');
        if (nextColon >= 0)
        {
            segment = segment.Substring(0, nextColon);
        }

        return colon + FindWord(segment, symbol) + 1;
    }

    /// <summary>Marks the symbol in the text after <c>from</c> (the tupleset of <c>a from b</c>).</summary>
    public static int ResolveAfterFrom(string line, string symbol)
    {
        var start = Math.Min(line.IndexOf("from", StringComparison.Ordinal) + "from".Length, line.Length);
        return start + line.Substring(start).IndexOf(symbol, StringComparison.Ordinal);
    }
}
```

`src/OpenFga.Language/Internal/LineNumbers.cs`:
```csharp
using System;
using System.Text.RegularExpressions;

namespace OpenFga.Language.Internal;

/// <summary>Finds definition lines in DSL text (JS util/line-numbers.ts). Names are matched literally.</summary>
internal static class LineNumbers
{
    private static readonly TimeSpan MatchTimeout = TimeSpan.FromSeconds(1);
    private static readonly Regex OptionalComment = new(@"^(\s+#.*)?$", RegexOptions.CultureInvariant, MatchTimeout);
    private static readonly Regex StartsWithParen = new(@"^\s*\(", RegexOptions.CultureInvariant, MatchTimeout);
    private static readonly Regex StartsWithColon = new(@"^\s*:", RegexOptions.CultureInvariant, MatchTimeout);
    private static readonly Regex RepeatedSpaces = new(" {2,}", RegexOptions.CultureInvariant, MatchTimeout);

    public static int? GetTypeLine(string typeName, string[]? lines, int? skipIndex = null, bool extension = false)
    {
        var prefix = (extension ? "extend " : string.Empty) + "type " + typeName;
        return Find(lines, skipIndex, line =>
        {
            var trimmed = line.Trim();
            return trimmed.StartsWith(prefix, StringComparison.Ordinal) && OptionalComment.IsMatch(trimmed.Substring(prefix.Length));
        });
    }

    public static int? GetRelationLine(string relation, string[]? lines, int? skipIndex = null)
    {
        var prefix = "define " + relation;
        return Find(lines, skipIndex, line =>
        {
            var normalized = RepeatedSpaces.Replace(line.Trim(), " ");
            return normalized.StartsWith(prefix, StringComparison.Ordinal) && StartsWithColon.IsMatch(normalized.Substring(prefix.Length));
        });
    }

    public static int? GetConditionLine(string conditionName, string[]? lines, int? skipIndex = null)
    {
        // `(` must follow the name, so `less` cannot match `less_than`.
        var prefix = "condition " + conditionName;
        return Find(lines, skipIndex, line =>
        {
            var trimmed = line.Trim();
            return trimmed.StartsWith(prefix, StringComparison.Ordinal) && StartsWithParen.IsMatch(trimmed.Substring(prefix.Length));
        });
    }

    /// <summary>Returns the schema line, or 0 when it is not found (as JS does).</summary>
    public static int? GetSchemaLine(string schema, string[]? lines)
    {
        var prefix = "schema " + schema;
        var index = Find(lines, 0, line =>
        {
            var normalized = RepeatedSpaces.Replace(line.Trim(), " ");
            return normalized.StartsWith(prefix, StringComparison.Ordinal) && OptionalComment.IsMatch(normalized.Substring(prefix.Length));
        });

        return index is null ? null : Math.Max(index.Value, 0);
    }

    private static int? Find(string[]? lines, int? skipIndex, Func<string, bool> predicate)
    {
        if (lines is null)
        {
            return null;
        }

        var start = skipIndex is > 0 ? skipIndex.Value : 0;
        for (var i = start; i < lines.Length; i++)
        {
            if (predicate(lines[i]))
            {
                return i;
            }
        }

        return -1;
    }
}
```

- [ ] **Step 4: Implement type restriction helpers**

`src/OpenFga.Language/Validation/TypeRestrictions.cs`:
```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using OpenFga.Language.Internal;
using OpenFga.Sdk.Model;

namespace OpenFga.Language.Validation;

internal readonly struct Origin
{
    public Origin(string? file, string? module)
    {
        File = file;
        Module = module;
    }

    public string? File { get; }

    public string? Module { get; }
}

internal readonly struct DecodedRestriction
{
    public DecodedRestriction(string type, string? relation, bool isWildcard, string? condition)
    {
        Type = type;
        Relation = relation;
        IsWildcard = isWildcard;
        Condition = condition;
    }

    public string Type { get; }

    public string? Relation { get; }

    public bool IsWildcard { get; }

    public string? Condition { get; }
}

/// <summary>String form of type restrictions used by validation (JS getTypeRestrictionString / destructTupleToUserset).</summary>
internal static class TypeRestrictions
{
    /// <summary>Reserved condition name for inline expressions; it needs no entry in the model's conditions.</summary>
    public const string InlineExpressionCondition = "$expression";

    private static readonly string[] WithSeparator = [" with "];

    public static string ToText(RelationReference restriction)
    {
        var text = restriction.Type;
        if (restriction.Wildcard != null)
        {
            text += ":*";
        }
        else if (!string.IsNullOrEmpty(restriction.Relation))
        {
            text += "#" + restriction.Relation;
        }

        if (!string.IsNullOrEmpty(restriction.Condition))
        {
            text += " with " + restriction.Condition;
        }

        return text;
    }

    public static List<string> ToTexts(IEnumerable<RelationReference>? restrictions) => restrictions.OrEmpty().Select(ToText).ToList();

    public static DecodedRestriction Decode(string restriction)
    {
        var parts = restriction.Split(WithSeparator, StringSplitOptions.None);
        var tuple = parts[0];
        var condition = parts.Length > 1 && parts[1].Length > 0 ? parts[1] : null;
        var wildcardAt = tuple.IndexOf(":*", StringComparison.Ordinal);
        var withoutWildcard = wildcardAt < 0 ? tuple : tuple.Remove(wildcardAt, 2);
        var words = withoutWildcard.Split('#');
        var relation = words.Length > 1 && words[1].Length > 0 ? words[1] : null;
        return new DecodedRestriction(words[0], relation, wildcardAt >= 0, condition);
    }
}
```

- [ ] **Step 5: Implement the error collector**

`src/OpenFga.Language/Validation/ValidationErrorCollector.cs`. The messages are copied from `pkg/js/util/exceptions.ts` and asserted by the fixtures:
```csharp
using System;
using System.Collections.Generic;
using OpenFga.Language.Internal;

namespace OpenFga.Language.Validation;

/// <summary>Builds <see cref="ModelValidationError"/>s with source ranges (JS ExceptionCollector + constructValidationError).</summary>
internal sealed class ValidationErrorCollector
{
    private readonly List<ModelValidationError> _errors;
    private readonly string[]? _lines;

    public ValidationErrorCollector(List<ModelValidationError> errors, string[]? lines)
    {
        _errors = errors;
        _lines = lines;
    }

    public void RaiseInvalidName(string symbol, string clause, string? typeName, int? lineIndex, Origin origin)
    {
        var message = (typeName != null ? $"relation '{symbol}' of type '{typeName}' " : $"type '{symbol}' ")
            + $"does not match naming rule: '{clause}'.";
        Add(message, lineIndex, ValidationErrorType.InvalidName, symbol, origin);
    }

    public void RaiseReservedTypeName(string symbol, int? lineIndex, Origin origin)
        => Add("a type cannot be named 'self' or 'this'.", lineIndex, ValidationErrorType.ReservedTypeKeywords, symbol, origin);

    public void RaiseReservedRelationName(string symbol, int? lineIndex, Origin origin)
        => Add("a relation cannot be named 'self' or 'this'.", lineIndex, ValidationErrorType.ReservedRelationKeywords, symbol, origin);

    public void RaiseTupleUsersetRequiresDirect(string symbol, string type, string relation, Origin origin, int? lineIndex)
        => Add(
            $"`{symbol}` relation used inside from allows only direct relation.",
            lineIndex,
            ValidationErrorType.TuplesetNotDirect,
            symbol,
            origin,
            typeName: type,
            relationName: relation,
            resolver: (_, line, value) => TextSearch.ResolveAfterFrom(line, value));

    public void RaiseDuplicateTypeName(string symbol, Origin origin, int? lineIndex)
        => Add($"the type `{symbol}` is a duplicate.", lineIndex, ValidationErrorType.DuplicatedError, symbol, origin, typeName: symbol);

    public void RaiseDuplicateTypeRestriction(string symbol, string relationName, string typeName, Origin origin, int? lineIndex)
        => Add(
            $"the type restriction `{symbol}` is a duplicate in the relation `{relationName}`.",
            lineIndex,
            ValidationErrorType.DuplicatedError,
            symbol,
            origin,
            typeName: typeName,
            relationName: relationName);

    public void RaiseDuplicateType(string symbol, string relationName, string typeName, Origin origin, int? lineIndex)
        => Add(
            $"the partial relation definition `{symbol}` is a duplicate in the relation `{relationName}`.",
            lineIndex,
            ValidationErrorType.DuplicatedError,
            symbol,
            origin,
            typeName: typeName,
            relationName: relationName);

    public void RaiseNoEntryPointLoop(string symbol, string typeName, Origin origin, int? lineIndex)
        => Add(
            $"`{symbol}` is an impossible relation for `{typeName}` (potential loop).",
            lineIndex,
            ValidationErrorType.RelationNoEntrypoint,
            symbol,
            origin,
            typeName: typeName,
            relationName: symbol);

    public void RaiseNoEntryPoint(string symbol, string typeName, Origin origin, int? lineIndex)
        => Add(
            $"`{symbol}` is an impossible relation for `{typeName}` (no entrypoint).",
            lineIndex,
            ValidationErrorType.RelationNoEntrypoint,
            symbol,
            origin,
            typeName: typeName,
            relationName: symbol);

    public void RaiseInvalidRelationOnTupleset(string symbol, string typeName, string typeDef, string relationName, string offendingRelation, string parent, int? lineIndex, Origin origin)
        => Add(
            $"the `{offendingRelation}` relation definition on type `{typeDef}` is not valid: `{offendingRelation}` does not exist on `{parent}`, which is of type `{typeName}`.",
            lineIndex,
            ValidationErrorType.InvalidRelationOnTupleset,
            symbol,
            origin,
            typeName: typeDef,
            relationName: relationName);

    public void RaiseInvalidTypeRelation(string symbol, string typeName, string relationName, string offendingRelation, string offendingType, int? lineIndex, Origin origin)
        => Add(
            $"`{offendingRelation}` is not a valid relation for `{typeName}`.",
            lineIndex,
            ValidationErrorType.InvalidRelationType,
            symbol,
            origin,
            typeName: typeName,
            relationName: relationName,
            offendingType: offendingType);

    public void RaiseInvalidType(string symbol, string typeName, string relation, Origin origin, int? lineIndex)
        => Add(
            $"`{symbol}` is not a valid type.",
            lineIndex,
            ValidationErrorType.InvalidType,
            symbol,
            origin,
            typeName: typeName,
            relationName: relation,
            resolver: (wordIndex, line, value) => TextSearch.ResolveAfterColon(line, value, wordIndex));

    public void RaiseAssignableRelationMustHaveTypes(int? lineIndex)
    {
        var rawLine = LineAt(lineIndex);
        if (rawLine is null)
        {
            Add("assignable relation '' must have types", null, ValidationErrorType.AssignableRelationsMustHaveType, string.Empty, default);
            return;
        }

        var actualValue = "self";
        var open = rawLine.IndexOf('[');
        if (open >= 0)
        {
            // JS rawLine.slice(indexOf("["), lastIndexOf("]")): a negative end counts from the end of the line.
            var close = rawLine.LastIndexOf(']');
            var end = close < 0 ? Math.Max(rawLine.Length + close, 0) : close;
            actualValue = end > open ? rawLine.Substring(open, end - open) : string.Empty;
        }

        Add(
            $"assignable relation '{actualValue}' must have types",
            lineIndex,
            ValidationErrorType.AssignableRelationsMustHaveType,
            actualValue,
            default,
            resolver: (_, line, value) => line.IndexOf(value.Length > 0 ? value.Substring(1) : value, StringComparison.Ordinal));
    }

    public void RaiseAssignableTypeWildcardRelation(string symbol, string type, string relation, Origin origin, int? lineIndex)
        => Add(
            $"type restriction `{symbol}` cannot contain both wildcard and relation",
            lineIndex,
            ValidationErrorType.TypeRestrictionCannotHaveWildcardAndRelation,
            symbol,
            origin,
            typeName: type,
            relationName: relation);

    public void RaiseInvalidRelationError(string symbol, string type, string relation, ICollection<string> validRelations, int? lineIndex, Origin origin)
    {
        if (validRelations.Contains(symbol))
        {
            return;
        }

        Add($"the relation `{symbol}` does not exist.", lineIndex, ValidationErrorType.MissingDefinition, symbol, origin, typeName: type, relationName: relation);
    }

    public void RaiseInvalidSchemaVersion(string symbol, int? lineIndex)
        => Add($"invalid schema {symbol}", lineIndex, ValidationErrorType.InvalidSchema, symbol, default);

    public void RaiseSchemaVersionRequired(string symbol, int? lineIndex)
        => Add("schema version required", lineIndex, ValidationErrorType.SchemaVersionRequired, symbol, default);

    public void RaiseInvalidConditionNameInParameter(string symbol, string typeName, string relationName, string conditionName, Origin origin, int? lineIndex)
        => Add(
            $"`{conditionName}` is not a defined condition in the model.",
            lineIndex,
            ValidationErrorType.ConditionNotDefined,
            symbol,
            origin,
            typeName: typeName,
            relationName: relationName);

    public void RaiseUnusedCondition(string symbol, Origin origin, int? lineIndex)
        => Add($"`{symbol}` condition is not used in the model.", lineIndex, ValidationErrorType.ConditionNotUsed, symbol, origin, conditionName: symbol);

    public void RaiseDifferentNestedConditionName(string condition, string nestedConditionName)
        => Add(
            $"condition key is `{condition}` but nested name property is {nestedConditionName}",
            null,
            ValidationErrorType.DifferentNestedConditionName,
            nestedConditionName,
            default);

    public void RaiseMultipleModulesInSingleFile(string file, IEnumerable<string> modules)
        => Add(
            $"file {file} would contain multiple module definitions ({string.Join(", ", modules)}) when transforming to DSL. Only one module can be defined per file.",
            null,
            ValidationErrorType.MultipleModulesInFile,
            file,
            default);

    private string? LineAt(int? lineIndex)
        => _lines is { Length: > 0 } && lineIndex is int index && index >= 0 && index < _lines.Length ? _lines[index] : null;

    private void Add(
        string message,
        int? lineIndex,
        ValidationErrorType errorType,
        string symbol,
        Origin origin,
        string? typeName = null,
        string? relationName = null,
        string? conditionName = null,
        string? offendingType = null,
        Func<int, string, string, int>? resolver = null)
    {
        SourceRange? range = null;
        if (_lines is { Length: > 0 } && lineIndex is int index && index >= 0)
        {
            var rawLine = LineAt(index);
            var wordIndex = TextSearch.FindWordOrZero(rawLine, symbol);
            if (resolver != null && rawLine != null)
            {
                wordIndex = resolver(wordIndex, rawLine, symbol);
            }

            range = new SourceRange(index, index, wordIndex, wordIndex + symbol.Length);
        }

        _errors.Add(new ModelValidationError(message, origin.File, range, errorType, symbol, typeName, relationName, conditionName, origin.Module, offendingType));
    }
}
```

- [ ] **Step 6: Run the tests to verify they pass**

Run: `dotnet test --filter FullyQualifiedName~ValidationBuildingBlocksTests`
Expected: all pass.

- [ ] **Step 7: Commit**

```bash
git add pkg/dotnet
git commit -m "feat(dotnet): add validation helpers and error collector

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01U8M7PfSCNGaz27dr4xJPEu"
```

---

### Task 7: Semantic model validation (`ModelValidator`)

Port of `validateJSON`, `validateDSL`, `modelValidation`, `populateRelations`, `relationDefined`, `childDefDefined`, `allowableTypes`, the duplicate checks and `hasEntryPointOrLoop` from `pkg/js/validator/validate-dsl.ts`.

**Files:**
- Create: `pkg/dotnet/src/OpenFga.Language/Validation/ModelValidationOptions.cs`, `ModelValidator.cs`, `ValidationPatterns.cs`, `VisitedRelations.cs`, `EntryPointAnalyzer.cs`, `ModelValidationEngine.cs`
- Modify: `pkg/dotnet/src/OpenFga.Language/PublicAPI.Unshipped.txt`
- Test: `pkg/dotnet/test/OpenFga.Language.Tests/ModelValidatorTests.cs`

**Interfaces:**
- Consumes: Task 6 helpers, `DslParser` (Task 3), `ModelJson` (Task 3), `Rules` (Task 5), fixture helpers (Task 3).
- Produces:
  - `public sealed class ModelValidationOptions { string TypePattern; string RelationPattern; }` (namespace `OpenFga.Language.Validation`).
  - `public static class ModelValidator` with `Validate(string)`, `Validate(string, ModelValidationOptions)`, `Validate(AuthorizationModel)`, `Validate(AuthorizationModel, ModelValidationOptions)`.
  - `internal sealed class ValidationPatterns { static ValidationPatterns From(ModelValidationOptions options, string paramName); static ValidationPatterns Default { get; } }`.
  - `internal static class ModelValidationEngine { static List<ModelValidationError> Collect(AuthorizationModel model, ValidationPatterns patterns, string? dsl) }`. It never throws for model content. Task 9 calls it directly.

- [ ] **Step 1: Write the failing tests**

`test/OpenFga.Language.Tests/ModelValidatorTests.cs`:
```csharp
using OpenFga.Language.Tests.Fixtures;
using OpenFga.Language.Validation;
using OpenFga.Sdk.Model;

namespace OpenFga.Language.Tests;

public class ModelValidatorTests
{
    private static readonly IReadOnlyList<DslCase> Syntax = TestData.DslSyntaxCases();
    private static readonly IReadOnlyList<DslCase> Semantic = TestData.DslSemanticCases();
    private static readonly IReadOnlyList<JsonCase> Json = TestData.JsonValidationCases();

    public static TheoryData<int, string> SyntaxData => TestData.Indexes(Syntax, c => c.Name);

    public static TheoryData<int, string> SemanticData => TestData.Indexes(Semantic, c => c.Name);

    public static TheoryData<int, string> JsonData => TestData.Indexes(Json, c => c.Name);

    [Theory]
    [MemberData(nameof(SyntaxData))]
    public void ValidateDslReportsSyntaxErrors(int index, string name)
    {
        _ = name;
        var testCase = Syntax[index];

        if (testCase.ExpectedErrors.Count == 0)
        {
            ModelValidator.Validate(testCase.Dsl);
            return;
        }

        var exception = Assert.Throws<DslSyntaxException>(() => ModelValidator.Validate(testCase.Dsl));
        Assert.Equal(ErrorAssert.ExpectedAggregate(testCase.ExpectedErrors, _ => "syntax", includeLocation: true), exception.Message);
        ErrorAssert.AllMatch(testCase.ExpectedErrors, exception.Errors);
    }

    [Theory]
    [MemberData(nameof(SemanticData))]
    public void ValidateDslReportsSemanticErrors(int index, string name)
    {
        _ = name;
        var testCase = Semantic[index];

        if (testCase.ExpectedErrors.Count == 0)
        {
            ModelValidator.Validate(testCase.Dsl);
            return;
        }

        var exception = Assert.Throws<ModelValidationException>(() => ModelValidator.Validate(testCase.Dsl));
        Assert.Equal(ErrorAssert.ExpectedAggregate(testCase.ExpectedErrors, e => e.Metadata?.ErrorType ?? "validation", includeLocation: true), exception.Message);
        ErrorAssert.AllMatch(testCase.ExpectedErrors, exception.Errors);
    }

    [Theory]
    [MemberData(nameof(SemanticData))]
    public void ValidateModelFromDslReportsSemanticErrorsWithoutLocations(int index, string name)
    {
        _ = name;
        var testCase = Semantic[index];
        var model = ModelTransformer.TransformDslToModel(testCase.Dsl);

        if (testCase.ExpectedErrors.Count == 0)
        {
            ModelValidator.Validate(model);
            return;
        }

        var exception = Assert.Throws<ModelValidationException>(() => ModelValidator.Validate(model));
        Assert.Equal(ErrorAssert.ExpectedAggregate(testCase.ExpectedErrors, e => e.Metadata?.ErrorType ?? "validation", includeLocation: false), exception.Message);
        Assert.Equal(testCase.ExpectedErrors.Count, exception.Errors.Count);
        for (var i = 0; i < exception.Errors.Count; i++)
        {
            Assert.Null(exception.Errors[i].Range);
            if (testCase.ExpectedErrors[i].Metadata?.Symbol is { } symbol)
            {
                Assert.Equal(symbol, exception.Errors[i].Symbol);
            }
        }
    }

    [Theory]
    [MemberData(nameof(JsonData))]
    public void ValidateModelReportsErrors(int index, string name)
    {
        _ = name;
        var testCase = Json[index];
        var model = AuthorizationModel.FromJson(testCase.Json);

        if (testCase.ExpectedErrors.Count == 0)
        {
            ModelValidator.Validate(model);
            return;
        }

        var exception = Assert.Throws<ModelValidationException>(() => ModelValidator.Validate(model));
        Assert.Equal(ErrorAssert.ExpectedAggregate(testCase.ExpectedErrors, e => e.Metadata?.ErrorType ?? "validation", includeLocation: false), exception.Message);
        ErrorAssert.AllMatch(testCase.ExpectedErrors, exception.Errors);
    }

    [Theory]
    [InlineData("")]
    [InlineData("type user")]
    [InlineData("model\n  schema 1.1\ntype user\n  relations\n    define viewer: [user] or")]
    [InlineData("model\n  schema 1.1\ntype user\n  relations\n    define viewer: ([user] or (a and")]
    [InlineData("model\n  schema 1.1\ntype user\ncondition c(x: int) {")]
    public void ValidateMalformedDslThrowsOnlyDslSyntaxException(string dsl)
        => Assert.IsType<DslSyntaxException>(Record.Exception(() => ModelValidator.Validate(dsl)));

    [Fact]
    public void ConditionedTuplesetRestrictionDoesNotCrashEntryPointAnalysis()
    {
        // JS throws a TypeError here (it indexes types by "folder with c"); .NET resolves the restriction's type.
        const string dsl = """
            model
              schema 1.1
            type user
            type folder
              relations
                define viewer: [user]
            type document
              relations
                define parent: [folder with c]
                define viewer: viewer from parent
            condition c(x: int) {
              x < 100
            }
            """;

        ModelValidator.Validate(dsl);
    }

    [Fact]
    public void CustomNamingRuleAppearsInMessage()
    {
        var options = new ModelValidationOptions { TypePattern = "^[a-z]{1,3}$" };

        var exception = Assert.Throws<ModelValidationException>(() => ModelValidator.Validate("model\n  schema 1.1\ntype document\n", options));

        var error = Assert.Single(exception.Errors);
        Assert.Equal(ValidationErrorType.InvalidName, error.ErrorType);
        Assert.Equal("type 'document' does not match naming rule: '^[a-z]{1,3}$'.", error.Message);
        Assert.Equal(new SourceRange(2, 2, 5, 13), error.Range);
    }

    [Fact]
    public void DefaultOptionsUseSharedRules()
    {
        var options = new ModelValidationOptions();

        Assert.Equal(@"^[^:#@\*\s]{1,254}$", options.TypePattern);
        Assert.Equal(@"^[^:#@\*\s]{1,50}$", options.RelationPattern);
    }

    [Fact]
    public void InvalidRegexThrowsArgumentException()
    {
        var exception = Assert.Throws<ArgumentException>(() => ModelValidator.Validate("model\n  schema 1.1\ntype user\n", new ModelValidationOptions { RelationPattern = "(" }));

        Assert.Equal("options", exception.ParamName);
        Assert.StartsWith("Incorrect relation regex specification for (", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void NullArgumentsThrowArgumentNullException()
    {
        var model = new AuthorizationModel { SchemaVersion = "1.1", TypeDefinitions = [] };

        Assert.Equal("dsl", Assert.Throws<ArgumentNullException>(() => ModelValidator.Validate((string)null!)).ParamName);
        Assert.Equal("model", Assert.Throws<ArgumentNullException>(() => ModelValidator.Validate((AuthorizationModel)null!)).ParamName);
        Assert.Equal("options", Assert.Throws<ArgumentNullException>(() => ModelValidator.Validate("model\n  schema 1.1\n", null!)).ParamName);
        Assert.Equal("options", Assert.Throws<ArgumentNullException>(() => ModelValidator.Validate(model, null!)).ParamName);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test --filter FullyQualifiedName~ModelValidatorTests`
Expected: build FAILS with CS0103 (`ModelValidator` does not exist).

- [ ] **Step 3: Implement options and patterns**

`src/OpenFga.Language/Validation/ModelValidationOptions.cs`:
```csharp
namespace OpenFga.Language.Validation;

/// <summary>Naming rules applied by <see cref="ModelValidator"/>.</summary>
public sealed class ModelValidationOptions
{
    /// <summary>Gets or sets the regular expression type names must match. Defaults to <c>^[^:#@\*\s]{1,254}$</c>.</summary>
    public string TypePattern { get; set; } = Rules.DefaultTypePattern;

    /// <summary>Gets or sets the regular expression relation names must match. Defaults to <c>^[^:#@\*\s]{1,50}$</c>.</summary>
    public string RelationPattern { get; set; } = Rules.DefaultRelationPattern;
}
```

`src/OpenFga.Language/Validation/ValidationPatterns.cs`:
```csharp
using System;
using System.Text.RegularExpressions;

namespace OpenFga.Language.Validation;

/// <summary>Compiled naming rules. An empty pattern falls back to the default, as JS does with <c>||</c>.</summary>
internal sealed class ValidationPatterns
{
    private static readonly TimeSpan MatchTimeout = TimeSpan.FromSeconds(1);

    private ValidationPatterns(string typeRule, Regex type, string relationRule, Regex relation)
    {
        TypeRule = typeRule;
        Type = type;
        RelationRule = relationRule;
        Relation = relation;
    }

    public static ValidationPatterns Default { get; } = From(new ModelValidationOptions(), "options");

    public string TypeRule { get; }

    public Regex Type { get; }

    public string RelationRule { get; }

    public Regex Relation { get; }

    public static ValidationPatterns From(ModelValidationOptions options, string paramName)
    {
        var typeRule = string.IsNullOrEmpty(options.TypePattern) ? Rules.DefaultTypePattern : options.TypePattern;
        var relationRule = string.IsNullOrEmpty(options.RelationPattern) ? Rules.DefaultRelationPattern : options.RelationPattern;
        return new ValidationPatterns(
            typeRule,
            Compile(typeRule, "type", paramName),
            relationRule,
            Compile(relationRule, "relation", paramName));
    }

    private static Regex Compile(string pattern, string kind, string paramName)
    {
        try
        {
            return new Regex(pattern, RegexOptions.CultureInvariant, MatchTimeout);
        }
        catch (ArgumentException ex)
        {
            throw new ArgumentException($"Incorrect {kind} regex specification for {pattern}", paramName, ex);
        }
    }
}
```

- [ ] **Step 4: Implement entry-point analysis**

`src/OpenFga.Language/Validation/VisitedRelations.cs`:
```csharp
using System.Collections.Generic;

namespace OpenFga.Language.Validation;

/// <summary>Type → relations already visited on the current path. Copied on every branch, as JS deep-copies it.</summary>
internal sealed class VisitedRelations
{
    private readonly Dictionary<string, HashSet<string>> _visited;

    public VisitedRelations()
        : this([])
    {
    }

    private VisitedRelations(Dictionary<string, HashSet<string>> visited)
    {
        _visited = visited;
    }

    public VisitedRelations Clone()
    {
        var copy = new Dictionary<string, HashSet<string>>(_visited.Count);
        foreach (var entry in _visited)
        {
            copy[entry.Key] = new HashSet<string>(entry.Value);
        }

        return new VisitedRelations(copy);
    }

    public void Add(string type, string relation)
    {
        if (!_visited.TryGetValue(type, out var relations))
        {
            relations = [];
            _visited[type] = relations;
        }

        relations.Add(relation);
    }

    public bool Contains(string type, string relation) => _visited.TryGetValue(type, out var relations) && relations.Contains(relation);
}
```

`src/OpenFga.Language/Validation/EntryPointAnalyzer.cs`:
```csharp
using System.Collections.Generic;
using OpenFga.Language.Internal;
using OpenFga.Sdk.Model;

namespace OpenFga.Language.Validation;

/// <summary>
/// Decides whether a relation can ever be satisfied (has an entry point) and whether a loop was found on the way
/// (JS hasEntryPointOrLoop).
/// </summary>
internal static class EntryPointAnalyzer
{
    public static (bool HasEntry, bool Loop) Analyze(Dictionary<string, TypeDefinition> typeMap, string typeName, string? relationName, Userset rewrite, VisitedRelations visitedRecords)
    {
        var visited = visitedRecords.Clone();
        if (string.IsNullOrEmpty(relationName))
        {
            return (false, false);
        }

        visited.Add(typeName, relationName!);

        if (!typeMap.TryGetValue(typeName, out var typeDef) || typeDef.Relations == null || !typeDef.Relations.ContainsKey(relationName!))
        {
            return (false, false);
        }

        var relations = typeDef.Relations;
        var relationsMetadata = typeDef.Metadata?.Relations;

        if (rewrite.This != null)
        {
            foreach (var restriction in TypeRestrictions.ToTexts(relationsMetadata.GetOrNull(relationName!)?.DirectlyRelatedUserTypes))
            {
                var decoded = TypeRestrictions.Decode(restriction);
                if (decoded.Relation is null || decoded.IsWildcard)
                {
                    return (true, false);
                }

                var assignableRelation = typeMap.GetOrNull(decoded.Type)?.Relations.GetOrNull(decoded.Relation);
                if (assignableRelation == null)
                {
                    return (false, false);
                }

                if (visited.Contains(decoded.Type, decoded.Relation))
                {
                    continue;
                }

                if (Analyze(typeMap, decoded.Type, decoded.Relation, assignableRelation, visited).HasEntry)
                {
                    return (true, false);
                }
            }

            return (false, false);
        }

        if (rewrite.ComputedUserset != null)
        {
            var computedRelationName = rewrite.ComputedUserset.Relation;
            if (computedRelationName is not { Length: > 0 })
            {
                return (false, false);
            }

            var computedRelation = relations.GetOrNull(computedRelationName);
            if (computedRelation == null)
            {
                return (false, false);
            }

            if (visited.Contains(typeName, computedRelationName))
            {
                return (false, true);
            }

            return Analyze(typeMap, typeName, computedRelationName, computedRelation, visited);
        }

        if (rewrite.TupleToUserset != null)
        {
            var tuplesetRelationName = rewrite.TupleToUserset.Tupleset?.Relation;
            var computedRelationName = rewrite.TupleToUserset.ComputedUserset?.Relation;
            if (tuplesetRelationName is not { Length: > 0 } || computedRelationName is not { Length: > 0 } || relations.GetOrNull(tuplesetRelationName) == null)
            {
                return (false, false);
            }

            foreach (var restriction in TypeRestrictions.ToTexts(relationsMetadata.GetOrNull(tuplesetRelationName)?.DirectlyRelatedUserTypes))
            {
                // JS indexes types by the full restriction text, which throws for "folder with c"; use the type part.
                var targetType = TypeRestrictions.Decode(restriction).Type;
                var assignableRelation = typeMap.GetOrNull(targetType)?.Relations.GetOrNull(computedRelationName);
                if (assignableRelation == null)
                {
                    continue;
                }

                if (visited.Contains(targetType, computedRelationName))
                {
                    continue;
                }

                if (Analyze(typeMap, targetType, computedRelationName, assignableRelation, visited).HasEntry)
                {
                    return (true, false);
                }
            }

            return (false, false);
        }

        if (rewrite.Union != null)
        {
            var hasLoop = false;
            foreach (var child in rewrite.Union.Child.OrEmpty())
            {
                var result = Analyze(typeMap, typeName, relationName, child, visited.Clone());
                if (result.HasEntry)
                {
                    return (true, false);
                }

                hasLoop = hasLoop || result.Loop;
            }

            return (false, hasLoop);
        }

        if (rewrite.Intersection != null)
        {
            foreach (var child in rewrite.Intersection.Child.OrEmpty())
            {
                var result = Analyze(typeMap, typeName, relationName, child, visited.Clone());
                if (!result.HasEntry)
                {
                    return (false, result.Loop);
                }
            }

            return (true, false);
        }

        if (rewrite.Difference != null)
        {
            // JS restarts from the caller's visited set here, not the one that includes this relation.
            var differenceVisited = visitedRecords.Clone();
            var baseResult = Analyze(typeMap, typeName, relationName, rewrite.Difference.Base ?? new Userset(), differenceVisited);
            if (!baseResult.HasEntry)
            {
                return (false, baseResult.Loop);
            }

            var subtractResult = Analyze(typeMap, typeName, relationName, rewrite.Difference.Subtract ?? new Userset(), differenceVisited);
            if (!subtractResult.HasEntry)
            {
                return (false, subtractResult.Loop);
            }

            return (true, false);
        }

        return (false, false);
    }
}
```

- [ ] **Step 5: Implement the validation engine**

`src/OpenFga.Language/Validation/ModelValidationEngine.cs`:
```csharp
using System.Collections.Generic;
using System.Linq;
using OpenFga.Language.Internal;
using OpenFga.Sdk.Model;

namespace OpenFga.Language.Validation;

/// <summary>Semantic validation of an AuthorizationModel (JS validateJSON and helpers).</summary>
internal static class ModelValidationEngine
{
    private const string SelfKeyword = "self";
    private const string ThisKeyword = "this";

    /// <summary>Returns every validation error; ranges are set only when <paramref name="dsl"/> is given.</summary>
    public static List<ModelValidationError> Collect(AuthorizationModel model, ValidationPatterns patterns, string? dsl)
    {
        var lines = dsl?.Split('\n');
        var errors = new List<ModelValidationError>();
        var collector = new ValidationErrorCollector(errors, lines);
        var fileToModules = new Dictionary<string, List<string>>();

        PopulateRelations(collector, model, patterns, fileToModules, lines);

        // Deliberately mirrors JS: "" is both "required" and "invalid".
        string? schemaVersion = model.SchemaVersion;
        if (string.IsNullOrEmpty(schemaVersion))
        {
            collector.RaiseSchemaVersionRequired(string.Empty, 0);
        }

        switch (schemaVersion)
        {
            case "1.1":
            case "1.2":
                ValidateModel(collector, errors, model, fileToModules, lines);
                break;
            case null:
                break;
            default:
                collector.RaiseInvalidSchemaVersion(schemaVersion, LineNumbers.GetSchemaLine(schemaVersion, lines));
                break;
        }

        foreach (var entry in fileToModules.Where(e => e.Value.Count > 1))
        {
            collector.RaiseMultipleModulesInSingleFile(entry.Key, entry.Value);
        }

        return errors;
    }

    private static void PopulateRelations(ValidationErrorCollector collector, AuthorizationModel model, ValidationPatterns patterns, Dictionary<string, List<string>> fileToModules, string[]? lines)
    {
        foreach (var typeDef in model.TypeDefinitions.OrEmpty())
        {
            var typeName = typeDef.Type ?? string.Empty;
            var typeOrigin = new Origin(typeDef.Metadata?.SourceInfo?.File, typeDef.Metadata?.Module);
            TrackModule(fileToModules, typeOrigin);

            if (typeName is SelfKeyword or ThisKeyword)
            {
                collector.RaiseReservedTypeName(typeName, LineNumbers.GetTypeLine(typeName, lines), typeOrigin);
            }

            if (!patterns.Type.IsMatch(typeName))
            {
                collector.RaiseInvalidName(typeName, patterns.TypeRule, null, LineNumbers.GetTypeLine(typeName, lines), typeOrigin);
            }

            foreach (var relationName in (typeDef.Relations?.Keys).OrEmpty())
            {
                // A relation without its own module belongs to the type's file and module.
                var relationMetadata = typeDef.Metadata?.Relations.GetOrNull(relationName);
                var origin = relationMetadata?.Module is { Length: > 0 }
                    ? new Origin(relationMetadata.SourceInfo?.File, relationMetadata.Module)
                    : typeOrigin;

                if (relationName is SelfKeyword or ThisKeyword)
                {
                    collector.RaiseReservedRelationName(relationName, RelationLine(typeName, relationName, lines), origin);
                }

                if (!patterns.Relation.IsMatch(relationName))
                {
                    collector.RaiseInvalidName(relationName, patterns.RelationRule, typeName, RelationLine(typeName, relationName, lines), origin);
                }
            }
        }
    }

    private static void ValidateModel(ValidationErrorCollector collector, List<ModelValidationError> errors, AuthorizationModel model, Dictionary<string, List<string>> fileToModules, string[]? lines)
    {
        // No point checking assignability when the model already has naming problems.
        if (errors.Count > 0)
        {
            return;
        }

        var typeDefinitions = model.TypeDefinitions.OrEmpty().ToList();
        var typeMap = new Dictionary<string, TypeDefinition>();
        var usedConditionNames = new HashSet<string>();
        foreach (var typeDef in typeDefinitions)
        {
            typeMap[typeDef.Type ?? string.Empty] = typeDef;
            foreach (var relationMetadata in (typeDef.Metadata?.Relations?.Values).OrEmpty())
            {
                foreach (var restriction in (relationMetadata?.DirectlyRelatedUserTypes).OrEmpty())
                {
                    if (restriction.Condition is { Length: > 0 })
                    {
                        usedConditionNames.Add(restriction.Condition);
                    }
                }
            }
        }

        // First, every referenced relation, type and condition must be defined.
        foreach (var typeDef in typeDefinitions)
        {
            foreach (var relationName in (typeDef.Relations?.Keys).OrEmpty())
            {
                RelationDefined(collector, typeMap, typeDef.Type ?? string.Empty, relationName, model.Conditions, lines);
            }
        }

        if (errors.Count == 0)
        {
            var typeNames = new HashSet<string>();
            foreach (var typeDef in typeDefinitions)
            {
                var typeName = typeDef.Type ?? string.Empty;
                var origin = new Origin(typeDef.Metadata?.SourceInfo?.File, typeDef.Metadata?.Module);
                if (!typeNames.Add(typeName))
                {
                    collector.RaiseDuplicateTypeName(typeName, origin, LineNumbers.GetTypeLine(typeName, lines));
                }

                foreach (var relationMetadata in (typeDef.Metadata?.Relations).OrEmpty())
                {
                    CheckDuplicateTypeRestrictions(collector, relationMetadata.Value, relationMetadata.Key, typeName, origin, lines);
                    CheckDuplicatePartials(collector, typeDef, relationMetadata.Key, lines);
                }
            }
        }

        // Next, every relation needs an entry point. Skipped on errors: missing relations would all look unreachable.
        if (errors.Count == 0)
        {
            foreach (var typeDef in typeDefinitions)
            {
                var typeName = typeDef.Type ?? string.Empty;
                foreach (var relation in typeDef.Relations.OrEmpty())
                {
                    var relationMetadata = typeDef.Metadata?.Relations.GetOrNull(relation.Key);
                    var origin = new Origin(
                        relationMetadata?.SourceInfo?.File is { Length: > 0 } relationFile ? relationFile : typeDef.Metadata?.SourceInfo?.File,
                        relationMetadata?.Module is { Length: > 0 } relationModule ? relationModule : typeDef.Metadata?.Module);
                    TrackModule(fileToModules, origin);

                    var currentRelation = typeMap[typeName].Relations.GetOrNull(relation.Key) ?? new Userset();
                    var (hasEntry, loop) = EntryPointAnalyzer.Analyze(typeMap, typeName, relation.Key, currentRelation, new VisitedRelations());
                    if (hasEntry)
                    {
                        continue;
                    }

                    var lineIndex = RelationLine(typeName, relation.Key, lines);
                    if (loop)
                    {
                        collector.RaiseNoEntryPointLoop(relation.Key, typeName, origin, lineIndex);
                    }
                    else
                    {
                        collector.RaiseNoEntryPoint(relation.Key, typeName, origin, lineIndex);
                    }
                }
            }
        }

        foreach (var condition in model.Conditions.OrEmpty())
        {
            TrackModule(fileToModules, new Origin(condition.Value?.Metadata?.SourceInfo?.File, condition.Value?.Metadata?.Module));

            if (condition.Key != condition.Value?.Name)
            {
                collector.RaiseDifferentNestedConditionName(condition.Key, condition.Value?.Name ?? string.Empty);
            }

            if (!usedConditionNames.Contains(condition.Key))
            {
                collector.RaiseUnusedCondition(
                    condition.Key,
                    new Origin(condition.Value?.Metadata?.SourceInfo?.File, condition.Value?.Metadata?.Module),
                    LineNumbers.GetConditionLine(condition.Key, lines));
            }
        }
    }

    private static void RelationDefined(ValidationErrorCollector collector, Dictionary<string, TypeDefinition> typeMap, string type, string relation, Dictionary<string, Condition>? conditions, string[]? lines)
    {
        var root = typeMap[type].Relations.GetOrNull(relation);
        if (root == null)
        {
            return;
        }

        var pending = new Queue<Userset>();
        pending.Enqueue(root);
        while (pending.Count > 0)
        {
            var child = pending.Dequeue();
            if (child.Union?.Child is { Count: > 0 } unionChildren)
            {
                unionChildren.ForEach(pending.Enqueue);
            }
            else if (child.Intersection?.Child is { Count: > 0 } intersectionChildren)
            {
                intersectionChildren.ForEach(pending.Enqueue);
            }
            else if (child.Difference?.Base != null && child.Difference.Subtract != null)
            {
                pending.Enqueue(child.Difference.Base);
                pending.Enqueue(child.Difference.Subtract);
            }
            else
            {
                ChildDefDefined(collector, typeMap, type, relation, RelationTarget.Of(child), conditions, lines);
            }
        }
    }

    private static void ChildDefDefined(ValidationErrorCollector collector, Dictionary<string, TypeDefinition> typeMap, string type, string relation, RelationTarget childDef, Dictionary<string, Condition>? conditions, string[]? lines)
    {
        var relations = typeMap[type].Relations;
        if (relations == null || !relations.ContainsKey(relation))
        {
            return;
        }

        var typeMetadata = typeMap[type].Metadata;
        var relationMetadata = typeMetadata?.Relations.GetOrNull(relation);
        var origin = new Origin(
            relationMetadata?.SourceInfo?.File is { Length: > 0 } relationFile ? relationFile : typeMetadata?.SourceInfo?.File,
            relationMetadata?.Module is { Length: > 0 } relationModule ? relationModule : typeMetadata?.Module);
        var lineIndex = RelationLine(type, relation, lines);

        switch (childDef.Rewrite)
        {
            case RewriteType.Direct:
                {
                    var fromPossibleTypes = TypeRestrictions.ToTexts(relationMetadata?.DirectlyRelatedUserTypes);
                    if (fromPossibleTypes.Count == 0)
                    {
                        collector.RaiseAssignableRelationMustHaveTypes(lineIndex);
                    }

                    foreach (var item in fromPossibleTypes)
                    {
                        var decoded = TypeRestrictions.Decode(item);
                        if (!typeMap.ContainsKey(decoded.Type))
                        {
                            collector.RaiseInvalidType(decoded.Type, type, relation, origin, lineIndex);
                        }

                        if (decoded.Condition != null
                            && decoded.Condition != TypeRestrictions.InlineExpressionCondition
                            && (conditions == null || !conditions.ContainsKey(decoded.Condition)))
                        {
                            collector.RaiseInvalidConditionNameInParameter(decoded.Condition, type, relation, decoded.Condition, origin, lineIndex);
                        }

                        if (decoded.IsWildcard && decoded.Relation != null)
                        {
                            collector.RaiseAssignableTypeWildcardRelation(item, type, relation, origin, lineIndex);
                        }
                        else if (decoded.Relation != null && typeMap.GetOrNull(decoded.Type)?.Relations.GetOrNull(decoded.Relation) == null)
                        {
                            collector.RaiseInvalidTypeRelation($"{decoded.Type}#{decoded.Relation}", decoded.Type, relation, decoded.Relation, type, lineIndex, origin);
                        }
                    }

                    break;
                }

            case RewriteType.ComputedUserset:
                {
                    if (childDef.Target != null && !relations.ContainsKey(childDef.Target))
                    {
                        collector.RaiseInvalidRelationError(childDef.Target, type, relation, relations.Keys, lineIndex, origin);
                    }

                    break;
                }

            case RewriteType.TupleToUserset:
                {
                    if (childDef.From == null || childDef.Target == null)
                    {
                        break;
                    }

                    // `a from b`: b must exist on this type...
                    if (!relations.ContainsKey(childDef.From))
                    {
                        collector.RaiseInvalidTypeRelation($"{childDef.Target} from {childDef.From}", type, relation, childDef.From, type, lineIndex, origin);
                        break;
                    }

                    // ...be directly assignable with plain types only, and at least one of those types must define a.
                    var (fromTypes, isValid) = AllowableTypes(typeMap, type, childDef.From);
                    if (!isValid || fromTypes.Count == 0)
                    {
                        collector.RaiseTupleUsersetRequiresDirect(childDef.From, type, relation, origin, lineIndex);
                        break;
                    }

                    var missingOn = new List<string>();
                    foreach (var item in fromTypes)
                    {
                        var decoded = TypeRestrictions.Decode(item);
                        if (decoded.IsWildcard || decoded.Relation != null)
                        {
                            collector.RaiseTupleUsersetRequiresDirect(childDef.From, type, relation, origin, lineIndex);
                        }
                        else if (typeMap.GetOrNull(decoded.Type)?.Relations.GetOrNull(childDef.Target) == null)
                        {
                            missingOn.Add(decoded.Type);
                        }
                    }

                    if (missingOn.Count == fromTypes.Count)
                    {
                        foreach (var missingType in missingOn)
                        {
                            collector.RaiseInvalidRelationOnTupleset($"{childDef.Target} from {childDef.From}", missingType, type, relation, childDef.Target, childDef.From, lineIndex, origin);
                        }
                    }

                    break;
                }
        }
    }

    /// <summary>The restrictions of a directly assignable, non-rewritten relation, and whether it is a single (non-operator) relation.</summary>
    private static (List<string> Types, bool IsValid) AllowableTypes(Dictionary<string, TypeDefinition> typeMap, string type, string relation)
    {
        var typeDef = typeMap[type];
        var currentRelation = typeDef.Relations.GetOrNull(relation) ?? new Userset();
        var isValid = currentRelation.Union == null && currentRelation.Intersection == null && currentRelation.Difference == null;
        var allowed = isValid && RelationTarget.Of(currentRelation).Rewrite == RewriteType.Direct
            ? TypeRestrictions.ToTexts(typeDef.Metadata?.Relations.GetOrNull(relation)?.DirectlyRelatedUserTypes)
            : [];
        return (allowed, isValid);
    }

    private static void CheckDuplicateTypeRestrictions(ValidationErrorCollector collector, RelationMetadata? relationMetadata, string relationName, string typeName, Origin typeOrigin, string[]? lines)
    {
        var seen = new HashSet<string>();
        foreach (var restriction in (relationMetadata?.DirectlyRelatedUserTypes).OrEmpty())
        {
            var text = TypeRestrictions.ToText(restriction);
            if (!seen.Add(text))
            {
                // JS looks up the restriction's type line (not the owning type) before searching for the relation.
                var lineIndex = LineNumbers.GetRelationLine(relationName, lines, LineNumbers.GetTypeLine(restriction.Type, lines));
                var origin = new Origin(
                    relationMetadata!.SourceInfo?.File is { Length: > 0 } file ? file : typeOrigin.File,
                    relationMetadata.Module is { Length: > 0 } module ? module : typeOrigin.Module);
                collector.RaiseDuplicateTypeRestriction(text, relationName, typeName, origin, lineIndex);
            }
        }
    }

    private static void CheckDuplicatePartials(ValidationErrorCollector collector, TypeDefinition typeDef, string relationName, string[]? lines)
    {
        var relationDef = typeDef.Relations.GetOrNull(relationName);
        if (relationDef == null)
        {
            return;
        }

        var typeName = typeDef.Type ?? string.Empty;
        var origin = new Origin(typeDef.Metadata?.SourceInfo?.File, typeDef.Metadata?.Module);

        foreach (var children in new[] { relationDef.Union?.Child, relationDef.Intersection?.Child })
        {
            var seen = new HashSet<string>();
            foreach (var userset in children.OrEmpty())
            {
                // Direct assignments have no name, and JS skips empty names.
                var name = RelationTarget.Of(userset).DefinitionName(userset);
                if (name is not { Length: > 0 })
                {
                    continue;
                }

                if (!seen.Add(name))
                {
                    collector.RaiseDuplicateType(name, relationName, typeName, origin, RelationLine(typeName, relationName, lines));
                }
            }
        }

        if (relationDef.Difference != null)
        {
            var baseName = RelationTarget.Of(relationDef.Difference.Base ?? new Userset()).DefinitionName(relationDef.Difference.Base ?? new Userset());
            var subtractName = RelationTarget.Of(relationDef.Difference.Subtract ?? new Userset()).DefinitionName(relationDef.Difference.Subtract ?? new Userset());
            if (baseName is { Length: > 0 } && baseName == subtractName)
            {
                collector.RaiseDuplicateType(baseName, relationName, typeName, origin, RelationLine(typeName, relationName, lines));
            }
        }
    }

    private static int? RelationLine(string typeName, string relationName, string[]? lines)
        => LineNumbers.GetRelationLine(relationName, lines, LineNumbers.GetTypeLine(typeName, lines));

    private static void TrackModule(Dictionary<string, List<string>> fileToModules, Origin origin)
    {
        if (origin.File is not { Length: > 0 } file || origin.Module is not { Length: > 0 } module)
        {
            return;
        }

        if (!fileToModules.TryGetValue(file, out var modules))
        {
            modules = [];
            fileToModules[file] = modules;
        }

        if (!modules.Contains(module))
        {
            modules.Add(module);
        }
    }

    private enum RewriteType
    {
        Direct,
        ComputedUserset,
        TupleToUserset,
    }

    /// <summary>What a leaf userset points at (JS getRelationalParserResult).</summary>
    private readonly struct RelationTarget
    {
        private RelationTarget(string? target, string? from, RewriteType rewrite)
        {
            Target = target;
            From = from;
            Rewrite = rewrite;
        }

        public string? Target { get; }

        public string? From { get; }

        public RewriteType Rewrite { get; }

        public static RelationTarget Of(Userset userset)
        {
            string? target;
            string? from = null;
            if (userset.ComputedUserset != null)
            {
                target = NullIfEmpty(userset.ComputedUserset.Relation);
            }
            else
            {
                target = NullIfEmpty(userset.TupleToUserset?.ComputedUserset?.Relation);
                from = NullIfEmpty(userset.TupleToUserset?.Tupleset?.Relation);
            }

            var rewrite = from != null ? RewriteType.TupleToUserset : target != null ? RewriteType.ComputedUserset : RewriteType.Direct;
            return new RelationTarget(target, from, rewrite);
        }

        /// <summary>JS getRelationDefName: <c>a</c> for computed usersets, <c>a from b</c> for tuple-to-usersets.</summary>
        public string? DefinitionName(Userset userset) => Rewrite switch
        {
            RewriteType.ComputedUserset => Target,
            RewriteType.TupleToUserset => $"{Target} from {From}",
            _ => userset.ComputedUserset?.Relation,
        };

        private static string? NullIfEmpty(string? value) => value is { Length: > 0 } ? value : null;
    }
}
```

`src/OpenFga.Language/Validation/ModelValidator.cs`:
```csharp
using System;
using System.Collections.Generic;
using OpenFga.Language.Parsing;
using OpenFga.Sdk.Model;

namespace OpenFga.Language.Validation;

/// <summary>Checks OpenFGA models for semantic errors (undefined types and relations, unreachable relations, naming rules, ...).</summary>
public static class ModelValidator
{
    /// <summary>Parses and validates DSL text.</summary>
    /// <param name="dsl">The model in OpenFGA DSL syntax.</param>
    /// <exception cref="ArgumentNullException"><paramref name="dsl"/> is null.</exception>
    /// <exception cref="DslSyntaxException">The DSL does not parse.</exception>
    /// <exception cref="ModelValidationException">The model is invalid; errors carry source ranges.</exception>
    public static void Validate(string dsl) => Validate(dsl, new ModelValidationOptions());

    /// <summary>Parses and validates DSL text with custom naming rules.</summary>
    /// <param name="dsl">The model in OpenFGA DSL syntax.</param>
    /// <param name="options">The naming rules.</param>
    /// <exception cref="ArgumentNullException"><paramref name="dsl"/> or <paramref name="options"/> is null.</exception>
    /// <exception cref="ArgumentException">A pattern in <paramref name="options"/> is not a valid regular expression.</exception>
    /// <exception cref="DslSyntaxException">The DSL does not parse.</exception>
    /// <exception cref="ModelValidationException">The model is invalid; errors carry source ranges.</exception>
    public static void Validate(string dsl, ModelValidationOptions options)
    {
        if (dsl is null)
        {
            throw new ArgumentNullException(nameof(dsl));
        }

        if (options is null)
        {
            throw new ArgumentNullException(nameof(options));
        }

        var patterns = ValidationPatterns.From(options, nameof(options));
        var result = DslParser.Parse(dsl);
        if (result.Errors.Count > 0)
        {
            throw new DslSyntaxException(result.Errors);
        }

        ThrowIfAny(ModelValidationEngine.Collect(result.Model, patterns, dsl));
    }

    /// <summary>Validates a model.</summary>
    /// <param name="model">The model.</param>
    /// <exception cref="ArgumentNullException"><paramref name="model"/> is null.</exception>
    /// <exception cref="ModelValidationException">The model is invalid; errors have no source ranges.</exception>
    public static void Validate(AuthorizationModel model) => Validate(model, new ModelValidationOptions());

    /// <summary>Validates a model with custom naming rules.</summary>
    /// <param name="model">The model.</param>
    /// <param name="options">The naming rules.</param>
    /// <exception cref="ArgumentNullException"><paramref name="model"/> or <paramref name="options"/> is null.</exception>
    /// <exception cref="ArgumentException">A pattern in <paramref name="options"/> is not a valid regular expression.</exception>
    /// <exception cref="ModelValidationException">The model is invalid; errors have no source ranges.</exception>
    public static void Validate(AuthorizationModel model, ModelValidationOptions options)
    {
        if (model is null)
        {
            throw new ArgumentNullException(nameof(model));
        }

        if (options is null)
        {
            throw new ArgumentNullException(nameof(options));
        }

        ThrowIfAny(ModelValidationEngine.Collect(model, ValidationPatterns.From(options, nameof(options)), null));
    }

    private static void ThrowIfAny(List<ModelValidationError> errors)
    {
        if (errors.Count > 0)
        {
            throw new ModelValidationException(errors);
        }
    }
}
```

- [ ] **Step 6: Record the public API**

Run:
```bash
printf '#nullable enable
' > src/OpenFga.Language/PublicAPI.Unshipped.txt
dotnet format analyzers src/OpenFga.Language/OpenFga.Language.csproj --diagnostics RS0016 --severity info
```
(The RS0016 fixer only fills an API file that has no entries yet, so the file is regenerated each time. Nothing has shipped, so `PublicAPI.Unshipped.txt` holds the whole public surface.)
Expected: `git diff src/OpenFga.Language/PublicAPI.Unshipped.txt` adds entries for `ModelValidationOptions` (type, constructor, both properties' get/set) and the four `ModelValidator.Validate` overloads only.

- [ ] **Step 7: Run the tests to verify they pass**

Run: `dotnet test --filter FullyQualifiedName~ModelValidatorTests`
Expected: all pass. If a semantic fixture fails, run the same case in JS (`cd pkg/js && npx jest tests/validator.test.ts -t "<case name>"`) and diff behaviour. The JS code in `validate-dsl.ts` is the reference, and the port keeps its order of checks and its early returns on `errors.length`.

- [ ] **Step 8: Run the whole suite and commit**

Run: `dotnet test`
Expected: all pass.

```bash
git add pkg/dotnet
git commit -m "feat(dotnet): add semantic model validation

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01U8M7PfSCNGaz27dr4xJPEu"
```

---

### Task 8: `fga.mod` parsing (`ModFile.Parse`)

Port of `pkg/js/transformer/modules/mod-to-json.ts` using YamlDotNet.

**Files:**
- Create: `pkg/dotnet/src/OpenFga.Language/Transformation/ModFile.cs`, `ModFileReader.cs`
- Modify: `pkg/dotnet/src/OpenFga.Language/PublicAPI.Unshipped.txt`
- Test: `pkg/dotnet/test/OpenFga.Language.Tests/ModFileTests.cs`

**Interfaces:**
- Consumes: `LanguageErrors.ModFile`, `ModFileValidationException`, `SourceRange` (Task 2); fixture helpers (Task 3).
- Produces: `public sealed class ModFile { static ModFile Parse(string text); string Schema; IReadOnlyList<string> Contents; }` (namespace `OpenFga.Language`). `Contents` holds the decoded relative file names in file order. `internal static class ModFileReader { static ModFile Read(string text) }` (namespace `OpenFga.Language.Transformation`).

- [ ] **Step 1: Write the failing tests**

`test/OpenFga.Language.Tests/ModFileTests.cs`:
```csharp
using System.Text.Json.Nodes;
using OpenFga.Language.Tests.Fixtures;

namespace OpenFga.Language.Tests;

public class ModFileTests
{
    private static readonly IReadOnlyList<ModFileCase> Cases = TestData.ModFileCases();

    public static TheoryData<int, string> CaseData => TestData.Indexes(Cases, c => c.Name);

    [Theory]
    [MemberData(nameof(CaseData))]
    public void ParsesModFiles(int index, string name)
    {
        _ = name;
        var testCase = Cases[index];

        if (testCase.ExpectedErrors is null)
        {
            var modFile = ModFile.Parse(testCase.ModFile);
            var expected = JsonNode.Parse(testCase.Json!)!;
            Assert.Equal(expected["schema"]!["value"]!.GetValue<string>(), modFile.Schema);
            Assert.Equal(expected["contents"]!["value"]!.AsArray().Select(n => n!["value"]!.GetValue<string>()), modFile.Contents);
            return;
        }

        var exception = Assert.Throws<ModFileValidationException>(() => ModFile.Parse(testCase.ModFile));
        Assert.Equal(ErrorAssert.ExpectedAggregate(testCase.ExpectedErrors, _ => "validation", includeLocation: true), exception.Message);
        ErrorAssert.AllMatch(testCase.ExpectedErrors, exception.Errors);
    }

    [Fact]
    public void NonMappingDocumentReportsMissingFields()
    {
        var exception = Assert.Throws<ModFileValidationException>(() => ModFile.Parse("- core.fga\n"));

        Assert.Equal(["missing schema field", "missing contents field"], exception.Errors.Select(e => e.Message));
    }

    [Fact]
    public void EmptyValuesAreNotStrings()
    {
        var exception = Assert.Throws<ModFileValidationException>(() => ModFile.Parse("schema:\ncontents:\n"));

        Assert.Equal(
            ["unexpected schema type, expected string got value null", "unexpected contents type, expected list of strings got value null"],
            exception.Errors.Select(e => e.Message));
    }

    [Fact]
    public void MalformedYamlThrowsModFileValidationException()
    {
        var exception = Assert.Throws<ModFileValidationException>(() => ModFile.Parse("schema: [\n"));

        Assert.NotNull(Assert.Single(exception.Errors).Range);
    }

    [Fact]
    public void NullThrowsArgumentNullException()
        => Assert.Equal("text", Assert.Throws<ArgumentNullException>(() => ModFile.Parse(null!)).ParamName);
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test --filter FullyQualifiedName~ModFileTests`
Expected: build FAILS with CS0103 (`ModFile` does not exist).

- [ ] **Step 3: Implement `ModFile` and `ModFileReader`**

`src/OpenFga.Language/Transformation/ModFile.cs`:
```csharp
using System;
using System.Collections.Generic;
using OpenFga.Language.Transformation;

namespace OpenFga.Language;

/// <summary>The contents of an <c>fga.mod</c> file, which lists the module files of a modular model.</summary>
public sealed class ModFile
{
    internal ModFile(string schema, IReadOnlyList<string> contents)
    {
        Schema = schema;
        Contents = contents;
    }

    /// <summary>Gets the schema version. Always <c>1.2</c>, the only version <c>fga.mod</c> supports.</summary>
    public string Schema { get; }

    /// <summary>Gets the relative paths of the module files, in file order.</summary>
    public IReadOnlyList<string> Contents { get; }

    /// <summary>Parses and validates the text of an <c>fga.mod</c> file.</summary>
    /// <param name="text">The YAML text of the file.</param>
    /// <returns>The parsed file.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="text"/> is null.</exception>
    /// <exception cref="ModFileValidationException">The file is not valid YAML or does not describe a valid module list.</exception>
    public static ModFile Parse(string text)
    {
        if (text is null)
        {
            throw new ArgumentNullException(nameof(text));
        }

        return ModFileReader.Read(text);
    }
}
```

`src/OpenFga.Language/Transformation/ModFileReader.cs`:
```csharp
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

namespace OpenFga.Language.Transformation;

/// <summary>Reads fga.mod YAML (JS mod-to-json.ts). Messages match JS; YAML syntax errors carry YamlDotNet's text.</summary>
internal static class ModFileReader
{
    private const string SupportedSchema = "1.2";
    private static readonly TimeSpan MatchTimeout = TimeSpan.FromSeconds(1);

    // YAML 1.2 core schema: plain (unquoted) scalars matching these are not strings.
    private static readonly Regex NullScalar = new("^(~|null|Null|NULL)?$", RegexOptions.CultureInvariant, MatchTimeout);
    private static readonly Regex BoolScalar = new("^(true|True|TRUE|false|False|FALSE)$", RegexOptions.CultureInvariant, MatchTimeout);
    private static readonly Regex IntScalar = new("^([-+]?[0-9]+|0o[0-7]+|0x[0-9a-fA-F]+)$", RegexOptions.CultureInvariant, MatchTimeout);
    private static readonly Regex FloatScalar = new(@"^([-+]?(\.[0-9]+|[0-9]+(\.[0-9]*)?)([eE][-+]?[0-9]+)?|[-+]?\.(inf|Inf|INF)|\.(nan|NaN|NAN))$", RegexOptions.CultureInvariant, MatchTimeout);

    public static ModFile Read(string text)
    {
        var root = Load(text);
        var errors = new List<LanguageError>();

        var schemaNode = Get(root, "schema");
        if (schemaNode is null)
        {
            errors.Add(LanguageErrors.ModFile("missing schema field", default));
        }
        else if (!IsString(schemaNode))
        {
            errors.Add(LanguageErrors.ModFile($"unexpected schema type, expected string got value {Describe(schemaNode)}", RangeOf(schemaNode)));
        }
        else if (((YamlScalarNode)schemaNode).Value != SupportedSchema)
        {
            errors.Add(LanguageErrors.ModFile("unsupported schema version, fga.mod only supported in version `1.2`", RangeOf(schemaNode)));
        }

        var files = new List<string>();
        var contentsNode = Get(root, "contents");
        if (contentsNode is null)
        {
            errors.Add(LanguageErrors.ModFile("missing contents field", default));
        }
        else if (contentsNode is not YamlSequenceNode sequence)
        {
            errors.Add(LanguageErrors.ModFile($"unexpected contents type, expected list of strings got value {Describe(contentsNode)}", RangeOf(contentsNode)));
        }
        else
        {
            foreach (var item in sequence.Children)
            {
                if (!IsString(item))
                {
                    errors.Add(LanguageErrors.ModFile($"unexpected contents item type, expected string got value {DescribeItem(item)}", RangeOf(item)));
                    continue;
                }

                var original = ((YamlScalarNode)item).Value ?? string.Empty;
                var fileName = DecodePath(original);

                // Reject directory traversal and absolute paths.
                if (fileName.IndexOf("../", StringComparison.Ordinal) >= 0
                    || fileName.IndexOf("..\\", StringComparison.Ordinal) >= 0
                    || fileName.StartsWith("/", StringComparison.Ordinal))
                {
                    errors.Add(LanguageErrors.ModFile($"invalid contents item {original}", RangeOf(item)));
                    continue;
                }

                if (!fileName.EndsWith(".fga", StringComparison.Ordinal))
                {
                    errors.Add(LanguageErrors.ModFile($"contents items should use fga file extension, got {original}", RangeOf(item)));
                    continue;
                }

                files.Add(fileName);
            }
        }

        if (errors.Count > 0)
        {
            throw new ModFileValidationException(errors);
        }

        return new ModFile(SupportedSchema, new ReadOnlyCollection<string>(files));
    }

    private static YamlMappingNode? Load(string text)
    {
        try
        {
            var stream = new YamlStream();
            stream.Load(new StringReader(text));
            return stream.Documents.Count > 0 ? stream.Documents[0].RootNode as YamlMappingNode : null;
        }
        catch (YamlException ex)
        {
            var range = new SourceRange((int)ex.Start.Line - 1, (int)ex.End.Line - 1, (int)ex.Start.Column - 1, (int)ex.End.Column - 1);
            throw new ModFileValidationException([LanguageErrors.ModFile(ex.Message, range)]);
        }
    }

    private static YamlNode? Get(YamlMappingNode? root, string key)
    {
        if (root is null)
        {
            return null;
        }

        foreach (var entry in root.Children)
        {
            if (entry.Key is YamlScalarNode scalar && scalar.Value == key)
            {
                return entry.Value;
            }
        }

        return null;
    }

    private static bool IsString(YamlNode node)
        => node is YamlScalarNode scalar && !(scalar.Style == ScalarStyle.Plain && IsCoreNonString(scalar.Value ?? string.Empty));

    private static bool IsCoreNonString(string value)
        => NullScalar.IsMatch(value) || BoolScalar.IsMatch(value) || IntScalar.IsMatch(value) || FloatScalar.IsMatch(value);

    /// <summary>Renders a value the way JS template literals print the YAML value (<c>true</c>, <c>1</c>, <c>null</c>, ...).</summary>
    private static string Describe(YamlNode node)
    {
        if (node is not YamlScalarNode scalar)
        {
            return node is YamlMappingNode ? "[object Object]" : node.ToString();
        }

        var value = scalar.Value ?? string.Empty;
        if (scalar.Style != ScalarStyle.Plain)
        {
            return value;
        }

        if (NullScalar.IsMatch(value))
        {
            return "null";
        }

        if (BoolScalar.IsMatch(value))
        {
            return value.ToLowerInvariant();
        }

        if (IntScalar.IsMatch(value) || FloatScalar.IsMatch(value))
        {
            return DescribeNumber(value);
        }

        return value;
    }

    private static string DescribeItem(YamlNode node) => node is YamlScalarNode ? Describe(node) : "undefined";

    private static string DescribeNumber(string value)
    {
        if (value.StartsWith("0x", StringComparison.Ordinal))
        {
            return Convert.ToInt64(value.Substring(2), 16).ToString(CultureInfo.InvariantCulture);
        }

        if (value.StartsWith("0o", StringComparison.Ordinal))
        {
            return Convert.ToInt64(value.Substring(2), 8).ToString(CultureInfo.InvariantCulture);
        }

        var lower = value.ToLowerInvariant();
        if (lower.EndsWith(".nan", StringComparison.Ordinal))
        {
            return "NaN";
        }

        if (lower.EndsWith(".inf", StringComparison.Ordinal))
        {
            return lower.StartsWith("-", StringComparison.Ordinal) ? "-Infinity" : "Infinity";
        }

        return double.Parse(value, NumberStyles.Float, CultureInfo.InvariantCulture).ToString("R", CultureInfo.InvariantCulture);
    }

    // Decode URI-encoded dots and slashes so encoded traversal (%2e%2e%2f) is caught.
    private static string DecodePath(string value)
    {
        var decoded = Regex.Replace(value, "%2e", ".", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, MatchTimeout);
        decoded = Regex.Replace(decoded, "%2f", "/", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, MatchTimeout);
        return Regex.Replace(decoded, "%5c", "\\", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, MatchTimeout);
    }

    // YamlDotNet marks are one-based; the end mark is just past the node.
    private static SourceRange RangeOf(YamlNode node)
        => new((int)node.Start.Line - 1, (int)node.End.Line - 1, (int)node.Start.Column - 1, (int)node.End.Column - 1);
}
```

- [ ] **Step 4: Record the public API and run the tests**

Run:
```bash
printf '#nullable enable
' > src/OpenFga.Language/PublicAPI.Unshipped.txt
dotnet format analyzers src/OpenFga.Language/OpenFga.Language.csproj --diagnostics RS0016 --severity info
```
(The RS0016 fixer only fills an API file that has no entries yet, so the file is regenerated each time. Nothing has shipped, so `PublicAPI.Unshipped.txt` holds the whole public surface.)
Expected: `git diff src/OpenFga.Language/PublicAPI.Unshipped.txt` adds entries for `ModFile`, `ModFile.Parse`, `ModFile.Schema.get`, `ModFile.Contents.get` only.

Run: `dotnet test --filter FullyQualifiedName~ModFileTests`
Expected: all pass. If a fixture fails only on a column `end`, print `node.End` for that node. JS reports the end of the value (`range[1]`), which for a quoted scalar is just past the closing quote.

- [ ] **Step 5: Commit**

```bash
git add pkg/dotnet
git commit -m "feat(dotnet): parse fga.mod files

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01U8M7PfSCNGaz27dr4xJPEu"
```

---

### Task 9: Combine module files into a model (`TransformModulesToModel`)

Port of `pkg/js/transformer/modules/modules-to-model.ts`.

**Files:**
- Create: `pkg/dotnet/src/OpenFga.Language/Transformation/ModuleFile.cs`, `ModuleCombiner.cs`
- Modify: `pkg/dotnet/src/OpenFga.Language/Transformation/ModelTransformer.cs`, `PublicAPI.Unshipped.txt`
- Test: `pkg/dotnet/test/OpenFga.Language.Tests/ModuleTransformerTests.cs`

**Interfaces:**
- Consumes: `DslParser` (Task 3), `ModelValidationEngine.Collect` and `ValidationPatterns.Default` (Task 7), `LineNumbers`, `TextSearch`, `CollectionExtensions` (Task 6), `ModuleTransformationError`/`ModuleTransformationException` (Task 2), `ModuleCase` fixtures (Task 3).
- Produces:
  - `public sealed class ModuleFile(string name, string contents)` with `Name` and `Contents` (namespace `OpenFga.Language`).
  - `ModelTransformer.TransformModulesToModel(IEnumerable<ModuleFile> files, string schemaVersion) -> AuthorizationModel`.
  - `internal static class ModuleCombiner { static AuthorizationModel Combine(IReadOnlyList<ModuleFile> files, string schemaVersion) }`.

- [ ] **Step 1: Write the failing tests**

`test/OpenFga.Language.Tests/ModuleTransformerTests.cs`:
```csharp
using System.Text.RegularExpressions;
using OpenFga.Language.Tests.Fixtures;

namespace OpenFga.Language.Tests;

public class ModuleTransformerTests
{
    private static readonly IReadOnlyList<ModuleCase> Modules = TestData.ModuleCases().Where(c => c.Modules.Count > 0).ToList();

    // Mirrors the JS test: semantic cases rewritten from `model` to `module test`, only for schema 1.1 models.
    private static readonly IReadOnlyList<DslCase> Semantic = TestData.DslSemanticCases()
        .Where(c => !c.Dsl.TrimStart().StartsWith("module", StringComparison.Ordinal) && c.Dsl.Contains("schema 1.1", StringComparison.Ordinal))
        .ToList();

    public static TheoryData<int, string> ModuleData => TestData.Indexes(Modules, c => c.Name);

    public static TheoryData<int, string> SemanticData => TestData.Indexes(Semantic, c => c.Name);

    [Theory]
    [MemberData(nameof(ModuleData))]
    public void CombinesModules(int index, string name)
    {
        _ = name;
        var testCase = Modules[index];
        var files = testCase.Modules.Select(m => new ModuleFile(m.Name, m.Contents)).ToList();

        if (testCase.ExpectedErrors is null)
        {
            JsonAssert.Equivalent(testCase.Json!, ModelTransformer.TransformModulesToModel(files, "1.2").ToJson());
            return;
        }

        var exception = Assert.Throws<ModuleTransformationException>(() => ModelTransformer.TransformModulesToModel(files, "1.2"));
        Assert.Equal(ErrorAssert.ExpectedAggregate(testCase.ExpectedErrors, Kind, includeLocation: true), exception.Message);
        ErrorAssert.AllMatch(testCase.ExpectedErrors, exception.Errors);
    }

    [Theory]
    [MemberData(nameof(SemanticData))]
    public void ReportsSemanticErrorsInModules(int index, string name)
    {
        _ = name;
        var testCase = Semantic[index];
        var dsl = new Regex("model\n {2}schema 1\\.1").Replace(testCase.Dsl, "module test\n", 1);

        // Like the JS test, a case that combines without errors is not a failure: some checks only apply to whole models.
        var exception = Record.Exception(() => ModelTransformer.TransformModulesToModel([new ModuleFile("test.fga", dsl)], "1.2"));
        if (exception is null)
        {
            return;
        }

        var moduleException = Assert.IsType<ModuleTransformationException>(exception);
        var expected = testCase.ExpectedErrors.Select(e => new ExpectedError
        {
            Msg = e.Msg,
            Line = e.Line,
            Column = e.Column,
            Metadata = new ExpectedMetadata { Symbol = e.Metadata?.Symbol, ErrorType = e.Metadata?.ErrorType, Module = "test" },
        }).ToList();
        Assert.Equal(ErrorAssert.ExpectedAggregate(expected, Kind, includeLocation: true), moduleException.Message);
        ErrorAssert.AllMatch(expected, moduleException.Errors);
    }

    [Fact]
    public void AcceptsCustomSchemaVersion()
    {
        var model = ModelTransformer.TransformModulesToModel([new ModuleFile("core.fga", "module core\n  type user")], "1.1");

        Assert.Equal("1.1", model.SchemaVersion);
    }

    [Fact]
    public void NullArgumentsThrow()
    {
        Assert.Equal("files", Assert.Throws<ArgumentNullException>(() => ModelTransformer.TransformModulesToModel(null!, "1.2")).ParamName);
        Assert.Equal("schemaVersion", Assert.Throws<ArgumentNullException>(() => ModelTransformer.TransformModulesToModel([], null!)).ParamName);
        Assert.Equal("files", Assert.Throws<ArgumentException>(() => ModelTransformer.TransformModulesToModel([null!], "1.2")).ParamName);
        Assert.Equal("name", Assert.Throws<ArgumentNullException>(() => new ModuleFile(null!, "")).ParamName);
        Assert.Equal("contents", Assert.Throws<ArgumentNullException>(() => new ModuleFile("a.fga", null!)).ParamName);
    }

    private static string Kind(ExpectedError error)
        => error.Type == "syntax" ? "syntax" : error.Metadata?.ErrorType ?? "transformation-error";
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test --filter FullyQualifiedName~ModuleTransformerTests`
Expected: build FAILS with CS0117 (`ModelTransformer` has no `TransformModulesToModel`).

- [ ] **Step 3: Implement `ModuleFile` and `ModuleCombiner`**

`src/OpenFga.Language/Transformation/ModuleFile.cs`:
```csharp
using System;

namespace OpenFga.Language;

/// <summary>One file of a modular model.</summary>
public sealed class ModuleFile
{
    /// <summary>Initializes a new <see cref="ModuleFile"/>.</summary>
    /// <param name="name">The file name, as listed in <c>fga.mod</c>; used in errors and source information.</param>
    /// <param name="contents">The DSL text of the module.</param>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> or <paramref name="contents"/> is null.</exception>
    public ModuleFile(string name, string contents)
    {
        Name = name ?? throw new ArgumentNullException(nameof(name));
        Contents = contents ?? throw new ArgumentNullException(nameof(contents));
    }

    /// <summary>Gets the file name.</summary>
    public string Name { get; }

    /// <summary>Gets the DSL text of the module.</summary>
    public string Contents { get; }
}
```

`src/OpenFga.Language/Transformation/ModuleCombiner.cs`:
```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using OpenFga.Language.Internal;
using OpenFga.Language.Parsing;
using OpenFga.Language.Validation;
using OpenFga.Sdk.Model;

namespace OpenFga.Language.Transformation;

/// <summary>Combines module files into one model and validates it (JS transformModuleFilesToModel).</summary>
internal static class ModuleCombiner
{
    public static AuthorizationModel Combine(IReadOnlyList<ModuleFile> files, string schemaVersion)
    {
        var typeDefs = new List<TypeDefinition>();
        var extendedTypeDefs = new Dictionary<string, List<TypeDefinition>>();
        var conditions = new Dictionary<string, Condition>();
        var errors = new List<LanguageError>();
        var moduleFiles = new Dictionary<string, string>();
        foreach (var file in files)
        {
            moduleFiles[file.Name] = file.Contents;
        }

        foreach (var file in files)
        {
            var lines = file.Contents.Split('\n');
            var parsed = DslParser.Parse(file.Contents);
            if (parsed.Errors.Count > 0)
            {
                foreach (var error in parsed.Errors)
                {
                    error.File = file.Name;
                    errors.Add(error);
                }

                continue;
            }

            foreach (var typeDef in parsed.Model.TypeDefinitions)
            {
                // Extensions are merged once every file has been read.
                if (parsed.TypeDefExtensions.ContainsKey(typeDef.Type))
                {
                    if (!extendedTypeDefs.TryGetValue(file.Name, out var extended))
                    {
                        extended = [];
                        extendedTypeDefs[file.Name] = extended;
                    }

                    extended.Add(typeDef);
                    continue;
                }

                if (typeDef.Metadata == null)
                {
                    errors.Add(new ModuleTransformationError("file is not a module", file.Name, new SourceRange(0, 0, 0, 0), null));
                    continue;
                }

                typeDef.Metadata.SourceInfo = new SourceInfo { File = file.Name };
                typeDefs.Add(typeDef);
            }

            foreach (var condition in parsed.Model.Conditions.OrEmpty())
            {
                if (conditions.ContainsKey(condition.Key))
                {
                    errors.Add(TransformationError($"duplicate condition {condition.Key}", lines, LineNumbers.GetConditionLine(condition.Key, lines), condition.Key, file.Name));
                    continue;
                }

                condition.Value.Metadata ??= new ConditionMetadata();
                condition.Value.Metadata.SourceInfo = new SourceInfo { File = file.Name };
                conditions[condition.Key] = condition.Value;
            }
        }

        MergeExtensions(typeDefs, extendedTypeDefs, moduleFiles, errors);

        var model = new AuthorizationModel { SchemaVersion = schemaVersion, TypeDefinitions = typeDefs, Conditions = conditions };
        foreach (var error in ModelValidationEngine.Collect(model, ValidationPatterns.Default, null))
        {
            ResolveLocation(error, moduleFiles);
            errors.Add(error);
        }

        if (errors.Count > 0)
        {
            throw new ModuleTransformationException(errors);
        }

        return model;
    }

    private static void MergeExtensions(List<TypeDefinition> typeDefs, Dictionary<string, List<TypeDefinition>> extendedTypeDefs, Dictionary<string, string> moduleFiles, List<LanguageError> errors)
    {
        foreach (var entry in extendedTypeDefs)
        {
            var fileName = entry.Key;
            var lines = moduleFiles.TryGetValue(fileName, out var text) ? text.Split('\n') : null;
            foreach (var typeDef in entry.Value)
            {
                if (typeDef.Relations == null)
                {
                    continue;
                }

                var original = typeDefs.Find(t => t.Type == typeDef.Type);
                if (original == null)
                {
                    errors.Add(TransformationError($"extended type {typeDef.Type} does not exist", lines, LineNumbers.GetTypeLine(typeDef.Type, lines, 0, extension: true), typeDef.Type, fileName));
                    continue;
                }

                var existingRelationNames = (original.Relations?.Keys).OrEmpty().ToList();
                if (existingRelationNames.Count == 0)
                {
                    original.Relations = typeDef.Relations;
                    original.Metadata ??= new Metadata();
                    original.Metadata.Relations = typeDef.Metadata?.Relations;
                    foreach (var relationMetadata in (original.Metadata.Relations?.Values).OrEmpty())
                    {
                        relationMetadata.SourceInfo = new SourceInfo { File = fileName };
                    }

                    continue;
                }

                foreach (var relation in typeDef.Relations)
                {
                    if (existingRelationNames.Contains(relation.Key))
                    {
                        errors.Add(TransformationError($"relation {relation.Key} already exists on type {typeDef.Type}", lines, LineNumbers.GetRelationLine(relation.Key, lines), relation.Key, fileName));
                        continue;
                    }

                    var metadata = typeDef.Metadata?.Relations.GetOrNull(relation.Key);
                    if (metadata == null)
                    {
                        errors.Add(new ModuleTransformationError($"unable to find relation metadata for {relation.Key}", null, null, null));
                        continue;
                    }

                    metadata.SourceInfo = new SourceInfo { File = fileName };
                    original.Relations![relation.Key] = relation.Value;
                    original.Metadata ??= new Metadata();
                    original.Metadata.Relations ??= [];
                    original.Metadata.Relations[relation.Key] = metadata;
                }
            }
        }
    }

    /// <summary>Points a validation error of the combined model back into its module file.</summary>
    private static void ResolveLocation(ModelValidationError error, Dictionary<string, string> moduleFiles)
    {
        if (error.File is not { Length: > 0 } file
            || error.Module is not { Length: > 0 }
            || error.Symbol is not { Length: > 0 } symbol
            || !moduleFiles.TryGetValue(file, out var text))
        {
            return;
        }

        var lines = text.Split('\n');
        var lineIndex = ResolveLineIndex(error, symbol, lines);
        if (lineIndex < 0)
        {
            return;
        }

        var wordIndex = error.ErrorType switch
        {
            ValidationErrorType.InvalidType => TextSearch.ResolveAfterColon(lines[lineIndex], symbol, -1),
            ValidationErrorType.TuplesetNotDirect => TextSearch.ResolveAfterFrom(lines[lineIndex], symbol),
            _ => TextSearch.FindWord(lines[lineIndex], symbol),
        };
        wordIndex = Math.Max(wordIndex, 0);
        error.Range = new SourceRange(lineIndex, lineIndex, wordIndex, wordIndex + symbol.Length);
    }

    private static int ResolveLineIndex(ModelValidationError error, string symbol, string[] lines)
    {
        switch (error.ErrorType)
        {
            case ValidationErrorType.ConditionNotUsed:
                return LineNumbers.GetConditionLine(symbol, lines) ?? -1;
            case ValidationErrorType.ReservedTypeKeywords:
                return LineNumbers.GetTypeLine(symbol, lines) ?? -1;
            case ValidationErrorType.InvalidName:
                // Only the message distinguishes a relation name from a type name.
                return (error.Message.StartsWith("relation", StringComparison.Ordinal)
                    ? LineNumbers.GetRelationLine(symbol, lines)
                    : LineNumbers.GetTypeLine(symbol, lines)) ?? -1;
            case ValidationErrorType.ReservedRelationKeywords:
                return LineNumbers.GetRelationLine(symbol, lines) ?? -1;
            case ValidationErrorType.InvalidRelationType:
                return LineNumbers.GetRelationLine(error.RelationName ?? string.Empty, lines, TypeOrExtensionLine(error.OffendingType ?? string.Empty, lines)) ?? -1;
            case ValidationErrorType.InvalidRelationOnTupleset:
            case ValidationErrorType.MissingDefinition:
            case ValidationErrorType.InvalidType:
            case ValidationErrorType.ConditionNotDefined:
            case ValidationErrorType.TuplesetNotDirect:
            case ValidationErrorType.TypeRestrictionCannotHaveWildcardAndRelation:
            case ValidationErrorType.RelationNoEntrypoint:
            case ValidationErrorType.DuplicatedError:
                var typeIndex = TypeOrExtensionLine(error.TypeName ?? string.Empty, lines);
                return error.RelationName is { Length: > 0 } relation
                    ? LineNumbers.GetRelationLine(relation, lines, typeIndex) ?? -1
                    : typeIndex;
            default:
                return -1;
        }
    }

    private static int TypeOrExtensionLine(string typeName, string[] lines)
    {
        var index = LineNumbers.GetTypeLine(typeName, lines) ?? -1;
        return index == -1 ? LineNumbers.GetTypeLine(typeName, lines, null, extension: true) ?? -1 : index;
    }

    /// <summary>JS constructTransformationError: without a usable line the error keeps no file or range.</summary>
    private static ModuleTransformationError TransformationError(string message, string[]? lines, int? lineIndex, string symbol, string file)
    {
        if (lines is not { Length: > 0 } || lineIndex is not int index || index < 0 || index >= lines.Length)
        {
            return new ModuleTransformationError(message, null, null, symbol);
        }

        var wordIndex = TextSearch.FindWordOrZero(lines[index], symbol);
        return new ModuleTransformationError(message, file, new SourceRange(index, index, wordIndex, wordIndex + symbol.Length), symbol);
    }
}
```

- [ ] **Step 4: Add the public entry point**

Add `using System.Collections.Generic;` and `using System.Linq;` to `Transformation/ModelTransformer.cs`, then append to the class:
```csharp
    /// <summary>Combines the files of a modular model into one validated model.</summary>
    /// <param name="files">The module files, typically those listed by <see cref="ModFile.Contents"/>.</param>
    /// <param name="schemaVersion">The schema version of the combined model, typically <see cref="ModFile.Schema"/>.</param>
    /// <returns>The combined model, with <c>source_info</c> metadata naming each definition's file.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="files"/> or <paramref name="schemaVersion"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="files"/> contains null.</exception>
    /// <exception cref="ModuleTransformationException">A file does not parse, definitions conflict, or the combined model is invalid.</exception>
    public static AuthorizationModel TransformModulesToModel(IEnumerable<ModuleFile> files, string schemaVersion)
    {
        if (files is null)
        {
            throw new ArgumentNullException(nameof(files));
        }

        if (schemaVersion is null)
        {
            throw new ArgumentNullException(nameof(schemaVersion));
        }

        var fileList = files.ToList();
        if (fileList.Any(f => f is null))
        {
            throw new ArgumentException("The collection contains a null module file.", nameof(files));
        }

        return ModuleCombiner.Combine(fileList, schemaVersion);
    }
```

- [ ] **Step 5: Record the public API and run the tests**

Run:
```bash
printf '#nullable enable
' > src/OpenFga.Language/PublicAPI.Unshipped.txt
dotnet format analyzers src/OpenFga.Language/OpenFga.Language.csproj --diagnostics RS0016 --severity info
```
(The RS0016 fixer only fills an API file that has no entries yet, so the file is regenerated each time. Nothing has shipped, so `PublicAPI.Unshipped.txt` holds the whole public surface.)
Expected: `git diff src/OpenFga.Language/PublicAPI.Unshipped.txt` adds entries for `ModuleFile` (type, constructor, `Name.get`, `Contents.get`) and `ModelTransformer.TransformModulesToModel` only.

Run: `dotnet test --filter FullyQualifiedName~ModuleTransformerTests`
Expected: all pass. The fixture test fails if the error order differs from JS. JS reads files in the order given, and the fixture loader sorts file names ordinally, as `readdirSync` does on the CI filesystem.

- [ ] **Step 6: Run the whole suite and commit**

Run: `dotnet test`
Expected: all pass.

```bash
git add pkg/dotnet
git commit -m "feat(dotnet): combine module files into an authorization model

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01U8M7PfSCNGaz27dr4xJPEu"
```

---

### Task 10: `ModelUtilities`

Port of `getModuleForObjectTypeRelation` and `isRelationAssignable` from `pkg/js/util/model_utils.ts`.

**Files:**
- Create: `pkg/dotnet/src/OpenFga.Language/Utilities/ModelUtilities.cs`
- Modify: `pkg/dotnet/src/OpenFga.Language/PublicAPI.Unshipped.txt`
- Test: `pkg/dotnet/test/OpenFga.Language.Tests/ModelUtilitiesTests.cs`

**Interfaces:**
- Consumes: `DictionaryExtensions.GetOrNull`, `CollectionExtensions.OrEmpty` (Tasks 3, 6).
- Produces: `public static class ModelUtilities` (namespace `OpenFga.Language.Utilities`) with `bool IsRelationAssignable(Userset relationDefinition)` and `string? GetModuleForObjectTypeRelation(TypeDefinition typeDefinition, string relation)`.

- [ ] **Step 1: Write the failing tests**

`test/OpenFga.Language.Tests/ModelUtilitiesTests.cs`:
```csharp
using OpenFga.Language.Utilities;
using OpenFga.Sdk.Model;

namespace OpenFga.Language.Tests;

public class ModelUtilitiesTests
{
    private static readonly TypeDefinition WithModule = new()
    {
        Type = "type1",
        Relations = new() { ["relation1"] = new(), ["relation2"] = new(), ["relation3"] = new(), ["relation4"] = new() },
        Metadata = new Metadata
        {
            Module = "type_module1",
            Relations = new()
            {
                ["relation1"] = new RelationMetadata { Module = "module1" },
                ["relation2"] = new RelationMetadata { Module = "" },
                ["relation3"] = new RelationMetadata(),
                ["relation5"] = new RelationMetadata(),
            },
        },
    };

    private static readonly TypeDefinition WithoutModule = new() { Type = "type2", Relations = new() { ["relation7"] = new() } };

    [Theory]
    [InlineData("relation1", "module1")]
    [InlineData("relation2", "type_module1")]
    [InlineData("relation3", "type_module1")]
    [InlineData("relation4", "type_module1")]
    public void GetsModuleForRelation(string relation, string expected)
        => Assert.Equal(expected, ModelUtilities.GetModuleForObjectTypeRelation(WithModule, relation));

    [Fact]
    public void ReturnsNullWhenNeitherRelationNorTypeHasAModule()
        => Assert.Null(ModelUtilities.GetModuleForObjectTypeRelation(WithoutModule, "relation7"));

    [Theory]
    [InlineData("relation5")]
    [InlineData("relation6")]
    public void MissingRelationThrowsArgumentException(string relation)
    {
        var exception = Assert.Throws<ArgumentException>(() => ModelUtilities.GetModuleForObjectTypeRelation(WithModule, relation));

        Assert.StartsWith($"relation {relation} does not exist in type type1", exception.Message, StringComparison.Ordinal);
        Assert.Equal("relation", exception.ParamName);
    }

    [Fact]
    public void DetectsAssignableRelations()
    {
        Assert.True(ModelUtilities.IsRelationAssignable(new Userset { This = new object() }));
        Assert.True(ModelUtilities.IsRelationAssignable(new Userset { Union = new Usersets { Child = [new Userset { This = new object() }] } }));
        Assert.True(ModelUtilities.IsRelationAssignable(new Userset { Intersection = new Usersets { Child = [new Userset { This = new object() }] } }));
        Assert.True(ModelUtilities.IsRelationAssignable(new Userset { Difference = new Difference { Base = new Userset { This = new object() }, Subtract = new Userset() } }));
        Assert.True(ModelUtilities.IsRelationAssignable(new Userset { Difference = new Difference { Base = new Userset(), Subtract = new Userset { This = new object() } } }));
        Assert.False(ModelUtilities.IsRelationAssignable(new Userset { Union = new Usersets { Child = [new Userset { Intersection = new Usersets { Child = [new Userset()] } }] } }));
        Assert.False(ModelUtilities.IsRelationAssignable(new Userset { ComputedUserset = new ObjectRelation { Relation = "a" } }));
    }

    [Fact]
    public void NullArgumentsThrowArgumentNullException()
    {
        Assert.Equal("relationDefinition", Assert.Throws<ArgumentNullException>(() => ModelUtilities.IsRelationAssignable(null!)).ParamName);
        Assert.Equal("typeDefinition", Assert.Throws<ArgumentNullException>(() => ModelUtilities.GetModuleForObjectTypeRelation(null!, "a")).ParamName);
        Assert.Equal("relation", Assert.Throws<ArgumentNullException>(() => ModelUtilities.GetModuleForObjectTypeRelation(WithModule, null!)).ParamName);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test --filter FullyQualifiedName~ModelUtilitiesTests`
Expected: build FAILS with CS0234 (`OpenFga.Language.Utilities` does not exist).

- [ ] **Step 3: Implement `ModelUtilities`**

`src/OpenFga.Language/Utilities/ModelUtilities.cs`:
```csharp
using System;
using System.Linq;
using OpenFga.Language.Internal;
using OpenFga.Sdk.Model;

namespace OpenFga.Language.Utilities;

/// <summary>Helpers for inspecting authorization model definitions.</summary>
public static class ModelUtilities
{
    /// <summary>Determines whether tuples can be written directly for a relation, i.e. its definition contains a direct assignment.</summary>
    /// <param name="relationDefinition">The relation's rewrite definition.</param>
    /// <returns><see langword="true"/> if the definition, or any union, intersection or difference operand, is a direct assignment.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="relationDefinition"/> is null.</exception>
    public static bool IsRelationAssignable(Userset relationDefinition)
    {
        if (relationDefinition is null)
        {
            throw new ArgumentNullException(nameof(relationDefinition));
        }

        return IsAssignable(relationDefinition);
    }

    /// <summary>Gets the module that defines a relation: the relation's own module when it was added by <c>extend type</c>, otherwise the type's.</summary>
    /// <param name="typeDefinition">The type that holds the relation.</param>
    /// <param name="relation">The relation name.</param>
    /// <returns>The module name, or <see langword="null"/> when the model is not modular.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="typeDefinition"/> or <paramref name="relation"/> is null.</exception>
    /// <exception cref="ArgumentException">The type has no relation named <paramref name="relation"/>.</exception>
    public static string? GetModuleForObjectTypeRelation(TypeDefinition typeDefinition, string relation)
    {
        if (typeDefinition is null)
        {
            throw new ArgumentNullException(nameof(typeDefinition));
        }

        if (relation is null)
        {
            throw new ArgumentNullException(nameof(relation));
        }

        if (typeDefinition.Relations?.ContainsKey(relation) != true)
        {
            throw new ArgumentException($"relation {relation} does not exist in type {typeDefinition.Type}", nameof(relation));
        }

        var relationModule = typeDefinition.Metadata?.Relations.GetOrNull(relation)?.Module;
        if (relationModule is { Length: > 0 })
        {
            return relationModule;
        }

        return typeDefinition.Metadata?.Module is { Length: > 0 } typeModule ? typeModule : null;
    }

    // Computed usersets and tuple-to-usersets are not assignable.
    private static bool IsAssignable(Userset? userset)
        => userset != null
            && (userset.This != null
                || (userset.Union?.Child).OrEmpty().Any(IsAssignable)
                || (userset.Intersection?.Child).OrEmpty().Any(IsAssignable)
                || (userset.Difference != null && (IsAssignable(userset.Difference.Base) || IsAssignable(userset.Difference.Subtract))));
}
```

- [ ] **Step 4: Record the public API, run the tests, commit**

Run:
```bash
printf '#nullable enable
' > src/OpenFga.Language/PublicAPI.Unshipped.txt
dotnet format analyzers src/OpenFga.Language/OpenFga.Language.csproj --diagnostics RS0016 --severity info
```
(The RS0016 fixer only fills an API file that has no entries yet, so the file is regenerated each time. Nothing has shipped, so `PublicAPI.Unshipped.txt` holds the whole public surface.)
Expected: `git diff src/OpenFga.Language/PublicAPI.Unshipped.txt` adds entries for `OpenFga.Language.Utilities.ModelUtilities` and its two methods only.

Run: `dotnet test`
Expected: all pass.

```bash
git add pkg/dotnet
git commit -m "feat(dotnet): add model utilities

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01U8M7PfSCNGaz27dr4xJPEu"
```

---

### Task 11: Repository integration, CI, release and docs

**Files:**
- Modify: `Makefile`, `.gitattributes`, `release-please-config.json`, `.release-please-manifest.json`, `AGENTS.md`, `README.md`, `pkg/dotnet/.gitignore`, `pkg/dotnet/README.md`
- Create: `pkg/dotnet/CHANGELOG.md`, `pkg/dotnet/version.txt`
- Create: `.github/workflows/pkg-dotnet-pr.yaml`, `.github/workflows/pkg-dotnet-build.yaml`, `.github/workflows/pkg-dotnet-release.yaml`

**Interfaces:**
- Consumes: everything above.
- Produces: root `make build|test|lint|antlr-gen` include .NET; CI workflow `Test (.NET)`; release on tags `pkg/dotnet/v*`.

- [ ] **Step 1: Wire .NET into the aggregate make targets**

In the repo-root `Makefile`, change the four aggregate targets to:
```make
antlr-gen: antlr-gen-go antlr-gen-js antlr-gen-java antlr-gen-dotnet
```
```make
build: build-go build-js build-java build-dotnet
```
```make
test: test-go test-js test-java test-dotnet
```
```make
lint: lint-go lint-js lint-java lint-dotnet lint-tests
```

Append to `.gitattributes`:
```
pkg/dotnet/src/OpenFga.Language/Gen/* linguist-generated=true
```

Append to `pkg/dotnet/.gitignore`:
```
artifacts/
```

- [ ] **Step 2: Add the CI workflows**

`.github/workflows/pkg-dotnet-pr.yaml`:
```yaml
name: PR (.NET)
permissions:
  contents: read

on:
  workflow_dispatch:
  merge_group:
    types: [checks_requested]
  push:
    branches:
      - main
  pull_request:
    paths:
      - "pkg/dotnet/**"
      - "OpenFGAParser.g4"
      - "OpenFGALexer.g4"
      - "tests/**"
      - ".github/workflows/pkg-dotnet-pr.yaml"
      - ".github/workflows/pkg-dotnet-build.yaml"

jobs:
  test:
    uses: ./.github/workflows/pkg-dotnet-build.yaml
    secrets: inherit
```

`.github/workflows/pkg-dotnet-build.yaml`:
```yaml
name: Test (.NET)

on:
  workflow_call:

permissions:
  contents: read

jobs:
  lint:
    name: Lint .NET
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@3d3c42e5aac5ba805825da76410c181273ba90b1 # v7.0.1
        with:
          fetch-depth: 0

      - name: Set up Docker Buildx
        uses: docker/setup-buildx-action@37fe631027851001ddb9b187196cc803df7f5f0e # v4.3.0

      - name: Set up .NET
        uses: actions/setup-dotnet@a98b56852c35b8e3190ac28c8c2271da59106c68 # v6.0.0
        with:
          dotnet-version: "10.0.x"

      - name: Lint .NET source
        run: make lint-dotnet

  check-gen:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@3d3c42e5aac5ba805825da76410c181273ba90b1 # v7.0.1

      - name: Generate Antlr code and validate up to date
        run: |
          make antlr-gen-dotnet
          git diff --text --exit-code ./pkg/dotnet/src/OpenFga.Language/Gen

  test:
    name: Test .NET
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@3d3c42e5aac5ba805825da76410c181273ba90b1 # v7.0.1
        with:
          fetch-depth: 0

      - name: Set up Docker Buildx
        uses: docker/setup-buildx-action@37fe631027851001ddb9b187196cc803df7f5f0e # v4.3.0

      - name: Set up .NET
        uses: actions/setup-dotnet@a98b56852c35b8e3190ac28c8c2271da59106c68 # v6.0.0
        with:
          dotnet-version: "10.0.x"

      - name: Test
        run: make test-dotnet

  audit:
    name: Audit .NET dependencies
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@3d3c42e5aac5ba805825da76410c181273ba90b1 # v7.0.1

      - name: Set up Docker Buildx
        uses: docker/setup-buildx-action@37fe631027851001ddb9b187196cc803df7f5f0e # v4.3.0

      - name: Set up .NET
        uses: actions/setup-dotnet@a98b56852c35b8e3190ac28c8c2271da59106c68 # v6.0.0
        with:
          dotnet-version: "10.0.x"

      - name: Audit
        run: make audit-dotnet
```

`.github/workflows/pkg-dotnet-release.yaml` (NuGet trusted publishing through OIDC; the `NUGET_USER` secret holds the nuget.org account that owns the trusted-publishing policy):
```yaml
name: Release (.NET)

on:
  workflow_dispatch:
  push:
    tags:
      - "pkg/dotnet/v*"

jobs:
  test:
    permissions:
      contents: read
    uses: ./.github/workflows/pkg-dotnet-build.yaml
    secrets: inherit

  publish:
    runs-on: ubuntu-latest
    needs: [test]
    permissions:
      contents: read
      id-token: write # NuGet trusted publishing (OIDC)
    defaults:
      run:
        working-directory: "pkg/dotnet"

    steps:
      - uses: actions/checkout@3d3c42e5aac5ba805825da76410c181273ba90b1 # v7.0.1
        with:
          fetch-depth: 0

      - name: Set up .NET
        uses: actions/setup-dotnet@a98b56852c35b8e3190ac28c8c2271da59106c68 # v6.0.0
        with:
          dotnet-version: "10.0.x"

      - name: Pack
        run: dotnet pack src/OpenFga.Language/OpenFga.Language.csproj -c Release -o artifacts

      - name: NuGet login
        id: login
        uses: NuGet/login@8d196754b4036150537f80ac539e15c2f1028841 # v1.2.0
        with:
          user: ${{ secrets.NUGET_USER }}

      - name: Publish package
        run: dotnet nuget push "artifacts/*.nupkg" --api-key "${{ steps.login.outputs.NUGET_API_KEY }}" --source https://api.nuget.org/v3/index.json --skip-duplicate

  # The release-please post-release job already created a draft GitHub release when it pushed this tag.
  # Publish (undraft) it only after the NuGet publish succeeded.
  finalize-release:
    runs-on: ubuntu-latest
    needs: [publish]
    if: startsWith(github.ref, 'refs/tags/')
    permissions:
      contents: write # publish (undraft) the github release
    steps:
      - name: Publish the draft GitHub release
        env:
          GH_TOKEN: ${{ secrets.GITHUB_TOKEN }}
          REPO: ${{ github.repository }}
          TAG: ${{ github.ref_name }}
        run: |
          set -euo pipefail
          for _ in $(seq 1 10); do
            if gh release view "$TAG" --repo "$REPO" >/dev/null 2>&1; then
              break
            fi
            echo "Waiting for draft release $TAG ..."
            sleep 6
          done
          gh release edit "$TAG" --repo "$REPO" --draft=false
```

- [ ] **Step 3: Configure release-please**

In `release-please-config.json`, add to `packages` (after `pkg/java`):
```json
    "pkg/dotnet": {
      "release-type": "simple",
      "component": "pkg/dotnet",
      "package-name": "OpenFga.Language",
      "extra-files": ["src/OpenFga.Language/OpenFga.Language.csproj"]
    }
```

In `.release-please-manifest.json`, add `"pkg/dotnet": "0.0.0"`. The first `feat` release then becomes `0.1.0` (`bump-minor-pre-major`).

`pkg/dotnet/version.txt`:
```
0.0.0
```

`pkg/dotnet/CHANGELOG.md`:
```markdown
# Changelog
```

- [ ] **Step 4: Write the package README**

`pkg/dotnet/README.md`:
````markdown
# OpenFGA Language - .NET

[![NuGet](https://img.shields.io/nuget/v/OpenFga.Language?style=flat-square&label=OpenFga.Language)](https://www.nuget.org/packages/OpenFga.Language)

Parse, transform and validate [OpenFGA](https://openfga.dev) authorization models in .NET. Models use the
[`OpenFga.Sdk`](https://www.nuget.org/packages/OpenFga.Sdk) types, so a transformed model can be written with `OpenFgaClient`.

Targets .NET Standard 2.0 (.NET Framework 4.6.1+, .NET 8+).

## Installation

```bash
dotnet add package OpenFga.Language
```

## Usage

### DSL to model and JSON

```csharp
using OpenFga.Language;

var model = ModelTransformer.TransformDslToModel(dsl);   // OpenFga.Sdk.Model.AuthorizationModel
var json = ModelTransformer.TransformDslToJson(dsl);
```

### Model or JSON to DSL

```csharp
var dsl = ModelTransformer.TransformModelToDsl(model);
var dslFromJson = ModelTransformer.TransformJsonToDsl(json);
```

### Modular models

```csharp
var modFile = ModFile.Parse(File.ReadAllText("fga.mod"));
var modules = modFile.Contents.Select(path => new ModuleFile(path, File.ReadAllText(path)));
var model = ModelTransformer.TransformModulesToModel(modules, modFile.Schema);
```

### Validation

```csharp
using OpenFga.Language.Validation;

try
{
    ModelValidator.Validate(dsl);
}
catch (ModelValidationException ex)
{
    foreach (var error in ex.Errors)
    {
        // error.ErrorType, error.Message, error.Range?.StartLine (zero-based), ...
    }
}

TupleSyntax.IsValidObject("document:budget"); // true
```

All problems in the input are reported as exceptions deriving from `OpenFgaLanguageException`, each exposing an
`Errors` list with zero-based source ranges.

## License

Apache-2.0
````

- [ ] **Step 5: Update the repository docs**

In `README.md`, add a row to the Packages table after the Java row:
```markdown
| .NET     | [![OpenFga.Language on NuGet](https://img.shields.io/nuget/v/OpenFga.Language?style=flat-square&label=OpenFga.Language)](https://www.nuget.org/packages/OpenFga.Language)                                                    | [README](/pkg/dotnet/README.md) |
```
In the feature table, add a `.NET` column with these values, in row order: Transformer ✅, Syntactic Model Validations ✅, Schema 1.0 ❌, Schema 1.1 ✅, Schema 1.2 (Modular Models) ✅, Semantic Model Validations ✅, Graphing & Utility Methods ❌. Keep the Markdown table columns aligned by hand.

In `AGENTS.md`:
- Under **Published packages**, add ``- NuGet: `OpenFga.Language` ``.
- Change the first line of Build Commands' comment to `# Build all packages (Go, JS, Java, .NET)`.
- Add a section after "Java Package":
  ````markdown
  ### .NET Package (`pkg/dotnet/`)

  ```bash
  make test-dotnet    # Run xUnit tests
  make lint-dotnet    # dotnet format check + analyzers
  make audit-dotnet   # Check for vulnerable NuGet packages

  # Run specific test
  cd pkg/dotnet && dotnet test --filter FullyQualifiedName~ModelValidatorTests
  ```
  ````
- In the Architecture tree, add `│   └── dotnet/                          # .NET implementation (src/OpenFga.Language, Gen/ = generated ANTLR parser)` and change the `java/` line's prefix from `└──` to `├──`.
- In the Feature parity table, add a `.NET` column: Transformer ✓, Syntactic ✓, Semantic ✓, Graph ✗.

- [ ] **Step 6: Verify the whole package end to end**

Run (repo root): `make lint-dotnet && make test-dotnet && make audit-dotnet`
Expected: every target exits 0. `lint-dotnet` reports no formatting changes; tests all pass; audit reports no vulnerable packages.

Run (in `pkg/dotnet`): `dotnet pack src/OpenFga.Language/OpenFga.Language.csproj -c Release -o artifacts && unzip -l artifacts/OpenFga.Language.0.1.0.nupkg`
Expected: the listing contains `lib/netstandard2.0/OpenFga.Language.dll`, `lib/netstandard2.0/OpenFga.Language.xml` and `README.md`, and no `Gen` sources. `artifacts/OpenFga.Language.0.1.0.snupkg` exists.

Run: `unzip -p artifacts/OpenFga.Language.0.1.0.nupkg OpenFga.Language.nuspec`
Expected: dependencies on `OpenFga.Sdk` 0.10.4, `Antlr4.Runtime.Standard` 4.13.1 and `YamlDotNet` 18.1.0 for `.NETStandard2.0`; **no** `Microsoft.CodeAnalysis.PublicApiAnalyzers`; `<license type="expression">Apache-2.0</license>`.

Run: `grep -c "^[^#]" src/OpenFga.Language/PublicAPI.Unshipped.txt && grep -i "antlr\|internal\|Parsing\|Internal\." src/OpenFga.Language/PublicAPI.Unshipped.txt`
Expected: the first command prints the number of public API entries; the second prints nothing (no generated or internal types leaked).

- [ ] **Step 7: Commit**

```bash
git add Makefile .gitattributes .github/workflows/pkg-dotnet-*.yaml release-please-config.json .release-please-manifest.json AGENTS.md README.md pkg/dotnet
git commit -m "ci(dotnet): add .NET build, release and docs

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01U8M7PfSCNGaz27dr4xJPEu"
```
