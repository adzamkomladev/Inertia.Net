using System.Text.Encodings.Web;

namespace Inertia.Net;

/// <summary>
/// Head tags managed by the server: sent as the <c>head</c> prop (see <see cref="InertiaHeadOptions"/>), which the client renders
/// with <c>createInertiaApp({ serverHead: true })</c> (Inertia 3.5 or later), and written into the initial HTML when the page is not
/// server-side rendered. Each tag carries <c>data-inertia</c> with its key (the title, the meta name or property, or the link rel),
/// so a page's own <c>&lt;Head&gt;</c> tag with the same <c>head-key</c> replaces it. Setting a key again replaces its tag.
/// </summary>
public sealed class InertiaHead
{
    // A list, not a dictionary: a head has a handful of tags, and a list keeps them in first-set order even after a removal.
    private readonly List<(string Key, Kind Kind, string Name, string Value)> _tags = new(8);

    private enum Kind
    {
        Title,
        Meta,
        Property,
        Link,
        Removed, // a Remove() in WithHead(...) that also drops the shared tag
    }

    internal InertiaHead()
    {
    }

    /// <summary>Whether no tag has been set.</summary>
    public bool IsEmpty => Count == 0;

    private int Count
    {
        get
        {
            var count = 0;
            foreach (var (_, kind, _, _) in _tags)
            {
                if (kind != Kind.Removed) count++;
            }

            return count;
        }
    }

    /// <summary>Sets the page title, formatted with <see cref="InertiaHeadOptions.TitleTemplate"/> when one is set.</summary>
    public InertiaHead Title(string title) => Set("title", Kind.Title, "title", title);

    /// <summary>Sets <c>&lt;meta name="<paramref name="name"/>" content="<paramref name="content"/>"&gt;</c>.</summary>
    public InertiaHead Meta(string name, string content) => Set(name, Kind.Meta, name, content);

    /// <summary>Sets <c>&lt;meta property="<paramref name="property"/>" content="<paramref name="content"/>"&gt;</c>, e.g. Open Graph tags.</summary>
    public InertiaHead Property(string property, string content) => Set(property, Kind.Property, property, content);

    /// <summary>Sets <c>&lt;link rel="<paramref name="rel"/>" href="<paramref name="href"/>"&gt;</c>, e.g. the canonical URL.</summary>
    public InertiaHead Link(string rel, string href) => Set(rel, Kind.Link, rel, href);

    /// <summary>
    /// Removes the tag with <paramref name="key"/> (<c>"title"</c>, a meta name or property, or a link rel); in <c>WithHead(...)</c>
    /// it also removes the shared tag.
    /// </summary>
    public InertiaHead Remove(string key)
    {
        ArgumentNullException.ThrowIfNull(key);
        Put((key, Kind.Removed, key, ""));
        return this;
    }

    private InertiaHead Set(string key, Kind kind, string name, string value)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentNullException.ThrowIfNull(value);
        Put((key, kind, name, value));
        return this;
    }

    private void Put((string Key, Kind Kind, string Name, string Value) tag)
    {
        var i = IndexOf(tag.Key);
        if (i < 0)
        {
            _tags.Add(tag);
        }
        else
        {
            _tags[i] = tag;
        }
    }

    private int IndexOf(string key)
    {
        for (var i = 0; i < _tags.Count; i++)
        {
            if (string.Equals(_tags[i].Key, key, StringComparison.Ordinal)) return i;
        }

        return -1;
    }

    internal void CopyTo(InertiaHead target)
    {
        foreach (var tag in _tags)
        {
            if (tag.Kind != Kind.Removed)
            {
                target.Put(tag);
            }
            else if (target.IndexOf(tag.Key) is >= 0 and var i)
            {
                target._tags.RemoveAt(i);
            }
        }
    }

    /// <summary>The tags as HTML, in the order their keys were first set, every name and value HTML-encoded.</summary>
    internal string[] ToHtml(string? titleTemplate)
    {
        var encoder = HtmlEncoder.Default;
        var html = new string[Count];
        var i = 0;
        foreach (var (key, kind, name, value) in _tags)
        {
            if (kind == Kind.Removed)
            {
                continue;
            }

            var dataKey = encoder.Encode(key);
            html[i++] = kind switch
            {
                Kind.Title => $"<title data-inertia=\"{dataKey}\">{encoder.Encode(titleTemplate is null ? value : string.Format(System.Globalization.CultureInfo.InvariantCulture, titleTemplate, value))}</title>",
                Kind.Meta => $"<meta name=\"{encoder.Encode(name)}\" content=\"{encoder.Encode(value)}\" data-inertia=\"{dataKey}\">",
                Kind.Property => $"<meta property=\"{encoder.Encode(name)}\" content=\"{encoder.Encode(value)}\" data-inertia=\"{dataKey}\">",
                _ => $"<link rel=\"{encoder.Encode(name)}\" href=\"{encoder.Encode(value)}\" data-inertia=\"{dataKey}\">",
            };
        }

        return html;
    }
}

/// <summary>Server-managed head settings (see <see cref="InertiaHead"/>).</summary>
public sealed class InertiaHeadOptions
{
    /// <summary>The prop the head tags are sent in. Default <c>head</c>, which <c>serverHead: true</c> reads.</summary>
    public string PropName { get; set; } = "head";

    /// <summary>
    /// A composite format for titles, e.g. <c>"{0} - My App"</c>; null for none. Applied on the server, so do not also format titles
    /// with the client's <c>title</c> option.
    /// </summary>
    public string? TitleTemplate { get; set; }
}
