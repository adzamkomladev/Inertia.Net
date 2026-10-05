using System.Numerics;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Inertia.Net.Testing;

/// <summary>A parsed Inertia v3 page object, from a JSON response or the initial HTML <c>&lt;script data-page&gt;</c>.</summary>
public sealed partial class InertiaPage
{
    private static readonly string[] None = [];

    private InertiaPage(JsonObject raw, JsonObject props) => (Raw, Props) = (raw, props);

    /// <summary>The whole page object.</summary>
    public JsonObject Raw { get; }

    /// <summary>The page <c>props</c>.</summary>
    public JsonObject Props { get; }

    /// <summary>The page component name.</summary>
    public string Component => (string)Raw["component"]!;

    /// <summary>The page URL (path and query).</summary>
    public string Url => (string)Raw["url"]!;

    /// <summary>The asset version (<c>""</c> when absent).</summary>
    public string Version => Raw["version"] is JsonValue v && v.TryGetValue<string>(out var s) ? s : "";

    /// <summary>Deferred prop paths by group.</summary>
    public IReadOnlyDictionary<string, string[]> DeferredProps =>
        Raw["deferredProps"] is JsonObject o ? o.ToDictionary(p => p.Key, p => Strings(p.Value)) : [];

    /// <summary>Paths of props to append-merge.</summary>
    public string[] MergeProps => Strings(Raw["mergeProps"]);

    /// <summary>Paths of props to prepend-merge.</summary>
    public string[] PrependProps => Strings(Raw["prependProps"]);

    /// <summary>Paths of props to deep-merge.</summary>
    public string[] DeepMergeProps => Strings(Raw["deepMergeProps"]);

    /// <summary>Merge match keys (<c>path.field</c>).</summary>
    public string[] MatchPropsOn => Strings(Raw["matchPropsOn"]);

    /// <summary>Paths of props whose loader failed and was rescued.</summary>
    public string[] RescuedProps => Strings(Raw["rescuedProps"]);

    /// <summary>Top-level keys that came from shared props.</summary>
    public string[] SharedProps => Strings(Raw["sharedProps"]);

    /// <summary>Infinite-scroll metadata by path (empty object when absent).</summary>
    public JsonObject ScrollProps => Raw["scrollProps"] as JsonObject ?? [];

    /// <summary>Once-prop metadata by key (empty object when absent).</summary>
    public JsonObject OnceProps => Raw["onceProps"] as JsonObject ?? [];

    /// <summary>Flash data (empty object when absent).</summary>
    public JsonObject Flash => Raw["flash"] as JsonObject ?? [];

    /// <summary>Whether <c>encryptHistory</c> is set.</summary>
    public bool EncryptHistory => Flag("encryptHistory");

    /// <summary>Whether <c>clearHistory</c> is set.</summary>
    public bool ClearHistory => Flag("clearHistory");

    /// <summary>Whether <c>preserveFragment</c> is set.</summary>
    public bool PreserveFragment => Flag("preserveFragment");

    /// <summary>Whether <c>preserveBigIntegers</c> is set.</summary>
    public bool PreserveBigIntegers => Flag("preserveBigIntegers");

    private bool Flag(string name) => Raw[name] is JsonValue v && v.TryGetValue<bool>(out var b) && b;

    private static string[] Strings(JsonNode? n) => n is JsonArray a ? [.. a.Select(x => x!.GetValue<string>())] : None;

    /// <summary>Returns the prop at a dot path (array indexes allowed), or null when it is missing or JSON null.</summary>
    public JsonNode? Prop(string dotPath) => Json.TryGet(Props, dotPath, out var v) ? v : null;

    /// <summary>Whether a prop exists at the dot path (JSON null counts as existing).</summary>
    public bool HasProp(string dotPath) => Json.TryGet(Props, dotPath, out _);

    /// <summary>Reads a prop as <see cref="BigInteger"/>: decodes <c>{"$bigint":"..."}</c> or a plain JSON integer.</summary>
    public BigInteger PropBigInteger(string dotPath) => Prop(dotPath) switch
    {
        JsonObject o when o["$bigint"] is JsonValue s && s.TryGetValue<string>(out var digits) => BigInteger.Parse(digits),
        JsonValue v when v.GetValueKind() == JsonValueKind.Number => BigInteger.Parse(v.ToJsonString()),
        var other => throw new InertiaAssertionException($"Property [{dotPath}] is not an integer or $bigint: {Json.Show(other)}."),
    };

    /// <summary>
    /// Parses an Inertia page from a JSON response (needs <c>X-Inertia: true</c>) or an HTML response
    /// containing <c>&lt;script data-page="..." type="application/json"&gt;</c>.
    /// </summary>
    public static async Task<InertiaPage> ParseAsync(HttpResponseMessage response, CancellationToken cancellationToken = default)
    {
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        var snippet = body.Length > 200 ? body[..200] + "..." : body;
        var status = $"HTTP {(int)response.StatusCode} {response.StatusCode}, Content-Type: {response.Content.Headers.ContentType?.MediaType ?? "none"}";

        InertiaAssertionException Invalid(string why) =>
            new($"Not a valid Inertia response: {why} [{status}] Body: {snippet}");

        string json;
        if (response.Headers.Contains("X-Inertia")) json = body;
        else
        {
            var m = ScriptTag().Matches(body).FirstOrDefault(s => DataPageAttr().IsMatch(s.Groups[1].Value));
            if (m is null) throw Invalid("no X-Inertia response header and no <script data-page> element found.");
            json = m.Groups[2].Value;
        }

        JsonObject? raw;
        try { raw = JsonNode.Parse(json) as JsonObject; }
        catch (JsonException e) { throw Invalid($"page is not valid JSON ({e.Message})"); }

        if (raw is null) throw Invalid("page is not a JSON object.");
        if (raw["component"] is not JsonValue c || !c.TryGetValue<string>(out _)) throw Invalid("page has no string 'component'.");
        if (raw["url"] is not JsonValue u || !u.TryGetValue<string>(out _)) throw Invalid("page has no string 'url'.");
        if (raw["props"] is not JsonObject props) throw Invalid("page has no 'props' object.");
        return new InertiaPage(raw, props);
    }

    /// <summary>Partial reload asking only for <paramref name="only"/>; asserts that those props exist in the result.</summary>
    public async Task<InertiaPage> ReloadOnlyAsync(HttpClient client, params string[] only)
    {
        var page = await ReloadAsync(client, "X-Inertia-Partial-Data", only);
        new AssertableInertia(page).HasAll(only);
        return page;
    }

    /// <summary>Partial reload excluding <paramref name="except"/>; asserts that those props are missing in the result.</summary>
    public async Task<InertiaPage> ReloadExceptAsync(HttpClient client, params string[] except)
    {
        var page = await ReloadAsync(client, "X-Inertia-Partial-Except", except);
        new AssertableInertia(page).MissingAll(except);
        return page;
    }

    /// <summary>Loads deferred props of the given groups (all groups when none given) with a partial reload.</summary>
    public Task<InertiaPage> LoadDeferredPropsAsync(HttpClient client, params string[] groups)
    {
        var paths = DeferredProps.Where(g => groups.Length == 0 || groups.Contains(g.Key)).SelectMany(g => g.Value).ToArray();
        if (paths.Length == 0)
            throw new InertiaAssertionException($"No deferred props to load{(groups.Length > 0 ? $" in groups [{string.Join(", ", groups)}]" : "")}.");
        return ReloadAsync(client, "X-Inertia-Partial-Data", paths);
    }

    private async Task<InertiaPage> ReloadAsync(HttpClient client, string header, string[] paths)
    {
        using var response = await client.InertiaGetAsync(Url, Version, r =>
        {
            r.Headers.TryAddWithoutValidation("X-Inertia-Partial-Component", Component);
            r.Headers.TryAddWithoutValidation(header, string.Join(',', paths));
        });
        return await ParseAsync(response);
    }

    [GeneratedRegex(@"<script\b([^>]*)>(.*?)</script>", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex ScriptTag();

    [GeneratedRegex(@"(^|\s)data-page\s*=", RegexOptions.IgnoreCase)]
    private static partial Regex DataPageAttr();
}
