using System.Collections.Generic;
using System.Linq;
using OpenFga.Language.Errors;
using OpenFga.Language.Utilities;
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
            if (typeDef is null)
            {
                continue;
            }

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

        var typeDefinitions = model.TypeDefinitions.OrEmpty().Where(t => t != null).ToList();
        var typeMap = new Dictionary<string, TypeDefinition>();
        var usedConditionNames = new HashSet<string>();
        foreach (var typeDef in typeDefinitions)
        {
            typeMap[typeDef.Type ?? string.Empty] = typeDef;
            foreach (var relationMetadata in (typeDef.Metadata?.Relations?.Values).OrEmpty())
            {
                foreach (var restriction in (relationMetadata?.DirectlyRelatedUserTypes).OrEmpty())
                {
                    if (restriction?.Condition is { Length: > 0 })
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
            if (child is null)
            {
                continue;
            }

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
                    var fromPossibleTypes = (relationMetadata?.DirectlyRelatedUserTypes).OrEmpty().Where(r => r != null).ToList();
                    if (fromPossibleTypes.Count == 0)
                    {
                        collector.RaiseAssignableRelationMustHaveTypes(lineIndex);
                    }

                    foreach (var item in fromPossibleTypes)
                    {
                        var itemType = item.Type ?? string.Empty;
                        var itemRelation = item.Relation is { Length: > 0 } ? item.Relation : null;
                        var itemCondition = item.Condition is { Length: > 0 } ? item.Condition : null;
                        if (!typeMap.ContainsKey(itemType))
                        {
                            collector.RaiseInvalidType(itemType, type, relation, origin, lineIndex);
                        }

                        if (itemCondition != null
                            && itemCondition != TypeRestrictions.InlineExpressionCondition
                            && (conditions == null || !conditions.ContainsKey(itemCondition)))
                        {
                            collector.RaiseInvalidConditionNameInParameter(itemCondition, type, relation, itemCondition, origin, lineIndex);
                        }

                        if (item.Wildcard != null && itemRelation != null)
                        {
                            collector.RaiseAssignableTypeWildcardRelation(TypeRestrictions.ToText(item), type, relation, origin, lineIndex);
                        }
                        else if (itemRelation != null && typeMap.GetOrNull(itemType)?.Relations.GetOrNull(itemRelation) == null)
                        {
                            collector.RaiseInvalidTypeRelation($"{itemType}#{itemRelation}", itemType, relation, itemRelation, type, lineIndex, origin);
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
                        if (item.Wildcard != null || item.Relation is { Length: > 0 })
                        {
                            collector.RaiseTupleUsersetRequiresDirect(childDef.From, type, relation, origin, lineIndex);
                        }
                        else if (typeMap.GetOrNull(item.Type ?? string.Empty)?.Relations.GetOrNull(childDef.Target) == null)
                        {
                            missingOn.Add(item.Type ?? string.Empty);
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
    private static (List<RelationReference> Types, bool IsValid) AllowableTypes(Dictionary<string, TypeDefinition> typeMap, string type, string relation)
    {
        var typeDef = typeMap[type];
        var currentRelation = typeDef.Relations.GetOrNull(relation) ?? new Userset();
        var isValid = currentRelation.Union == null && currentRelation.Intersection == null && currentRelation.Difference == null;
        var allowed = isValid && RelationTarget.Of(currentRelation).Rewrite == RewriteType.Direct
            ? (typeDef.Metadata?.Relations.GetOrNull(relation)?.DirectlyRelatedUserTypes).OrEmpty().Where(r => r != null).ToList()
            : [];
        return (allowed, isValid);
    }

    private static void CheckDuplicateTypeRestrictions(ValidationErrorCollector collector, RelationMetadata? relationMetadata, string relationName, string typeName, Origin typeOrigin, string[]? lines)
    {
        var seen = new HashSet<string>();
        foreach (var restriction in (relationMetadata?.DirectlyRelatedUserTypes).OrEmpty())
        {
            if (restriction is null)
            {
                continue;
            }

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
                if (userset is null)
                {
                    continue;
                }

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
