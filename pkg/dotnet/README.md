# OpenFGA Language - .NET

[![NuGet](https://img.shields.io/nuget/v/OpenFga.Language?style=flat-square&label=OpenFga.Language)](https://www.nuget.org/packages/OpenFga.Language)

Parse, transform and validate [OpenFGA](https://openfga.dev) authorization models in .NET. Models use the
[`OpenFga.Sdk`](https://www.nuget.org/packages/OpenFga.Sdk) types, so a transformed model can be written with `OpenFgaClient`.

Targets .NET Standard 2.0 (.NET Framework 4.7.2+, .NET 8+).

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
var modPath = "fga.mod";
var modFile = ModFile.Parse(File.ReadAllText(modPath));
var modules = modFile.Contents.Select(path => new ModuleFile(path, File.ReadAllText(Path.Combine(Path.GetDirectoryName(modPath)!, path))));
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

All problems in the input are reported as exceptions deriving from `OpenFgaLanguageException`. `DslTransformationException`
carries only a message; the other exceptions (`DslSyntaxException`, `ModelValidationException`, `ModFileValidationException`,
...) each expose an `Errors` list with zero-based source ranges.

## License

Apache-2.0
