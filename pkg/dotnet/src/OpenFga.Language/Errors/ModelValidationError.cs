namespace OpenFga.Language.Errors;

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
