using System;

namespace OpenFga.Language.Errors;

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
