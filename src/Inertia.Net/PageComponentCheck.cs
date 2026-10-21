using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Inertia.Net;

/// <summary>
/// The Development check that a rendered component has a page file (<see cref="InertiaOptions.Pages"/>). Matches names exactly, as the
/// client's page resolver does, even on case-insensitive file systems. Off outside Development and without a pages directory.
/// </summary>
internal sealed class PageComponentCheck
{
    private static readonly TimeSpan ScanInterval = TimeSpan.FromSeconds(1);

    private readonly string? _root;
    private readonly string _directory;
    private readonly string[] _extensions;
    private readonly TimeProvider _time;
    private Scan? _scan;

    public PageComponentCheck(IOptions<InertiaOptions> options, IHostEnvironment environment, TimeProvider time)
    {
        var pages = options.Value.Pages;
        _directory = pages.Directory ?? "";
        _extensions = [.. pages.Extensions];
        _time = time;
        if (environment.IsDevelopment() && pages.Directory is { Length: > 0 } directory)
        {
            _root = Path.GetFullPath(directory, environment.ContentRootPath); // an absolute Directory is used as is
        }
    }

    public bool Enabled => _root is not null;

    /// <summary>Throws, with the closest page name, when <paramref name="component"/> has no page file and the check is on.</summary>
    /// <exception cref="InvalidOperationException">The component has no page file.</exception>
    public void EnsureExists(string component)
    {
        if (_root is null || Find(component) is not null)
        {
            return;
        }

        var suggestion = Closest(component, Current(rescan: false).Components.Keys);
        throw new InvalidOperationException(
            $"The Inertia page component '{component}' was not found in '{_directory}' (looked for {string.Join(", ", _extensions)})."
            + (suggestion is null ? "" : $" Did you mean '{suggestion}'?")
            + " Check the name passed to Render, or InertiaOptions.Pages.");
    }

    /// <summary>The page file of <paramref name="component"/>, or null when there is none or the check is off.</summary>
    public string? Find(string component)
    {
        if (_root is null)
        {
            return null;
        }

        // A component created since the last scan is found by scanning again before giving up.
        return Current(rescan: false).Components.TryGetValue(component, out var path) || Current(rescan: true).Components.TryGetValue(component, out path)
            ? path
            : null;
    }

    private Scan Current(bool rescan)
    {
        var now = _time.GetTimestamp();
        if (_scan is { } scan && !rescan && _time.GetElapsedTime(scan.Taken, now) < ScanInterval)
        {
            return scan;
        }

        var components = new Dictionary<string, string>(StringComparer.Ordinal);
        if (Directory.Exists(_root))
        {
            foreach (var file in Directory.EnumerateFiles(_root!, "*", SearchOption.AllDirectories))
            {
                var extension = Path.GetExtension(file);
                if (_extensions.Contains(extension, StringComparer.Ordinal))
                {
                    var relative = Path.GetRelativePath(_root!, file);
                    components.TryAdd(relative[..^extension.Length].Replace('\\', '/'), file);
                }
            }
        }

        return _scan = new Scan(now, components);
    }

    // A case-only difference first, then the nearest name within a few edits.
    internal static string? Closest(string component, IEnumerable<string> names)
    {
        string? best = null;
        var bestDistance = Math.Max(2, component.Length / 4) + 1;
        foreach (var name in names)
        {
            if (string.Equals(name, component, StringComparison.OrdinalIgnoreCase))
            {
                return name;
            }

            var distance = Distance(component, name);
            if (distance < bestDistance)
            {
                (best, bestDistance) = (name, distance);
            }
        }

        return best;
    }

    // Levenshtein distance with one row.
    private static int Distance(string a, string b)
    {
        var row = new int[b.Length + 1];
        for (var j = 0; j <= b.Length; j++)
        {
            row[j] = j;
        }

        for (var i = 1; i <= a.Length; i++)
        {
            var diagonal = row[0];
            row[0] = i;
            for (var j = 1; j <= b.Length; j++)
            {
                var above = row[j];
                row[j] = Math.Min(Math.Min(row[j] + 1, row[j - 1] + 1), diagonal + (a[i - 1] == b[j - 1] ? 0 : 1));
                diagonal = above;
            }
        }

        return row[b.Length];
    }

    private sealed record Scan(long Taken, Dictionary<string, string> Components);
}
