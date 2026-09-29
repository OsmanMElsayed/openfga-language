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
