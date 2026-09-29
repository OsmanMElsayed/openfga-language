using System;
using System.Text.RegularExpressions;

namespace OpenFga.Language.Internal;

/// <summary>Column resolution shared by validation and module combining (JS <c>line.search(new RegExp("\\b" + symbol + "\\b"))</c>).</summary>
internal static class TextSearch
{
    private static readonly TimeSpan MatchTimeout = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Index of the first whole-word occurrence of <paramref name="symbol"/>, or -1. The symbol is deliberately not
    /// escaped, as in JS; ECMAScript mode gives JS <c>\b</c> semantics. An invalid pattern counts as not found.
    /// </summary>
    public static int FindWord(string? line, string symbol)
    {
        if (line is null)
        {
            return -1;
        }

        try
        {
            var match = Regex.Match(line, @"\b" + symbol + @"\b", RegexOptions.ECMAScript, MatchTimeout);
            return match.Success ? match.Index : -1;
        }
        catch (ArgumentException)
        {
            return -1;
        }
    }

    public static int FindWordOrZero(string? line, string symbol)
    {
        var index = FindWord(line, symbol);
        return index < 0 ? 0 : index;
    }

    /// <summary>Marks the symbol in the text after the first <c>:</c> (the value, not the key).</summary>
    public static int ResolveAfterColon(string line, string symbol, int fallback)
    {
        var colon = line.IndexOf(':');
        if (colon < 0)
        {
            return fallback;
        }

        var segment = line.Substring(colon + 1);
        var nextColon = segment.IndexOf(':');
        if (nextColon >= 0)
        {
            segment = segment.Substring(0, nextColon);
        }

        return colon + FindWord(segment, symbol) + 1;
    }

    /// <summary>Marks the symbol in the text after <c>from</c> (the tupleset of <c>a from b</c>).</summary>
    public static int ResolveAfterFrom(string line, string symbol)
    {
        var start = Math.Min(line.IndexOf("from", StringComparison.Ordinal) + "from".Length, line.Length);
        return start + line.Substring(start).IndexOf(symbol, StringComparison.Ordinal);
    }
}
