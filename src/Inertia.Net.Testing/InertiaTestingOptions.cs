namespace Inertia.Net.Testing;

/// <summary>Optional page-file checks for response assertions. Paths are relative to the test process unless absolute.</summary>
public sealed class InertiaTestingOptions
{
    /// <summary>Directories containing page components. Names are relative to each directory.</summary>
    public IList<string> PageDirectories { get; } = [];
    /// <summary>Allowed component extensions, including the dot.</summary>
    public IList<string> PageExtensions { get; } = [".tsx", ".jsx", ".ts", ".js", ".vue", ".svelte"];
    /// <summary>Whether Component assertions also check the filesystem. Default false.</summary>
    public bool EnsurePagesExist { get; set; }
}
