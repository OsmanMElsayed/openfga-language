using System.Collections.Generic;
using System.Linq;

namespace OpenFga.Language.Utilities;

internal static class CollectionExtensions
{
    public static IEnumerable<T> OrEmpty<T>(this IEnumerable<T>? source) => source ?? Enumerable.Empty<T>();
}
