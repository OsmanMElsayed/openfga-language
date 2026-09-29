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
