using System;
using System.Collections.Generic;
using OpenFga.Language.Errors;
using OpenFga.Language.Utilities;

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
