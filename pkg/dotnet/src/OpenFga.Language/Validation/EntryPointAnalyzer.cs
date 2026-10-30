using System.Collections.Generic;
using OpenFga.Language.Utilities;
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
            foreach (var restriction in (relationsMetadata.GetOrNull(relationName!)?.DirectlyRelatedUserTypes).OrEmpty())
            {
                if (restriction is null)
                {
                    continue;
                }

                var restrictionType = restriction.Type ?? string.Empty;
                if (restriction.Relation is not { Length: > 0 } restrictionRelation || restriction.Wildcard != null)
                {
                    return (true, false);
                }

                var assignableRelation = typeMap.GetOrNull(restrictionType)?.Relations.GetOrNull(restrictionRelation);
                if (assignableRelation == null)
                {
                    return (false, false);
                }

                if (visited.Contains(restrictionType, restrictionRelation))
                {
                    continue;
                }

                if (Analyze(typeMap, restrictionType, restrictionRelation, assignableRelation, visited).HasEntry)
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

            foreach (var restriction in (relationsMetadata.GetOrNull(tuplesetRelationName)?.DirectlyRelatedUserTypes).OrEmpty())
            {
                if (restriction is null)
                {
                    continue;
                }

                // JS indexes types by the full restriction text, which throws for "folder with c"; use the type part.
                var targetType = restriction.Type ?? string.Empty;
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
                if (child is null)
                {
                    continue;
                }

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
                if (child is null)
                {
                    continue;
                }

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
