using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace OpenFga.Language.Errors;

internal static class ErrorMessages
{
    /// <summary>Formats several errors as one message: <c>"{n} error(s) occurred:\n\t* {e1}\n\t* {e2}\n\n"</c>.</summary>
    public static string Aggregate(IReadOnlyCollection<LanguageError> errors)
        => string.Format(
            CultureInfo.InvariantCulture,
            "{0} error{1} occurred:\n\t* {2}\n\n",
            errors.Count,
            errors.Count > 1 ? "s" : string.Empty,
            string.Join("\n\t* ", errors.Select(e => e.ToString())));
}
