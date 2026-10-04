namespace OpenFga.Language.Validation;

internal readonly struct Origin
{
    public Origin(string? file, string? module)
    {
        File = file;
        Module = module;
    }

    public string? File { get; }

    public string? Module { get; }
}
