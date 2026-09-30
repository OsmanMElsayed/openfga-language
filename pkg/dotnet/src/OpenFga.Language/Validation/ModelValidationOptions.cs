namespace OpenFga.Language.Validation;

/// <summary>Naming rules applied by <see cref="ModelValidator"/>.</summary>
public sealed class ModelValidationOptions
{
    /// <summary>Gets or sets the regular expression type names must match. Defaults to <c>^[^:#@\*\s]{1,254}$</c>. Patterns are evaluated with a 1-second match timeout; a pattern that exceeds it makes validation throw <see cref="System.Text.RegularExpressions.RegexMatchTimeoutException"/>.</summary>
    public string TypePattern { get; set; } = Rules.DefaultTypePattern;

    /// <summary>Gets or sets the regular expression relation names must match. Defaults to <c>^[^:#@\*\s]{1,50}$</c>. Patterns are evaluated with a 1-second match timeout; a pattern that exceeds it makes validation throw <see cref="System.Text.RegularExpressions.RegexMatchTimeoutException"/>.</summary>
    public string RelationPattern { get; set; } = Rules.DefaultRelationPattern;
}
