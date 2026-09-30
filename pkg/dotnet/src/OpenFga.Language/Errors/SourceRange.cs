using System;
using System.Globalization;

namespace OpenFga.Language;

/// <summary>A span of source text. All values are zero-based.</summary>
public readonly struct SourceRange : IEquatable<SourceRange>
{
    /// <summary>Initializes a new <see cref="SourceRange"/>.</summary>
    /// <param name="startLine">Zero-based line where the span starts.</param>
    /// <param name="endLine">Zero-based line where the span ends.</param>
    /// <param name="startColumn">Zero-based column where the span starts.</param>
    /// <param name="endColumn">Zero-based column where the span ends (exclusive).</param>
    internal SourceRange(int startLine, int endLine, int startColumn, int endColumn)
    {
        StartLine = startLine;
        EndLine = endLine;
        StartColumn = startColumn;
        EndColumn = endColumn;
    }

    /// <summary>Gets the zero-based line where the span starts.</summary>
    public int StartLine { get; }

    /// <summary>Gets the zero-based line where the span ends.</summary>
    public int EndLine { get; }

    /// <summary>Gets the zero-based column where the span starts.</summary>
    public int StartColumn { get; }

    /// <summary>Gets the zero-based column where the span ends (exclusive).</summary>
    public int EndColumn { get; }

    /// <inheritdoc />
    public bool Equals(SourceRange other)
        => StartLine == other.StartLine && EndLine == other.EndLine && StartColumn == other.StartColumn && EndColumn == other.EndColumn;

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is SourceRange other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode()
    {
        unchecked
        {
            var hash = StartLine;
            hash = (hash * 397) ^ EndLine;
            hash = (hash * 397) ^ StartColumn;
            return (hash * 397) ^ EndColumn;
        }
    }

    /// <summary>Determines whether two ranges are equal.</summary>
    public static bool operator ==(SourceRange left, SourceRange right) => left.Equals(right);

    /// <summary>Determines whether two ranges differ.</summary>
    public static bool operator !=(SourceRange left, SourceRange right) => !left.Equals(right);

    /// <inheritdoc />
    public override string ToString()
        => string.Format(CultureInfo.InvariantCulture, "({0},{1})-({2},{3})", StartLine, StartColumn, EndLine, EndColumn);
}
