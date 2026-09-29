using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using OpenFga.Language.Internal;
using OpenFga.Language.Parsing;
using OpenFga.Sdk.Model;

namespace OpenFga.Language.Transformer;

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
