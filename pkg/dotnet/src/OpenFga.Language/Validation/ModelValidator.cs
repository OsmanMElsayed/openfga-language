using System;
using System.Collections.Generic;
using OpenFga.Language.Parsing;
using OpenFga.Sdk.Model;

namespace OpenFga.Language.Validation;

/// <summary>Checks OpenFGA models for semantic errors (undefined types and relations, unreachable relations, naming rules, ...).</summary>
public static class ModelValidator
{
    /// <summary>Parses and validates DSL text.</summary>
    /// <param name="dsl">The model in OpenFGA DSL syntax.</param>
    /// <exception cref="ArgumentNullException"><paramref name="dsl"/> is null.</exception>
    /// <exception cref="DslSyntaxException">The DSL does not parse.</exception>
    /// <exception cref="ModelValidationException">The model is invalid; errors carry source ranges.</exception>
    public static void Validate(string dsl) => Validate(dsl, new ModelValidationOptions());

    /// <summary>Parses and validates DSL text with custom naming rules.</summary>
    /// <param name="dsl">The model in OpenFGA DSL syntax.</param>
    /// <param name="options">The naming rules.</param>
    /// <exception cref="ArgumentNullException"><paramref name="dsl"/> or <paramref name="options"/> is null.</exception>
    /// <exception cref="ArgumentException">A pattern in <paramref name="options"/> is not a valid regular expression.</exception>
    /// <exception cref="DslSyntaxException">The DSL does not parse.</exception>
    /// <exception cref="ModelValidationException">The model is invalid; errors carry source ranges.</exception>
    public static void Validate(string dsl, ModelValidationOptions options)
    {
        if (dsl is null)
        {
            throw new ArgumentNullException(nameof(dsl));
        }

        if (options is null)
        {
            throw new ArgumentNullException(nameof(options));
        }

        var patterns = ValidationPatterns.From(options, nameof(options));
        var result = DslParser.Parse(dsl);
        if (result.Errors.Count > 0)
        {
            throw new DslSyntaxException(result.Errors);
        }

        ThrowIfAny(ModelValidationEngine.Collect(result.Model, patterns, dsl));
    }

    /// <summary>Validates a model.</summary>
    /// <param name="model">The model.</param>
    /// <exception cref="ArgumentNullException"><paramref name="model"/> is null.</exception>
    /// <exception cref="ModelValidationException">The model is invalid; errors have no source ranges.</exception>
    public static void Validate(AuthorizationModel model) => Validate(model, new ModelValidationOptions());

    /// <summary>Validates a model with custom naming rules.</summary>
    /// <param name="model">The model.</param>
    /// <param name="options">The naming rules.</param>
    /// <exception cref="ArgumentNullException"><paramref name="model"/> or <paramref name="options"/> is null.</exception>
    /// <exception cref="ArgumentException">A pattern in <paramref name="options"/> is not a valid regular expression.</exception>
    /// <exception cref="ModelValidationException">The model is invalid; errors have no source ranges.</exception>
    public static void Validate(AuthorizationModel model, ModelValidationOptions options)
    {
        if (model is null)
        {
            throw new ArgumentNullException(nameof(model));
        }

        if (options is null)
        {
            throw new ArgumentNullException(nameof(options));
        }

        ThrowIfAny(ModelValidationEngine.Collect(model, ValidationPatterns.From(options, nameof(options)), null));
    }

    private static void ThrowIfAny(List<ModelValidationError> errors)
    {
        if (errors.Count > 0)
        {
            throw new ModelValidationException(errors);
        }
    }
}
