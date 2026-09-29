namespace OpenFga.Language.Tests.Fixtures;

internal static class FixturePaths
{
    public static string DataDirectory { get; } = Locate();

    /// <summary>Reads a fixture, normalising CRLF (git autocrlf on Windows) to LF as in the repository.</summary>
    public static string Read(params string[] parts)
        => File.ReadAllText(Path.Combine([DataDirectory, .. parts])).Replace("\r\n", "\n", StringComparison.Ordinal);

    private static string Locate()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "tests", "data");
            if (Directory.Exists(Path.Combine(candidate, "transformer")))
            {
                return candidate;
            }
        }

        throw new InvalidOperationException("Could not locate the shared tests/data directory above " + AppContext.BaseDirectory);
    }
}
