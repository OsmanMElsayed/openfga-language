using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using OpenFga.Language.Errors;
using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

namespace OpenFga.Language.Transformation;

/// <summary>Reads fga.mod YAML (JS mod-to-json.ts). Messages match JS; YAML syntax errors carry YamlDotNet's text.</summary>
internal static class ModFileReader
{
    private const string SupportedSchema = "1.2";
    private static readonly TimeSpan MatchTimeout = TimeSpan.FromSeconds(1);

    // YAML 1.2 core schema: plain (unquoted) scalars matching these are not strings.
    private static readonly Regex NullScalar = new("^(~|null|Null|NULL)?$", RegexOptions.CultureInvariant, MatchTimeout);
    private static readonly Regex BoolScalar = new("^(true|True|TRUE|false|False|FALSE)$", RegexOptions.CultureInvariant, MatchTimeout);
    private static readonly Regex IntScalar = new("^([-+]?[0-9]+|0o[0-7]+|0x[0-9a-fA-F]+)$", RegexOptions.CultureInvariant, MatchTimeout);
    private static readonly Regex FloatScalar = new(@"^([-+]?(\.[0-9]+|[0-9]+(\.[0-9]*)?)([eE][-+]?[0-9]+)?|[-+]?\.(inf|Inf|INF)|\.(nan|NaN|NAN))$", RegexOptions.CultureInvariant, MatchTimeout);

    public static ModFile Read(string text)
    {
        var root = Load(text);
        var errors = new List<LanguageError>();

        var schemaNode = Get(root, "schema");
        if (schemaNode is null)
        {
            errors.Add(LanguageErrors.ModFile("missing schema field", default));
        }
        else if (!IsString(schemaNode))
        {
            errors.Add(LanguageErrors.ModFile($"unexpected schema type, expected string got value {Describe(schemaNode)}", RangeOf(schemaNode)));
        }
        else if (((YamlScalarNode)schemaNode).Value != SupportedSchema)
        {
            errors.Add(LanguageErrors.ModFile("unsupported schema version, fga.mod only supported in version `1.2`", RangeOf(schemaNode)));
        }

        var files = new List<string>();
        var contentsNode = Get(root, "contents");
        if (contentsNode is null)
        {
            errors.Add(LanguageErrors.ModFile("missing contents field", default));
        }
        else if (contentsNode is not YamlSequenceNode sequence)
        {
            errors.Add(LanguageErrors.ModFile($"unexpected contents type, expected list of strings got value {Describe(contentsNode)}", RangeOf(contentsNode)));
        }
        else
        {
            foreach (var item in sequence.Children)
            {
                if (!IsString(item))
                {
                    errors.Add(LanguageErrors.ModFile($"unexpected contents item type, expected string got value {DescribeItem(item)}", RangeOf(item)));
                    continue;
                }

                var original = ((YamlScalarNode)item).Value ?? string.Empty;
                var fileName = DecodePath(original);

                // Reject directory traversal and absolute paths.
                if (fileName.IndexOf("../", StringComparison.Ordinal) >= 0
                    || fileName.IndexOf("..\\", StringComparison.Ordinal) >= 0
                    || fileName.StartsWith("/", StringComparison.Ordinal)
                    || fileName.StartsWith("\\", StringComparison.Ordinal)
                    || (fileName.Length >= 2 && fileName[1] == ':' && IsAsciiLetter(fileName[0])))
                {
                    errors.Add(LanguageErrors.ModFile($"invalid contents item {original}", RangeOf(item)));
                    continue;
                }

                if (!fileName.EndsWith(".fga", StringComparison.Ordinal))
                {
                    errors.Add(LanguageErrors.ModFile($"contents items should use fga file extension, got {original}", RangeOf(item)));
                    continue;
                }

                files.Add(fileName);
            }
        }

        if (errors.Count > 0)
        {
            throw new ModFileValidationException(errors);
        }

        return new ModFile(SupportedSchema, new ReadOnlyCollection<string>(files));
    }

    private static YamlMappingNode? Load(string text)
    {
        try
        {
            var stream = new YamlStream();
            stream.Load(new StringReader(text));
            return stream.Documents.Count > 0 ? stream.Documents[0].RootNode as YamlMappingNode : null;
        }
        catch (YamlException ex)
        {
            var range = new SourceRange((int)ex.Start.Line - 1, (int)ex.End.Line - 1, (int)ex.Start.Column - 1, (int)ex.End.Column - 1);
            throw new ModFileValidationException([LanguageErrors.ModFile(ex.Message, range)]);
        }
    }

    private static YamlNode? Get(YamlMappingNode? root, string key)
    {
        if (root is null)
        {
            return null;
        }

        foreach (var entry in root.Children)
        {
            if (entry.Key is YamlScalarNode scalar && scalar.Value == key)
            {
                return entry.Value;
            }
        }

        return null;
    }

    private static bool IsString(YamlNode node)
        => node is YamlScalarNode scalar && !(scalar.Style == ScalarStyle.Plain && IsCoreNonString(scalar.Value ?? string.Empty));

    private static bool IsCoreNonString(string value)
        => NullScalar.IsMatch(value) || BoolScalar.IsMatch(value) || IntScalar.IsMatch(value) || FloatScalar.IsMatch(value);

    /// <summary>Renders a value the way JS template literals print the YAML value (<c>true</c>, <c>1</c>, <c>null</c>, ...).</summary>
    private static string Describe(YamlNode node)
    {
        if (node is not YamlScalarNode scalar)
        {
            return node is YamlMappingNode ? "[object Object]" : node.ToString();
        }

        var value = scalar.Value ?? string.Empty;
        if (scalar.Style != ScalarStyle.Plain)
        {
            return value;
        }

        if (NullScalar.IsMatch(value))
        {
            return "null";
        }

        if (BoolScalar.IsMatch(value))
        {
            return value.ToLowerInvariant();
        }

        if (IntScalar.IsMatch(value) || FloatScalar.IsMatch(value))
        {
            return DescribeNumber(value);
        }

        return value;
    }

    private static string DescribeItem(YamlNode node) => node is YamlScalarNode ? Describe(node) : "undefined";

    private static string DescribeNumber(string value)
    {
        if (value.StartsWith("0x", StringComparison.Ordinal))
        {
            try
            {
                return Convert.ToInt64(value.Substring(2), 16).ToString(CultureInfo.InvariantCulture);
            }
            catch (OverflowException)
            {
                return value;
            }
        }

        if (value.StartsWith("0o", StringComparison.Ordinal))
        {
            try
            {
                return Convert.ToInt64(value.Substring(2), 8).ToString(CultureInfo.InvariantCulture);
            }
            catch (OverflowException)
            {
                return value;
            }
        }

        var lower = value.ToLowerInvariant();
        if (lower.EndsWith(".nan", StringComparison.Ordinal))
        {
            return "NaN";
        }

        if (lower.EndsWith(".inf", StringComparison.Ordinal))
        {
            return lower.StartsWith("-", StringComparison.Ordinal) ? "-Infinity" : "Infinity";
        }

        try
        {
            return double.Parse(value, NumberStyles.Float, CultureInfo.InvariantCulture).ToString("R", CultureInfo.InvariantCulture);
        }
        catch (OverflowException)
        {
            // .NET Framework throws for values beyond double's range; .NET Core returns infinity.
            return value;
        }
    }

    private static bool IsAsciiLetter(char c) => (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z');

    // Decode URI-encoded dots and slashes so encoded traversal (%2e%2e%2f) is caught.
    private static string DecodePath(string value)
    {
        var decoded = Regex.Replace(value, "%2e", ".", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, MatchTimeout);
        decoded = Regex.Replace(decoded, "%2f", "/", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, MatchTimeout);
        return Regex.Replace(decoded, "%5c", "\\", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, MatchTimeout);
    }

    // YamlDotNet marks are one-based; the end mark is just past the node.
    private static SourceRange RangeOf(YamlNode node)
        => new((int)node.Start.Line - 1, (int)node.End.Line - 1, (int)node.Start.Column - 1, (int)node.End.Column - 1);
}
