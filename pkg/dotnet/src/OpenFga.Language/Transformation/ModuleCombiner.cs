using System;
using System.Collections.Generic;
using System.Linq;
using OpenFga.Language.Errors;
using OpenFga.Language.Parsing;
using OpenFga.Language.Utilities;
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
