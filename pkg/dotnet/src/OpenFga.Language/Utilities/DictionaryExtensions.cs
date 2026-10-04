using System.Collections.Generic;

namespace OpenFga.Language.Utilities;

internal static class DictionaryExtensions
{
    /// <summary>Returns the value for <paramref name="key"/>, or null when the dictionary or key is missing.</summary>
    public static TValue? GetOrNull<TValue>(this IDictionary<string, TValue>? dictionary, string key)
        where TValue : class
        => dictionary != null && dictionary.TryGetValue(key, out var value) ? value : null;
}
