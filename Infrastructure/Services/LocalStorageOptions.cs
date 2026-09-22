namespace RealEstateApi.Infrastructure.Services;

public class LocalStorageOptions
{
    public const string SectionName = "LocalStorage";

    /// <summary>
    /// Where uploaded files land on disk. Relative paths resolve against the
    /// app's content root (i.e. "wwwroot/uploads" is served by static files).
    /// </summary>
    public string BasePath { get; init; } = "wwwroot/uploads";

    /// <summary>URL prefix the files are served under. Must match the static-files mapping.</summary>
    public string PublicPrefix { get; init; } = "/uploads/";
}
